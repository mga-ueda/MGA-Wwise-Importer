using System.Text;

namespace MgaWwiseIMImporter.Wwise;

/// <summary>
/// <see cref="WwiseMusicPlan"/> を WAAPI（ak.wwise.core.object.set）で Wwise へ流し込む。
/// <para>
/// 1. WaveCopyDir があればエクスポート WAV をコピー。
/// 2. 各 Music Segment 用にリージョン範囲だけを切り出した WAV を用意する。
/// 3. 複数パート時は State Group／State を作成し、Music Switch Container に割当。
/// 4. object.set で Playlist／Segment／Track（＋切り出し WAV）と Cue を作成。
/// </para>
/// </summary>
internal static class WaapiMusicImporter
{
    public static async Task<string> ImportAsync(
        WaapiSettings waapiSettings,
        WwiseImportSettings importSettings,
        WwiseMusicPlan plan,
        string parentPath,
        uint sampleRate,
        ushort blockAlign,
        bool overwriteExistingStateGroup = false,
        CancellationToken cancellationToken = default)
    {
        if (sampleRate == 0 || blockAlign == 0)
        {
            throw new ArgumentException("サンプルレートまたは BlockAlign が不正です。");
        }

        var sb = new StringBuilder();
        sb.AppendLine("=== Wwise Import ===");
        sb.AppendLine($"Target  : {parentPath}");
        sb.AppendLine($"Mode    : {(plan.IsMultiPart ? "Music Switch Container" : "Music Playlist Container")}");
        sb.AppendLine($"Name    : {plan.ContainerName}");

        string? stateGroupPath = null;
        if (plan.IsMultiPart)
        {
            stateGroupPath = importSettings.ResolveStateGroupPath(plan.ContainerName);
            sb.AppendLine($"StateGrp : {stateGroupPath}");
            if (overwriteExistingStateGroup)
            {
                await WaapiObjectUtil.DeleteAsync(waapiSettings, stateGroupPath, cancellationToken)
                    .ConfigureAwait(false);
                sb.AppendLine("StateGrp : 既存を削除して新規作成");
            }
            else
            {
                sb.AppendLine("StateGrp : 新規作成");
            }
        }

        // 1. パート WAV のコピー先を決める
        var partWavs = ResolvePartWavPaths(plan, importSettings, sb);

        // 2. セグメント単位に切り出し（タイムライン先頭へ載せるため）
        var segmentWavs = SliceSegmentWavs(plan, partWavs, importSettings, sampleRate, blockAlign, sb);

        // タイムアウトは import を含むので長めに取る
        var timeout = TimeSpan.FromMilliseconds(Math.Max(waapiSettings.TimeoutMs, 30000));
        using var client = new WaapiHttpClient(waapiSettings.Url, timeout);

        // 3. 構造の一括作成（複数パート時は State Group も同じ呼び出しで作成）
        var setArgs = BuildSetArgs(
            plan,
            parentPath,
            segmentWavs,
            importSettings,
            stateGroupPath);
        _ = await client.CallAsync(
                "ak.wwise.core.object.set",
                setArgs,
                new Dictionary<string, object> { ["return"] = new[] { "id", "name", "type", "path" } },
                cancellationToken)
            .ConfigureAwait(false);

        if (plan.IsMultiPart)
        {
            sb.AppendLine(
                "Transition : any→any / Exit Source at=Immediate"
                + " / Destination Sync To=Entry Cue / Fade-out ON");
            sb.AppendLine("Transition : Fade Time/Offset/Curve は WAAPI 非対応のため未設定（Wwise 上で手動設定）");
        }

        foreach (var playlist in plan.Playlists)
        {
            sb.AppendLine($"--- Playlist: {playlist.Name} ({playlist.Segments.Count} segments) ---");
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
                sb.AppendLine(
                    $"  {segment.Name}"
                    + $"  len={durationMs:0}ms"
                    + (entryLocal > 0.5 ? $"  entry=+{entryLocal:0}ms" : string.Empty)
                    + $"  T{segment.TempoBpm:0.##}-{segment.TimeSignatureUpper}/{segment.TimeSignatureLower}"
                    + (flags.Count > 0 ? $"  ({string.Join(", ", flags)})" : string.Empty));
            }
        }

