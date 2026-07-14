using System.Globalization;

namespace MgaWwiseImporter.Wave;

/// <summary>
/// Nuendo トラックリストから WAV cue／リージョンを構築する。
/// 実ファイルへの書き込みは <see cref="WavCueWriter"/> が担当する。
/// </summary>
internal static class WavMarkerEmbedder
{
    private const double PpqEpsilon = 1e-9;

    public static string BuildOutputPath(string sourceWavPath)
    {
        var directory = Path.GetDirectoryName(sourceWavPath) ?? string.Empty;
        var fileName = Path.GetFileNameWithoutExtension(sourceWavPath);
        var extension = Path.GetExtension(sourceWavPath);
        return Path.Combine(directory, $"{fileName}_e{extension}");
    }

    public static WaveEmbedBuildResult BuildCueItems(
        NuendoTracklistInfo tracklist,
        WavFileInfo wavInfo)
    {
        var warnings = new List<string>();
        var tempoMap = new TempoMap(tracklist.TempoEvents, tracklist.SignatureEvents);
        var sampleRate = wavInfo.SampleRate;
        var timelineOffset = (long)wavInfo.TimeReferenceSamples;
        var frameCount = wavInfo.FrameCount;
        var waveStartPpq = tempoMap.FindPpqForSamples(timelineOffset, sampleRate);
        var waveEndPpq = tempoMap.FindPpqForSamples(timelineOffset + frameCount, sampleRate);

        var cycleMarkers = tracklist.MarkerEvents
            .Where(marker => marker.Kind == NuendoMarkerKind.CycleRegion)
            .ToList();
        var pointMarkers = tracklist.MarkerEvents
            .Where(marker => marker.Kind == NuendoMarkerKind.Marker)
            .ToList();

        // リージョン境界:
        // - 波形先端/終端
        // - テンポ変化（ジャンプ・ランプ開始/終了）／拍子変化
        // - 先頭・終端の半端小節
        // - -L/-R サイクル: 範囲内はテンポ/拍子で切らない。終端で区切り、直後が小節途中ならアウフタクト小節を別リージョンに
        // 一定テンポ区間・ランプ区間内部は小節ごとには割らない。各リージョンは等価 BPM コメント。
        var loopSideRanges = cycleMarkers
            .Where(IsLoopSideCycleMarker)
            .Select(cycle => (
                Start: cycle.StartPpq,
                End: cycle.EndPpq,
                Side: NormalizeLoopSideName(cycle.Name)))
            .Where(range => range.End > range.Start)
            .ToList();
        var loopSideSpans = loopSideRanges
            .Select(range => (range.Start, range.End))
            .ToList();

        var auftaktRanges = new List<(double Start, double End)>();
        var splitPpqs = CollectRegionSplitPpqs(
            tracklist,
            waveStartPpq,
            waveEndPpq,
            loopSideRanges,
            loopSideSpans,
            auftaktRanges);

        long ToLocalSample(double ppq) => Math.Clamp(
            tempoMap.PpqToSampleIndex(ppq, sampleRate) - timelineOffset,
            0L,
            frameCount);

        var splitPoints = NormalizeSplitPoints(
            splitPpqs.Select(ppq => (Ppq: ppq, Sample: ToLocalSample(ppq))),
            waveStartPpq,
            waveEndPpq,
            frameCount);

        var items = new List<WavCueItem>();
        uint nextCueId = 1;

        AppendAutoRegions(
            items,
            ref nextCueId,
            splitPoints,
            tempoMap,
            sampleRate,
            loopSideRanges,
            auftaktRanges);

        CollectOutOfRangeCycleWarnings(
            warnings,
            cycleMarkers,
            tempoMap,
            sampleRate,
            timelineOffset,
            frameCount);

        AppendLoopSideRegions(
            items,
            ref nextCueId,
            cycleMarkers,
            tempoMap,
            sampleRate,
            timelineOffset,
            frameCount);

        AppendPointMarkers(
            items,
            warnings,
            ref nextCueId,
            pointMarkers,
            tempoMap,
            sampleRate,
            timelineOffset,
            frameCount);

        return new WaveEmbedBuildResult
        {
            Cues = items,
            Warnings = warnings,
        };
    }

