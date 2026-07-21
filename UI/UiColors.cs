using System.Globalization;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// アプリ全体の色定義。既定値はこのクラス、実行時の調整は [Colors] INI と開発者パネル。
/// </summary>
internal static class UiColors
{
    public const string IniSection = "Colors";

    // --- 共通トークン ---
    public static Color PrimaryFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color MutedFore { get; set; } = Color.FromArgb(150, 150, 150);
    public static Color AccentCyan { get; set; } = Color.FromArgb(0, 245, 255);
    public static Color SurfaceBack { get; set; } = Color.FromArgb(30, 30, 30);
    public static Color ChromeBack { get; set; } = Color.FromArgb(26, 27, 38);
    public static Color ChromeBorder { get; set; } = Color.FromArgb(55, 55, 58);
    public static Color ChromeMid { get; set; } = Color.FromArgb(72, 72, 72);
    public static Color ChromeDim { get; set; } = Color.FromArgb(91, 91, 91);

    // 用途名は描画コードの可読性のために残し、設定値は共通トークンへ集約する。

    // --- ウィンドウ ---
    public static Color WindowBack => SurfaceBack;
    public static Color WindowFore => PrimaryFore;

    // --- 波形ビュー（画面上部） ---
    public static Color WaveformBack { get; set; } = Color.FromArgb(38, 38, 38);
    public static Color EmptyHint { get; set; } = Color.FromArgb(140, 140, 140);
    public static Color BarNumberBg => ChromeMid;
    public static Color TempoBg { get; set; } = Color.FromArgb(53, 55, 70);
    public static Color SignatureBg { get; set; } = Color.FromArgb(44, 46, 58);
    public static Color MarkerRowBg { get; set; } = Color.FromArgb(43, 43, 45);
    public static Color WaveformInfoFg => PrimaryFore;
    public static Color MarkerTriangle { get; set; } = Color.FromArgb(255, 255, 128, 64);
    public static Color BarLine { get; set; } = Color.FromArgb(90, 204, 204, 204);
    public static Color BeatLine { get; set; } = Color.FromArgb(45, 170, 170, 170);
    public static Color TempoChangeLine { get; set; } = Color.FromArgb(180, 180, 255, 180);
    public static Color WaveFill { get; set; } = Color.FromArgb(182, 182, 182);
    public static Color WaveCenter { get; set; } = Color.FromArgb(55, 55, 55);
    public static Color WaveformSourceMeterTrack { get; set; } = Color.FromArgb(18, 18, 18);
    public static Color WaveformSourceMeterMinimum { get; set; } = Color.FromArgb(47, 23, 0);
    public static Color WaveformSourceMeterMaximum { get; set; } = Color.FromArgb(255, 128, 0);
    public static Color RegionWaveFillGray { get; set; } = Color.FromArgb(255, 54, 54, 58);
    public static Color RegionWaveFillExcluded { get; set; } = Color.FromArgb(255, 31, 31, 33);
    public static Color RegionWaveFillLoop { get; set; } = Color.FromArgb(255, 26, 62, 94);
    public static Color RegionWaveFillAnacrusis { get; set; } = Color.FromArgb(255, 52, 70, 32);
    public static Color RegionWaveFillExit { get; set; } = Color.FromArgb(255, 68, 30, 30);
    public static Color RegionBoundaryMarker { get; set; } = Color.FromArgb(195, 195, 195);
    public static Color EntryCueMarker { get; set; } = Color.FromArgb(255, 163, 195, 80);
    public static Color ExitCueMarker { get; set; } = Color.FromArgb(255, 186, 0, 0);
    public static Color OutputPartFg => PrimaryFore;
    public static Color OutputPartShadow { get; set; } = Color.FromArgb(230, 0, 0, 0);
    public static Color MusicSegmentLaneBg { get; set; } = Color.FromArgb(44, 46, 58);
    public static Color MusicPlaylistLaneBg { get; set; } = Color.FromArgb(53, 55, 70);
    public static Color ExportPartGlow => AccentCyan;
    public static Color SeekCyan => AccentCyan;
    public static Color SeekExit { get; set; } = Color.FromArgb(255, 220, 45, 45);
    public static Color SeekAnacrusis { get; set; } = Color.FromArgb(80, 220, 110);
    public static Color SeekFadeOut { get; set; } = Color.White;
    public static Color MouseGuide { get; set; } = Color.FromArgb(220, 255, 255, 255);
    public static Color WaveformScrollTrack { get; set; } = Color.FromArgb(18, 18, 18);
    public static Color WaveformScrollThumb => ChromeMid;
    public static Color WaveformScrollThumbHover => ChromeDim;