        sb.AppendLine($"Slices  : {segmentWavs.Count}");
        sb.AppendLine("=== Wwise Import complete ===");
        sb.AppendLine();
        return sb.ToString();
    }

    /// <summary>
    /// パート WAV（エクスポート結果）の参照パスを決める。WaveCopyDir があればそこへコピー。
    /// キーは元の SourceWavPath。
    /// </summary>
    private static Dictionary<string, string> ResolvePartWavPaths(
        WwiseMusicPlan plan,
        WwiseImportSettings settings,
        StringBuilder log)
    {
        var sources = plan.Playlists
            .SelectMany(p => p.Segments)
            .SelectMany(s => s.Tracks)
            .Select(t => t.SourceWavPath)
            .Concat(plan.Playlists.Select(p => p.SourceWavPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var copyDir = settings.WaveCopyDir;
        if (copyDir.Length == 0)
        {
            log.AppendLine("Copy    : （パート WAV のコピーなし）");
            foreach (var source in sources)
            {
                map[source] = source;
            }

            return map;
        }

        Directory.CreateDirectory(copyDir);
        log.AppendLine($"Copy    : {copyDir}");
        foreach (var source in sources)
        {
            var dest = Path.Combine(copyDir, Path.GetFileName(source));
            File.Copy(source, dest, overwrite: true);
            map[source] = dest;
        }

        return map;
    }

    /// <summary>
    /// 各トラックの可聴範囲だけを切り出して個別 WAV にする。
    /// 返り値は TrackSliceKey → 切り出し WAV パス。
    /// </summary>
    private static Dictionary<string, string> SliceSegmentWavs(
        WwiseMusicPlan plan,
        IReadOnlyDictionary<string, string> partWavs,
        WwiseImportSettings settings,
        uint sampleRate,
        ushort blockAlign,
        StringBuilder log)
    {
        var sliceDir = settings.WaveCopyDir.Length > 0
            ? Path.Combine(settings.WaveCopyDir, "_segments")
            : Path.Combine(
                Path.GetDirectoryName(plan.Playlists[0].SourceWavPath) ?? Path.GetTempPath(),
                ".mga_wwise_segments",
                plan.ContainerName);

        Directory.CreateDirectory(sliceDir);
        log.AppendLine($"Slices  : {sliceDir}");

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
                    if (!partWavs.TryGetValue(track.SourceWavPath, out var partPath))
                    {
                        throw new InvalidOperationException(
                            $"パート WAV が見つかりません: {track.SourceWavPath}");
                    }

                    var startSample = MsToSample(track.ClipStartMs, sampleRate);
                    var endSample = MsToSample(track.ClipEndMs, sampleRate);
                    if (endSample <= startSample)
                    {
                        throw new InvalidOperationException(
                            $"トラック範囲が空です: {segment.Name}/{track.Name}"
                            + $" ({track.ClipStartMs}..{track.ClipEndMs} ms)");
                    }

                    // Track 名は Segment 名＋レイヤー番号で一意に付けているため、
                    // 旧形式の segment__track 結合は冗長（例: BGM_x__BGM_x_1.wav）。
                    var fileName = UniqueSliceFileName(
                        $"{track.Name}.wav",
                        usedFileNames);
                    var dest = Path.Combine(sliceDir, fileName);
                    WavCueWriter.WriteSegment(
                        partPath,
                        dest,
                        startSample,
                        endSample,
                        blockAlign,
                        cues: []);
                    map[TrackSliceKey(segment.Name, track.Name)] = dest;
                }
            }
        }

        return map;
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

    private static Dictionary<string, object?> BuildSetArgs(
        WwiseMusicPlan plan,
        string parentPath,
        IReadOnlyDictionary<string, string> segmentWavs,
        WwiseImportSettings importSettings,
        string? stateGroupPath)
    {
        var lookAheadMs = importSettings.LookAheadMs;
        var prefetchLengthMs = importSettings.PrefetchLengthMs;
        var containerPath = $"{parentPath}\\{plan.ContainerName}";

        object BuildPlaylistDef(WwisePlaylistPlan playlist)
        {
            var playlistPath = plan.IsMultiPart
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

            var name = plan.IsMultiPart ? playlist.Name : plan.ContainerName;
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

        var rootObjects = new List<object>();

        object topLevel;
        if (plan.IsMultiPart)
        {
            if (stateGroupPath is null || stateGroupPath.Length == 0)
            {
                throw new InvalidOperationException("複数パート時は State Group パスが必要です。");
            }

            // State 名 = エクスポート WAV 名（拡張子無し）= Playlist 名
            var stateChildren = plan.Playlists
                .Select(p => (object)new Dictionary<string, object?>
                {
                    ["type"] = "State",
                    ["name"] = p.Name,
                })
                .ToList();

            rootObjects.Add(new Dictionary<string, object?>
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
            });

            var entries = plan.Playlists
                .Select(p => (object)new Dictionary<string, object?>
                {
                    ["type"] = "MultiSwitchEntry",
                    ["name"] = string.Empty,
                    ["@EntryPath"] = new[] { $"{stateGroupPath}\\{p.Name}" },
                    ["@AudioNode"] = $"{containerPath}\\{p.Name}",
                })
                .ToList();

            topLevel = new Dictionary<string, object?>
            {
                ["type"] = "MusicSwitchContainer",
                ["name"] = plan.ContainerName,
                ["@Arguments"] = new[] { stateGroupPath },
                ["@Entries"] = entries,
                ["@TransitionRoot"] = WaapiMusicTransitionDefaults.BuildAnyToAnyTransitionRoot(),
                ["children"] = plan.Playlists.Select(BuildPlaylistDef).ToList(),
            };
        }
        else
        {
            topLevel = BuildPlaylistDef(plan.Playlists[0]);
        }

        rootObjects.Add(new Dictionary<string, object?>
        {
            ["object"] = parentPath,
            ["children"] = new[] { topLevel },
        });

        return new Dictionary<string, object?>
        {
            ["objects"] = rootObjects,
            ["onNameConflict"] = "merge",
            ["listMode"] = "replaceAll",
        };
    }

    private static Dictionary<string, object?> BuildSegmentDef(
        WwiseSegmentPlan segment,
        IReadOnlyDictionary<string, string> trackWavs,
        bool isFirstSegment,
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

            var zeroLatency = isFirstSegment && t == 0;
            trackDefs.Add(new Dictionary<string, object?>
            {
                ["type"] = "MusicTrack",
                ["name"] = track.Name,
                ["@IsStreamingEnabled"] = true,
                ["@IsZeroLatency"] = zeroLatency,
                ["@LookAheadTime"] = zeroLatency ? 0 : lookAheadMs,
                ["@PreFetchLength"] = prefetchLengthMs,
                ["import"] = new Dictionary<string, object?>
                {
                    ["files"] = new object[]
                    {
                        new Dictionary<string, object?> { ["audioFile"] = wavPath },
                    },
                },
            });
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