    private static SortedSet<double> CollectRegionSplitPpqs(
        NuendoTracklistInfo tracklist,
        double waveStartPpq,
        double waveEndPpq,
        IReadOnlyList<(double Start, double End, string Side)> loopSideRanges,
        IReadOnlyList<(double Start, double End)> loopSideSpans,
        List<(double Start, double End)> auftaktRanges)
    {
        var splitPpqs = new SortedSet<double> { waveStartPpq, waveEndPpq };
        var barBoundaries = BarGrid.GetBarBoundaries(tracklist.SignatureEvents, waveEndPpq);

        void TryAddCoveringSplit(double ppq)
        {
            if (ppq <= waveStartPpq + PpqEpsilon || ppq >= waveEndPpq - PpqEpsilon)
            {
                return;
            }

            // -L/-R 範囲の内部ではテンポ/拍子/半端小節などの境界を入れない
            if (IsStrictlyInsideAnyRange(ppq, loopSideSpans))
            {
                return;
            }

            splitPpqs.Add(ppq);
        }

        foreach (var tempoChangePpq in GetTempoChangePpqs(tracklist.TempoEvents))
        {
            TryAddCoveringSplit(tempoChangePpq);
        }

        foreach (var signatureEvent in tracklist.SignatureEvents)
        {
            TryAddCoveringSplit(signatureEvent.Ppq);
        }

        // 先頭が小節途中ならアウフタクト（その小節終わりまでを別リージョン、-A）
        var barStartAtWave = BarGrid.FindPreviousBarPpq(barBoundaries, waveStartPpq);
        if (barStartAtWave is not null && waveStartPpq > barStartAtWave.Value + PpqEpsilon)
        {
            var nextBar = BarGrid.FindNextBarPpq(barBoundaries, waveStartPpq);
            if (nextBar is not null
                && nextBar.Value > waveStartPpq
                && nextBar.Value < waveEndPpq
                && !IsStrictlyInsideAnyRange(nextBar.Value, loopSideSpans))
            {
                splitPpqs.Add(nextBar.Value);
                auftaktRanges.Add((waveStartPpq, nextBar.Value));
            }
        }

        // 終端が小節途中なら、その半端小節を別リージョンにする
        var barStartAtEnd = BarGrid.FindPreviousBarPpq(barBoundaries, waveEndPpq);
        if (barStartAtEnd is not null
            && waveEndPpq > barStartAtEnd.Value + PpqEpsilon
            && barStartAtEnd.Value > waveStartPpq)
        {
            TryAddCoveringSplit(barStartAtEnd.Value);
        }

        // -L/-R: 開始/終了で区切る。終了直後が小節途中ならアウフタクト（その小節だけ）を挟む
        foreach (var range in loopSideRanges)
        {
            if (range.Start > waveStartPpq && range.Start < waveEndPpq)
            {
                splitPpqs.Add(range.Start);
            }

            if (range.End <= waveStartPpq || range.End >= waveEndPpq)
            {
                continue;
            }

            splitPpqs.Add(range.End);

            var barStartAtLoopEnd = BarGrid.FindPreviousBarPpq(barBoundaries, range.End);
            if (barStartAtLoopEnd is null || range.End <= barStartAtLoopEnd.Value + PpqEpsilon)
            {
                continue;
            }

            var nextBarAfterLoop = BarGrid.FindNextBarPpq(barBoundaries, range.End);
            if (nextBarAfterLoop is null
                || nextBarAfterLoop.Value <= waveStartPpq
                || nextBarAfterLoop.Value >= waveEndPpq)
            {
                continue;
            }

            splitPpqs.Add(nextBarAfterLoop.Value);
            auftaktRanges.Add((range.End, nextBarAfterLoop.Value));
        }

        return splitPpqs;
    }

