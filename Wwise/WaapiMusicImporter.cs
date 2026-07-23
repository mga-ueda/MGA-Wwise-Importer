using System.Text;
using System.Text.Json;
using MgaWwiseIMImporter.UI;
using MgaWwiseIMImporter.Wave;

namespace MgaWwiseIMImporter.Wwise;

/// <summary>
/// <see cref="WwiseMusicPlan"/> を WAAPI（ak.wwise.core.object.set）で Wwise へ流し込む。
/// <para>
/// 1. 各 Music Segment 用の WAV を用意する。複数波形で焼き込み不要なら元ファイルを
///    outputDirectory へコピーして共有し、MusicClip の Begin/End Offset で範囲を合わせる。
///    フェード／ラウドネス焼き込みが必要なときだけ切り出し WAV を書く。
/// 2. 複数パート時は State Group／State を作成または更新し、Music Switch Container に割当。
/// 3. object.set で Playlist／Segment／Track（＋WAV）と Cue を作成。
/// 4. 必要なら MusicClip トリムを設定する。
/// </para>
/// </summary>
internal static class WaapiMusicImporter
{
    public static async Task<string> ImportAsync(
        WaapiSettings waapiSettings,
        WwiseImportSettings importSettings,
        WwiseMusicPlan plan,
        string parentPath,
        string sourceWavPath,
        string outputDirectory,
        IReadOnlyList<WaveformOutputPart> outputParts,
        WavFileInfo wavInfo,
        IReadOnlyDictionary<int, int>? partGroupIds = null,
        bool loudnessNormalizeEnabled = false,
        double loudnessTargetLkfs = -24.0,
        bool loudnessPreserveGroupBalance = true,
        bool autoVolumeEnabled = true,
        AutoVolumeTarget autoVolumeTarget = AutoVolumeTarget.MakeUpGain,
        bool updateExistingStateGroup = false,
        IReadOnlyList<RegionEdgeFade>? regionEdgeFades = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (wavInfo.SampleRate == 0 || wavInfo.BlockAlign == 0)
        {
            throw new ArgumentException(UiStrings.ErrBadSampleRate);
        }

        var sampleRate = wavInfo.SampleRate;
        var blockAlign = wavInfo.BlockAlign;

        var sb = new StringBuilder();
        void Log(string line = "")
        {
            sb.AppendLine(line);
            progress?.Report(line);
        }

        Log(UiStrings.LogWwiseImportHeader);
        Log($"{UiStrings.KeyTarget} {parentPath}");
        Log(
            $"{UiStrings.KeyMode} "
            + (plan.IsMultiPart
                ? UiStrings.LabelMusicSwitchContainer
                : UiStrings.LabelMusicPlaylistContainer));
        Log($"{UiStrings.KeyName} {plan.ContainerName}");

        string? stateGroupPath = null;
        if (plan.IsMultiPart)
        {
            stateGroupPath = importSettings.ResolveStateGroupPath(plan.ContainerName);
            Log($"{UiStrings.KeyStateGrp} {stateGroupPath}");
            if (updateExistingStateGroup)
            {
                // onNameConflict=merge により、State Group 自体を維持したまま
                // State 一覧を現在の Playlist 構成へ同期する。
                Log(UiStrings.LogStateGroupUpdateExisting);
            }
            else
            {
                Log(UiStrings.LogStateGroupCreateNew);
            }
        }

        Dictionary<int, float>? partGains = null;
        if (loudnessNormalizeEnabled)
        {
            Log(UiStrings.LogLoudnessNormalizeOn(loudnessTargetLkfs, loudnessPreserveGroupBalance));
            partGains = LoudnessMeter.ComputePartGains(
                sourceWavPath,
                wavInfo,
                outputParts,
                partGroupIds,
                loudnessTargetLkfs,
                loudnessPreserveGroupBalance,
                Log);
            if (autoVolumeEnabled)
            {
                Log(
                    UiStrings.LogAutoVolumeOn(
                        autoVolumeTarget == AutoVolumeTarget.VoiceVolume
                            ? UiStrings.LabelVoiceVolume
                            : UiStrings.LabelMakeUpGain));
            }
            else
            {
                Log(UiStrings.LogAutoVolumeOff);
            }
        }

        var applyAutoVolume = loudnessNormalizeEnabled && autoVolumeEnabled && partGains is not null;

        // 中間パート WAV は作らず、元 WAV から最終セグメント WAV を直接切り出す。
        var fadesForBake = regionEdgeFades?
            .Select(fade => fade.Normalized())
            .Where(fade => fade.HasAnyFade)
            .ToList() ?? [];

        var segmentMedia = SliceSegmentWavs(
            plan,
            sourceWavPath,
            outputDirectory,
            outputParts,
            sampleRate,
            blockAlign,
            wavInfo,
            partGains,
            fadesForBake,
            Log);

        // タイムアウトは import を含むので長めに取る
        var timeout = TimeSpan.FromMilliseconds(Math.Max(waapiSettings.TimeoutMs, 30000));
        using var client = new WaapiHttpClient(waapiSettings.Url, timeout);
        var returnOptions = new Dictionary<string, object>
        {
            ["return"] = new[] { "id", "name", "type", "path" },
        };

        // 一括 object.set だと長時間 UI が止まったように見えるため、段階的に投げて進捗を出す。
        if (plan.IsMultiPart)
        {
            if (stateGroupPath is null || stateGroupPath.Length == 0)
            {
                throw new InvalidOperationException(UiStrings.ErrStateGroupPathRequired);
            }

            Log(UiStrings.LogCreatingStateGroup);
            await CallObjectSetAsync(
                    client,
                    BuildStateGroupSetArgs(plan, importSettings),
                    returnOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            var containerPath = $"{parentPath.TrimEnd('\\')}\\{plan.ContainerName}";
            Log(UiStrings.LogCreatingMusicSwitch);
            await CallObjectSetAsync(
                    client,
                    BuildMusicSwitchShellSetArgs(plan, parentPath, stateGroupPath),
                    returnOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            for (var i = 0; i < plan.Playlists.Count; i++)
            {
                var playlist = plan.Playlists[i];
                Log(UiStrings.LogCreatingPlaylist(i + 1, plan.Playlists.Count, playlist.Name));
                await CallObjectSetAsync(
                        client,
                        BuildPlaylistAppendSetArgs(
                            plan,
                            containerPath,
                            playlist,
                            segmentMedia,
                            importSettings,
                            applyAutoVolume,
                            autoVolumeTarget,
                            partGains,
                            Log),
                        returnOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            // Playlist 未作成時に @AudioNode / Destination を張ると空参照になるため、子作成後に結ぶ。
            Log(UiStrings.LogBindingStates);
            await CallObjectSetAsync(
                    client,
                    BuildMusicSwitchEntriesSetArgs(plan, containerPath, stateGroupPath),
                    returnOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            Log(UiStrings.LogConfiguringTransitions);
            await CallObjectSetAsync(
                    client,
                    BuildMusicSwitchTransitionsSetArgs(plan, containerPath),
                    returnOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            // DestinationContextObject は Reference のため、ネスト作成だけでは空になり得る。
            await BindTransitionDestinationsAsync(
                    client,
                    containerPath,
                    plan,
                    Log,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            Log(UiStrings.LogCreatingWwiseObjects);
            await CallObjectSetAsync(
                    client,
                    BuildSinglePlaylistSetArgs(
                        plan,
                        parentPath,
                        segmentMedia,
                        importSettings,
                        applyAutoVolume,
                        autoVolumeTarget,
                        partGains,
                        Log),
                    returnOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        Log(UiStrings.LogWwiseObjectsCreated);

        // MusicSegment は作成時に既定の Entry/Exit を持つ。
        // 作成と同時の @Cues 追加は二重化するため、作成後に replaceAll で差し替える。
        var musicRootPath = $"{parentPath.TrimEnd('\\')}\\{plan.ContainerName}";
        await ReplaceAllSegmentCuesAsync(
                client,
                plan,
                musicRootPath,
                cancellationToken)
            .ConfigureAwait(false);

        var playAtFixes = await ApplyMusicClipTrimsAsync(
                client,
                plan,
                musicRootPath,
                segmentMedia,
                Log,
                cancellationToken)
            .ConfigureAwait(false);

        // 負の PlayAt は WAAPI では設定不可のため、プロジェクトを保存→クローズし、
        // WWU（XML）を直接書き換えてから再オープンする。
        await ApplyPlayAtFixesViaWorkUnitAsync(
                client,
                playAtFixes,
                Log,
                cancellationToken)
            .ConfigureAwait(false);

        if (plan.IsMultiPart)
        {
            foreach (var playlist in plan.Playlists)
            {
                Log(
                    UiStrings.LogTransitionAnyToPlaylist(
                        playlist.Name,
                        playlist.ExitSourceAt.ToUiName()));
            }
        }

        foreach (var playlist in plan.Playlists)
        {
            Log(UiStrings.LogPlaylistSummary(playlist.Name, playlist.Segments.Count));
            for (var segmentIndex = 0; segmentIndex < playlist.Segments.Count; segmentIndex++)
            {
                var segment = playlist.Segments[segmentIndex];
                var flags = new List<string>();
                if (segment.LoopInfinite)
                {
                    flags.Add("loop=∞");
                }

                if (segment.EntryCueMs > segment.ClipStartMs)
                {
                    flags.Add("anacrusis");
                }

                if (segment.ExitCueMs < segment.ClipEndMs)
                {
                    flags.Add("exit-tail");
                }

                if (segment.CustomCues.Count > 0)
                {
                    flags.Add($"cues={segment.CustomCues.Count}");
                }

                flags.Add($"tracks={segment.Tracks.Count}");

                var durationMs = Math.Max(0.0, segment.ClipEndMs - segment.ClipStartMs);
                var entryLocal = Math.Max(0.0, segment.EntryCueMs - segment.ClipStartMs);
                Log(
                    $"  [{segmentIndex + 1}/{playlist.Segments.Count}] {segment.Name}"
                    + $"  len={durationMs:0}ms"
                    + (entryLocal > 0.5 ? $"  entry=+{entryLocal:0}ms" : string.Empty)
                    + $"  T{segment.TempoBpm:0.##}-{segment.TimeSignatureUpper}/{segment.TimeSignatureLower}"
                    + $"  ({string.Join(", ", flags)})");

                foreach (var track in segment.Tracks)
                {
                    var key = TrackSliceKey(segment.Name, track.Name);
                    if (!segmentMedia.TryGetValue(key, out var media))
                    {
                        Log($"    Track {track.Name}: (media missing)");
                        continue;
                    }

                    var beginMs = media.SampleRate == 0
                        ? 0.0
                        : media.SourceStartSample * 1000.0 / media.SampleRate;
                    var endMs = media.SampleRate == 0
                        ? 0.0
                        : media.SourceEndSample * 1000.0 / media.SampleRate;
                    Log(
                        $"    Track {track.Name}: {Path.GetFileName(media.WavPath)}"
                        + $"  src=[{media.SourceStartSample} .. {media.SourceEndSample})"
                        + $"  ({beginMs:0.###} .. {endMs:0.###} ms)"
                        + (media.ApplyClipTrim
                            ? "  [copy+trim]"
                            : media.ReusedOriginal
                                ? "  [copy]"
                                : "  [slice]"));
                }
            }
        }

        var uniqueWavCount = segmentMedia.Values
            .Select(binding => binding.WavPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        Log($"{UiStrings.KeySlices} {uniqueWavCount}");
        Log(UiStrings.LogWwiseImportComplete);
        Log();
        return sb.ToString();
    }

    /// <summary>
    /// EXPORT 直前に Playlist / Segment / Track 構成をログへ出す。
    /// </summary>
    public static string FormatPlanSummary(WwiseMusicPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine(UiStrings.LogImportPlanHeader);
        sb.AppendLine(UiStrings.LogImportPlanPlaylists(plan.Playlists.Count, plan.ContainerName));
        foreach (var playlist in plan.Playlists)
        {
            sb.AppendLine(UiStrings.LogPlaylistSummary(playlist.Name, playlist.Segments.Count));
            for (var i = 0; i < playlist.Segments.Count; i++)
            {
                var segment = playlist.Segments[i];
                var flags = new List<string>();
                if (segment.LoopInfinite)
                {
                    flags.Add("loop=∞");
                }

                if (segment.EntryCueMs > segment.ClipStartMs)
                {
                    flags.Add("anacrusis");
                }

                if (segment.ExitCueMs < segment.ClipEndMs)
                {
                    flags.Add("exit-tail");
                }

                flags.Add($"tracks={segment.Tracks.Count}");
                var durationMs = Math.Max(0.0, segment.ClipEndMs - segment.ClipStartMs);
                var entryLocal = Math.Max(0.0, segment.EntryCueMs - segment.ClipStartMs);
                sb.AppendLine(
                    $"  [{i + 1}/{playlist.Segments.Count}] {segment.Name}"
                    + $"  len={durationMs:0}ms"
                    + (entryLocal > 0.5 ? $"  entry=+{entryLocal:0}ms" : string.Empty)
                    + $"  ({string.Join(", ", flags)})");
                foreach (var track in segment.Tracks)
                {
                    sb.AppendLine(
                        $"    Track {track.Name}"
                        + $"  clip=[{track.ClipStartMs:0.###} .. {track.ClipEndMs:0.###}] ms"
                        + $"  samples=[{track.AbsoluteStartSample} .. {track.AbsoluteEndSample})");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }

    private static async Task CallObjectSetAsync(
        WaapiHttpClient client,
        Dictionary<string, object?> setArgs,
        Dictionary<string, object> returnOptions,
        CancellationToken cancellationToken)
    {
        _ = await client.CallAsync(
                "ak.wwise.core.object.set",
                setArgs,
                returnOptions,
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Any → Playlist トランジションの Destination 参照を setReference で結ぶ。
    /// 作成時の @DestinationContextObject で足りる場合もあるが、Reference は空のままのことがある。
    /// </summary>
    private static async Task BindTransitionDestinationsAsync(
        WaapiHttpClient client,
        string containerPath,
        WwiseMusicPlan plan,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var transitionsByName = await QueryMusicTransitionsByNameAsync(
                client,
                containerPath,
                cancellationToken)
            .ConfigureAwait(false);

        foreach (var playlist in plan.Playlists)
        {
            if (!transitionsByName.TryGetValue(playlist.Name, out var transitionIds)
                || transitionIds.Count == 0)
            {
                // 規則名で見つからなくても、object.set 時の Destination 参照が効いていることがある。
                // 誤検知のエラーログは出さない。
                continue;
            }

            var playlistPath = $"{containerPath}\\{playlist.Name}";
            var playlistId = await TryGetObjectIdAsync(client, playlistPath, cancellationToken)
                .ConfigureAwait(false);
            var destination = !string.IsNullOrEmpty(playlistId) ? playlistId : playlistPath;

            foreach (var transitionId in transitionIds)
            {
                await client.CallAsync(
                        "ak.wwise.core.object.setProperty",
                        new Dictionary<string, object?>
                        {
                            ["object"] = transitionId,
                            ["property"] = "DestinationContextType",
                            ["value"] = 2,
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                await client.CallAsync(
                        "ak.wwise.core.object.setReference",
                        new Dictionary<string, object?>
                        {
                            ["object"] = transitionId,
                            ["reference"] = "DestinationContextObject",
                            ["value"] = destination,
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            log(UiStrings.LogTransitionDestinationSet(playlist.Name));
        }
    }

    private static async Task<Dictionary<string, List<string>>> QueryMusicTransitionsByNameAsync(
        WaapiHttpClient client,
        string containerPath,
        CancellationToken cancellationToken)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var escaped = containerPath.Replace("\"", "\\\"", StringComparison.Ordinal);
        var result = await client.CallAsync(
                "ak.wwise.core.object.get",
                new Dictionary<string, object?>
                {
                    ["waql"] = $"$ \"{escaped}\" select descendants where type = \"MusicTransition\"",
                },
                new Dictionary<string, object?>
                {
                    ["return"] = new[] { "id", "name", "type" },
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.TryGetProperty("return", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idEl)
                || !item.TryGetProperty("name", out var nameEl))
            {
                continue;
            }

            var id = idEl.GetString();
            var name = nameEl.GetString();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
            {
                continue;
            }

            // TransitionRoot フォルダ（空名）と既定 Any→Any（Transition）は対象外。
            if (name.Length == 0
                || string.Equals(
                    name,
                    WaapiMusicTransitionDefaults.DefaultAnyToAnyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!map.TryGetValue(name, out var list))
            {
                list = [];
                map[name] = list;
            }

            list.Add(id);
        }

        return map;
    }

    private static async Task<string?> TryGetObjectIdAsync(
        WaapiHttpClient client,
        string objectPath,
        CancellationToken cancellationToken)
    {
        var escaped = objectPath.Replace("\"", "\\\"", StringComparison.Ordinal);
        try
        {
            var result = await client.CallAsync(
                    "ak.wwise.core.object.get",
                    new Dictionary<string, object?>
                    {
                        ["waql"] = $"$ \"{escaped}\"",
                    },
                    new Dictionary<string, object?>
                    {
                        ["return"] = new[] { "id", "path" },
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            if (!result.TryGetProperty("return", out var arr)
                || arr.ValueKind != JsonValueKind.Array
                || arr.GetArrayLength() == 0)
            {
                return null;
            }

            var first = arr[0];
            return first.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        }
        catch (WaapiException)
        {
            return null;
        }
    }

    /// <summary>
    /// 各トラックのメディアを用意する。返り値は TrackSliceKey → バインディング。
    /// 複数波形で焼き込み不要なら元 WAV を outputDirectory へコピーして再利用する。
    /// </summary>
    private static Dictionary<string, TrackMediaBinding> SliceSegmentWavs(
        WwiseMusicPlan plan,
        string sourceWavPath,
        string outputDirectory,
        IReadOnlyList<WaveformOutputPart> outputParts,
        uint sampleRate,
        ushort blockAlign,
        WavFileInfo wavInfo,
        IReadOnlyDictionary<int, float>? partGains,
        IReadOnlyList<RegionEdgeFade>? regionEdgeFades,
        Action<string> log)
    {
        Directory.CreateDirectory(outputDirectory);
        log($"{UiStrings.KeyOutput} {outputDirectory}");

        var partByPath = outputParts.ToDictionary(
            part => Path.GetFullPath(Path.Combine(outputDirectory, part.FileName)),
            part => part,
            StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, TrackMediaBinding>(StringComparer.OrdinalIgnoreCase);
        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var loggedReusePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var playlist in plan.Playlists)
        {
            foreach (var segment in playlist.Segments)
            {
                if (segment.Tracks.Count == 0)
                {
                    throw new InvalidOperationException(UiStrings.ErrNoTracks(segment.Name));
                }

                foreach (var track in segment.Tracks)
                {
                    var partPath = Path.GetFullPath(track.SourceWavPath);
                    if (!partByPath.TryGetValue(partPath, out var part))
                    {
                        throw new InvalidOperationException(
                            UiStrings.ErrCannotResolveOutputPart(track.SourceWavPath));
                    }

                    var startSample = track.AbsoluteStartSample;
                    var endSample = track.AbsoluteEndSample;
                    if (endSample <= startSample)
                    {
                        startSample = checked(
                            part.StartSampleOffset + MsToSample(track.ClipStartMs, sampleRate));
                        endSample = checked(
                            part.StartSampleOffset + MsToSample(track.ClipEndMs, sampleRate));
                    }

                    if (endSample <= startSample)
                    {
                        throw new InvalidOperationException(
                            UiStrings.ErrTrackRangeEmpty(
                                segment.Name,
                                track.Name,
                                $"{track.ClipStartMs}..{track.ClipEndMs} ms"));
                    }

                    var gain = 1f;
                    if (partGains is not null
                        && partGains.TryGetValue(part.Number, out var partGain))
                    {
                        gain = partGain;
                    }

                    var trackKey = TrackSliceKey(segment.Name, track.Name);
                    var sliceSourcePath = part.ResolveSourcePath(sourceWavPath);
                    var localStart = part.VirtualToLocal(startSample);
                    var localEnd = part.VirtualToLocal(endSample);
                    var sliceInfo = part.HasDedicatedSource
                        ? WavFileInfo.Read(sliceSourcePath)
                        : wavInfo;
                    var sliceBlockAlign = sliceInfo.BlockAlign != 0
                        ? sliceInfo.BlockAlign
                        : blockAlign;
                    var localFades = RemapFadesToLocal(regionEdgeFades, part);

                    // 複数波形: 焼き込み不要なら元 WAV を outputDirectory へコピーして共有（2 本のまま）。
                    // セグメントごとの範囲は MusicClip Begin/End Offset で合わせる（手動作業の自動化）。
                    if (CanReuseDedicatedSourceWav(
                            part,
                            sliceSourcePath,
                            localStart,
                            localEnd,
                            gain,
                            localFades,
                            sliceInfo))
                    {
                        var desiredFileName = string.IsNullOrWhiteSpace(part.FileName)
                            ? $"{track.Name}.wav"
                            : part.FileName;
                        if (!desiredFileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                        {
                            desiredFileName += ".wav";
                        }

                        var dest = Path.GetFullPath(Path.Combine(outputDirectory, desiredFileName));
                        var sourceFull = Path.GetFullPath(sliceSourcePath);
                        if (!string.Equals(sourceFull, dest, StringComparison.OrdinalIgnoreCase))
                        {
                            if (!File.Exists(dest)
                                || new FileInfo(dest).Length != new FileInfo(sourceFull).Length
                                || File.GetLastWriteTimeUtc(dest) != File.GetLastWriteTimeUtc(sourceFull))
                            {
                                File.Copy(sourceFull, dest, overwrite: true);
                            }
                        }

                        var needsTrim = localStart != part.ResolveLocalStart()
                            || localEnd != part.ResolveLocalEnd();
                        var effectiveRate = sliceInfo.SampleRate != 0
                            ? sliceInfo.SampleRate
                            : sampleRate;
                        map[trackKey] = new TrackMediaBinding(
                            dest,
                            localStart,
                            localEnd,
                            sliceInfo.FrameCount,
                            effectiveRate,
                            ApplyClipTrim: needsTrim,
                            ReusedOriginal: true);
                        if (loggedReusePaths.Add(dest))
                        {
                            log(UiStrings.LogWavSourceReused(Path.GetFileName(dest)));
                        }

                        log(
                            UiStrings.LogTrackMediaBinding(
                                segment.Name,
                                track.Name,
                                Path.GetFileName(dest),
                                localStart,
                                localEnd,
                                reusedOriginal: true,
                                applyClipTrim: needsTrim));
                        usedFileNames.Add(Path.GetFileName(dest));
                        continue;
                    }

                    // フェード／ゲイン焼き込み時のみ切り出し（仕様上やむを得ない例外）。
                    var desiredSliceName = string.IsNullOrWhiteSpace(part.FileName)
                        ? $"{track.Name}.wav"
                        : part.FileName;
                    if (!desiredSliceName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        desiredSliceName += ".wav";
                    }

                    var fileName = UniqueSliceFileName(desiredSliceName, usedFileNames);
                    var destSlice = Path.Combine(outputDirectory, fileName);
                    WriteSegmentSafely(
                        sliceSourcePath,
                        destSlice,
                        localStart,
                        localEnd,
                        sliceBlockAlign,
                        gain,
                        sliceInfo,
                        localFades);
                    var writtenInfo = WavFileInfo.Read(destSlice);
                    map[trackKey] = new TrackMediaBinding(
                        Path.GetFullPath(destSlice),
                        0,
                        writtenInfo.FrameCount,
                        writtenInfo.FrameCount,
                        writtenInfo.SampleRate != 0 ? writtenInfo.SampleRate : sampleRate,
                        ApplyClipTrim: false,
                        ReusedOriginal: false);
                    log(Math.Abs(gain - 1f) < 0.000001f
                        ? UiStrings.LogWavSliceWritten(fileName)
                        : UiStrings.LogWavSliceWrittenWithGain(fileName, gain));
                    log(
                        UiStrings.LogTrackMediaBinding(
                            segment.Name,
                            track.Name,
                            fileName,
                            localStart,
                            localEnd,
                            reusedOriginal: false,
                            applyClipTrim: false));
                }
            }
        }

        return map;
    }

    /// <summary>
    /// 複数波形の専用ソースで、焼き込みなしに元 WAV を共有できるか。
    /// 部分範囲は MusicClip トリムで合わせる前提（手動と同じ 2 波形構成）。
    /// </summary>
    private static bool CanReuseDedicatedSourceWav(
        WaveformOutputPart part,
        string sliceSourcePath,
        long localStart,
        long localEnd,
        float gain,
        IReadOnlyList<RegionEdgeFade>? localFades,
        WavFileInfo sliceInfo)
    {
        if (!part.HasDedicatedSource || sliceInfo.FrameCount <= 0)
        {
            return false;
        }

        var localMin = part.ResolveLocalStart();
        var localMax = part.ResolveLocalEnd();
        if (localStart < localMin || localEnd > localMax || localEnd <= localStart)
        {
            return false;
        }

        if (Math.Abs(gain - 1f) >= 0.000001f)
        {
            return false;
        }

        if (localFades is { Count: > 0 }
            && RegionEdgeFade.OverlapsRange(localStart, localEnd, localFades))
        {
            return false;
        }

        return File.Exists(sliceSourcePath);
    }

    private readonly record struct TrackMediaBinding(
        string WavPath,
        long SourceStartSample,
        long SourceEndSample,
        long SourceFrameCount,
        uint SampleRate,
        bool ApplyClipTrim,
        bool ReusedOriginal);

    private static IReadOnlyList<RegionEdgeFade>? RemapFadesToLocal(
        IReadOnlyList<RegionEdgeFade>? regionEdgeFades,
        WaveformOutputPart part)
    {
        if (regionEdgeFades is null || regionEdgeFades.Count == 0 || !part.HasDedicatedSource)
        {
            return regionEdgeFades;
        }

        var offset = part.StartSampleOffset - part.LocalStartSample;
        if (offset == 0)
        {
            return regionEdgeFades;
        }

        var remapped = new List<RegionEdgeFade>(regionEdgeFades.Count);
        foreach (var fade in regionEdgeFades)
        {
            remapped.Add(fade with
            {
                InSample = fade.InSample - offset,
                OutSample = fade.OutSample - offset,
                FadeInEndSample = fade.FadeInEndSample is long fadeIn
                    ? fadeIn - offset
                    : null,
                FadeOutStartSample = fade.FadeOutStartSample is long fadeOut
                    ? fadeOut - offset
                    : null,
            });
        }

        return remapped;
    }

    private static void WriteSegmentSafely(
        string sourcePath,
        string destinationPath,
        long startSample,
        long endSample,
        ushort blockAlign,
        float gain,
        WavFileInfo wavInfo,
        IReadOnlyList<RegionEdgeFade>? regionEdgeFades)
    {
        if (!string.Equals(
                Path.GetFullPath(sourcePath),
                Path.GetFullPath(destinationPath),
                StringComparison.OrdinalIgnoreCase))
        {
            WavSegmentWriter.WriteSegment(
                sourcePath,
                destinationPath,
                startSample,
                endSample,
                blockAlign,
                gain,
                wavInfo,
                regionEdgeFades);
            return;
        }

        var temporaryPath = destinationPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            WavSegmentWriter.WriteSegment(
                sourcePath,
                temporaryPath,
                startSample,
                endSample,
                blockAlign,
                gain,
                wavInfo,
                regionEdgeFades);
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string TrackSliceKey(string segmentName, string trackName) =>
        segmentName + "\u001f" + trackName;

    private static string UniqueSliceFileName(string desired, HashSet<string> used)
    {
        var name = desired;
        var stem = Path.GetFileNameWithoutExtension(desired);
        var ext = Path.GetExtension(desired);
        var suffix = 2;
        while (!used.Add(name))
        {
            name = $"{stem}_{suffix++}{ext}";
        }

        return name;
    }

    private static long MsToSample(double ms, uint sampleRate) =>
        (long)Math.Round(ms * sampleRate / 1000.0, MidpointRounding.AwayFromZero);

    private static Dictionary<string, object?> BuildStateGroupSetArgs(
        WwiseMusicPlan plan,
        WwiseImportSettings importSettings)
    {
        var stateChildren = plan.Playlists
            .Select(p => (object)new Dictionary<string, object?>
            {
                ["type"] = "State",
                ["name"] = p.Name,
            })
            .ToList();

        return new Dictionary<string, object?>
        {
            ["objects"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["object"] = importSettings.StateGroupParentPath.TrimEnd('\\'),
                    ["children"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "StateGroup",
                            ["name"] = plan.ContainerName,
                            ["children"] = stateChildren,
                        },
                    },
                },
            },
            ["onNameConflict"] = "merge",
            ["listMode"] = "replaceAll",
        };
    }

    /// <summary>
    /// Music Switch Container 本体（Playlist 子は空、State 割当は後段）。
    /// children を replaceAll で空にすることで、再 EXPORT 時の古い Playlist を落とす。
    /// </summary>
    private static Dictionary<string, object?> BuildMusicSwitchShellSetArgs(
        WwiseMusicPlan plan,
        string parentPath,
        string stateGroupPath) =>
        new()
        {
            ["objects"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["object"] = parentPath,
                    ["children"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "MusicSwitchContainer",
                            ["name"] = plan.ContainerName,
                            ["@Arguments"] = new[] { stateGroupPath },
                            ["children"] = Array.Empty<object>(),
                        },
                    },
                },
            },
            ["onNameConflict"] = "merge",
            ["listMode"] = "replaceAll",
        };

    /// <summary>
    /// Playlist 作成後に State→Playlist 割当を結ぶ。
    /// </summary>
    private static Dictionary<string, object?> BuildMusicSwitchEntriesSetArgs(
        WwiseMusicPlan plan,
        string containerPath,
        string stateGroupPath)
    {
        var entries = plan.Playlists
            .Select(p => (object)new Dictionary<string, object?>
            {
                ["type"] = "MultiSwitchEntry",
                // 再 EXPORT 時に merge できるよう、Playlist 名で安定させる。
                ["name"] = p.Name,
                ["@EntryPath"] = new[] { $"{stateGroupPath}\\{p.Name}" },
                ["@AudioNode"] = $"{containerPath}\\{p.Name}",
            })
            .ToList();

        return new Dictionary<string, object?>
        {
            ["objects"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["object"] = containerPath,
                    ["@Entries"] = entries,
                },
            },
            ["onNameConflict"] = "merge",
            ["listMode"] = "replaceAll",
        };
    }

    /// <summary>
    /// Playlist 作成後にトランジション（Any→Any + Any→各 Playlist）を結ぶ。
    /// DestinationContextObject は実在する Playlist パスを参照する必要がある。
    /// </summary>
    private static Dictionary<string, object?> BuildMusicSwitchTransitionsSetArgs(
        WwiseMusicPlan plan,
        string containerPath) =>
        new()
        {
            ["objects"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["object"] = containerPath,
                    ["@TransitionRoot"] = WaapiMusicTransitionDefaults.BuildTransitionRoot(
                        containerPath,
                        plan.Playlists),
                },
            },
            ["onNameConflict"] = "merge",
            ["listMode"] = "replaceAll",
        };

    private static Dictionary<string, object?> BuildPlaylistAppendSetArgs(
        WwiseMusicPlan plan,
        string containerPath,
        WwisePlaylistPlan playlist,
        IReadOnlyDictionary<string, TrackMediaBinding> segmentMedia,
        WwiseImportSettings importSettings,
        bool applyAutoVolume,
        AutoVolumeTarget autoVolumeTarget,
        IReadOnlyDictionary<int, float>? partGains,
        Action<string> log) =>
        new()
        {
            ["objects"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["object"] = containerPath,
                    ["children"] = new object[]
                    {
                        BuildPlaylistDef(
                            plan,
                            containerPath,
                            playlist,
                            segmentMedia,
                            importSettings,
                            isMultiPart: true,
                            applyAutoVolume,
                            autoVolumeTarget,
                            partGains,
                            log),
                    },
                },
            },
            ["onNameConflict"] = "merge",
            ["listMode"] = "append",
        };

    private static Dictionary<string, object?> BuildSinglePlaylistSetArgs(
        WwiseMusicPlan plan,
        string parentPath,
        IReadOnlyDictionary<string, TrackMediaBinding> segmentMedia,
        WwiseImportSettings importSettings,
        bool applyAutoVolume,
        AutoVolumeTarget autoVolumeTarget,
        IReadOnlyDictionary<int, float>? partGains,
        Action<string> log)
    {
        var containerPath = $"{parentPath}\\{plan.ContainerName}";
        return new Dictionary<string, object?>
        {
            ["objects"] = new object[]
            {
                new Dictionary<string, object?>
                {
                    ["object"] = parentPath,
                    ["children"] = new object[]
                    {
                        BuildPlaylistDef(
                            plan,
                            containerPath,
                            plan.Playlists[0],
                            segmentMedia,
                            importSettings,
                            isMultiPart: false,
                            applyAutoVolume,
                            autoVolumeTarget,
                            partGains,
                            log),
                    },
                },
            },
            ["onNameConflict"] = "merge",
            ["listMode"] = "replaceAll",
        };
    }

    private static Dictionary<string, object?> BuildPlaylistDef(
        WwiseMusicPlan plan,
        string containerPath,
        WwisePlaylistPlan playlist,
        IReadOnlyDictionary<string, TrackMediaBinding> segmentMedia,
        WwiseImportSettings importSettings,
        bool isMultiPart,
        bool applyAutoVolume,
        AutoVolumeTarget autoVolumeTarget,
        IReadOnlyDictionary<int, float>? partGains,
        Action<string> log)
    {
        var streamEnabled = importSettings.StreamEnabled;
        var lookAheadMs = importSettings.LookAheadMs;
        var prefetchLengthMs = importSettings.PrefetchLengthMs;
        var playlistPath = isMultiPart
            ? $"{containerPath}\\{playlist.Name}"
            : containerPath;

        var segmentDefs = new List<object>();
        var itemDefs = new List<object>();
        for (var i = 0; i < playlist.Segments.Count; i++)
        {
            var segment = playlist.Segments[i];
            segmentDefs.Add(BuildSegmentDef(
                segment,
                segmentMedia,
                isFirstSegment: i == 0,
                streamEnabled,
                lookAheadMs,
                prefetchLengthMs));
            itemDefs.Add(new Dictionary<string, object?>
            {
                ["type"] = "MusicPlaylistItem",
                ["name"] = string.Empty,
                ["@PlaylistItemType"] = 1,
                ["@LoopCount"] = segment.LoopInfinite ? 0 : 1,
                ["@Segment"] = $"{playlistPath}\\{segment.Name}",
            });
        }

        var name = isMultiPart ? playlist.Name : plan.ContainerName;
        var def = new Dictionary<string, object?>
        {
            ["type"] = "MusicPlaylistContainer",
            ["name"] = name,
            ["children"] = segmentDefs,
            ["@PlaylistRoot"] = new Dictionary<string, object?>
            {
                ["type"] = "MusicPlaylistItem",
                ["name"] = string.Empty,
                ["@PlaylistItemType"] = 0,
                ["@PlayMode"] = 0,
                ["@LoopCount"] = 1,
                ["children"] = itemDefs,
            },
        };

        if (applyAutoVolume && partGains is not null)
        {
            var compensationDb = ResolveAutoVolumeCompensationDb(playlist, partGains, log);
            if (autoVolumeTarget == AutoVolumeTarget.VoiceVolume)
            {
                def["@Volume"] = compensationDb;
                def["@MakeUpGain"] = 0f;
                log(
                    $"Auto Volume: playlist {name} → Voice Volume {compensationDb:0.##} dB"
                    + " (Make-Up Gain = 0)");
            }
            else
            {
                def["@MakeUpGain"] = compensationDb;
                def["@Volume"] = 0f;
                log(
                    $"Auto Volume: playlist {name} → Make-Up Gain {compensationDb:0.##} dB"
                    + " (Voice Volume = 0)");
            }
        }

        return def;
    }

    /// <summary>
    /// Playlist 構成パートの線形ゲインから補償 dB を求める。
    /// 複数パートでゲインが食い違う場合は先頭メンバーを使い警告する。
    /// </summary>
    private static float ResolveAutoVolumeCompensationDb(
        WwisePlaylistPlan playlist,
        IReadOnlyDictionary<int, float> partGains,
        Action<string> log)
    {
        if (playlist.SourcePartNumbers.Count == 0)
        {
            return 0f;
        }

        float? firstGain = null;
        var mismatched = false;
        foreach (var partNumber in playlist.SourcePartNumbers)
        {
            if (!partGains.TryGetValue(partNumber, out var gain))
            {
                continue;
            }

            if (firstGain is null)
            {
                firstGain = gain;
                continue;
            }

            if (Math.Abs(gain - firstGain.Value) > 1e-4f)
            {
                mismatched = true;
            }
        }

        if (firstGain is null)
        {
            return 0f;
        }

        if (mismatched)
        {
            log(UiStrings.LogAutoVolumeGainMismatch(playlist.Name, playlist.SourcePartNumbers[0]));
        }

        return CompensationDb(firstGain.Value);
    }

    private static float CompensationDb(float linearGain) =>
        linearGain <= 0f || Math.Abs(linearGain - 1f) < 1e-6f
            ? 0f
            : (float)(-20.0 * Math.Log10(linearGain));

    private static Dictionary<string, object?> BuildSegmentDef(
        WwiseSegmentPlan segment,
        IReadOnlyDictionary<string, TrackMediaBinding> trackMedia,
        bool isFirstSegment,
        bool streamEnabled,
        int lookAheadMs,
        int prefetchLengthMs)
    {
        // 切り出し WAV の先頭がセグメント 0。Cue は相対時刻。
        // 元 WAV 再利用時は作成後に MusicClip トリム＋WWU の PlayAt パッチで範囲を合わせる。
        var origin = segment.ClipStartMs;
        var exitLocal = Math.Max(0.0, segment.ExitCueMs - origin);
        var endLocal = Math.Max(exitLocal, segment.ClipEndMs - origin);

        var trackDefs = new List<object>();
        for (var t = 0; t < segment.Tracks.Count; t++)
        {
            var track = segment.Tracks[t];
            var key = TrackSliceKey(segment.Name, track.Name);
            if (!trackMedia.TryGetValue(key, out var media))
            {
                throw new InvalidOperationException(
                    UiStrings.ErrSlicedWavMissing(segment.Name, track.Name));
            }

            // 先頭セグメント内の全トラック（グループ化レイヤー含む）に
            // Zero latency ＋ Prefetch を付け、2 番目以降は Look-ahead のみ。
            var zeroLatency = streamEnabled && isFirstSegment;
            var trackProps = new Dictionary<string, object?>
            {
                ["type"] = "MusicTrack",
                ["name"] = track.Name,
                ["@IsStreamingEnabled"] = streamEnabled,
                ["import"] = new Dictionary<string, object?>
                {
                    ["files"] = new object[]
                    {
                        new Dictionary<string, object?> { ["audioFile"] = media.WavPath },
                    },
                },
            };
            if (streamEnabled)
            {
                trackProps["@IsZeroLatency"] = zeroLatency;
                trackProps["@LookAheadTime"] = zeroLatency ? 0 : lookAheadMs;
                if (zeroLatency)
                {
                    trackProps["@PreFetchLength"] = prefetchLengthMs;
                }
            }

            trackDefs.Add(trackProps);
        }

        // Entry/Exit/Custom Cue は作成後に listMode=replaceAll で一括設定する
        // （ここへ @Cues を載せる・既定 Cue と二重になる）。
        return new Dictionary<string, object?>
        {
            ["type"] = "MusicSegment",
            ["name"] = segment.Name,
            ["@OverrideClockSettings"] = true,
            ["@Tempo"] = segment.TempoBpm,
            ["@TimeSignatureUpper"] = segment.TimeSignatureUpper,
            ["@TimeSignatureLower"] = segment.TimeSignatureLower,
            ["@EndPosition"] = endLocal,
            ["children"] = trackDefs,
        };
    }

    /// <summary>
    /// 共有 WAV のセグメント範囲を MusicClip Begin/End Offset（ミリ秒）で合わせる。
    /// MusicClip は Track の descendants に出ないことがあるため、from type MusicClip で探す。
    /// 頭トリムしたクリップを 0 位置へ寄せる PlayAt（負値）は WAAPI の制約
    /// [0, 1e10] で設定できないため、必要なパッチ一覧を返し WWU 直接更新へ回す。
    /// </summary>
    private static async Task<List<MusicClipPlayAtFix>> ApplyMusicClipTrimsAsync(
        WaapiHttpClient client,
        WwiseMusicPlan plan,
        string musicRootPath,
        IReadOnlyDictionary<string, TrackMediaBinding> segmentMedia,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var playAtFixes = new List<MusicClipPlayAtFix>();
        var anyTrim = segmentMedia.Values.Any(m => m.ApplyClipTrim);
        if (!anyTrim)
        {
            return playAtFixes;
        }

        // Track 配下の descendants では取れないことがあるため、プロジェクト全体から取る。
        var allClips = await QueryAllMusicClipsAsync(client, cancellationToken)
            .ConfigureAwait(false);
        log(UiStrings.LogMusicClipCatalog(allClips.Count));

        foreach (var playlist in plan.Playlists)
        {
            var playlistPath = plan.IsMultiPart
                ? $"{musicRootPath}\\{playlist.Name}"
                : musicRootPath;
            foreach (var segment in playlist.Segments)
            {
                var segmentPath = $"{playlistPath}\\{segment.Name}";
                foreach (var track in segment.Tracks)
                {
                    var key = TrackSliceKey(segment.Name, track.Name);
                    if (!segmentMedia.TryGetValue(key, out var media) || !media.ApplyClipTrim)
                    {
                        continue;
                    }

                    if (media.SampleRate == 0 || media.SourceFrameCount <= 0)
                    {
                        throw new InvalidOperationException(
                            UiStrings.ErrMusicClipTrimMissingRate(track.Name, segment.Name));
                    }

                    var beginMs = media.SourceStartSample * 1000.0 / media.SampleRate;
                    // MusicClip のプロパティ規約（WWU 実測）:
                    //   BeginTrimOffset / EndTrimOffset : ソース内の開始／終了位置（絶対ミリ秒）
                    //   PlayAt : ソース先頭のタイムライン位置。手動編集同様、トリム後の内容を
                    //            0 に詰めるには -Begin（負値）が必要だが WAAPI では書けないので
                    //            後段の WWU 直接パッチで設定する。
                    var endMs = media.SourceEndSample * 1000.0 / media.SampleRate;
                    var trackPath = $"{segmentPath}\\{track.Name}";
                    var clipIds = FindMusicClipsForTrack(
                        allClips,
                        trackPath,
                        Path.GetFileNameWithoutExtension(media.WavPath));
                    if (clipIds.Count == 0)
                    {
                        // パス推定でもう一度直接 get を試す。
                        var guessed = await TryGetObjectIdAsync(
                                client,
                                $"{trackPath}\\{Path.GetFileNameWithoutExtension(media.WavPath)}",
                                cancellationToken)
                            .ConfigureAwait(false);
                        if (!string.IsNullOrEmpty(guessed))
                        {
                            clipIds.Add(guessed);
                        }
                    }

                    if (clipIds.Count == 0)
                    {
                        throw new InvalidOperationException(
                            UiStrings.ErrMusicClipNotFound(trackPath));
                    }

                    if (clipIds.Count > 1)
                    {
                        throw new InvalidOperationException(
                            UiStrings.ErrMusicClipAmbiguous(trackPath, clipIds.Count));
                    }

                    var clipId = clipIds[0];
                    await SetClipPropertyAsync(
                            client, clipId, "BeginTrimOffset", beginMs, cancellationToken)
                        .ConfigureAwait(false);
                    await SetClipPropertyAsync(
                            client, clipId, "EndTrimOffset", endMs, cancellationToken)
                        .ConfigureAwait(false);
                    if (beginMs > 0.0005)
                    {
                        playAtFixes.Add(new MusicClipPlayAtFix(clipId, -beginMs));
                    }

                    log(
                        UiStrings.LogMusicClipTrimApplied(
                            track.Name,
                            segment.Name,
                            beginMs,
                            endMs));
                }
            }
        }

        return playAtFixes;
    }

    private readonly record struct MusicClipPlayAtFix(string ClipId, double PlayAtMs);

    /// <summary>
    /// 負の PlayAt を WWU 直接編集で設定する。
    /// WAAPI の PlayAt は制約 [0, 1e10] のため、手動編集と同じ結果
    /// （頭トリムしたクリップをタイムライン 0 に配置）を API 経由では作れない。
    /// 手順: project.save → 対象 WWU 特定 → project.close → XML パッチ → project.open。
    /// </summary>
    private static async Task ApplyPlayAtFixesViaWorkUnitAsync(
        WaapiHttpClient client,
        IReadOnlyList<MusicClipPlayAtFix> fixes,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        if (fixes.Count == 0)
        {
            return;
        }

        log(UiStrings.LogPlayAtPatchStart(fixes.Count));

        // クリップの属する WWU ファイルとプロジェクト（.wproj）パスを先に取得する。
        var clipFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fix in fixes)
        {
            var filePath = await QuerySingleReturnStringAsync(
                    client,
                    $"$ \"{fix.ClipId}\"",
                    "filePath",
                    cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                throw new InvalidOperationException(
                    UiStrings.ErrPlayAtWorkUnitNotFound(fix.ClipId));
            }

            clipFiles[fix.ClipId] = filePath;
        }

        var projectPath = await QuerySingleReturnStringAsync(
                client,
                "$ from type Project",
                "filePath",
                cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
        {
            throw new InvalidOperationException(UiStrings.ErrPlayAtProjectPathUnknown);
        }

        // 未保存の変更（今回の作成分を含む）を WWU へ書き出してから閉じる。
        await client.CallAsync(
                "ak.wwise.core.project.save",
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        // ここから先は途中中断するとプロジェクトが閉じたままになるため、
        // キャンセル要求があっても再オープンまで完走させる。
        await client.CallAsync(
                "ak.wwise.ui.project.close",
                new Dictionary<string, object?> { ["bypassSave"] = true },
                cancellationToken: CancellationToken.None)
            .ConfigureAwait(false);

        try
        {
            // close は非同期に進行する。Wwise がまだ WWU を掴んでいる間に
            // 書き換えると、パッチが失われたり Wwise 本体が落ちることがある。
            // プロジェクトが完全に閉じるまで待ってからパッチする。
            await WaitForProjectClosedAsync(client).ConfigureAwait(false);

            foreach (var group in fixes.GroupBy(f => clipFiles[f.ClipId], StringComparer.OrdinalIgnoreCase))
            {
                PatchPlayAtInWorkUnitFile(group.Key, group.ToList(), log);
            }
        }
        finally
        {
            // 「Closing project in progress」ロックが残っていても解除までリトライする。
            log(UiStrings.LogPlayAtProjectReopen(Path.GetFileName(projectPath)));
            await CallWithLockRetryAsync(
                    client,
                    "ak.wwise.ui.project.open",
                    new Dictionary<string, object?>
                    {
                        ["path"] = projectPath,
                        ["bypassSave"] = true,
                    })
                .ConfigureAwait(false);
        }

        // open はロード完了前に返る。ロード中の WAAPI アクセスは既定値（PlayAt=0）を
        // 返したり Wwise を不安定にするため、ロード完了を待ってから検証する。
        await WaitForProjectLoadedAsync(client, projectPath).ConfigureAwait(false);

        // 書き込み結果を WAAPI で読み戻して照合する。
        // WWU フォーマットが将来変わってパッチが無効になった場合に、ここで確実に検出する。
        // ロード直後は既定値 0 が見えることがあるため、期待値に一致するまでリトライする。
        foreach (var fix in fixes)
        {
            double? actual = null;
            var verifyDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (true)
            {
                actual = await QueryClipPlayAtAsync(client, fix.ClipId)
                    .ConfigureAwait(false);
                if ((actual is not null && Math.Abs(actual.Value - fix.PlayAtMs) <= 0.01)
                    || DateTime.UtcNow >= verifyDeadline)
                {
                    break;
                }

                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }

            if (actual is null || Math.Abs(actual.Value - fix.PlayAtMs) > 0.01)
            {
                throw new InvalidOperationException(
                    UiStrings.ErrPlayAtVerifyFailed(fix.ClipId, fix.PlayAtMs, actual));
            }
        }

        log(UiStrings.LogPlayAtPatchDone(fixes.Count));
    }

    /// <summary>
    /// プロジェクトが完全に閉じるまで待つ。
    /// クローズ進行中は ak.wwise.locked、完了後は「プロジェクト未ロード」系エラーか空結果になる。
    /// </summary>
    private static async Task WaitForProjectClosedAsync(WaapiHttpClient client)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await client.CallAsync(
                        "ak.wwise.core.object.get",
                        new Dictionary<string, object?> { ["waql"] = "$ from type Project" },
                        new Dictionary<string, object?> { ["return"] = new[] { "id" } },
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (!result.TryGetProperty("return", out var arr)
                    || arr.ValueKind != JsonValueKind.Array
                    || arr.GetArrayLength() == 0)
                {
                    return;
                }
            }
            catch (WaapiException ex) when (
                ex.Message.Contains("ak.wwise.locked", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("exclusive lock", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("in progress", StringComparison.OrdinalIgnoreCase))
            {
                // クローズ進行中。待って再確認する。
            }
            catch (WaapiException)
            {
                // 「プロジェクトが読み込まれていない」等 → クローズ完了とみなす。
                return;
            }

            await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 再オープンしたプロジェクトのロード完了（クエリで .wproj パスが返る状態）まで待つ。
    /// タイムアウト時はそのまま返し、後段の検証で失敗として検出する。
    /// </summary>
    private static async Task WaitForProjectLoadedAsync(WaapiHttpClient client, string projectPath)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var result = await client.CallAsync(
                        "ak.wwise.core.object.get",
                        new Dictionary<string, object?> { ["waql"] = "$ from type Project" },
                        new Dictionary<string, object?> { ["return"] = new[] { "filePath" } },
                        CancellationToken.None)
                    .ConfigureAwait(false);
                if (result.TryGetProperty("return", out var arr)
                    && arr.ValueKind == JsonValueKind.Array
                    && arr.GetArrayLength() > 0
                    && arr[0].TryGetProperty("filePath", out var pathEl)
                    && string.Equals(
                        pathEl.GetString(),
                        projectPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }
            catch (WaapiException)
            {
                // ロック中／ロード中。待って再確認する。
            }

            await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Wwise が排他ロック中（ak.wwise.locked、プロジェクトのクローズ／ロード進行中）の間、
    /// 解除されるまで呼び出しをリトライする。
    /// </summary>
    private static async Task<JsonElement> CallWithLockRetryAsync(
        WaapiHttpClient client,
        string uri,
        object? args = null,
        object? options = null)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (true)
        {
            try
            {
                return await client.CallAsync(uri, args, options, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (WaapiException ex) when (
                DateTime.UtcNow < deadline
                && (ex.Message.Contains("ak.wwise.locked", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("exclusive lock", StringComparison.OrdinalIgnoreCase)))
            {
                await Task.Delay(500, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>再オープン後の MusicClip から PlayAt 実値を読み戻す。</summary>
    private static async Task<double?> QueryClipPlayAtAsync(
        WaapiHttpClient client,
        string clipId)
    {
        var result = await CallWithLockRetryAsync(
                client,
                "ak.wwise.core.object.get",
                new Dictionary<string, object?> { ["waql"] = $"$ \"{clipId}\"" },
                new Dictionary<string, object?> { ["return"] = new[] { "id", "@PlayAt" } })
            .ConfigureAwait(false);
        if (!result.TryGetProperty("return", out var arr)
            || arr.ValueKind != JsonValueKind.Array
            || arr.GetArrayLength() == 0)
        {
            return null;
        }

        return arr[0].TryGetProperty("@PlayAt", out var el)
               && el.ValueKind == JsonValueKind.Number
            ? el.GetDouble()
            : null;
    }

    /// <summary>WWU（XML）内の MusicClip に PlayAt プロパティを直接書き込む。</summary>
    private static void PatchPlayAtInWorkUnitFile(
        string wwuPath,
        IReadOnlyList<MusicClipPlayAtFix> fixes,
        Action<string> log)
    {
        // Wwise のクローズ処理がファイルを掴んだまま残ることがあるため、
        // 排他で開けるようになるまで待ってから書き換える。
        WaitForExclusiveFileAccess(wwuPath);

        var doc = new System.Xml.XmlDocument { PreserveWhitespace = true };
        doc.Load(wwuPath);

        foreach (var fix in fixes)
        {
            var clipNode = doc.SelectSingleNode(
                $"//MusicClip[@ID='{fix.ClipId}']") as System.Xml.XmlElement;
            if (clipNode is null)
            {
                throw new InvalidOperationException(
                    UiStrings.ErrPlayAtClipXmlMissing(fix.ClipId, wwuPath));
            }

            var propertyList = clipNode.SelectSingleNode("PropertyList") as System.Xml.XmlElement;
            if (propertyList is null)
            {
                propertyList = doc.CreateElement("PropertyList");
                clipNode.PrependChild(propertyList);
            }

            var value = fix.PlayAtMs.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (propertyList.SelectSingleNode("Property[@Name='PlayAt']")
                is System.Xml.XmlElement existing)
            {
                existing.SetAttribute("Value", value);
            }
            else
            {
                var property = doc.CreateElement("Property");
                property.SetAttribute("Name", "PlayAt");
                property.SetAttribute("Type", "Real64");
                property.SetAttribute("Value", value);
                propertyList.AppendChild(property);
            }
        }

        doc.Save(wwuPath);
        log(UiStrings.LogPlayAtPatchFile(Path.GetFileName(wwuPath), fixes.Count));
    }

    /// <summary>指定ファイルを排他モードで開けるまで待つ（最大 30 秒）。</summary>
    private static void WaitForExclusiveFileAccess(string path)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.ReadWrite,
                    FileShare.None);
                return;
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(250);
            }
        }
    }

    /// <summary>WAQL で 1 件だけ取得し、指定の return フィールド（文字列）を返す。</summary>
    private static async Task<string?> QuerySingleReturnStringAsync(
        WaapiHttpClient client,
        string waql,
        string field,
        CancellationToken cancellationToken)
    {
        var result = await client.CallAsync(
                "ak.wwise.core.object.get",
                new Dictionary<string, object?> { ["waql"] = waql },
                new Dictionary<string, object?> { ["return"] = new[] { "id", field } },
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.TryGetProperty("return", out var arr)
            || arr.ValueKind != JsonValueKind.Array
            || arr.GetArrayLength() == 0)
        {
            return null;
        }

        return arr[0].TryGetProperty(field, out var el) ? el.GetString() : null;
    }

    private static async Task SetClipPropertyAsync(
        WaapiHttpClient client,
        string clipId,
        string property,
        double value,
        CancellationToken cancellationToken)
    {
        await client.CallAsync(
                "ak.wwise.core.object.setProperty",
                new Dictionary<string, object?>
                {
                    ["object"] = clipId,
                    ["property"] = property,
                    ["value"] = value,
                },
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<List<(string Id, string Path)>> QueryAllMusicClipsAsync(
        WaapiHttpClient client,
        CancellationToken cancellationToken)
    {
        var list = new List<(string Id, string Path)>();
        var result = await client.CallAsync(
                "ak.wwise.core.object.get",
                new Dictionary<string, object?>
                {
                    ["waql"] = "$ from type MusicClip",
                },
                new Dictionary<string, object?>
                {
                    ["return"] = new[] { "id", "name", "type", "path" },
                },
                cancellationToken)
            .ConfigureAwait(false);

        if (!result.TryGetProperty("return", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return list;
        }

        foreach (var item in arr.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idEl))
            {
                continue;
            }

            var id = idEl.GetString();
            if (string.IsNullOrEmpty(id))
            {
                continue;
            }

            var path = item.TryGetProperty("path", out var pathEl)
                ? pathEl.GetString() ?? string.Empty
                : string.Empty;
            list.Add((id, path));
        }

        return list;
    }

    private static List<string> FindMusicClipsForTrack(
        IReadOnlyList<(string Id, string Path)> allClips,
        string trackPath,
        string wavStem)
    {
        var matches = new List<string>();
        var trackFull = trackPath.TrimEnd('\\');
        var prefix = trackFull + "\\";

        // Track パス直下だけを対象にする。
        // 緩い Contains は bgm_st_0040_a が bgm_st_0040_a_a / _a_b にも
        // マッチして全クリップへトリムが上書きされるため使わない。
        foreach (var (id, path) in allClips)
        {
            if (string.IsNullOrEmpty(path))
            {
                continue;
            }

            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || string.Equals(path, trackFull, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(id);
            }
        }

        if (matches.Count > 0 || string.IsNullOrEmpty(wavStem))
        {
            return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // フォールバック: Track\\{wavStem} と一致するパスのみ。
        var exactClipPath = prefix + wavStem;
        foreach (var (id, path) in allClips)
        {
            if (string.Equals(path, exactClipPath, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(id);
            }
        }

        return matches.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static bool IsReservedCueName(string name) =>
        string.Equals(name, "Entry Cue", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Exit Cue", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Entry", StringComparison.OrdinalIgnoreCase)
        || string.Equals(name, "Exit", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 各 Music Segment の Cue 一覧を Entry / Exit / Custom だけに差し替える。
    /// listMode=replaceAll により既定 Cue との二重化を防ぐ。
    /// </summary>
    private static async Task ReplaceAllSegmentCuesAsync(
        WaapiHttpClient client,
        WwiseMusicPlan plan,
        string musicRootPath,
        CancellationToken cancellationToken)
    {
        foreach (var playlist in plan.Playlists)
        {
            var playlistPath = plan.IsMultiPart
                ? $"{musicRootPath}\\{playlist.Name}"
                : musicRootPath;
            foreach (var segment in playlist.Segments)
            {
                var segmentPath = $"{playlistPath}\\{segment.Name}";
                var origin = segment.ClipStartMs;
                var entryLocal = Math.Max(0.0, segment.EntryCueMs - origin);
                var exitLocal = Math.Max(0.0, segment.ExitCueMs - origin);
                var cues = BuildSegmentCueList(segment, origin, entryLocal, exitLocal);
                await client.CallAsync(
                        "ak.wwise.core.object.set",
                        new Dictionary<string, object?>
                        {
                            ["objects"] = new object[]
                            {
                                new Dictionary<string, object?>
                                {
                                    ["object"] = segmentPath,
                                    ["@Cues"] = cues,
                                },
                            },
                            ["onNameConflict"] = "merge",
                            ["listMode"] = "replaceAll",
                        },
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private static List<object> BuildSegmentCueList(
        WwiseSegmentPlan segment,
        double origin,
        double entryLocal,
        double exitLocal)
    {
        var cues = new List<object>
        {
            new Dictionary<string, object?>
            {
                ["type"] = "MusicCue",
                ["name"] = string.Empty,
                ["@CueType"] = 0,
                ["@TimeMs"] = entryLocal,
            },
            new Dictionary<string, object?>
            {
                ["type"] = "MusicCue",
                ["name"] = string.Empty,
                ["@CueType"] = 1,
                ["@TimeMs"] = exitLocal,
            },
        };

        foreach (var custom in segment.CustomCues)
        {
            if (IsReservedCueName(custom.Name))
            {
                continue;
            }

            cues.Add(new Dictionary<string, object?>
            {
                ["type"] = "MusicCue",
                ["name"] = custom.Name,
                ["@CueType"] = 2,
                ["@TimeMs"] = Math.Max(0.0, custom.TimeMs - origin),
            });
        }

        return cues;
    }
}
