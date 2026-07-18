using System.Text;

namespace MgaWwiseIMImporter.Processing;

/// <summary>
/// ドロップされた Wave（＋任意で同名 XML）を読み、波形プレビュー用データを返す。
/// </summary>
internal static class DroppedFilesProcessor
{
    public static string Process(IEnumerable<string> paths, out WaveformPreviewData? preview)
    {
        WaveformPreviewData? lastPreview = null;
        var report = ProcessCore(paths, p => lastPreview = p);
        preview = lastPreview;
        return report;
    }

    private static string ProcessCore(IEnumerable<string> paths, Action<WaveformPreviewData>? preview)
    {
        var dropped = paths
            .Select(path => Path.GetFullPath(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Dropped files: {dropped.Count}");
        foreach (var file in dropped)
        {
            sb.AppendLine($"- {file}");
        }

        sb.AppendLine();

        var pairKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in dropped.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            var extension = Path.GetExtension(path);
            if (!extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                && !extension.Equals(".xml", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine("=== エラー ===");
                sb.AppendLine($"Path    : {path}");
                sb.AppendLine("Message : .wav または .xml をドロップしてください。");
                sb.AppendLine();
                continue;
            }

            var directory = Path.GetDirectoryName(path) ?? string.Empty;
            var baseName = Path.GetFileNameWithoutExtension(path);
            var pairKey = Path.Combine(directory, baseName);
            if (!pairKeys.Add(pairKey))
            {
                continue;
            }

            var wavPath = Path.Combine(directory, baseName + ".wav");
            var xmlPath = Path.Combine(directory, baseName + ".xml");
            ProcessPair(sb, wavPath, xmlPath, preview);
        }

        return sb.ToString();
    }

    private static void ProcessPair(
        StringBuilder sb,
        string wavPath,
        string xmlPath,
        Action<WaveformPreviewData>? preview)
    {
        var wavExists = File.Exists(wavPath);
        var xmlExists = File.Exists(xmlPath);

        if (!wavExists)
        {
            sb.AppendLine("=== エラー ===");
            sb.AppendLine($"Wave : {wavPath} (なし)");
            sb.AppendLine($"Xml  : {xmlPath} ({(xmlExists ? "あり" : "なし")})");
            sb.AppendLine("Message : 波形表示には .wav が必要です。");
            sb.AppendLine();
            return;
        }

        try
        {
            var wavInfo = WavFileInfo.Read(wavPath);
            sb.AppendLine(wavInfo.ToDisplayText());

            IReadOnlyList<WaveformBarMark> bars = [];
            IReadOnlyList<WaveformMarkerMark> markers = [];
            IReadOnlyList<WaveformCycleMark> cycles = [];
            IReadOnlyList<WaveformRegionMark> regions = [];
            IReadOnlyList<WaveformOutputPart> outputParts = [];
            WaveformBarOverlayResult? barOverlay = null;
            if (xmlExists)
            {
                var tracklist = NuendoTracklistInfo.Read(xmlPath);
                sb.AppendLine(tracklist.ToDisplayText());
                barOverlay = WaveformBarOverlayBuilder.Build(tracklist, wavInfo);
                bars = barOverlay.Marks;
                markers = barOverlay.Markers;
                cycles = barOverlay.Cycles;
                regions = barOverlay.Regions;
                outputParts = barOverlay.OutputParts;

                if (!barOverlay.HasIXml || barOverlay.TimeReferenceSamples == 0)
                {
                    sb.AppendLine("=== 警告 ===");
                    sb.AppendLine("Message : iXML の TimeReference が取れません（無し、または 0）。");
                    sb.AppendLine(
                        "Message : アウフタクト判定と小節位置の対応には iXML TimeReference が必要です。"
                        + " 0 のときは波形先頭＝PPQ 0 とみなします。");
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine("=== 警告 ===");
                sb.AppendLine($"Xml  : {xmlPath} (なし)");
                sb.AppendLine("Message : 同名 .xml が無いため小節線は表示しません。");
                sb.AppendLine();
            }

            var peaks = WavPeakReader.Read(wavInfo, peakCount: 2400);
            preview?.Invoke(new WaveformPreviewData(
                peaks,
                wavPath,
                wavInfo,
                bars,
                markers,
                cycles,
                regions,
                outputParts));

            sb.AppendLine("=== Waveform ===");
            sb.AppendLine($"Source : {wavPath}");
            sb.AppendLine($"Peaks  : {peaks.Mins.Length} buckets / {peaks.FrameCount:N0} frames");
            sb.AppendLine($"Regions: {regions.Count}");
            sb.AppendLine($"Outputs: {outputParts.Count}");
            foreach (var part in outputParts)
            {
                sb.AppendLine(
                    $"  - {part.FileName}"
                    + $"  samples=[{part.StartSampleOffset:N0} .. {part.EndSampleOffset:N0})");
            }
            sb.AppendLine($"Bars   : {bars.Count}");
            if (barOverlay is not null)
            {
                sb.AppendLine(
                    $"Timeline: TimeRef={barOverlay.TimeReferenceSamples:N0}"
                    + $"  waveStartPpq={barOverlay.WaveStartPpq:0.###}"
                    + $"  waveEndPpq={barOverlay.WaveEndPpq:0.###}"
                    + $"  prevBarPpq={FormatOptionalPpq(barOverlay.PreviousBarPpqAtWaveStart)}");
                if (barOverlay.HasAnacrusis)
                {
                    sb.AppendLine("Anacrusis : yes (relative Bar 1 @ wave start, next bar line = 2)");
                }
                else
                {
                    sb.AppendLine("Anacrusis : no (wave starts on a bar line → relative Bar 1)");
                }

                if (barOverlay.IgnoredOutsideMarks.Count > 0)
                {
                    sb.AppendLine("=== 波形範囲外（無視） ===");
                    sb.AppendLine(
                        "Message : 波形タイムライン外のマーカー／サイクルは描画せず、"
                        + "出力にも含めません。");
                    sb.AppendLine(
                        $"WavePpq : [{barOverlay.WaveStartPpq:0.###} .. {barOverlay.WaveEndPpq:0.###}]");
                    foreach (var ignored in barOverlay.IgnoredOutsideMarks)
                    {
                        var span = ignored.Kind == "Cycle"
                            ? $"PPQ=[{ignored.StartPpq:0.###} .. {ignored.EndPpq:0.###}]"
                            : $"PPQ={ignored.StartPpq:0.###}";
                        sb.AppendLine(
                            $"  - {ignored.Kind} \"{ignored.Name}\"  {span}"
                            + $"  ({ignored.Reason})");
                    }
                }
            }

            sb.AppendLine();
        }
        catch (Exception ex)
        {
            AppendError(sb, wavPath, ex);
        }
    }

    private static string FormatOptionalPpq(double? ppq)
    {
        return ppq is null ? "-" : ppq.Value.ToString("0.###");
    }

    private static void AppendError(StringBuilder sb, string path, Exception ex)
    {
        sb.AppendLine("=== エラー ===");
        sb.AppendLine($"Path    : {path}");
        sb.AppendLine($"Message : {ex.Message}");
        sb.AppendLine();
    }
}