    private static List<(double Ppq, long Sample)> NormalizeSplitPoints(
        IEnumerable<(double Ppq, long Sample)> candidates,
        double waveStartPpq,
        double waveEndPpq,
        long frameCount)
    {
        // 同一サンプル位置に複数 PPQ がある場合は小さい PPQ を残す。
        // 波形全範囲を必ず覆うため、先端/終端を明示的に付け直す。
        return candidates
            .Where(point => point.Sample > 0 && point.Sample < frameCount)
            .Prepend((Ppq: waveStartPpq, Sample: 0L))
            .Append((Ppq: waveEndPpq, Sample: frameCount))
            .GroupBy(point => point.Sample)
            .Select(group => group.OrderBy(point => point.Ppq).First())
            .OrderBy(point => point.Sample)
            .ToList();
    }

    private static void AppendAutoRegions(
        List<WavCueItem> items,
        ref uint nextCueId,
        IReadOnlyList<(double Ppq, long Sample)> splitPoints,
        TempoMap tempoMap,
        double sampleRate,
        IReadOnlyList<(double Start, double End, string Side)> loopSideRanges,
        IReadOnlyList<(double Start, double End)> auftaktRanges)
    {
        for (var i = 0; i < splitPoints.Count - 1; i++)
        {
            var (startPpq, startSample) = splitPoints[i];
            var (endPpq, endSample) = splitPoints[i + 1];
            if (endSample <= startSample)
            {
                continue;
            }

            var sampleLength = endSample - startSample;
            var equivalentBpm = TempoMap.CalculateEquivalentBpm(
                startPpq,
                endPpq,
                sampleLength,
                sampleRate);
            if (equivalentBpm <= 0)
            {
                equivalentBpm = tempoMap.GetBpmAt(startPpq);
            }

            var comment = TempoMap.FormatTempoSignatureComment(
                equivalentBpm,
                tempoMap.GetSignatureAt(startPpq));

            var loopSide = FindLoopSideForRegion(startPpq, endPpq, loopSideRanges);
            if (loopSide is not null)
            {
                comment = $"{comment} {loopSide}";
            }

            if (IsRegionInsideAnyRange(startPpq, endPpq, auftaktRanges))
            {
                comment = $"{comment} -A";
            }

            items.Add(new WavCueItem
            {
                Id = nextCueId++,
                SampleOffset = startSample,
                SampleLength = sampleLength,
                Name = string.Empty,
                Comment = comment,
                IsRegion = true,
            });
        }
    }

    private static void CollectOutOfRangeCycleWarnings(
        List<string> warnings,
        IReadOnlyList<NuendoMarkerEvent> cycleMarkers,
        TempoMap tempoMap,
        double sampleRate,
        long timelineOffset,
        long frameCount)
    {
        foreach (var cycle in cycleMarkers)
        {
            var localStart = tempoMap.PpqToSampleIndex(cycle.StartPpq, sampleRate) - timelineOffset;
            var localEnd = tempoMap.PpqToSampleIndex(cycle.EndPpq, sampleRate) - timelineOffset;
            if (IsFullyInsideWave(localStart, localEnd, frameCount))
            {
                continue;
            }

            warnings.Add(
                $"[警告] CycleMarker 範囲外のため埋め込みスキップ: "
                + $"Name=\"{DisplayName(cycle.Name)}\" "
                + $"Sample={localStart}..{localEnd} "
                + $"(有効範囲 0..{frameCount}) "
                + $"PPQ={FormatPpq(cycle.StartPpq)}..{FormatPpq(cycle.EndPpq)}");
        }
    }

    private static void AppendLoopSideRegions(
        List<WavCueItem> items,
        ref uint nextCueId,
        IReadOnlyList<NuendoMarkerEvent> cycleMarkers,
        TempoMap tempoMap,
        double sampleRate,
        long timelineOffset,
        long frameCount)
    {
        // Nuendo サイクルマーカーの Name が -L / -R のものは、波形内に完全に収まる場合のみ追加リージョン化
        foreach (var cycle in cycleMarkers.Where(IsLoopSideCycleMarker))
        {
            var localStart = tempoMap.PpqToSampleIndex(cycle.StartPpq, sampleRate) - timelineOffset;
            var localEnd = tempoMap.PpqToSampleIndex(cycle.EndPpq, sampleRate) - timelineOffset;
            if (!IsFullyInsideWave(localStart, localEnd, frameCount))
            {
                continue;
            }

            items.Add(new WavCueItem
            {
                Id = nextCueId++,
                SampleOffset = localStart,
                SampleLength = localEnd - localStart,
                Name = string.Empty,
                Comment = NormalizeLoopSideName(cycle.Name),
                IsRegion = true,
            });
        }
    }

