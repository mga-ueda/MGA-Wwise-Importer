namespace MgaWwiseIMImporter.Wwise;

/// <summary>
/// エクスポート計画（パート／リージョン／マーカー）から Wwise の Music 構造計画を組み立てる。
/// <list type="bullet">
/// <item>未グループのパート 1 つ = Music Playlist Container 1 つ。</item>
/// <item>グループ（2 パート以上）= Music Playlist Container 1 つ（同期 Segment 内に複数 Music Track）。</item>
/// <item>最終 Playlist が複数なら Music Switch Container の下に並べる。</item>
/// <item>リージョン 1 つ = Music Segment 1 つ。ただし -A は次のリージョンと、-E は直前のリージョンと同一セグメントに束ねる。</item>
/// <item>-A 部分は Entry Cue より前（アウフタクト）、-E 部分は Exit Cue より後として扱う。</item>
/// <item>-L の付いたリージョンはプレイリスト上で無限ループにする。</item>
/// <item>パート内の単発マーカーは Custom Cue にする。</item>
/// </list>
/// </summary>
internal static class WwiseMusicPlanBuilder
{
    public static WwiseMusicPlan Build(
        string sourcePath,
        uint sampleRate,
        IReadOnlyList<WaveformOutputPart> outputParts,
        IReadOnlyList<WaveformRegionMark> regions,
        IReadOnlyList<WaveformBarMark> bars,
        IReadOnlyList<WaveformMarkerMark> markers,
        IReadOnlyDictionary<int, int>? partGroupIds = null,
        IReadOnlyDictionary<int, string>? playlistNameOverrides = null)
    {
        if (sampleRate == 0)
        {
            throw new ArgumentException("サンプルレートが 0 です。", nameof(sampleRate));
        }

        if (outputParts.Count == 0)
        {
            throw new ArgumentException("出力パートがありません。", nameof(outputParts));
        }

        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "wave";
        }

        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        var units = BuildPlaylistUnits(outputParts, partGroupIds);
        var playlists = new List<WwisePlaylistPlan>();

        for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
        {
            var unit = units[unitIndex];
            var playlistName = ResolvePlaylistName(
                baseName,
                units,
                unitIndex,
                playlistNameOverrides);
            if (unit.Parts.Count == 1)
            {
                playlists.Add(BuildSinglePartPlaylist(
                    playlistName,
                    unit.Parts[0],
                    directory,
                    segmentBase: playlistName,
                    sampleRate,
                    regions,
                    bars,
                    markers));
            }
            else
            {
                playlists.Add(BuildLayeredPlaylist(
                    playlistName,
                    unit.Parts,
                    directory,
                    sampleRate,
                    regions,
                    bars,
                    markers));
            }
        }

