namespace MgaWwiseImporter.Wave;

/// <summary>
/// 出力計画用リージョンを、重なり・入れ子のない連続区画として構築する。
/// <para>
/// 境界はサンプル単位で厳密に隣接する（領域 i の終端 == 領域 i+1 の開始）。
/// </para>
/// <para>
/// 分割条件:
/// <list type="bullet">
/// <item>隣り合う小節頭の BPM が異なるとき、後ろの小節頭で分割（ランプ含む。小節途中イベントだけでは分割しない）。</item>
/// <item>拍子が変わる位置（拍子イベント）。</item>
/// <item>サイクルマーカーの In / Out。</item>
/// </list>
/// 例外:
/// <list type="bullet">
/// <item>波形冒頭が小節頭でないとき、次の小節線までを単独リージョンにする（名前に -A）。</item>
/// <item>名前が -R で終わるサイクル範囲は Out で分割し、Out が小節途中なら次の小節頭までを 1 リージョンにする（-A）。範囲内は出力計画から除外（色分けのみ別扱い）。</item>
/// <item>名前が -L で終わるサイクル範囲内のリージョン名には -L を添える。</item>
/// <item>名前が -E で終わるサイクル範囲（Wwise の Exit Cue 以降に相当）内のリージョン名には -E を添える。</item>
/// </list>
/// -R / -L / -E / -A の各範囲は重ならない前提。重なっていたらエラー。
/// </para>
/// </summary>
internal static class WaveformRegionBuilder
{
    private const double PpqEpsilon = 1e-6;
    private const double BpmEpsilon = 1e-3;
    /// <summary>
    /// PPQ↔サンプル往復の丸めで末尾などに生まれる微小な空き区画の下限。
    /// これ未満は独立リージョンにせず、直前の区画へ吸い込む。
    /// </summary>
    private const long MinRegionFrames = 2;
    private const string ExcludeRangeSuffix = "-R";
    private const string LoopLeftSuffix = "-L";
    private const string LoopEndSuffix = "-E";
    private const string AnacrusisSuffix = "-A";

    public static IReadOnlyList<WaveformRegionMark> Build(
        NuendoTracklistInfo tracklist,
        TempoMap tempoMap,
        uint sampleRate,
        long timelineOffset,
        long frameCount,
        double waveStartPpq,
        double waveEndPpq,
        IReadOnlyList<double> barBoundaries,
        IReadOnlyList<WaveformCycleMark> cycles)
    {
        if (frameCount <= 0 || sampleRate == 0)
        {
            return [];
        }

        var splitSamples = new SortedSet<long> { 0, frameCount };
        var anacrusisRanges = new List<(long Start, long End)>();

        // 例外: 冒頭が小節頭でない → 次の小節線で区切り、冒頭半端小節だけを 1 リージョンにする
        if (!BarGrid.IsNearAny(barBoundaries, waveStartPpq))
        {
            var nextBar = BarGrid.FindNextBarPpq(barBoundaries, waveStartPpq);
            if (nextBar is double nextBarPpq
                && nextBarPpq <= waveEndPpq + PpqEpsilon)
            {
                var nextSample = tempoMap.PpqToSampleIndex(nextBarPpq, sampleRate) - timelineOffset;
                nextSample = Math.Clamp(nextSample, 0L, frameCount);
                AddClampedSplit(splitSamples, nextSample, frameCount);
                if (nextSample > 0)
                {
                    anacrusisRanges.Add((0, nextSample));
                }
            }
        }

        // 小節頭 BPM が前の小節頭と違うとき分割（連続ランプで次小節頭の BPM が変わる場合を含む）
        AddSplitsWhereBarStartBpmChanges(
            splitSamples,
            tempoMap,
            sampleRate,
            timelineOffset,
            frameCount,
            waveStartPpq,
            waveEndPpq,
            barBoundaries);

        // 拍子変化
        foreach (var signature in tracklist.SignatureEvents)
        {
            var sigPpq = signature.Ppq;
            if (sigPpq < waveStartPpq - PpqEpsilon || sigPpq > waveEndPpq + PpqEpsilon)
            {
                continue;
            }

            TryAddSplitSample(
                splitSamples,
                tempoMap,
                sampleRate,
                timelineOffset,
                frameCount,
                sigPpq);
        }

        // サイクル In / Out。-R は Out が小節途中なら次小節頭までを 1 区画にするため分割を追加
        foreach (var cycle in cycles)
        {
            AddClampedSplit(splitSamples, cycle.StartSampleOffset, frameCount);
            AddClampedSplit(splitSamples, cycle.EndSampleOffset, frameCount);

            if (!IsExcludeRange(cycle))
            {
                continue;
            }

            // PPQ 往復の誤差で小節頭を外しやすいので、サンプル位置で小節頭判定する
            if (IsNearBarLocalSample(
                    barBoundaries,
                    tempoMap,
                    sampleRate,
                    timelineOffset,
                    cycle.EndSampleOffset)
                || cycle.EndSampleOffset >= frameCount)
            {
                continue;
            }

            var nextBarSample = FindNextBarLocalSample(
                barBoundaries,
                tempoMap,
                sampleRate,
                timelineOffset,
                frameCount,
                cycle.EndSampleOffset);
            if (nextBarSample is long nextSample && nextSample > cycle.EndSampleOffset)
            {
                AddClampedSplit(splitSamples, nextSample, frameCount);
                anacrusisRanges.Add((cycle.EndSampleOffset, nextSample));
            }
        }

        var excludeRanges = cycles
            .Where(IsExcludeRange)
            .Select(c => (c.StartSampleOffset, c.EndSampleOffset))
            .ToList();
        var loopLeftRanges = cycles
            .Where(IsLoopLeftRange)
            .Select(c => (c.StartSampleOffset, c.EndSampleOffset))
            .ToList();
        var loopEndRanges = cycles
            .Where(IsLoopEndRange)
            .Select(c => (c.StartSampleOffset, c.EndSampleOffset))
            .ToList();

        var points = splitSamples.ToList();
        var regions = new List<WaveformRegionMark>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var start = points[i];
            var end = points[i + 1];
            if (end <= start)
            {
                continue;
            }

            // 末尾付近などで 1 サンプルだけ残る区画は独立させない（直前へ吸収）。
            // 例: -R Out=frameCount-1 と frameCount のあいだにできるゴミ領域。
            if (end - start < MinRegionFrames)
            {
                if (regions.Count > 0)
                {
                    var prev = regions[^1];
                    regions[^1] = new WaveformRegionMark(
                        prev.StartSampleOffset,
                        end,
                        prev.IsExcluded,
                        prev.NameSuffix);
                }

                continue;
            }

            var mid = start + (end - start) / 2;
            var excluded = ContainsSample(excludeRanges, mid);
            var suffix = BuildNameSuffix(
                mid,
                excluded,
                loopLeftRanges,
                loopEndRanges,
                anacrusisRanges);
            regions.Add(new WaveformRegionMark(start, end, excluded, suffix));
        }