    private static void AppendPointMarkers(
        List<WavCueItem> items,
        List<string> warnings,
        ref uint nextCueId,
        IReadOnlyList<NuendoMarkerEvent> pointMarkers,
        TempoMap tempoMap,
        double sampleRate,
        long timelineOffset,
        long frameCount)
    {
        foreach (var marker in pointMarkers)
        {
            var localStart = tempoMap.PpqToSampleIndex(marker.StartPpq, sampleRate) - timelineOffset;
            if (localStart < 0 || localStart >= frameCount)
            {
                warnings.Add(
                    $"[警告] Marker 範囲外のため埋め込みスキップ: "
                    + $"Name=\"{DisplayName(marker.Name)}\" "
                    + $"Sample={localStart} "
                    + $"(有効範囲 0..{frameCount - 1}) "
                    + $"PPQ={FormatPpq(marker.StartPpq)}");
                continue;
            }

            items.Add(new WavCueItem
            {
                Id = nextCueId++,
                SampleOffset = localStart,
                SampleLength = 0,
                Name = marker.Name,
                Comment = TempoMap.FormatTempoSignatureComment(
                    tempoMap.GetBpmAt(marker.StartPpq),
                    tempoMap.GetSignatureAt(marker.StartPpq)),
                IsRegion = false,
            });
        }
    }

    private static bool IsFullyInsideWave(long localStart, long localEnd, long frameCount)
    {
        return localStart >= 0
            && localEnd > localStart
            && localEnd <= frameCount;
    }

    private static string FormatPpq(double ppq)
    {
        return ppq.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string DisplayName(string? name)
    {
        return string.IsNullOrEmpty(name) ? "(unnamed)" : name;
    }

    private static bool IsLoopSideCycleMarker(NuendoMarkerEvent cycle)
    {
        return NormalizeLoopSideName(cycle.Name) is "-L" or "-R";
    }

    private static string NormalizeLoopSideName(string? name)
    {
        return (name ?? string.Empty).Trim();
    }

    private static bool IsStrictlyInsideAnyRange(
        double ppq,
        IReadOnlyList<(double Start, double End)> ranges)
    {
        foreach (var range in ranges)
        {
            if (ppq > range.Start + PpqEpsilon && ppq < range.End - PpqEpsilon)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsRegionMidpoint(
        double startPpq,
        double endPpq,
        double rangeStart,
        double rangeEnd)
    {
        var midPpq = (startPpq + endPpq) * 0.5d;
        return midPpq >= rangeStart - PpqEpsilon && midPpq < rangeEnd - PpqEpsilon;
    }

    private static string? FindLoopSideForRegion(
        double startPpq,
        double endPpq,
        IReadOnlyList<(double Start, double End, string Side)> ranges)
    {
        string? side = null;
        var bestLength = double.MaxValue;

        foreach (var range in ranges)
        {
            if (ContainsRegionMidpoint(startPpq, endPpq, range.Start, range.End)
                && range.End - range.Start < bestLength)
            {
                side = range.Side;
                bestLength = range.End - range.Start;
            }
        }

        return side;
    }

    private static bool IsRegionInsideAnyRange(
        double startPpq,
        double endPpq,
        IReadOnlyList<(double Start, double End)> ranges)
    {
        foreach (var range in ranges)
        {
            if (ContainsRegionMidpoint(startPpq, endPpq, range.Start, range.End))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<double> GetTempoChangePpqs(IReadOnlyList<NuendoTempoEvent> tempoEvents)
    {
        for (var i = 1; i < tempoEvents.Count; i++)
        {
            var previous = tempoEvents[i - 1];
            var current = tempoEvents[i];

            if (current.IsRamp)
            {
                // ランプ: 直前点が開始、この点が終了
                yield return previous.Ppq;
                yield return current.Ppq;
                continue;
            }

            // ジャンプ（または一定のまま）: BPM が変わる点だけ境界にする
            if (Math.Abs(current.Bpm - previous.Bpm) > 0.0001)
            {
                yield return current.Ppq;
            }
        }
    }
}
