using System.Globalization;
using System.Text;

namespace MgaWwiseIMImporter.Wave;

/// <summary>
/// 出力計画（<see cref="WaveformOutputPart"/>）に従い、リージョン／マーカー付き WAV を分割書き出しする。
/// 除外区画（-R など）は書き出さず、その前後でファイルが分かれる。
/// </summary>
internal static class WaveformExporter
{
    public static string Export(
        string sourcePath,
        WavFileInfo wavInfo,
        IReadOnlyList<WaveformOutputPart> outputParts,
        IReadOnlyList<WaveformRegionMark> regions,
        IReadOnlyList<WaveformBarMark> bars,
        IReadOnlyList<WaveformMarkerMark> markers,
        string? outputDirectory = null,
        Action<WaveformOutputPart>? onPartBegin = null,
        Action<WaveformOutputPart>? onPartEnd = null,
        Action<string>? onLog = null)
    {
        var sb = new StringBuilder();

        void Log(string text)
        {
            sb.Append(text);
            onLog?.Invoke(text);
        }

        Log("=== Export ===" + Environment.NewLine);
        Log($"Source : {sourcePath}" + Environment.NewLine);
        Log($"Parts  : {outputParts.Count}" + Environment.NewLine);

        if (outputParts.Count == 0)
        {
            Log("Message : 出力パートが無いため書き出しをスキップします。" + Environment.NewLine);
            Log(Environment.NewLine);
            return sb.ToString();
        }

        var directory = string.IsNullOrWhiteSpace(outputDirectory)
            ? Path.GetDirectoryName(sourcePath) ?? string.Empty
            : outputDirectory.Trim();
        if (directory.Length > 0)
        {
            Directory.CreateDirectory(directory);
        }

        Log($"OutputDir : {directory}" + Environment.NewLine);
        Log(Environment.NewLine);

        var written = 0;
        foreach (var part in outputParts)
        {
            onPartBegin?.Invoke(part);

            var destPath = Path.Combine(directory, part.FileName);
            try
            {
                var cues = BuildCuesForPart(part, regions, bars, markers);
                WavCueWriter.WriteSegment(
                    sourcePath,
                    destPath,
                    part.StartSampleOffset,
                    part.EndSampleOffset,
                    wavInfo.BlockAlign,
                    cues);

                var info = new FileInfo(destPath);
                var frames = part.EndSampleOffset - part.StartSampleOffset;
                var durationSec = wavInfo.SampleRate == 0
                    ? 0d
                    : frames / (double)wavInfo.SampleRate;
                var regionCount = cues.Count(c => c.IsRegion);
                var markerCount = cues.Count - regionCount;

                var partLog = new StringBuilder();
                partLog.AppendLine($"--- Part #{part.Number} ---");
                partLog.AppendLine($"File    : {destPath}");
                partLog.AppendLine(
                    $"Samples : [{part.StartSampleOffset:N0} .. {part.EndSampleOffset:N0})"
                    + $"  frames={frames:N0}"
                    + $"  duration={durationSec:0.000}s");
                partLog.AppendLine($"Size    : {info.Length:N0} bytes");
                partLog.AppendLine($"Cues    : {cues.Count} (Regions={regionCount}, Markers={markerCount})");
                foreach (var cue in cues)
                {
                    var kind = cue.IsRegion ? "Region" : "Marker";
                    partLog.AppendLine(
                        $"  - {kind}#{cue.Id}"
                        + $"  sample={cue.SampleOffset.ToString(CultureInfo.InvariantCulture)}"
                        + (cue.IsRegion
                            ? $"  length={cue.SampleLength.ToString(CultureInfo.InvariantCulture)}"
                            : string.Empty)
                        + $"  \"{cue.Comment}\"");
                }

                partLog.AppendLine();
                Log(partLog.ToString());
                written++;
            }
            catch (Exception ex)
            {
                Log("=== エラー ===" + Environment.NewLine);
                Log($"File    : {destPath}" + Environment.NewLine);
                Log($"Message : {ex.Message}" + Environment.NewLine);
                Log(Environment.NewLine);
            }
            finally
            {
                // 成功／失敗いずれでも「このパートの書き出し終わり」を UI 側へ伝える
                onPartEnd?.Invoke(part);
            }
        }

        Log("=== Export complete ===" + Environment.NewLine);
        Log($"Written : {written} / {outputParts.Count}" + Environment.NewLine);
        Log($"Time    : {DateTime.Now:yyyy-MM-dd HH:mm:ss}" + Environment.NewLine);
        Log(Environment.NewLine);
        return sb.ToString();
    }

