using System.Globalization;
using System.Text;

namespace MgaWwiseImporter.Processing;

internal static class DroppedFilesProcessor
{
    public static string Process(IEnumerable<string> paths)
    {
        return ProcessCore(paths, outputPaths: null);
    }

    /// <summary>ログ文言と、書き出しに成功した出力 Wave パスを返す。</summary>
    public static IReadOnlyList<string> ProcessAndGetOutputs(IEnumerable<string> paths, out string report)
    {
        var outputs = new List<string>();
        report = ProcessCore(paths, outputs);
        return outputs;
    }

    private static string ProcessCore(IEnumerable<string> paths, List<string>? outputPaths)
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
            ProcessPair(sb, wavPath, xmlPath, outputPaths);
        }

        return sb.ToString();
    }

    private static void ProcessPair(
        StringBuilder sb,
        string wavPath,
        string xmlPath,
        List<string>? outputPaths)
    {
        var wavExists = File.Exists(wavPath);
        var xmlExists = File.Exists(xmlPath);

        if (!wavExists || !xmlExists)
        {
            sb.AppendLine("=== エラー ===");
            sb.AppendLine($"Wave : {wavPath} ({(wavExists ? "あり" : "なし")})");
            sb.AppendLine($"Xml  : {xmlPath} ({(xmlExists ? "あり" : "なし")})");

            if (!wavExists && !xmlExists)
            {
                sb.AppendLine("Message : ペアとなる .wav / .xml のどちらも見つかりません。");
            }
            else if (!wavExists)
            {
                sb.AppendLine("Message : ペアとなる .wav が見つかりません。");
            }
            else
            {
                sb.AppendLine("Message : ペアとなる .xml が見つかりません。");
            }

            sb.AppendLine();
            return;
        }

        try
        {
            var wavInfo = WavFileInfo.Read(wavPath);
            sb.AppendLine(wavInfo.ToDisplayText());

            var tracklist = NuendoTracklistInfo.Read(xmlPath);
            sb.AppendLine(tracklist.ToDisplayText());
            sb.AppendLine(WriteEmbeddedWave(wavPath, wavInfo, tracklist, outputPaths));
        }
        catch (Exception ex)
        {
            AppendError(sb, wavPath, ex);
        }
    }

    private static string WriteEmbeddedWave(
        string wavPath,
        WavFileInfo wavInfo,
        NuendoTracklistInfo tracklist,
        List<string>? outputPaths)
    {
        var buildResult = WavMarkerEmbedder.BuildCueItems(tracklist, wavInfo);
        var cues = buildResult.Cues;
        var outputPath = WavMarkerEmbedder.BuildOutputPath(wavPath);
        WavCueWriter.Write(wavPath, outputPath, cues);
        outputPaths?.Add(outputPath);

        var outputInfo = new FileInfo(outputPath);
        var regionCount = cues.Count(cue => cue.IsRegion);
        var markerCount = cues.Count(cue => !cue.IsRegion);

        var sb = new StringBuilder();
        sb.AppendLine("=== Embedded Wave ===");
        sb.AppendLine($"Output : {outputPath}");
        sb.AppendLine($"Offset : {wavInfo.TimeReferenceSamples:N0} samples (bext TimeReference)");
        sb.AppendLine($"Cues   : {cues.Count} (Regions={regionCount}, Markers={markerCount})");
        sb.AppendLine();

        if (buildResult.Warnings.Count > 0)
        {
            sb.AppendLine("=== 警告 ===");
            foreach (var warning in buildResult.Warnings)
            {
                sb.AppendLine(warning);
            }

            sb.AppendLine();
        }

        foreach (var cue in cues)
        {
            var kind = cue.IsRegion ? "Region" : "Marker";
            var displayName = cue.IsRegion && !string.IsNullOrWhiteSpace(cue.Comment)
                ? (string.IsNullOrWhiteSpace(cue.Name) ? cue.Comment : $"{cue.Comment} {cue.Name}")
                : cue.Name;
            sb.AppendLine(
                $"{kind}#{cue.Id}"
                + $"  Sample={cue.SampleOffset.ToString(CultureInfo.InvariantCulture)}"
                + (cue.IsRegion
                    ? $"  Length={cue.SampleLength.ToString(CultureInfo.InvariantCulture)}"
                    : string.Empty)
                + $"  Name=\"{displayName}\""
                + $"  Comment={cue.Comment}");
        }

        sb.AppendLine();
        sb.AppendLine("=== Write complete ===");
        sb.AppendLine($"File   : {outputPath}");
        sb.AppendLine($"Size   : {outputInfo.Length:N0} bytes");
        sb.AppendLine($"Time   : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void AppendError(StringBuilder sb, string path, Exception ex)
    {
        sb.AppendLine("=== エラー ===");
        sb.AppendLine($"Path    : {path}");
        sb.AppendLine($"Message : {ex.Message}");
        sb.AppendLine();
    }
}
