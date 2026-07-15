using System.Globalization;

namespace MgaWwiseImporter.UI;

/// <summary>
/// アプリ全体の色定義。既定値はこのクラス、実行時の調整は [Colors] INI と開発者パネル。
/// </summary>
internal static class UiColors
{
    public const string IniSection = "Colors";

    // --- ウィンドウ／ログ ---
    public static Color WindowBack { get; set; } = Color.FromArgb(30, 30, 30);
    public static Color WindowFore { get; set; } = Color.White;
    public static Color LogBack { get; set; } = Color.FromArgb(30, 30, 30);
    public static Color LogDefault { get; set; } = Color.FromArgb(220, 220, 220);
    public static Color LogHeader { get; set; } = Color.FromArgb(110, 180, 255);
    public static Color LogWarning { get; set; } = Color.FromArgb(255, 180, 70);
    public static Color LogError { get; set; } = Color.FromArgb(255, 110, 110);
    public static Color LogMuted { get; set; } = Color.FromArgb(150, 150, 150);

    // --- 波形ビュー共通 ---
    public static Color WaveformBack { get; set; } = Color.FromArgb(30, 30, 30);
    public static Color WaveFill { get; set; } = Color.White;
    public static Color WaveCenter { get; set; } = Color.FromArgb(55, 55, 55);
    public static Color EmptyHint { get; set; } = Color.FromArgb(140, 140, 140);
    public static Color SeekCyan { get; set; } = Color.FromArgb(0, 245, 255);
    public static Color MouseGuide { get; set; } = Color.FromArgb(220, 255, 255, 255);
    public static Color BarLine { get; set; } = Color.FromArgb(90, 170, 170, 170);
    public static Color TempoChangeLine { get; set; } = Color.FromArgb(180, 180, 255, 180);