    // --- トランスポート ---
    public static Color TransportBack => ChromeBack;
    public static Color TransportBorder => ChromeBorder;
    public static Color TransportFore => PrimaryFore;
    public static Color TransportDisabledFore => ChromeDim;
    public static Color TransportSectionFore => MutedFore;
    public static Color TransportHoverBack => ChromeBorder;
    public static Color TransportPressedBack => ChromeMid;
    public static Color TransportBadgeBack { get; set; } = Color.FromArgb(38, 38, 38);

    // --- ログ ---
    public static Color LogBack => SurfaceBack;
    public static Color LogDefault { get; set; } = Color.FromArgb(220, 220, 220);
    public static Color LogHeader { get; set; } = Color.FromArgb(110, 180, 255);
    public static Color LogWarning { get; set; } = Color.FromArgb(255, 180, 70);
    public static Color LogError { get; set; } = Color.FromArgb(255, 110, 110);
    public static Color LogMuted => MutedFore;
    public static Color LogButtonBack => SurfaceBack;
    public static Color LogButtonFore => PrimaryFore;
    public static Color LogButtonBorder => ChromeBorder;
    public static Color LogButtonHoverBack { get; set; } = Color.FromArgb(39, 43, 63);

    // --- ツールチップ（共通トークンの別名） ---
    public static Color ToolTipBack => ChromeBack;
    public static Color ToolTipFore => PrimaryFore;
    public static Color ToolTipBorder => ChromeBorder;

    // --- 共通オプションコントロール ---
    public static Color OptionGlyphBorder => MutedFore;
    public static Color OptionGlyphChecked => AccentCyan;
    public static Color OptionGlyphHover => PrimaryFore;
    public static Color OptionGlyphDisabled => ChromeDim;
    public static Color OptionGlyphCheckMark { get; set; } = Color.FromArgb(26, 27, 38);

    // --- Transition Settings / Playlist ---
    public static Color PlaylistBack => SurfaceBack;
    public static Color PlaylistDefaultFore => MutedFore;
    public static Color PlaylistOptionFore => PrimaryFore;
    public static Color SectionHeaderBack { get; set; } = Color.FromArgb(45, 45, 48);
    public static Color PlaylistHoverFore => PrimaryFore;
    public static Color PlaylistActiveFore => PrimaryFore;
    public static Color PlaylistButtonBorder => ChromeBorder;
    public static Color PlaylistAutoBack { get; set; } = Color.FromArgb(35, 52, 75);
    public static Color PlaylistManualBack { get; set; } = Color.FromArgb(65, 64, 33);
    public static Color PlaylistHoverBorder => PrimaryFore;
    public static Color PlaylistTransitionBorder => AccentCyan;
    public static Color PlaylistManualBorder { get; set; } = Color.FromArgb(255, 255, 0);
    public static Color MarkerCommentErrorFore { get; set; } = Color.FromArgb(255, 110, 110);

