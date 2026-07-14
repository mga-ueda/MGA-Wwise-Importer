using System.Text;

namespace MgaWwiseImporter.UI;

/// <summary>
/// 簡易 INI（セクション単位のキー=値）。コメント行と未知セクションは保持する。
/// </summary>
internal static class IniFile
{
    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, "MgaWwiseImporter.ini");

    public static Dictionary<string, string> ReadSection(string section)
    {
        var path = Path;
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var inSection = false;

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inSection = string.Equals(line[1..^1], section, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inSection)
            {
                continue;
            }

            var separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            values[line[..separatorIndex].Trim()] = line[(separatorIndex + 1)..].Trim();
        }

        return values;
    }

    public static void WriteSection(string section, IReadOnlyDictionary<string, string> values)
    {
        var path = Path;
        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : [];

        var sectionHeader = $"[{section}]";
        var start = -1;
        var end = lines.Count;

        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
            {
                if (start >= 0)
                {
                    end = i;
                    break;
                }

                if (string.Equals(trimmed, sectionHeader, StringComparison.OrdinalIgnoreCase))
                {
                    start = i;
                }
            }
        }

        var replacement = new List<string> { sectionHeader };
        foreach (var pair in values)
        {
            replacement.Add($"{pair.Key}={pair.Value}");
        }

        if (start < 0)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
            {
                lines.Add(string.Empty);
            }

            lines.AddRange(replacement);
        }
        else
        {
            lines.RemoveRange(start, end - start);
            // 直前が空行でない場合はそのまま挿入（後続セクションとの区切りは既存に委ねる）
            lines.InsertRange(start, replacement);
            if (start + replacement.Count < lines.Count
                && lines[start + replacement.Count].Trim().Length > 0
                && lines[start + replacement.Count].Trim().StartsWith('['))
            {
                lines.Insert(start + replacement.Count, string.Empty);
            }
        }

        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }
}