        return new WwiseMusicPlan
        {
            ContainerName = baseName,
            IsMultiPart = playlists.Count > 1,
            Playlists = playlists,
        };
    }

    /// <summary>
    /// 現在のグループ割当に対応する、一覧各パートの Music Playlist 名を返す。
    /// 同一グループは同名、グループ後の単位に対して suffix を 1 から詰める。
    /// </summary>
    internal static IReadOnlyDictionary<int, string> BuildPlaylistDisplayNames(
        string sourcePath,
        IReadOnlyList<WaveformOutputPart> outputParts,
        IReadOnlyDictionary<int, int>? partGroupIds,
        IReadOnlyDictionary<int, string>? playlistNameOverrides = null)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "wave";
        }

        var units = BuildPlaylistUnits(outputParts, partGroupIds);
        var result = new Dictionary<int, string>();
        for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
        {
            var name = ResolvePlaylistName(
                baseName,
                units,
                unitIndex,
                playlistNameOverrides);
            foreach (var part in units[unitIndex].Parts)
            {
                result[part.Number] = name;
            }
        }

        return result;
    }

    /// <summary>
    /// プレビュー用。Wwise 計画と同じ規則で Music Segment 名とソース波形上の範囲を列挙する。
    /// グループ化後の Playlist 名を segment の基底にし、各パートのリージョン束ね単位へ _a / _b … を付ける。
    /// </summary>
    public static IReadOnlyList<WaveformSegmentNameMark> BuildSegmentLabelMarks(
        string sourcePath,
        IReadOnlyList<WaveformOutputPart> outputParts,
        IReadOnlyList<WaveformRegionMark> regions,
        IReadOnlyDictionary<int, int>? partGroupIds = null,
        IReadOnlyDictionary<int, string>? playlistNameOverrides = null)
    {
        if (outputParts.Count == 0)
        {
            return [];
        }

        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "wave";
        }

        var units = BuildPlaylistUnits(outputParts, partGroupIds);
        var marks = new List<WaveformSegmentNameMark>();
        for (var unitIndex = 0; unitIndex < units.Count; unitIndex++)
        {
            var playlistName = ResolvePlaylistName(
                baseName,
                units,
                unitIndex,
                playlistNameOverrides);
            // Playlist 名と同じ基底（単独時は baseName、複数時は baseName_1 …）。
            var segmentBase = playlistName;
            var unitSegmentCount = units[unitIndex].Parts
                .Select(part => GroupRegions(CollectPartRegions(part, regions)).Count)
                .DefaultIfEmpty(0)
                .Max();
            foreach (var part in units[unitIndex].Parts)
            {
                var partRegions = CollectPartRegions(part, regions);
                var groups = GroupRegions(partRegions);
                for (var i = 0; i < groups.Count; i++)
                {
                    var group = groups[i];
                    marks.Add(new WaveformSegmentNameMark(
                        BuildSegmentName(segmentBase, i, unitSegmentCount),
                        group[0].StartSampleOffset,
                        group[^1].EndSampleOffset));
                }
            }
        }

        return marks;
    }

    private readonly record struct PlaylistUnit(IReadOnlyList<WaveformOutputPart> Parts);

    private static string ResolvePlaylistName(
        string baseName,
        IReadOnlyList<PlaylistUnit> units,
        int unitIndex,
        IReadOnlyDictionary<int, string>? playlistNameOverrides)
    {
        var unit = units[unitIndex];
        if (playlistNameOverrides is { Count: > 0 }
            && unit.Parts.Count > 0
            && playlistNameOverrides.TryGetValue(unit.Parts[0].Number, out var overrideName)
            && !string.IsNullOrWhiteSpace(overrideName))
        {
            return overrideName;
        }

        return BuildPlaylistName(baseName, units.Count, unitIndex);
    }

    private static List<PlaylistUnit> BuildPlaylistUnits(
        IReadOnlyList<WaveformOutputPart> outputParts,
        IReadOnlyDictionary<int, int>? partGroupIds)
    {
        var units = new List<PlaylistUnit>();
        var groupedMembers = new Dictionary<int, List<WaveformOutputPart>>();
        if (partGroupIds is { Count: > 0 })
        {
            foreach (var part in outputParts)
            {
                if (!partGroupIds.TryGetValue(part.Number, out var groupId))
                {
                    continue;
                }

                if (!groupedMembers.TryGetValue(groupId, out var list))
                {
                    list = [];
                    groupedMembers[groupId] = list;
                }

                list.Add(part);
            }
        }

        // 2 件未満のグループは未グループ扱い（色だけ付いていても単独 Playlist）。
        var effectiveGroups = new Dictionary<int, List<WaveformOutputPart>>();
        foreach (var (groupId, members) in groupedMembers)
        {
            if (members.Count >= 2)
            {
                members.Sort((a, b) => a.Number.CompareTo(b.Number));
                effectiveGroups[groupId] = members;
            }
        }

        var emittedGroups = new HashSet<int>();
        foreach (var part in outputParts)
        {
            if (partGroupIds is not null
                && partGroupIds.TryGetValue(part.Number, out var groupId)
                && effectiveGroups.TryGetValue(groupId, out var members))
            {
                if (!emittedGroups.Add(groupId))
                {
                    continue;
                }

                units.Add(new PlaylistUnit(members));
                continue;
            }

            units.Add(new PlaylistUnit([part]));
        }

        return units;
    }

    private static WwisePlaylistPlan BuildSinglePartPlaylist(
        string playlistName,
        WaveformOutputPart part,
        string directory,
        string segmentBase,
        uint sampleRate,
        IReadOnlyList<WaveformRegionMark> regions,
        IReadOnlyList<WaveformBarMark> bars,
        IReadOnlyList<WaveformMarkerMark> markers)
    {
        var sourceWavPath = Path.Combine(directory, part.FileName);
        var partRegions = CollectPartRegions(part, regions);
        var groups = GroupRegions(partRegions);
        var segments = new List<WwiseSegmentPlan>();
        for (var i = 0; i < groups.Count; i++)
        {
            var built = BuildPartSegment(
                BuildSegmentName(segmentBase, i, groups.Count),
                groups[i],
                part,
                sampleRate,
                bars,
                markers);
            segments.Add(ToSingleTrackSegment(built, sourceWavPath, trackName: built.Name));
        }

        return new WwisePlaylistPlan
        {
            Name = playlistName,
            SourceWavPath = sourceWavPath,
            Segments = segments,
        };
    }

    private static WwisePlaylistPlan BuildLayeredPlaylist(
        string playlistName,
        IReadOnlyList<WaveformOutputPart> parts,
        string directory,
        uint sampleRate,
        IReadOnlyList<WaveformRegionMark> regions,
        IReadOnlyList<WaveformBarMark> bars,
        IReadOnlyList<WaveformMarkerMark> markers)
    {
        var memberPlans = new List<(WaveformOutputPart Part, string WavPath, List<PartSegmentDraft> Segments)>();
        foreach (var part in parts)
        {
            var wavPath = Path.Combine(directory, part.FileName);
            var partRegions = CollectPartRegions(part, regions);
            var groups = GroupRegions(partRegions);
            var drafts = new List<PartSegmentDraft>(groups.Count);
            for (var i = 0; i < groups.Count; i++)
            {
                // 名前は後段で Playlist 基準の Segment 名に揃える（下書きは時間情報のみ）。
                drafts.Add(BuildPartSegment(
                    string.Empty,
                    groups[i],
                    part,
                    sampleRate,
                    bars,
                    markers));
            }

            memberPlans.Add((part, wavPath, drafts));
        }

        var maxCount = memberPlans.Max(m => m.Segments.Count);
        var segments = new List<WwiseSegmentPlan>(maxCount);
        for (var i = 0; i < maxCount; i++)
        {
            PartSegmentDraft? primary = null;
            foreach (var member in memberPlans)
            {
                if (i < member.Segments.Count)
                {
                    primary = member.Segments[i];
                    break;
                }
            }

            if (primary is null)
            {
                continue;
            }

            var segmentName = BuildSegmentName(playlistName, i, maxCount);
            var tracks = new List<WwiseTrackPlan>();
            var maxDurationMs = 0.0;
            var loopInfinite = false;
            for (var layer = 0; layer < memberPlans.Count; layer++)
            {
                var member = memberPlans[layer];
                if (i >= member.Segments.Count)
                {
                    continue;
                }

                var draft = member.Segments[i];
                var durationMs = Math.Max(0.0, draft.ClipEndMs - draft.ClipStartMs);
                maxDurationMs = Math.Max(maxDurationMs, durationMs);
                loopInfinite |= draft.LoopInfinite;
                // レイヤー識別は Playlist と同系の連番（_1 / _2 …）。旧パート名は使わない。
                var trackName = memberPlans.Count == 1
                    ? segmentName
                    : $"{segmentName}_{layer + 1}";
                tracks.Add(new WwiseTrackPlan
                {
                    Name = trackName,
                    SourceWavPath = member.WavPath,
                    ClipStartMs = draft.ClipStartMs,
                    ClipEndMs = draft.ClipEndMs,
                });
            }

            if (tracks.Count == 0)
            {
                continue;
            }

            var entryLocal = Math.Max(0.0, primary.EntryCueMs - primary.ClipStartMs);
            var exitLocal = Math.Max(0.0, primary.ExitCueMs - primary.ClipStartMs);
            // セグメント長は最長トラックに合わせ、Exit は代表トラック基準を維持。
            var clipEndMs = Math.Max(maxDurationMs, exitLocal);
            var customCues = primary.CustomCues
                .Select(c => new WwiseCustomCue(
                    Math.Max(0.0, c.TimeMs - primary.ClipStartMs),
                    c.Name))
                .ToList();

            segments.Add(new WwiseSegmentPlan
            {
                Name = segmentName,
                ClipStartMs = 0,
                ClipEndMs = clipEndMs,
                EntryCueMs = entryLocal,
                ExitCueMs = Math.Min(exitLocal, clipEndMs),
                LoopInfinite = loopInfinite,
                TempoBpm = primary.TempoBpm,
                TimeSignatureUpper = primary.TimeSignatureUpper,
                TimeSignatureLower = primary.TimeSignatureLower,
                CustomCues = customCues,
                Tracks = tracks,
            });
        }

        return new WwisePlaylistPlan
        {
            Name = playlistName,
            SourceWavPath = memberPlans[0].WavPath,
            Segments = segments,
        };
    }

    private static WwiseSegmentPlan ToSingleTrackSegment(
        PartSegmentDraft draft,
        string sourceWavPath,
        string trackName) =>
        new()
        {
            Name = draft.Name,
            ClipStartMs = draft.ClipStartMs,
            ClipEndMs = draft.ClipEndMs,
            EntryCueMs = draft.EntryCueMs,
            ExitCueMs = draft.ExitCueMs,
            LoopInfinite = draft.LoopInfinite,
            TempoBpm = draft.TempoBpm,
            TimeSignatureUpper = draft.TimeSignatureUpper,
            TimeSignatureLower = draft.TimeSignatureLower,
            CustomCues = draft.CustomCues,
            Tracks =
            [
                new WwiseTrackPlan
                {
                    Name = trackName,
                    SourceWavPath = sourceWavPath,
                    ClipStartMs = draft.ClipStartMs,
                    ClipEndMs = draft.ClipEndMs,
                },
            ],
        };

    private sealed class PartSegmentDraft
    {
        public required string Name { get; init; }
        public required double ClipStartMs { get; init; }
        public required double ClipEndMs { get; init; }
        public required double EntryCueMs { get; init; }
        public required double ExitCueMs { get; init; }
        public required bool LoopInfinite { get; init; }
        public required double TempoBpm { get; init; }
        public required int TimeSignatureUpper { get; init; }
        public required int TimeSignatureLower { get; init; }
        public required IReadOnlyList<WwiseCustomCue> CustomCues { get; init; }
    }

    private static PartSegmentDraft BuildPartSegment(
        string name,
        List<WaveformRegionMark> group,
        WaveformOutputPart part,
        uint sampleRate,
        IReadOnlyList<WaveformBarMark> bars,
        IReadOnlyList<WaveformMarkerMark> markers)
    {
        double ToMs(long absSample) => (absSample - part.StartSampleOffset) * 1000.0 / sampleRate;

        var first = group[0];
        var last = group[^1];
        var clipStartMs = ToMs(first.StartSampleOffset);
        var clipEndMs = ToMs(last.EndSampleOffset);

        // Entry Cue: 先頭が -A ならアウフタクト明け（次リージョン開始）
        var entryCueMs = IsAnacrusis(first) && group.Count > 1
            ? ToMs(group[1].StartSampleOffset)
            : clipStartMs;

        // Exit Cue: 末尾が -E ならその開始位置
        var exitCueMs = IsExitTail(last) && group.Count > 1
            ? ToMs(last.StartSampleOffset)
            : clipEndMs;

        // テンポ・拍子・ループは主リージョン（-A を除く先頭）から
        var main = IsAnacrusis(first) && group.Count > 1 ? group[1] : first;
        var (bpm, upper, lower) = LookUpTempoSignature(main.StartSampleOffset, bars);

        var customCues = BuildCustomCues(
            group[0].StartSampleOffset,
            last.EndSampleOffset,
            part,
            sampleRate,
            markers);

        return new PartSegmentDraft
        {
            Name = name,
            ClipStartMs = clipStartMs,
            ClipEndMs = clipEndMs,
            EntryCueMs = entryCueMs,
            ExitCueMs = exitCueMs,
            LoopInfinite = group.Any(IsLoopRegion),
            TempoBpm = bpm,
            TimeSignatureUpper = upper,
            TimeSignatureLower = lower,
            CustomCues = customCues,
        };
    }

    private static List<WaveformRegionMark> CollectPartRegions(
        WaveformOutputPart part,
        IReadOnlyList<WaveformRegionMark> regions)
    {
        var result = new List<WaveformRegionMark>();
        foreach (var region in regions)
        {
            if (region.IsExcluded)
            {
                continue;
            }

            var start = Math.Max(region.StartSampleOffset, part.StartSampleOffset);
            var end = Math.Min(region.EndSampleOffset, part.EndSampleOffset);
            if (end <= start)
            {
                continue;
            }

            result.Add(new WaveformRegionMark(
                start,
                end,
                false,
                region.NameSuffix,
                region.IsAutoNameSuffix));
        }

        result.Sort((a, b) => a.StartSampleOffset.CompareTo(b.StartSampleOffset));
        return result;
    }

    /// <summary>
    /// リージョンをセグメント単位に束ねる。-A は次と、続く -E は同グループへ。
    /// </summary>
    private static List<List<WaveformRegionMark>> GroupRegions(List<WaveformRegionMark> partRegions)
    {
        var groups = new List<List<WaveformRegionMark>>();
        var i = 0;
        while (i < partRegions.Count)
        {
            var group = new List<WaveformRegionMark> { partRegions[i] };

            // -A は次のリージョンを取り込む（次が無い場合は単独のまま）
            if (IsAnacrusis(partRegions[i]) && i + 1 < partRegions.Count)
            {
                i++;
                group.Add(partRegions[i]);
            }

            // 直後の -E は同じグループに取り込む
            if (i + 1 < partRegions.Count && IsExitTail(partRegions[i + 1]))
            {
                i++;
                group.Add(partRegions[i]);
            }

            groups.Add(group);
            i++;
        }

        return groups;
    }

    private static List<WwiseCustomCue> BuildCustomCues(
        long groupStartAbs,
        long groupEndAbs,
        WaveformOutputPart part,
        uint sampleRate,
        IReadOnlyList<WaveformMarkerMark> markers)
    {
        var cues = new List<WwiseCustomCue>();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var marker in markers)
        {
            if (marker.SampleOffset < groupStartAbs || marker.SampleOffset >= groupEndAbs)
            {
                continue;
            }

            var name = marker.Comment.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            // 同一セグメント内で名前が重複しないように連番を付ける
            var unique = name;
            var suffix = 2;
            while (!usedNames.Add(unique))
            {
                unique = $"{name}_{suffix++}";
            }

            var timeMs = (marker.SampleOffset - part.StartSampleOffset) * 1000.0 / sampleRate;
            cues.Add(new WwiseCustomCue(timeMs, unique));
        }

        return cues;
    }

    /// <summary>
    /// リージョン開始位置のテンポ・拍子を小節マークから引く（Exporter のコメント生成と同じ規則）。
    /// </summary>
    private static (double Bpm, int Upper, int Lower) LookUpTempoSignature(
        long regionStartAbsSample,
        IReadOnlyList<WaveformBarMark> bars)
    {
        WaveformBarMark? barHead = null;
        WaveformBarMark? any = null;
        foreach (var bar in bars.OrderBy(b => b.SampleOffset))
        {
            if (bar.SampleOffset > regionStartAbsSample)
            {
                break;
            }

            any = bar;
            if (!bar.IsTempoChangeOnly)
            {
                barHead = bar;
            }
        }

        if ((barHead ?? any) is not WaveformBarMark mark)
        {
            return (120.0, 4, 4);
        }

        return (mark.Bpm, mark.Numerator, mark.Denominator);
    }

    /// <summary>セグメントが1件だけなら接尾辞を省略し、複数なら _a, _b…を付ける。</summary>
    private static string BuildSegmentName(string segmentBase, int index, int segmentCount) =>
        segmentCount == 1
            ? segmentBase
            : $"{segmentBase}_{IndexToLetters(index)}";

    /// <summary>0→a, 1→b, …, 25→z, 26→aa。</summary>
    internal static string IndexToLetters(int index)
    {
        var sb = new System.Text.StringBuilder();
        var n = index;
        do
        {
            sb.Insert(0, (char)('a' + n % 26));
            n = n / 26 - 1;
        }
        while (n >= 0);

        return sb.ToString();
    }

    private static string BuildPlaylistName(string baseName, int unitCount, int unitIndex) =>
        unitCount == 1 ? baseName : $"{baseName}_{unitIndex + 1}";

    private static bool IsAnacrusis(WaveformRegionMark region) =>
        region.NameSuffix.Equals(WaveformRegionBuilder.AnacrusisSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsExitTail(WaveformRegionMark region) =>
        region.NameSuffix.Equals(WaveformRegionBuilder.LoopEndSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsLoopRegion(WaveformRegionMark region) =>
        region.NameSuffix.Equals(WaveformRegionBuilder.LoopLeftSuffix, StringComparison.OrdinalIgnoreCase);
}
