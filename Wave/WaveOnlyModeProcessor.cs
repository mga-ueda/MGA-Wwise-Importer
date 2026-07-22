namespace MgaWwiseIMImporter.Wave;

/// <summary>
/// ペア XML が無い Wave 単体ドロップ時の処理モード。
/// 判定は埋め込みメタ（smpl / リージョン / それ以外）のみ。
/// </summary>
internal enum WaveOnlyMode
{
    /// <summary>マーカーのみ、またはマーカー無し（埋め込み無し含む）。</summary>
    MarkersOnly,

    /// <summary>smpl ループあり。</summary>
    SmplLoop,

    /// <summary>cue+adtl リージョン（ltxt）あり。</summary>
    Regions,
}

/// <summary>Wave 単体モードの判定と、マーカーのみ／smpl ループの表示データ構築。</summary>
internal static class WaveOnlyModeProcessor
{
    private const string LoopKeyword = "Loop";

    /// <summary>埋め込みマーカーコメントの自動／手動リネーム 1 件。</summary>
    internal readonly record struct MarkerCommentRename(string FromComment, string ToComment);

    /// <summary>smpl モードで破棄した埋め込みマーカー／リージョン 1 件。</summary>
    internal readonly record struct DiscardedEmbeddedMark(
        string Kind,
        long SampleOffset,
        string Comment);

    /// <summary>smpl ループから構築したマーカーと、破棄した埋め込みの一覧。</summary>
    internal readonly record struct SmplLoopBuildResult(
        IReadOnlyList<WaveformMarkerMark> Markers,
        int AcceptedLoopCount,
        int SkippedLoopCount,
        IReadOnlyList<DiscardedEmbeddedMark> DiscardedMarks);

    public static WaveOnlyMode Resolve(WavEmbeddedMarkerInfo embedded)
    {
        if (embedded.HasSmplLoops)
        {
            return WaveOnlyMode.SmplLoop;
        }

        if (embedded.HasRegions)
        {
            return WaveOnlyMode.Regions;
        }

        return WaveOnlyMode.MarkersOnly;
    }

    /// <summary>
    /// コメントが <c>-L</c> のみ（前後空白除く）なら true。
    /// </summary>
    public static bool IsLoopOnlyComment(string? comment) =>
        IsExactSuffixComment(comment, WaveformRegionBuilder.LoopLeftSuffix);