    /// <summary>
    /// Playlist グループ枠用の高コントラスト色列（Kelly 系の識別しやすい出現順）。
    /// </summary>
    public static IReadOnlyList<Color> PlaylistGroupPalette { get; } =
    [
        Color.FromArgb(255, 179, 40),   // vivid orange
        Color.FromArgb(0, 127, 255),    // strong blue
        Color.FromArgb(255, 255, 50),   // vivid yellow
        Color.FromArgb(160, 60, 200),   // strong purple
        Color.FromArgb(255, 80, 80),    // vivid red
        Color.FromArgb(0, 180, 120),    // vivid green
        Color.FromArgb(255, 140, 200),  // strong pink
        Color.FromArgb(100, 210, 255),  // light blue
        Color.FromArgb(200, 160, 60),   // olive/gold
        Color.FromArgb(120, 80, 40),    // brown
        Color.FromArgb(0, 220, 220),    // cyan
        Color.FromArgb(180, 220, 80),   // yellow-green
        Color.FromArgb(220, 100, 40),   // orange-red
        Color.FromArgb(80, 80, 220),    // blue-purple
        Color.FromArgb(255, 200, 120),  // light orange
        Color.FromArgb(40, 140, 80),    // dark green
        Color.FromArgb(220, 60, 140),   // magenta
        Color.FromArgb(140, 200, 180),  // seafoam
        Color.FromArgb(255, 120, 160),  // salmon pink
        Color.FromArgb(90, 90, 90),     // medium gray
    ];

    public static Color PlaylistGroupColorAt(int index)
    {
        var palette = PlaylistGroupPalette;
        if (palette.Count == 0)
        {
            return AccentCyan;
        }

        var i = index % palette.Count;
        if (i < 0)
        {
            i += palette.Count;
        }

        return palette[i];
    }

    // --- 下部アクションバー ---
    public static Color ActionBarBack => ChromeBack;
    public static Color ActionOptionFore => PrimaryFore;
    public static Color ActionCopyrightFore => MutedFore;
    public static Color ActionLinkFore { get; set; } = Color.FromArgb(110, 180, 255);
    public static Color ActionLinkHoverFore => AccentCyan;
    public static Color ActionButtonInnerBack => ChromeBack;
    public static Color ReloadButtonFill { get; set; } = Color.FromArgb(45, 33, 19);
    public static Color ReloadButtonHoverFill { get; set; } = Color.FromArgb(82, 55, 26);
    public static Color ReloadButtonBack { get; set; } = Color.FromArgb(217, 130, 43);
    public static Color ReloadButtonFore => PrimaryFore;
    public static Color ReloadButtonHoverBack { get; set; } = Color.FromArgb(245, 158, 66);
    public static Color ReloadButtonPressedBack { get; set; } = Color.FromArgb(184, 106, 31);
    public static Color ClearButtonFill { get; set; } = Color.FromArgb(47, 20, 20);
    public static Color ClearButtonHoverFill { get; set; } = Color.FromArgb(82, 35, 35);
    public static Color ClearButtonBack { get; set; } = Color.FromArgb(190, 50, 50);
    public static Color ClearButtonFore => PrimaryFore;
    public static Color ClearButtonHoverBack { get; set; } = Color.FromArgb(220, 70, 70);
    public static Color ClearButtonPressedBack { get; set; } = Color.FromArgb(150, 40, 40);
    public static Color ExportButtonFill { get; set; } = Color.FromArgb(15, 27, 45);
    public static Color ExportButtonHoverFill { get; set; } = Color.FromArgb(18, 40, 67);
    public static Color ExportButtonBack { get; set; } = Color.FromArgb(30, 110, 210);
    public static Color ExportButtonFore => PrimaryFore;
    public static Color ExportButtonHoverBack { get; set; } = Color.FromArgb(45, 130, 230);
    public static Color ExportButtonPressedBack { get; set; } = Color.FromArgb(20, 90, 180);
    public static Color ProjectBarBack => ChromeBack;
    public static Color ProjectBarBorder => ChromeBorder;
    public static Color ProjectBarInputBack => DialogInputBack;
    public static Color ProjectBarInputFore => PrimaryFore;
    public static Color SpectrumBar { get; set; } = Color.FromArgb(26, 121, 157);
    public static Color ActionButtonDisabledBorder => ChromeMid;
    public static Color ActionButtonDisabledFore => MutedFore;