    private static IReadOnlyList<WavCueItem> BuildCuesForPart(
        WaveformOutputPart part,
        IReadOnlyList<WaveformRegionMark> regions,
        IReadOnlyList<WaveformBarMark> bars,
        IReadOnlyList<WaveformMarkerMark> markers)
    {
        var cues = new List<WavCueItem>();
        uint nextId = 1;

        foreach (var region in regions)
        {
            if (region.IsExcluded)
            {
                continue;
            }

            // パート範囲と重なる着色リージョンのみ（パートは連続着色のまとまりなので通常は完全包含）
            var start = Math.Max(region.StartSampleOffset, part.StartSampleOffset);
            var end = Math.Min(region.EndSampleOffset, part.EndSampleOffset);
            if (end <= start)
            {
                continue;
            }

            var localStart = start - part.StartSampleOffset;
            var length = end - start;
            // NameSuffix は RegionBuilder が付与（例: "-L" / "-A" / 自動 "-E"。-R は除外のためここには来ない）
            var comment = BuildRegionComment(start, bars) + region.NameSuffix;

            cues.Add(new WavCueItem
            {
                Id = nextId++,
                SampleOffset = localStart,
                SampleLength = length,
                Comment = comment,
                IsRegion = true,
            });
        }

        foreach (var marker in markers)
        {
            if (marker.SampleOffset < part.StartSampleOffset
                || marker.SampleOffset >= part.EndSampleOffset)
            {
                continue;
            }

            var comment = marker.Comment?.Trim() ?? string.Empty;
            cues.Add(new WavCueItem
            {
                Id = nextId++,
                SampleOffset = marker.SampleOffset - part.StartSampleOffset,
                SampleLength = 0,
                Comment = comment,
                IsRegion = false,
            });
        }

        // cue チャンク上はサンプル位置順の方が扱いやすい
        cues.Sort((a, b) =>
        {
            var cmp = a.SampleOffset.CompareTo(b.SampleOffset);
            if (cmp != 0)
            {
                return cmp;
            }

            // 同一位置ならリージョンを先に
            return b.IsRegion.CompareTo(a.IsRegion);
        });

        // Id を並び順に振り直す
        for (var i = 0; i < cues.Count; i++)
        {
            cues[i] = new WavCueItem
            {
                Id = (uint)(i + 1),
                SampleOffset = cues[i].SampleOffset,
                SampleLength = cues[i].SampleLength,
                Comment = cues[i].Comment,
                IsRegion = cues[i].IsRegion,
            };
        }

        return cues;
    }

    private static string BuildRegionComment(long regionStartSample, IReadOnlyList<WaveformBarMark> bars)
    {
        WaveformBarMark? barHead = null;
        WaveformBarMark? any = null;
        foreach (var bar in bars.OrderBy(b => b.SampleOffset))
        {
            if (bar.SampleOffset > regionStartSample)
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
            return string.Empty;
        }

        var bpmText = Math.Round(mark.Bpm, MidpointRounding.AwayFromZero)
            .ToString(CultureInfo.InvariantCulture);
        return $"T{bpmText}-{mark.Numerator}/{mark.Denominator}";
    }
}
