using System.Text;

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
        bool updateExistingStateGroup = false,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (wavInfo.SampleRate == 0 || wavInfo.BlockAlign == 0)
        {
            throw new ArgumentException("サンプルレートまたは BlockAlign が不正です。");
        }

        var sampleRate = wavInfo.SampleRate;
        var blockAlign = wavInfo.BlockAlign;

        var sb = new StringBuilder();
        void Log(string line = "")
        {
            sb.AppendLine(line);
            progress?.Report(line);
        }

        Log("=== Wwise Import ===");
        Log($"Target  : {parentPath}");
        Log($"Mode    : {(plan.IsMultiPart ? "Music Switch Container" : "Music Playlist Container")}");
        Log($"Name    : {plan.ContainerName}");

        string? stateGroupPath = null;
        if (plan.IsMultiPart)
        {
            stateGroupPath = importSettings.ResolveStateGroupPath(plan.ContainerName);
            Log($"StateGrp : {stateGroupPath}");
            if (updateExistingStateGroup)
            {
                // BuildSetArgs の onNameConflict=merge により、State Group 自体を維持したまま
                // State 一覧を現在の Playlist 構成へ同期する。
                Log("StateGrp : 既存オブジェクトを変更");
            }
            else
            {
                Log("StateGrp : 新規作成");
            }
        }

        Dictionary<int, float>? partGains = null;
        if (loudnessNormalizeEnabled)
        {
            Log(
                $"Loudness: Normalize ON → target {loudnessTargetLkfs:0.##} LKFS"
                + (loudnessPreserveGroupBalance ? " (Preserve Group Balance)" : string.Empty));
            partGains = LoudnessMeter.ComputePartGains(
                sourceWavPath,
                wavInfo,
                outputParts,
                partGroupIds,
                loudnessTargetLkfs,
                loudnessPreserveGroupBalance,
                Log);
        }

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
                throw new InvalidOperationException("複数パート時は State Group パスが必要です。");
            }

            Log("Creating State Group...");
            await CallObjectSetAsync(
                    client,
                    BuildStateGroupSetArgs(plan, importSettings),
                    returnOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            var containerPath = $"{parentPath.TrimEnd('\\')}\\{plan.ContainerName}";
            Log("Creating Music Switch Container...");
            await CallObjectSetAsync(
                    client,
                    BuildMusicSwitchShellSetArgs(plan, parentPath, containerPath, stateGroupPath),
                    returnOptions,
                    cancellationToken)
                .ConfigureAwait(false);

            for (var i = 0; i < plan.Playlists.Count; i++)
            {
                var playlist = plan.Playlists[i];
                Log($"Creating playlist {i + 1}/{plan.Playlists.Count}: {playlist.Name}...");
                await CallObjectSetAsync(
                        client,
                        BuildPlaylistAppendSetArgs(
                            plan,
                            containerPath,
                            playlist,
                            segmentWavs,
                            importSettings),
                        returnOptions,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        else
        {
            Log("Creating Wwise objects...");
            await CallObjectSetAsync(
                    client,
                    BuildSinglePlaylistSetArgs(plan, parentPath, segmentWavs, importSettings),
                    returnOptions,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        Log("Wwise objects created.");

        if (plan.IsMultiPart)
        {
            Log("Transition : Any→Any（Transition）を先頭に維持");
            foreach (var playlist in plan.Playlists)
            {
                Log(
                    $"Transition : Any→{playlist.Name}"
                    + $" / Exit Source at={playlist.ExitSourceAt.ToUiName()}"
                    + " / Destination Sync To=Entry Cue / Fade-out ON");
            }

            Log("Transition : Fade Time/Offset/Curve は WAAPI 非対応のため未設定（Wwise 上で手動設定）");
        }

        foreach (var playlist in plan.Playlists)
        {
            Log($"--- Playlist: {playlist.Name} ({playlist.Segments.Count} segments) ---");
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

        Log($"Slices  : {segmentWavs.Count}");
        Log("=== Wwise Import complete ===");
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
        log($"Output  : {outputDirectory}");

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
                    throw new InvalidOperationException($"トラックがありません: {segment.Name}");
                }

                foreach (var track in segment.Tracks)
                {
                    var partPath = Path.GetFullPath(track.SourceWavPath);
                    if (!partByPath.TryGetValue(partPath, out var part))
                    {
                        throw new InvalidOperationException(
                            $"出力パートを特定できません: {track.SourceWavPath}");
                    }

                    var startSample = checked(
                        part.StartSampleOffset + MsToSample(track.ClipStartMs, sampleRate));
                    var endSample = checked(
                        part.StartSampleOffset + MsToSample(track.ClipEndMs, sampleRate));
                    if (endSample <= startSample)
                    {
                        throw new InvalidOperationException(
                            $"トラック範囲が空です: {segment.Name}/{track.Name}"
                            + $" ({track.ClipStartMs}..{track.ClipEndMs} ms)");
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
                        ? $"WAV: {fileName}"
                        : $"WAV: {fileName} (gain {gain:0.000})");
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
    /// Music Switch Container 本体（Playlist 子は空）。
    /// children を replaceAll で空にすることで、再 EXPORT 時の古い Playlist を落とす。
    /// </summary>
    private static Dictionary<string, object?> BuildMusicSwitchShellSetArgs(
        WwiseMusicPlan plan,
        string parentPath,
        string containerPath,
        string stateGroupPath)
    {
        var entries = plan.Playlists
            .Select(p => (object)new Dictionary<string, object?>
            {
                ["type"] = "MultiSwitchEntry",
                ["name"] = string.Empty,
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
                    ["object"] = parentPath,
                    ["children"] = new object[]
                    {
                        new Dictionary<string, object?>
                        {
                            ["type"] = "MusicSwitchContainer",
                            ["name"] = plan.ContainerName,
                            ["@Arguments"] = new[] { stateGroupPath },
                            ["@Entries"] = entries,
                            ["@TransitionRoot"] = WaapiMusicTransitionDefaults.BuildTransitionRoot(
                                containerPath,
                                plan.Playlists),
                            ["children"] = Array.Empty<object>(),
                        },
                    },
                },
            },
            ["onNameConflict"] = "merge",
            ["listMode"] = "replaceAll",
        };
    }

    private static Dictionary<string, object?> BuildPlaylistAppendSetArgs(
        WwiseMusicPlan plan,
        string containerPath,
        WwisePlaylistPlan playlist,
        IReadOnlyDictionary<string, string> segmentWavs,
        WwiseImportSettings importSettings) =>
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
                            isMultiPart: true),
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
        WwiseImportSettings importSettings)
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
                            isMultiPart: false),
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
        bool isMultiPart)
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
        return new Dictionary<string, object?>
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
    }

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
        var entryLocal = Math.Max(0.0, segment.EntryCueMs - origin);
        var exitLocal = Math.Max(0.0, segment.ExitCueMs - origin);
        var endLocal = Math.Max(exitLocal, segment.ClipEndMs - origin);

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
            cues.Add(new Dictionary<string, object?>
            {
                ["type"] = "MusicCue",
                ["name"] = custom.Name,
                ["@CueType"] = 2,
                ["@TimeMs"] = Math.Max(0.0, custom.TimeMs - origin),
            });
        }

        var trackDefs = new List<object>();
        for (var t = 0; t < segment.Tracks.Count; t++)
        {
            var track = segment.Tracks[t];
            var key = TrackSliceKey(segment.Name, track.Name);
            if (!trackWavs.TryGetValue(key, out var wavPath))
            {
                throw new InvalidOperationException(
                    $"切り出し WAV が見つかりません: {segment.Name}/{track.Name}");
            }

            var zeroLatency = streamEnabled && isFirstSegment && t == 0;
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
                if (zeroLatency)
                {
                    trackProps["@PreFetchLength"] = prefetchLengthMs;
                }
            }

            trackDefs.Add(trackProps);
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "MusicSegment",
            ["name"] = segment.Name,
            ["@OverrideClockSettings"] = true,
            ["@Tempo"] = segment.TempoBpm,
            ["@TimeSignatureUpper"] = segment.TimeSignatureUpper,
            ["@TimeSignatureLower"] = segment.TimeSignatureLower,
            ["@EndPosition"] = endLocal,
            ["@Cues"] = cues,
            ["children"] = trackDefs,
        };
    }
}
