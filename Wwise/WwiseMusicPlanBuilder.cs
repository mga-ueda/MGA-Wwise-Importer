namespace MgaWwiseIMImporter.Wwise;

/// <summary>
/// エクスポート計画（パート／リージョン／マーカー）から Wwise の Music 構造計画を組み立てる。
/// <list type="bullet">
/// <item>パート 1 つ = Music Playlist Container 1 つ（複数パート時は Music Switch Container の下）。</item>
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
        IReadOnlyList<WaveformMarkerMark> markers)
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
        var playlists = new List<WwisePlaylistPlan>();

        foreach (var part in outputParts)
        {
            var playlistName = Path.GetFileNameWithoutExtension(part.FileName);
            // 単一パート時は Playlist 名が元ファイル名になるので、セグメントもそれに合わせる
            var segmentBase = outputParts.Count > 1 ? playlistName : baseName;
            var partRegions = CollectPartRegions(part, regions);
            var groups = GroupRegions(partRegions);

            var segments = new List<WwiseSegmentPlan>();
            for (var i = 0; i < groups.Count; i++)
            {
                segments.Add(BuildSegment(
                    $"{segmentBase}_{IndexToLetters(i)}",
                    groups[i],
                    part,
                    sampleRate,
                    bars,
                    markers));
            }

            playlists.Add(new WwisePlaylistPlan
            {
                Name = playlistName,
                SourceWavPath = Path.Combine(directory, part.FileName),
                Segments = segments,
            });
        }

        return new WwiseMusicPlan
        {
            ContainerName = baseName,
            IsMultiPart = outputParts.Count > 1,
            Playlists = playlists,
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

    private static WwiseSegmentPlan BuildSegment(
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

        return new WwiseSegmentPlan
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

    private static bool IsAnacrusis(WaveformRegionMark region) =>
        region.NameSuffix.Equals(WaveformRegionBuilder.AnacrusisSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsExitTail(WaveformRegionMark region) =>
        region.NameSuffix.Equals(WaveformRegionBuilder.LoopEndSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsLoopRegion(WaveformRegionMark region) =>
        region.NameSuffix.Equals(WaveformRegionBuilder.LoopLeftSuffix, StringComparison.OrdinalIgnoreCase);
}