    // --- WAAPI ステータスバー ---
    public static Color StatusBarBack { get; set; } = Color.FromArgb(22, 22, 24);
    public static Color StatusBarBorder => ChromeBorder;
    public static Color StatusBarTitleFore => MutedFore;
    public static Color StatusBarDetailFore => PrimaryFore;
    /// <summary>接続バッジ（CONNECT）の背景。</summary>
    public static Color StatusBarConnectedBadgeBack { get; set; } = Color.FromArgb(0, 107, 215);
    /// <summary>切断バッジ（DISCONNECT）の背景。</summary>
    public static Color StatusBarDisconnectedBadgeBack { get; set; } = Color.FromArgb(190, 50, 50);
    /// <summary>切断時詳細・ターゲット未選択など、ステータス詳細のエラー文字。</summary>
    public static Color StatusBarErrorDetailFore { get; set; } = Color.FromArgb(255, 110, 110);
    public static Color KeepTargetLockFore { get; set; } = Color.FromArgb(255, 220, 48);
    public static Color KeepTargetLockHoverFore { get; set; } = Color.FromArgb(255, 240, 140);
    public static Color KeepTargetUnlockFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color KeepTargetUnlockHoverFore { get; set; } = Color.FromArgb(255, 255, 255);

    // --- ダイアログ／色設定パネル ---
    public static Color DialogBodyBack { get; set; } = Color.FromArgb(40, 40, 42);
    public static Color DialogInputBack { get; set; } = Color.FromArgb(28, 28, 30);
    public static Color DialogFore => PrimaryFore;
    public static Color DialogShadow { get; set; } = Color.Black;
    public static Color ColorPanelBack { get; set; } = Color.FromArgb(40, 40, 42);
    public static Color ColorPanelListBack { get; set; } = Color.FromArgb(32, 32, 34);
    public static Color ColorPanelInputBack { get; set; } = Color.FromArgb(28, 28, 30);
    public static Color ColorPanelInputFore => PrimaryFore;