    /// <summary>
    /// コメントに <c>Loop</c> が含まれる（大小無視）なら true。
    /// </summary>
    public static bool ContainsLoopKeyword(string? comment) =>
        !string.IsNullOrEmpty(comment)
        && comment.Contains(LoopKeyword, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// <c>-L</c> のみ、またはコメントに <c>Loop</c> を含むなら true（ログ集計用）。
    /// </summary>
    public static bool IsLoopRelatedComment(string? comment) =>
        IsLoopOnlyComment(comment) || ContainsLoopKeyword(comment);

    /// <summary>
    /// コメントが <c>-R</c> のみ（前後空白除く）なら true。
    /// Wave 単体モードではリムーブ（除外）範囲の開始を表す。
    /// </summary>
    public static bool IsRemoveOnlyComment(string? comment) =>
        IsExactSuffixComment(comment, WaveformRegionBuilder.ExcludeRangeSuffix);

    /// <summary>
    /// コメントが <c>-E</c> のみ（前後空白除く）なら true。
    /// Wave 単体モードでは Exit Cue 以降の範囲の開始を表す。
    /// </summary>
    public static bool IsExitOnlyComment(string? comment) =>
        IsExactSuffixComment(comment, WaveformRegionBuilder.LoopEndSuffix);

    /// <summary>
    /// コメントが <c>-A</c> のみ（前後空白除く）なら true。
    /// Wave 単体モードでは Entry Cue より前（アウフタクト）範囲の開始を表す。
    /// </summary>
    public static bool IsAnacrusisOnlyComment(string? comment) =>
        IsExactSuffixComment(comment, WaveformRegionBuilder.AnacrusisSuffix);

    /// <summary>Wave 単体の明示接尾辞（<c>-L</c>/<c>-R</c>/<c>-E</c>/<c>-A</c> のみ）なら true。</summary>
    public static bool HasExactWaveOnlySuffixComment(WaveformMarkerMark marker) =>
        IsLoopOnlyComment(marker.Comment)
        || IsRemoveOnlyComment(marker.Comment)
        || IsExitOnlyComment(marker.Comment)
        || IsAnacrusisOnlyComment(marker.Comment);

    private static bool IsExactSuffixComment(string? comment, string suffix) =>
        !string.IsNullOrEmpty(comment)
        && comment.Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// モード A: cue 単発マーカーを波形表示用に変換する。
    /// </summary>
    public static IReadOnlyList<WaveformMarkerMark> BuildMarkersOnly(
        WavEmbeddedMarkerInfo embedded,
        long frameCount)
    {
        if (frameCount <= 0 || embedded.PointMarkers.Count == 0)
        {
            return [];
        }

        var markers = new List<WaveformMarkerMark>(embedded.PointMarkers.Count);
        foreach (var point in embedded.PointMarkers)
        {
            if (point.SampleOffset < 0 || point.SampleOffset >= frameCount)
            {
                continue;
            }

            markers.Add(new WaveformMarkerMark(
                point.SampleOffset,
                point.DisplayComment,
                IsFromWaveEmbedded: true));
        }

        return markers;
    }

    /// <summary>
    /// モード B: smpl ループの Start / End を <c>-L</c> / <c>-E</c> マーカーへ差し替える。
    /// cue / adtl の単発マーカーとリージョンはすべて破棄対象として返す（表示・出力には使わない）。
    /// </summary>
    public static SmplLoopBuildResult BuildMarkersFromSmplLoops(
        WavEmbeddedMarkerInfo embedded,
        long frameCount)
    {
        var discarded = new List<DiscardedEmbeddedMark>(
            embedded.PointMarkers.Count + embedded.Regions.Count);
        foreach (var point in embedded.PointMarkers)
        {
            discarded.Add(new DiscardedEmbeddedMark(
                "marker",
                point.SampleOffset,
                point.DisplayComment));
        }

        foreach (var region in embedded.Regions)
        {
            var comment = region.Note.Trim();
            if (comment.Length == 0)
            {
                comment = region.Label.Trim();
            }

            discarded.Add(new DiscardedEmbeddedMark(
                "region",
                region.StartSampleOffset,
                comment));
        }

        if (frameCount <= 0 || embedded.SmplLoops.Count == 0)
        {
            return new SmplLoopBuildResult([], 0, embedded.SmplLoops.Count, discarded);
        }

        var markers = new List<WaveformMarkerMark>();
        var accepted = 0;
        var skipped = 0;
        foreach (var loop in embedded.SmplLoops)
        {
            long start = loop.StartSample;
            long end = loop.EndSample;
            if (end <= start
                || start < 0
                || end < 0
                || start >= frameCount
                || end >= frameCount)
            {
                skipped++;
                continue;
            }

            markers.Add(new WaveformMarkerMark(
                start,
                WaveformRegionBuilder.LoopLeftSuffix,
                IsFromWaveEmbedded: true));
            markers.Add(new WaveformMarkerMark(
                end,
                WaveformRegionBuilder.LoopEndSuffix,
                IsFromWaveEmbedded: true));
            accepted++;
        }

        markers.Sort((a, b) => a.SampleOffset.CompareTo(b.SampleOffset));
        return new SmplLoopBuildResult(markers, accepted, skipped, discarded);
    }

    /// <summary>
    /// モード A: マーカー位置で波形を区切り、特殊コメントからリージョンを作る。
    /// <list type="bullet">
    /// <item>マーカーが無い → 全体を 1 リージョン（冒頭 Entry Cue / 末尾 Exit Cue）。</item>
    /// <item>マーカーがちょうど 2 つで、まだ <c>-L</c>/<c>-R</c>/<c>-E</c>/<c>-A</c> が無い → その間を <c>-L</c> 範囲とする。</item>
    /// <item>コメントが <c>-L</c> のみ → 次マーカー（または終端）まで無限ループ。</item>
    /// <item>コメントに <c>Loop</c> を含む → <c>-L</c> と同等。2 つある場合はその間をループ範囲とする（奇数個の余りは単独 <c>-L</c>）。</item>
    /// <item>コメントが <c>-R</c> のみ → 次マーカー（または終端）までリムーブ（除外）。</item>
    /// <item>コメントが <c>-E</c> のみ → 次マーカー（または終端）まで Exit Cue 以降。</item>
    /// <item>コメントが <c>-A</c> のみ → 次マーカー（または終端）まで Entry Cue 前（アウフタクト）。</item>
    /// </list>
    /// 既存仕様と同様、連続 <c>-L</c> の直後（接尾辞なし）には自動で Exit 用 <c>-E</c> 接尾辞を付ける。
    /// </summary>
    public static IReadOnlyList<WaveformRegionMark> BuildRegionsFromMarkers(
        IReadOnlyList<WaveformMarkerMark> markers,
        long frameCount)
    {
        if (frameCount <= 0)
        {
            return [];
        }

        var ordered = markers
            .Where(marker => marker.SampleOffset >= 0 && marker.SampleOffset < frameCount)
            .GroupBy(marker => marker.SampleOffset)
            .Select(group => group.Last())
            .OrderBy(marker => marker.SampleOffset)
            .ToArray();

        // マーカー無し: 全体を1リージョン → 描画／Export とも冒頭 Entry・末尾 Exit。
        if (ordered.Length == 0)
        {
            return frameCount < 2
                ? []
                : [new WaveformRegionMark(0, frameCount)];
        }

        var splits = new SortedSet<long> { 0, frameCount };
        var loopStartSamples = new HashSet<long>();
        var removeStartSamples = new HashSet<long>();
        var exitStartSamples = new HashSet<long>();
        var anacrusisStartSamples = new HashSet<long>();

        // 2 つだけかつ未実体化（明示接尾辞なし）のときだけ、コメント無視で間を -L にする。
        // 実体化後（-L/-E など）は通常のコメント判定へ渡し、-R への書き換えも効くようにする。
        var useImplicitTwoMarkerLoop = ordered.Length == 2
            && !ordered.Any(HasExactWaveOnlySuffixComment);

        if (useImplicitTwoMarkerLoop)
        {
            foreach (var marker in ordered)
            {
                splits.Add(marker.SampleOffset);
            }

            ApplyLoopRange(
                ordered[0].SampleOffset,
                ordered[1].SampleOffset,
                splits,
                loopStartSamples);
        }
        else
        {
            var loopKeywordMarkers = new List<WaveformMarkerMark>();
            foreach (var marker in ordered)
            {
                splits.Add(marker.SampleOffset);
                if (IsLoopOnlyComment(marker.Comment))
                {
                    loopStartSamples.Add(marker.SampleOffset);
                }
                else if (IsRemoveOnlyComment(marker.Comment))
                {
                    removeStartSamples.Add(marker.SampleOffset);
                }
                else if (IsExitOnlyComment(marker.Comment))
                {
                    exitStartSamples.Add(marker.SampleOffset);
                }
                else if (IsAnacrusisOnlyComment(marker.Comment))
                {
                    anacrusisStartSamples.Add(marker.SampleOffset);
                }

                if (ContainsLoopKeyword(marker.Comment))
                {
                    loopKeywordMarkers.Add(marker);
                }
            }

            ApplyLoopKeywordMarkers(loopKeywordMarkers, splits, loopStartSamples);
        }

        if (loopStartSamples.Count == 0
            && removeStartSamples.Count == 0
            && exitStartSamples.Count == 0
            && anacrusisStartSamples.Count == 0)
        {
            return [];
        }

        // 特殊コメントがあるとき全区間を隣接リージョン化する（自動 Exit -E のため）。
        var splitList = splits.ToArray();
        var regions = new List<WaveformRegionMark>(splitList.Length - 1);
        for (var i = 0; i + 1 < splitList.Length; i++)
        {
            var start = splitList[i];
            var end = splitList[i + 1];
            if (end <= start)
            {
                continue;
            }

            if (removeStartSamples.Contains(start))
            {
                regions.Add(new WaveformRegionMark(start, end, IsExcluded: true));
                continue;
            }

            var suffix = loopStartSamples.Contains(start)
                ? WaveformRegionBuilder.LoopLeftSuffix
                : exitStartSamples.Contains(start)
                    ? WaveformRegionBuilder.LoopEndSuffix
                    : anacrusisStartSamples.Contains(start)
                        ? WaveformRegionBuilder.AnacrusisSuffix
                        : string.Empty;
            regions.Add(new WaveformRegionMark(start, end, NameSuffix: suffix));
        }

        WaveformRegionBuilder.ApplyAutoExitSuffixAfterLoop(regions);
        return regions;
    }

    /// <summary>
    /// <c>Loop</c> 含有マーカーを <c>-L</c> 相当にする。
    /// 2 つずつペアにしてその間をループ、余った 1 つは単独 <c>-L</c>（次マーカー／終端まで）。
    /// </summary>
    private static void ApplyLoopKeywordMarkers(
        IReadOnlyList<WaveformMarkerMark> loopKeywordMarkers,
        SortedSet<long> splits,
        HashSet<long> loopStartSamples)
    {
        if (loopKeywordMarkers.Count == 0)
        {
            return;
        }

        if (loopKeywordMarkers.Count == 1)
        {
            loopStartSamples.Add(loopKeywordMarkers[0].SampleOffset);
            return;
        }

        for (var i = 0; i + 1 < loopKeywordMarkers.Count; i += 2)
        {
            ApplyLoopRange(
                loopKeywordMarkers[i].SampleOffset,
                loopKeywordMarkers[i + 1].SampleOffset,
                splits,
                loopStartSamples);
        }

        if (loopKeywordMarkers.Count % 2 == 1)
        {
            loopStartSamples.Add(loopKeywordMarkers[^1].SampleOffset);
        }
    }

    /// <summary>[start, end) 内の分割開始点を <c>-L</c> 対象にする。</summary>
    private static void ApplyLoopRange(
        long start,
        long end,
        SortedSet<long> splits,
        HashSet<long> loopStartSamples)
    {
        if (end <= start)
        {
            return;
        }

        foreach (var sample in splits)
        {
            if (sample >= start && sample < end)
            {
                loopStartSamples.Add(sample);
            }
        }
    }

    /// <summary>
    /// 暗黙ループ（マーカー 2 つ／<c>Loop</c> キーワード）をアプリ上の <c>-L</c>・<c>-E</c> コメントへ実体化する。
    /// 変更したら true。既に実体化済みのペアは触らない（後続編集を許す）。
    /// </summary>
    /// <param name="allowTwoMarkerMaterialize">
    /// false のときはマーカー 2 つ特例のコメント書き換えを行わない（セッション側で一度だけ行う用）。
    /// </param>
    /// <param name="renames">
    /// 埋め込み由来マーカーのコメントが変わったとき、変更前後を追加する（省略可）。
    /// </param>
    public static bool TryMaterializeImplicitLoopComments(
        List<WaveformMarkerMark> markers,
        long frameCount,
        bool allowTwoMarkerMaterialize = true,
        ICollection<MarkerCommentRename>? renames = null)
    {
        if (markers.Count == 0 || frameCount <= 0)
        {
            return false;
        }

        var ordered = markers
            .Where(marker => marker.SampleOffset >= 0 && marker.SampleOffset < frameCount)
            .GroupBy(marker => marker.SampleOffset)
            .Select(group => group.Last())
            .OrderBy(marker => marker.SampleOffset)
            .ToArray();

        if (ordered.Length == 0)
        {
            return false;
        }

        var changed = false;

        if (ordered.Length == 2)
        {
            if (!allowTwoMarkerMaterialize)
            {
                return false;
            }

            if (IsLoopOnlyComment(ordered[0].Comment)
                && IsExitOnlyComment(ordered[1].Comment))
            {
                return false;
            }

            changed |= TrySetMarkerComment(
                markers,
                ordered[0].SampleOffset,
                WaveformRegionBuilder.LoopLeftSuffix,
                renames);
            changed |= TrySetMarkerComment(
                markers,
                ordered[1].SampleOffset,
                WaveformRegionBuilder.LoopEndSuffix,
                renames);
            return changed;
        }

        var loopKeywordMarkers = ordered
            .Where(marker => ContainsLoopKeyword(marker.Comment))
            .ToArray();
        if (loopKeywordMarkers.Length == 0)
        {
            return false;
        }

        if (loopKeywordMarkers.Length == 1)
        {
            return TrySetMarkerComment(
                markers,
                loopKeywordMarkers[0].SampleOffset,
                WaveformRegionBuilder.LoopLeftSuffix,
                renames);
        }

        for (var i = 0; i + 1 < loopKeywordMarkers.Length; i += 2)
        {
            changed |= TrySetMarkerComment(
                markers,
                loopKeywordMarkers[i].SampleOffset,
                WaveformRegionBuilder.LoopLeftSuffix,
                renames);
            changed |= TrySetMarkerComment(
                markers,
                loopKeywordMarkers[i + 1].SampleOffset,
                WaveformRegionBuilder.LoopEndSuffix,
                renames);
        }

        if (loopKeywordMarkers.Length % 2 == 1)
        {
            changed |= TrySetMarkerComment(
                markers,
                loopKeywordMarkers[^1].SampleOffset,
                WaveformRegionBuilder.LoopLeftSuffix,
                renames);
        }

        return changed;
    }

    private static bool TrySetMarkerComment(
        List<WaveformMarkerMark> markers,
        long sampleOffset,
        string comment,
        ICollection<MarkerCommentRename>? renames)
    {
        var changed = false;
        for (var i = 0; i < markers.Count; i++)
        {
            if (markers[i].SampleOffset != sampleOffset)
            {
                continue;
            }

            var previous = markers[i];
            if (string.Equals(previous.Comment, comment, StringComparison.Ordinal))
            {
                continue;
            }

            if (previous.IsFromWaveEmbedded)
            {
                renames?.Add(new MarkerCommentRename(previous.Comment, comment));
            }

            markers[i] = new WaveformMarkerMark(
                previous.SampleOffset,
                comment,
                previous.IsSharedProjection,
                previous.IsFromWaveEmbedded);
            changed = true;
        }

        return changed;
    }
}
