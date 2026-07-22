namespace MgaWwiseIMImporter.Wave;

/// <summary>
/// 複数波形モード向けに、仮想タイムライン上のマーカーからリージョン／出力パートを再構築する。
/// 1 ファイル＝1 プレイリスト区間として <see cref="WaveOnlyModeProcessor"/> を呼び、
/// 「マーカー 2 つ → 暗黙ループ」もそのプレイリスト区間内だけで判定する。
/// ファイル境界でパートを必ず分断する。
/// </summary>
internal static class MultiWaveOnlyRegionBuilder
{
    /// <summary>
    /// 仮想座標のマーカーとソース区間から、連結リージョンと出力パートを返す。
    /// 各 <paramref name="spans"/> を 1 プレイリストとして独立にリージョン構築する。
    /// </summary>
    public static (IReadOnlyList<WaveformRegionMark> Regions, IReadOnlyList<WaveformOutputPart> Parts) Build(
        IReadOnlyList<WaveformMarkerMark> virtualMarkers,
        IReadOnlyList<WaveformSourceSpan> spans)
    {
        if (spans.Count == 0)
        {
            return ([], []);
        }

        var allRegions = new List<WaveformRegionMark>();
        var allParts = new List<WaveformOutputPart>();
        var nextPartNumber = 1;

        for (var i = 0; i < spans.Count; i++)
        {
            var span = spans[i];
            if (i > 0)
            {
                // 長さ 0 の除外リージョンでファイル境界を明示し、パート連結を防ぐ。
                allRegions.Add(new WaveformRegionMark(
                    span.VirtualStartSample,
                    span.VirtualStartSample,
                    IsExcluded: true));
            }

            var localMarkers = new List<WaveformMarkerMark>();
            foreach (var marker in virtualMarkers)
            {
                if (marker.SampleOffset < span.VirtualStartSample
                    || marker.SampleOffset >= span.VirtualEndSample)
                {
                    continue;
                }

                localMarkers.Add(marker with
                {
                    SampleOffset = marker.SampleOffset - span.VirtualStartSample,
                });
            }

            var localRegions = WaveOnlyModeProcessor.BuildRegionsFromMarkers(
                localMarkers,
                span.FrameCount);
            foreach (var region in localRegions)
            {
                allRegions.Add(region with
                {
                    StartSampleOffset = region.StartSampleOffset + span.VirtualStartSample,
                    EndSampleOffset = region.EndSampleOffset + span.VirtualStartSample,
                });
            }

            if (localRegions.Count == 0)
            {
                continue;
            }

            var localParts = WaveformRegionBuilder.BuildOutputParts(localRegions, span.Path);
            var originalFileName = Path.GetFileName(span.Path);
            if (string.IsNullOrEmpty(originalFileName))
            {
                originalFileName = "wave.wav";
            }

            var originalBaseName = Path.GetFileNameWithoutExtension(originalFileName);
            if (string.IsNullOrEmpty(originalBaseName))
            {
                originalBaseName = "wave";
            }

            for (var partIndex = 0; partIndex < localParts.Count; partIndex++)
            {
                var part = localParts[partIndex];
                // ドロップ名を維持。同一ファイルから複数パートが出るときだけ _2 以降を付ける。
                var fileName = localParts.Count == 1
                    ? originalFileName
                    : partIndex == 0
                        ? originalFileName
                        : $"{originalBaseName}_{partIndex + 1}.wav";

                allParts.Add(new WaveformOutputPart(
                    nextPartNumber,
                    part.StartSampleOffset + span.VirtualStartSample,
                    part.EndSampleOffset + span.VirtualStartSample,
                    fileName,
                    SourcePath: span.Path,
                    LocalStartSample: part.StartSampleOffset,
                    LocalEndSample: part.EndSampleOffset));
                nextPartNumber++;
            }
        }

        return (allRegions, allParts);
    }
}