        return regions;
    }

    /// <summary>
    /// -R / -L / -E / -A は重ならない前提（マーカー運用ルール）。重なりを検出したらエラー。
    /// </summary>
    private static string BuildNameSuffix(
        long midSample,
        bool excluded,
        IReadOnlyList<(long Start, long End)> loopLeftRanges,
        IReadOnlyList<(long Start, long End)> loopEndRanges,
        IReadOnlyList<(long Start, long End)> anacrusisRanges)
    {
        var hits = new List<string>();
        if (excluded)
        {
            hits.Add(ExcludeRangeSuffix);
        }

        if (ContainsSample(loopLeftRanges, midSample))
        {
            hits.Add(LoopLeftSuffix);
        }

        if (ContainsSample(loopEndRanges, midSample))
        {
            hits.Add(LoopEndSuffix);
        }

        if (ContainsSample(anacrusisRanges, midSample))
        {
            hits.Add(AnacrusisSuffix);
        }

        if (hits.Count > 1)
        {
            throw new InvalidDataException(
                $"リージョン範囲が重なっています: sample={midSample} ({string.Join(" と ", hits)})。"
                + " -R / -L / -E（および内部生成の -A）は重ならないようにマーカーを配置してください。");
        }

        // -R は IsExcluded で表し、接尾辞には付けない。
        if (excluded || hits.Count == 0)
        {
            return string.Empty;
        }

        return hits[0];
    }

    private static bool ContainsSample(IReadOnlyList<(long Start, long End)> ranges, long sample)
    {
        foreach (var range in ranges)
        {
            if (sample >= range.Start && sample < range.End)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLoopLeftRange(WaveformCycleMark cycle)
    {
        return cycle.Comment.EndsWith(LoopLeftSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Wwise の Exit Cue 以降の波形部分を示す範囲（-E）。</summary>
    private static bool IsLoopEndRange(WaveformCycleMark cycle)
    {
        return cycle.Comment.EndsWith(LoopEndSuffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 連続する着色（非除外）リージョンを 1 ファイル分にまとめる。
    /// -R などで色付けしない区間は区切りとなり、続く着色列は次の _n になる。
    /// </summary>
    public static IReadOnlyList<WaveformOutputPart> BuildOutputParts(
        IReadOnlyList<WaveformRegionMark> regions,
        string sourcePath)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "wave";
        }

        var parts = new List<WaveformOutputPart>();
        long? runStart = null;
        long runEnd = 0;
        var number = 1;

        void Flush()
        {
            if (runStart is not long start || runEnd - start < MinRegionFrames)
            {
                runStart = null;
                return;
            }

            parts.Add(new WaveformOutputPart(
                number,
                start,
                runEnd,
                $"{baseName}_{number}.wav"));
            number++;
            runStart = null;
        }

        foreach (var region in regions)
        {
            if (region.IsExcluded)
            {
                Flush();
                continue;
            }

            if (runStart is null)
            {
                runStart = region.StartSampleOffset;
            }

            runEnd = region.EndSampleOffset;
        }

        Flush();
        return parts;
    }

    private static void AddSplitsWhereBarStartBpmChanges(
        SortedSet<long> splitSamples,
        TempoMap tempoMap,
        uint sampleRate,
        long timelineOffset,
        long frameCount,
        double waveStartPpq,
        double waveEndPpq,
        IReadOnlyList<double> barBoundaries)
    {
        double? previousBarPpq = BarGrid.FindPreviousBarPpq(barBoundaries, waveStartPpq);
        // 波形先頭が小節頭そのもののとき、その線を「直前小節頭」扱いにする
        if (BarGrid.IsNearAny(barBoundaries, waveStartPpq))
        {
            previousBarPpq = waveStartPpq;
        }

        double? previousBpm = previousBarPpq is double prev
            ? tempoMap.GetBpmAt(prev)
            : null;

        foreach (var barPpq in barBoundaries)
        {
            if (barPpq < waveStartPpq - PpqEpsilon || barPpq > waveEndPpq + PpqEpsilon)
            {
                continue;
            }

            // 波形先頭ちょうどは既に 0 がある
            if (Math.Abs(barPpq - waveStartPpq) <= PpqEpsilon)
            {
                previousBarPpq = barPpq;
                previousBpm = tempoMap.GetBpmAt(barPpq);
                continue;
            }

            var bpm = tempoMap.GetBpmAt(barPpq);
            if (previousBpm is double pb && Math.Abs(bpm - pb) > BpmEpsilon)
            {
                TryAddSplitSample(
                    splitSamples,
                    tempoMap,
                    sampleRate,
                    timelineOffset,
                    frameCount,
                    barPpq);
            }

            previousBarPpq = barPpq;
            previousBpm = bpm;
        }
    }

    private static bool IsExcludeRange(WaveformCycleMark cycle)
    {
        return cycle.Comment.EndsWith(ExcludeRangeSuffix, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryAddSplitSample(
        SortedSet<long> splits,
        TempoMap tempoMap,
        uint sampleRate,
        long timelineOffset,
        long frameCount,
        double ppq)
    {
        var local = tempoMap.PpqToSampleIndex(ppq, sampleRate) - timelineOffset;
        AddClampedSplit(splits, local, frameCount);
    }

    private static void AddClampedSplit(SortedSet<long> splits, long sample, long frameCount)
    {
        if (sample < 0 || sample > frameCount)
        {
            return;
        }

        splits.Add(sample);
    }

    private static bool IsNearBarLocalSample(
        IReadOnlyList<double> barBoundaries,
        TempoMap tempoMap,
        uint sampleRate,
        long timelineOffset,
        long localSample,
        long sampleTolerance = 1)
    {
        foreach (var barPpq in barBoundaries)
        {
            var barLocal = tempoMap.PpqToSampleIndex(barPpq, sampleRate) - timelineOffset;
            if (Math.Abs(barLocal - localSample) <= sampleTolerance)
            {
                return true;
            }
        }

        return false;
    }

    private static long? FindNextBarLocalSample(
        IReadOnlyList<double> barBoundaries,
        TempoMap tempoMap,
        uint sampleRate,
        long timelineOffset,
        long frameCount,
        long afterLocalSample)
    {
        long? best = null;
        foreach (var barPpq in barBoundaries)
        {
            var barLocal = tempoMap.PpqToSampleIndex(barPpq, sampleRate) - timelineOffset;
            if (barLocal <= afterLocalSample + 1)
            {
                continue;
            }

            if (barLocal > frameCount)
            {
                continue;
            }

            if (best is null || barLocal < best.Value)
            {
                best = barLocal;
            }
        }

        return best;
    }
}
