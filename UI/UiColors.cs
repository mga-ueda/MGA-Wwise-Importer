using System.Globalization;

namespace MgaWwiseIMImporter.UI;

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
    public static Color StatusBarBack { get; set; } = Color.FromArgb(22, 22, 24);
    public static Color StatusBarBorder { get; set; } = Color.FromArgb(55, 55, 58);

    // --- 波形ビュー共通 ---
    public static Color WaveformBack { get; set; } = Color.FromArgb(38, 38, 38);
    public static Color WaveFill { get; set; } = Color.FromArgb(182, 182, 182);
    public static Color WaveCenter { get; set; } = Color.FromArgb(55, 55, 55);
    public static Color EmptyHint { get; set; } = Color.FromArgb(140, 140, 140);
    public static Color SeekCyan { get; set; } = Color.FromArgb(0, 245, 255);
    /// <summary>-E 二重再生用のシークバー／軌跡。</summary>
    public static Color SeekExit { get; set; } = Color.FromArgb(255, 220, 45, 45);
    public static Color MouseGuide { get; set; } = Color.FromArgb(220, 255, 255, 255);
    public static Color BarLine { get; set; } = Color.FromArgb(90, 170, 170, 170);
    public static Color TempoChangeLine { get; set; } = Color.FromArgb(180, 180, 255, 180);

    // --- ラベル行（背景／文字） ---
    public static Color BarNumberBg { get; set; } = Color.FromArgb(71, 71, 73);
    public static Color TempoBg { get; set; } = Color.FromArgb(53, 55, 70);
    public static Color SignatureBg { get; set; } = Color.FromArgb(44, 46, 58);
    public static Color MarkerRowBg { get; set; } = Color.FromArgb(43, 43, 45);
    /// <summary>波形情報エリア（小節／テンポ／拍子／マーカー）の文字色。</summary>
    public static Color WaveformInfoFg { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color MarkerTriangle { get; set; } = Color.FromArgb(255, 255, 128, 64);
    /// <summary>波形リージョン塗り（接尾辞なし）。</summary>
    public static Color RegionWaveFillGray { get; set; } = Color.FromArgb(255, 54, 54, 58);
    /// <summary>波形リージョン塗り（-R）。波形の上に重ねる。</summary>
    public static Color RegionWaveFillExcluded { get; set; } = Color.FromArgb(255, 31, 31, 33);
    /// <summary>波形リージョン塗り（-L）。</summary>
    public static Color RegionWaveFillLoop { get; set; } = Color.FromArgb(255, 26, 62, 94);
    /// <summary>波形リージョン塗り（-A）。</summary>
    public static Color RegionWaveFillAnacrusis { get; set; } = Color.FromArgb(255, 52, 70, 32);
    /// <summary>波形リージョン塗り（-E）。</summary>
    public static Color RegionWaveFillExit { get; set; } = Color.FromArgb(255, 68, 30, 30);
    public static Color OutputPartFg { get; set; } = Color.FromArgb(255, 255, 255);
    public static Color OutputPartShadow { get; set; } = Color.FromArgb(230, 0, 0, 0);
    public static Color ExportPartGlow { get; set; } = Color.FromArgb(255, 0, 245, 255);
    /// <summary>リージョン固まり境界の縦線・半三角。</summary>
    public static Color RegionBoundaryMarker { get; set; } = Color.FromArgb(195, 195, 195);
    /// <summary>Wwise Entry Cue の縦線・半三角（開始形）。</summary>
    public static Color EntryCueMarker { get; set; } = Color.FromArgb(255, 163, 195, 80);
    /// <summary>Wwise Exit Cue の縦線・半三角（終了形）。</summary>
    public static Color ExitCueMarker { get; set; } = Color.FromArgb(255, 186, 0, 0);

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
        new("StatusBarBack", "ステータスバー背景", () => StatusBarBack, c => StatusBarBack = c),
        new("StatusBarBorder", "ステータスバー上線", () => StatusBarBorder, c => StatusBarBorder = c),
        new("WaveformBack", "波形エリア背景", () => WaveformBack, c => WaveformBack = c),
        new("WaveFill", "波形", () => WaveFill, c => WaveFill = c),
        new("WaveCenter", "波形センター線", () => WaveCenter, c => WaveCenter = c),
        new("EmptyHint", "空状態ヒント", () => EmptyHint, c => EmptyHint = c),
        new("SeekCyan", "再生ヘッド", () => SeekCyan, c => SeekCyan = c),
        new("SeekExit", "Exit 二重再生ヘッド", () => SeekExit, c => SeekExit = c),
        new("MouseGuide", "マウスガイド", () => MouseGuide, c => MouseGuide = c),
        new("BarLine", "小節線", () => BarLine, c => BarLine = c),
        new("TempoChangeLine", "テンポ変更線", () => TempoChangeLine, c => TempoChangeLine = c),
        new("BarNumberBg", "小節番号・背景", () => BarNumberBg, c => BarNumberBg = c),
        new("TempoBg", "テンポ・背景", () => TempoBg, c => TempoBg = c),
        new("SignatureBg", "拍子・背景", () => SignatureBg, c => SignatureBg = c),
        new("MarkerRowBg", "マーカー行・背景", () => MarkerRowBg, c => MarkerRowBg = c),
        new("WaveformInfoFg", "波形情報・文字", () => WaveformInfoFg, c => WaveformInfoFg = c),
        new("MarkerTriangle", "マーカー三角", () => MarkerTriangle, c => MarkerTriangle = c),
        new("RegionWaveFillGray", "波形リージョン塗り（通常）", () => RegionWaveFillGray, c => RegionWaveFillGray = c),
        new("RegionWaveFillExcluded", "波形リージョン塗り（-R）", () => RegionWaveFillExcluded, c => RegionWaveFillExcluded = c),
        new("RegionWaveFillLoop", "波形リージョン塗り（-L）", () => RegionWaveFillLoop, c => RegionWaveFillLoop = c),
        new("RegionWaveFillAnacrusis", "波形リージョン塗り（-A）", () => RegionWaveFillAnacrusis, c => RegionWaveFillAnacrusis = c),
        new("RegionWaveFillExit", "波形リージョン塗り（-E）", () => RegionWaveFillExit, c => RegionWaveFillExit = c),
        new("OutputPartFg", "出力パート名・文字", () => OutputPartFg, c => OutputPartFg = c),
        new("OutputPartShadow", "出力パート名・影", () => OutputPartShadow, c => OutputPartShadow = c),
        new("ExportPartGlow", "書き出し中パート枠", () => ExportPartGlow, c => ExportPartGlow = c),
        new("RegionBoundaryMarker", "リージョン境界マーカー", () => RegionBoundaryMarker, c => RegionBoundaryMarker = c),
        new("EntryCueMarker", "Entry Cue マーカー", () => EntryCueMarker, c => EntryCueMarker = c),
        new("ExitCueMarker", "Exit Cue マーカー", () => ExitCueMarker, c => ExitCueMarker = c),
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
        EnsureObsoleteKeysRemoved();

        var values = IniFile.ReadSection(IniSection);
        if (values.Count == 0)
        {
            return;
        }

        foreach (var entry in Entries)
        {
            if (values.TryGetValue(entry.Key, out var text) && TryParseColor(text, out var color))
            {
                // アルファはコード既定を使う（INI / 旧パネルで A=0 になっても起動不能にしない）
                var alpha = Defaults.TryGetValue(entry.Key, out var def) ? def.A : (byte)255;
                entry.Set(Color.FromArgb(alpha, color.R, color.G, color.B));
            }
        }
    }

    /// <summary>廃止キー（暗転％・半透明％・旧サイクル行色）を INI から除去する。</summary>
    public static void EnsureObsoleteKeysRemoved()
    {
        var values = IniFile.ReadSection(IniSection);
        string[] obsolete =
        [
            "RegionWaveFillDarkenPercent",
            "RegionWaveFillTransparencyPercent",
            "CycleRowBg",
            "CycleRangeFill",
            "CycleFg",
            "BarNumberFg",
            "TempoFg",
            "SignatureFg",
            "MarkerFg",
        ];

        var changed = false;
        foreach (var key in obsolete)
        {
            if (values.Remove(key))
            {
                changed = true;
            }
        }

        if (changed)
        {
            IniFile.WriteSection(IniSection, values);
        }
    }

    public static void SaveToIni()
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in Entries)
        {
            // 保存時もアルファは既定に揃える（パネルで RGB だけ変える運用）
            var color = entry.Get();
            var alpha = GetDefaultAlpha(entry.Key);
            var normalized = Color.FromArgb(alpha, color.R, color.G, color.B);
            entry.Set(normalized);
            values[entry.Key] = FormatColor(normalized);
        }

        IniFile.WriteSection(IniSection, values);
    }

    /// <summary>エントリのコード既定アルファ（無ければ 255）。</summary>
    public static int GetDefaultAlpha(string key) =>
        Defaults.TryGetValue(key, out var def) ? def.A : 255;

    /// <summary>WinForms コントロール背景用（アルファ非対応のため不透明化）。</summary>
    public static Color ForControlBack(Color color) =>
        Color.FromArgb(255, color.R, color.G, color.B);

    /// <summary>INI／パネル用。RGB のみ（<c>#RRGGBB</c>）。アルファは各色のコード既定を使う。</summary>
    public static string FormatColor(Color color) =>
        $"#{color.R:X2}{color.G:X2}{color.B:X2}";

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
            text = text[1..];
        }

        // #RRGGBB / RRGGBB（および旧 #AARRGGBB）
        if (text.Length == 6
            && int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            color = Color.FromArgb(
                255,
                (rgb >> 16) & 0xFF,
                (rgb >> 8) & 0xFF,
                rgb & 0xFF);
            return true;
        }

        if (text.Length == 8
            && uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb))
        {
            color = Color.FromArgb(
                (int)((argb >> 24) & 0xFF),
                (int)((argb >> 16) & 0xFF),
                (int)((argb >> 8) & 0xFF),
                (int)(argb & 0xFF));
            return true;
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