    /// <summary>パネル表示用の一覧（キー＝INI、表示名は UiStrings.ColorLabel で日英切替）。</summary>
    public static IReadOnlyList<UiColorEntry> Entries { get; } =
    [
        new("PrimaryFore", () => PrimaryFore, c => PrimaryFore = c),
        new("MutedFore", () => MutedFore, c => MutedFore = c),
        new("AccentCyan", () => AccentCyan, c => AccentCyan = c),
        new("SurfaceBack", () => SurfaceBack, c => SurfaceBack = c),
        new("ChromeBack", () => ChromeBack, c => ChromeBack = c),
        new("ChromeBorder", () => ChromeBorder, c => ChromeBorder = c),
        new("ChromeMid", () => ChromeMid, c => ChromeMid = c),
        new("ChromeDim", () => ChromeDim, c => ChromeDim = c),

        new("WaveformBack", () => WaveformBack, c => WaveformBack = c),
        new("EmptyHint", () => EmptyHint, c => EmptyHint = c),
        new("TempoBg", () => TempoBg, c => TempoBg = c),
        new("SignatureBg", () => SignatureBg, c => SignatureBg = c),
        new("MarkerRowBg", () => MarkerRowBg, c => MarkerRowBg = c),
        new("MarkerTriangle", () => MarkerTriangle, c => MarkerTriangle = c),
        new("BarLine", () => BarLine, c => BarLine = c),
        new("BeatLine", () => BeatLine, c => BeatLine = c),
        new("TempoChangeLine", () => TempoChangeLine, c => TempoChangeLine = c),
        new("WaveFill", () => WaveFill, c => WaveFill = c),
        new("WaveCenter", () => WaveCenter, c => WaveCenter = c),
        new("WaveformSourceMeterTrack", () => WaveformSourceMeterTrack, c => WaveformSourceMeterTrack = c),
        new("WaveformSourceMeterMinimum", () => WaveformSourceMeterMinimum, c => WaveformSourceMeterMinimum = c),
        new("WaveformSourceMeterMaximum", () => WaveformSourceMeterMaximum, c => WaveformSourceMeterMaximum = c),
        new("RegionWaveFillGray", () => RegionWaveFillGray, c => RegionWaveFillGray = c),
        new("RegionWaveFillExcluded", () => RegionWaveFillExcluded, c => RegionWaveFillExcluded = c),
        new("RegionWaveFillLoop", () => RegionWaveFillLoop, c => RegionWaveFillLoop = c),
        new("RegionWaveFillAnacrusis", () => RegionWaveFillAnacrusis, c => RegionWaveFillAnacrusis = c),
        new("RegionWaveFillExit", () => RegionWaveFillExit, c => RegionWaveFillExit = c),
        new("RegionBoundaryMarker", () => RegionBoundaryMarker, c => RegionBoundaryMarker = c),
        new("EntryCueMarker", () => EntryCueMarker, c => EntryCueMarker = c),
        new("ExitCueMarker", () => ExitCueMarker, c => ExitCueMarker = c),
        new("OutputPartShadow", () => OutputPartShadow, c => OutputPartShadow = c),
        new("MusicSegmentLaneBg", () => MusicSegmentLaneBg, c => MusicSegmentLaneBg = c),
        new("MusicPlaylistLaneBg", () => MusicPlaylistLaneBg, c => MusicPlaylistLaneBg = c),
        new("SeekExit", () => SeekExit, c => SeekExit = c),
        new("SeekAnacrusis", () => SeekAnacrusis, c => SeekAnacrusis = c),
        new("SeekFadeOut", () => SeekFadeOut, c => SeekFadeOut = c),
        new("MouseGuide", () => MouseGuide, c => MouseGuide = c),
        new("WaveformScrollTrack", () => WaveformScrollTrack, c => WaveformScrollTrack = c),

        new("TransportBadgeBack", () => TransportBadgeBack, c => TransportBadgeBack = c),

        new("LogDefault", () => LogDefault, c => LogDefault = c),
        new("LogHeader", () => LogHeader, c => LogHeader = c),
        new("LogWarning", () => LogWarning, c => LogWarning = c),
        new("LogError", () => LogError, c => LogError = c),
        new("LogButtonHoverBack", () => LogButtonHoverBack, c => LogButtonHoverBack = c),

        new("OptionGlyphCheckMark", () => OptionGlyphCheckMark, c => OptionGlyphCheckMark = c),

        new("SectionHeaderBack", () => SectionHeaderBack, c => SectionHeaderBack = c),
        new("PlaylistAutoBack", () => PlaylistAutoBack, c => PlaylistAutoBack = c),
        new("PlaylistManualBack", () => PlaylistManualBack, c => PlaylistManualBack = c),
        new("PlaylistManualBorder", () => PlaylistManualBorder, c => PlaylistManualBorder = c),
        new("MarkerCommentErrorFore", () => MarkerCommentErrorFore, c => MarkerCommentErrorFore = c),

        new("ActionLinkFore", () => ActionLinkFore, c => ActionLinkFore = c),
        new("ReloadButtonFill", () => ReloadButtonFill, c => ReloadButtonFill = c),
        new("ReloadButtonHoverFill", () => ReloadButtonHoverFill, c => ReloadButtonHoverFill = c),
        new("ReloadButtonBack", () => ReloadButtonBack, c => ReloadButtonBack = c),
        new("ReloadButtonHoverBack", () => ReloadButtonHoverBack, c => ReloadButtonHoverBack = c),
        new("ReloadButtonPressedBack", () => ReloadButtonPressedBack, c => ReloadButtonPressedBack = c),
        new("ClearButtonFill", () => ClearButtonFill, c => ClearButtonFill = c),
        new("ClearButtonHoverFill", () => ClearButtonHoverFill, c => ClearButtonHoverFill = c),
        new("ClearButtonBack", () => ClearButtonBack, c => ClearButtonBack = c),
        new("ClearButtonHoverBack", () => ClearButtonHoverBack, c => ClearButtonHoverBack = c),
        new("ClearButtonPressedBack", () => ClearButtonPressedBack, c => ClearButtonPressedBack = c),
        new("ExportButtonFill", () => ExportButtonFill, c => ExportButtonFill = c),
        new("ExportButtonHoverFill", () => ExportButtonHoverFill, c => ExportButtonHoverFill = c),
        new("ExportButtonBack", () => ExportButtonBack, c => ExportButtonBack = c),
        new("ExportButtonHoverBack", () => ExportButtonHoverBack, c => ExportButtonHoverBack = c),
        new("ExportButtonPressedBack", () => ExportButtonPressedBack, c => ExportButtonPressedBack = c),

        new("SpectrumBar", () => SpectrumBar, c => SpectrumBar = c),

        new("StatusBarBack", () => StatusBarBack, c => StatusBarBack = c),
        new("StatusBarConnectedBadgeBack", () => StatusBarConnectedBadgeBack, c => StatusBarConnectedBadgeBack = c),
        new("StatusBarDisconnectedBadgeBack", () => StatusBarDisconnectedBadgeBack, c => StatusBarDisconnectedBadgeBack = c),
        new("StatusBarErrorDetailFore", () => StatusBarErrorDetailFore, c => StatusBarErrorDetailFore = c),
        new("KeepTargetLockFore", () => KeepTargetLockFore, c => KeepTargetLockFore = c),
        new("KeepTargetLockHoverFore", () => KeepTargetLockHoverFore, c => KeepTargetLockHoverFore = c),
        new("KeepTargetUnlockFore", () => KeepTargetUnlockFore, c => KeepTargetUnlockFore = c),
        new("KeepTargetUnlockHoverFore", () => KeepTargetUnlockHoverFore, c => KeepTargetUnlockHoverFore = c),

        new("DialogBodyBack", () => DialogBodyBack, c => DialogBodyBack = c),
        new("DialogInputBack", () => DialogInputBack, c => DialogInputBack = c),
        new("DialogShadow", () => DialogShadow, c => DialogShadow = c),
        new("ColorPanelBack", () => ColorPanelBack, c => ColorPanelBack = c),
        new("ColorPanelListBack", () => ColorPanelListBack, c => ColorPanelListBack = c),
        new("ColorPanelInputBack", () => ColorPanelInputBack, c => ColorPanelInputBack = c),
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
#if DEBUG
        var values = IniFile.ReadSection(IniSection);
        if (values.Count == 0)
        {
            SaveToIni();
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

        // 新規キーの追記に加え、INI の [Colors] もパネルと同じ UI 順へ揃える。
        var expectedKeys = Entries.Select(entry => entry.Key);
        if (!values.Keys.SequenceEqual(expectedKeys, StringComparer.OrdinalIgnoreCase))
        {
            SaveToIni();
        }
#else
        // リリースでは色パネル無しのためコード既定のみ使い、[Colors] は除去する。
        IniFile.RemoveSection(IniSection);
#endif
    }

    public static void SaveToIni()
    {
#if DEBUG
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
#endif
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

    public UiColorEntry(string key, Func<Color> getter, Action<Color> setter)
    {
        Key = key;
        _getter = getter;
        _setter = setter;
    }

    public string Key { get; }

    /// <summary>色調整パネル用の表示名(現在の表示言語で解決)。</summary>
    public string Label => UiStrings.ColorLabel(Key);

    public Color Get() => _getter();
    public void Set(Color color) => _setter(color);
}
