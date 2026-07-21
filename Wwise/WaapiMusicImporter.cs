using System.Text;
using System.Text.Json;
using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Wwise;

/// <summary>
/// <see cref="WwiseMusicPlan"/> を WAAPI（ak.wwise.core.object.set）で Wwise へ流し込む。
/// <para>
/// 1. 各 Music Segment 用にリージョン範囲だけを切り出した WAV を用意する。
/// 2. 複数パート時は State Group／State を作成または更新し、Music Switch Container に割当。
/// 3. object.set で Playlist／Segment／Track（＋切り出し WAV）と Cue を作成。
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
        var segmentWavs = SliceSegmentWavs(
            plan,
            sourceWavPath,
            outputDirectory,
            outputParts,
            sampleRate,
            blockAlign,
            wavInfo,
            partGains,
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
                            segmentWavs,
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
                        segmentWavs,
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
            foreach (var segment in playlist.Segments)
            {
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

                if (segment.Tracks.Count > 1)
                {
                    flags.Add($"tracks={segment.Tracks.Count}");
                }

                var durationMs = Math.Max(0.0, segment.ClipEndMs - segment.ClipStartMs);
                var entryLocal = Math.Max(0.0, segment.EntryCueMs - segment.ClipStartMs);
                Log(
                    $"  {segment.Name}"
                    + $"  len={durationMs:0}ms"
                    + (entryLocal > 0.5 ? $"  entry=+{entryLocal:0}ms" : string.Empty)
                    + $"  T{segment.TempoBpm:0.##}-{segment.TimeSignatureUpper}/{segment.TimeSignatureLower}"
                    + (flags.Count > 0 ? $"  ({string.Join(", ", flags)})" : string.Empty));
            }
        }

        Log($"{UiStrings.KeySlices} {segmentWavs.Count}");
        Log(UiStrings.LogWwiseImportComplete);
        Log();
        return sb.ToString();
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
    /// 元 WAV から各トラックの可聴範囲だけを直接切り出す。
    /// 返り値は TrackSliceKey → 切り出し WAV パス。
    /// </summary>
    private static Dictionary<string, string> SliceSegmentWavs(
        WwiseMusicPlan plan,
        string sourceWavPath,
        string outputDirectory,
        IReadOnlyList<WaveformOutputPart> outputParts,
        uint sampleRate,
        ushort blockAlign,
        WavFileInfo wavInfo,
        IReadOnlyDictionary<int, float>? partGains,
        Action<string> log)
    {
        Directory.CreateDirectory(outputDirectory);
        log($"{UiStrings.KeyOutput} {outputDirectory}");

        var partByPath = outputParts.ToDictionary(
            part => Path.GetFullPath(Path.Combine(outputDirectory, part.FileName)),
            part => part,
            StringComparer.OrdinalIgnoreCase);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var usedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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

                    var startSample = checked(
                        part.StartSampleOffset + MsToSample(track.ClipStartMs, sampleRate));
                    var endSample = checked(
                        part.StartSampleOffset + MsToSample(track.ClipEndMs, sampleRate));
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

                    // Track 名は Segment 名＋レイヤー番号で一意に付けているため、
                    // 旧形式の segment__track 結合は冗長（例: BGM_x__BGM_x_1.wav）。
                    var fileName = UniqueSliceFileName(
                        $"{track.Name}.wav",
                        usedFileNames);
                    var dest = Path.Combine(outputDirectory, fileName);
                    WriteSegmentSafely(
                        sourceWavPath,
                        dest,
                        startSample,
                        endSample,
                        blockAlign,
                        gain,
                        wavInfo);
                    map[TrackSliceKey(segment.Name, track.Name)] = dest;
                    log(Math.Abs(gain - 1f) < 0.000001f
                        ? UiStrings.LogWavSliceWritten(fileName)
                        : UiStrings.LogWavSliceWrittenWithGain(fileName, gain));
                }
            }
        }

        return map;
    }

    private static void WriteSegmentSafely(
        string sourcePath,
        string destinationPath,
        long startSample,
        long endSample,
        ushort blockAlign,
        float gain,
        WavFileInfo wavInfo)
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
                wavInfo);
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
                wavInfo);
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
        IReadOnlyDictionary<string, string> segmentWavs,
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
                            segmentWavs,
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
        IReadOnlyDictionary<string, string> segmentWavs,
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
                            segmentWavs,
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
        IReadOnlyDictionary<string, string> segmentWavs,
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
                segmentWavs,
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
        IReadOnlyDictionary<string, string> trackWavs,
        bool isFirstSegment,
        bool streamEnabled,
        int lookAheadMs,
        int prefetchLengthMs)
    {
        // 切り出し WAV の先頭がセグメント 0。Cue は相対時刻。
        var origin = segment.ClipStartMs;
        var exitLocal = Math.Max(0.0, segment.ExitCueMs - origin);
        var endLocal = Math.Max(exitLocal, segment.ClipEndMs - origin);

        var trackDefs = new List<object>();
        for (var t = 0; t < segment.Tracks.Count; t++)
        {
            var track = segment.Tracks[t];
            var key = TrackSliceKey(segment.Name, track.Name);
            if (!trackWavs.TryGetValue(key, out var wavPath))
            {
                throw new InvalidOperationException(
                    UiStrings.ErrSlicedWavMissing(segment.Name, track.Name));
            }

            // 先頭セグメント内の全トラック（グループ化レイヤー含む）を Zero latency にする。
            // Prefetch は従来どおり先頭トラックのみ。
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
                        new Dictionary<string, object?> { ["audioFile"] = wavPath },
                    },
                },
            };
            if (streamEnabled)
            {
                trackProps["@IsZeroLatency"] = zeroLatency;
                trackProps["@LookAheadTime"] = zeroLatency ? 0 : lookAheadMs;
                if (isFirstSegment && t == 0)
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
