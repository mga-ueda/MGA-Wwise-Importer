using System.Text;
using MgaWwiseIMImporter.UI;

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
        sb.AppendLine(UiStrings.LogDroppedFilesHeader(dropped.Count));
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
                sb.AppendLine(UiStrings.LogErrorHeader);
                sb.AppendLine($"{UiStrings.KeyPath} {path}");
                sb.AppendLine(UiStrings.LogDropNeedWavOrXml);
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
            sb.AppendLine(UiStrings.LogErrorHeader);
            sb.AppendLine(UiStrings.LogWaveMissing(wavPath));
            sb.AppendLine(UiStrings.LogXmlPresence(xmlPath, xmlExists));
            sb.AppendLine(UiStrings.LogWaveRequired);
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
                    sb.AppendLine(UiStrings.LogWarningHeader);
                    sb.AppendLine(UiStrings.LogIxmlTimeRefMissing);
                    sb.AppendLine();
                }
            }
            else
            {
                sb.AppendLine(UiStrings.LogWarningHeader);
                sb.AppendLine(UiStrings.LogXmlMissing(xmlPath));
                sb.AppendLine(UiStrings.LogXmlMissingBars);
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

            sb.AppendLine(UiStrings.LogWaveformHeader);
            sb.AppendLine($"{UiStrings.KeySource} {wavPath}");
            sb.AppendLine(UiStrings.LogPeaksSummary(peaks.Mins.Length, peaks.FrameCount));
            sb.AppendLine($"{UiStrings.KeyRegions} {regions.Count}");
            sb.AppendLine($"{UiStrings.KeyOutputs} {outputParts.Count}");
            foreach (var part in outputParts)
            {
                sb.AppendLine(
                    $"  - {part.FileName}"
                    + $"  samples=[{part.StartSampleOffset:N0} .. {part.EndSampleOffset:N0})");
            }
            sb.AppendLine($"{UiStrings.KeyBars} {bars.Count}");
            if (barOverlay is not null)
            {
                sb.AppendLine(
                    $"{UiStrings.KeyTimeline} TimeRef={barOverlay.TimeReferenceSamples:N0}"
                    + $"  waveStartPpq={barOverlay.WaveStartPpq:0.###}"
                    + $"  waveEndPpq={barOverlay.WaveEndPpq:0.###}"
                    + $"  prevBarPpq={FormatOptionalPpq(barOverlay.PreviousBarPpqAtWaveStart)}");
                if (barOverlay.HasAnacrusis)
                {
                    sb.AppendLine(UiStrings.LogAnacrusisYes);
                }
                else
                {
                    sb.AppendLine(UiStrings.LogAnacrusisNo);
                }

                if (barOverlay.IgnoredOutsideMarks.Count > 0)
                {
                    sb.AppendLine(UiStrings.LogOutsideWaveHeader);
                    sb.AppendLine(UiStrings.LogOutsideWaveMessage);
                    sb.AppendLine(
                        $"{UiStrings.KeyWavePpq} [{barOverlay.WaveStartPpq:0.###} .. {barOverlay.WaveEndPpq:0.###}]");
                    foreach (var ignored in barOverlay.IgnoredOutsideMarks)
                    {
                        var span = ignored.Kind == "Cycle"
                            ? $"PPQ=[{ignored.StartPpq:0.###} .. {ignored.EndPpq:0.###}]"
                            : $"PPQ={ignored.StartPpq:0.###}";
                        sb.AppendLine(
                            $"  - {UiStrings.LabelIgnoredMarkKind(ignored.Kind)} \"{ignored.Name}\"  {span}"
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
        sb.AppendLine(UiStrings.LogErrorHeader);
        sb.AppendLine($"{UiStrings.KeyPath} {path}");
        sb.AppendLine($"{UiStrings.KeyMessage} {ex.Message}");
        sb.AppendLine();
    }
}