    // --- ラベル行（背景／文字） ---
    public static Color BarNumberBg { get; set; } = Color.FromArgb(43, 43, 45);
    public static Color BarNumberFg { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color TempoBg { get; set; } = Color.FromArgb(17, 60, 29);
    public static Color TempoFg { get; set; } = Color.FromArgb(230, 255, 230);
    public static Color SignatureBg { get; set; } = Color.FromArgb(98, 51, 14);
    public static Color SignatureFg { get; set; } = Color.FromArgb(255, 245, 230);
    public static Color MarkerRowBg { get; set; } = Color.FromArgb(43, 43, 45);
    public static Color MarkerFg { get; set; } = Color.FromArgb(245, 250, 255);
    public static Color MarkerTriangle { get; set; } = Color.FromArgb(255, 90, 200, 255);
    public static Color CycleRowBg { get; set; } = Color.FromArgb(28, 28, 30);
    public static Color CycleRangeFill { get; set; } = Color.FromArgb(200, 180, 40, 40);
    public static Color CycleFg { get; set; } = Color.FromArgb(255, 220, 220);
    public static Color RegionWaveFillRed { get; set; } = Color.FromArgb(95, 110, 22, 22);
    public static Color RegionWaveFillBlue { get; set; } = Color.FromArgb(95, 18, 38, 105);
    public static Color RegionWaveFillExcluded { get; set; } = Color.FromArgb(55, 50, 50, 55);
    public static Color OutputPartFg { get; set; } = Color.FromArgb(255, 255, 255);
    public static Color OutputPartShadow { get; set; } = Color.FromArgb(230, 0, 0, 0);
    public static Color ExportPartGlow { get; set; } = Color.FromArgb(255, 0, 245, 255);

    /// <summary>パネル表示用の一覧（キー＝INI、日本語ラベル）。</summary>
    public static IReadOnlyList<UiColorEntry> Entries { get; } =
    [
        new("WindowBack", "ウィンドウ背景", () => WindowBack, c => WindowBack = c),
        new("WindowFore", "ウィンドウ文字", () => WindowFore, c => WindowFore = c),
        new("LogBack", "ログ背景", () => LogBack, c => LogBack = c),
        new("LogDefault", "ログ文字（既定）", () => LogDefault, c => LogDefault = c),
        new("LogHeader", "ログ文字（ヘッダ）", () => LogHeader, c => LogHeader = c),
        new("LogWarning", "ログ文字（警告）", () => LogWarning, c => LogWarning = c),
        new("LogError", "ログ文字（エラー）", () => LogError, c => LogError = c),
        new("LogMuted", "ログ文字（弱）", () => LogMuted, c => LogMuted = c),
        new("WaveformBack", "波形エリア背景", () => WaveformBack, c => WaveformBack = c),
        new("WaveFill", "波形", () => WaveFill, c => WaveFill = c),
        new("WaveCenter", "波形センター線", () => WaveCenter, c => WaveCenter = c),
        new("EmptyHint", "空状態ヒント", () => EmptyHint, c => EmptyHint = c),
        new("SeekCyan", "再生ヘッド", () => SeekCyan, c => SeekCyan = c),
        new("MouseGuide", "マウスガイド", () => MouseGuide, c => MouseGuide = c),
        new("BarLine", "小節線", () => BarLine, c => BarLine = c),
        new("TempoChangeLine", "テンポ変更線", () => TempoChangeLine, c => TempoChangeLine = c),
        new("BarNumberBg", "小節番号・背景", () => BarNumberBg, c => BarNumberBg = c),
        new("BarNumberFg", "小節番号・文字", () => BarNumberFg, c => BarNumberFg = c),
        new("TempoBg", "テンポ・背景", () => TempoBg, c => TempoBg = c),
        new("TempoFg", "テンポ・文字", () => TempoFg, c => TempoFg = c),
        new("SignatureBg", "拍子・背景", () => SignatureBg, c => SignatureBg = c),
        new("SignatureFg", "拍子・文字", () => SignatureFg, c => SignatureFg = c),
        new("MarkerRowBg", "マーカー行・背景", () => MarkerRowBg, c => MarkerRowBg = c),
        new("MarkerFg", "マーカー・文字", () => MarkerFg, c => MarkerFg = c),
        new("MarkerTriangle", "マーカー三角", () => MarkerTriangle, c => MarkerTriangle = c),
        new("CycleRowBg", "サイクル行・背景", () => CycleRowBg, c => CycleRowBg = c),
        new("CycleRangeFill", "サイクル範囲", () => CycleRangeFill, c => CycleRangeFill = c),
        new("CycleFg", "サイクル・文字", () => CycleFg, c => CycleFg = c),
        new("RegionWaveFillRed", "リージョン塗り（赤）", () => RegionWaveFillRed, c => RegionWaveFillRed = c),
        new("RegionWaveFillBlue", "リージョン塗り（青）", () => RegionWaveFillBlue, c => RegionWaveFillBlue = c),
        new("RegionWaveFillExcluded", "リージョン塗り（除外）", () => RegionWaveFillExcluded, c => RegionWaveFillExcluded = c),
        new("OutputPartFg", "出力パート名・文字", () => OutputPartFg, c => OutputPartFg = c),
        new("OutputPartShadow", "出力パート名・影", () => OutputPartShadow, c => OutputPartShadow = c),
        new("ExportPartGlow", "書き出し中パート枠", () => ExportPartGlow, c => ExportPartGlow = c),
    ];

    private static readonly Dictionary<string, Color> Defaults = Entries
        .ToDictionary(e => e.Key, e => e.Get(), StringComparer.OrdinalIgnoreCase);

    public static void ResetToDefaults()
    {
        foreach (var entry in Entries)
        {
            entry.Set(Defaults[entry.Key]);
        }
    }

    public static void LoadFromIni()
    {
        var values = IniFile.ReadSection(IniSection);
        if (values.Count == 0)
        {
            return;
        }

        foreach (var entry in Entries)
        {
            if (values.TryGetValue(entry.Key, out var text) && TryParseColor(text, out var color))
            {
                entry.Set(color);
            }
        }
    }

    public static void SaveToIni()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            values[entry.Key] = FormatColor(entry.Get());
        }

        IniFile.WriteSection(IniSection, values);
    }

    public static string FormatColor(Color color) =>
        $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static bool TryParseColor(string text, out Color color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (text.StartsWith('#'))
        {
            var hex = text[1..];
            if (hex.Length == 6
                && int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
            {
                color = Color.FromArgb(
                    255,
                    (rgb >> 16) & 0xFF,
                    (rgb >> 8) & 0xFF,
                    rgb & 0xFF);
                return true;
            }

            if (hex.Length == 8
                && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
            {
                color = Color.FromArgb(
                    (int)((argb >> 24) & 0xFF),
                    (int)((argb >> 16) & 0xFF),
                    (int)((argb >> 8) & 0xFF),
                    (int)(argb & 0xFF));
                return true;
            }

            return false;
        }

        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3
            && byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            color = Color.FromArgb(r, g, b);
            return true;
        }

        if (parts.Length == 4
            && byte.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
            && byte.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out r)
            && byte.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out g)
            && byte.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out b))
        {
            color = Color.FromArgb(a, r, g, b);
            return true;
        }

        return false;
    }
}

internal sealed class UiColorEntry
{
    private readonly Func<Color> _getter;
    private readonly Action<Color> _setter;

    public UiColorEntry(string key, string label, Func<Color> getter, Action<Color> setter)
    {
        Key = key;
        Label = label;
        _getter = getter;
        _setter = setter;
    }

    public string Key { get; }
    public string Label { get; }
    public Color Get() => _getter();
    public void Set(Color color) => _setter(color);
}
