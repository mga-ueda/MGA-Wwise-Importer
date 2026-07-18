using System.Globalization;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// アプリ全体の色定義。既定値はこのクラス、実行時の調整は [Colors] INI と開発者パネル。
/// </summary>
internal static class UiColors
{
    public const string IniSection = "Colors";

    // --- ウィンドウ ---
    public static Color WindowBack { get; set; } = Color.FromArgb(30, 30, 30);
    public static Color WindowFore { get; set; } = Color.FromArgb(235, 235, 235);

    // --- 波形ビュー（画面上部） ---
    public static Color WaveformBack { get; set; } = Color.FromArgb(38, 38, 38);
    public static Color EmptyHint { get; set; } = Color.FromArgb(140, 140, 140);
    public static Color WaveRevealEdge { get; set; } = Color.FromArgb(180, 240, 255);
    public static Color BarNumberBg { get; set; } = Color.FromArgb(71, 71, 73);
    public static Color TempoBg { get; set; } = Color.FromArgb(53, 55, 70);
    public static Color SignatureBg { get; set; } = Color.FromArgb(44, 46, 58);
    public static Color MarkerRowBg { get; set; } = Color.FromArgb(43, 43, 45);
    public static Color WaveformInfoFg { get; set; } = Color.FromArgb(235, 235, 235);
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
    public static Color OutputPartFg { get; set; } = Color.FromArgb(255, 235, 235, 235);
    public static Color OutputPartShadow { get; set; } = Color.FromArgb(230, 0, 0, 0);
    public static Color MusicSegmentLaneBg { get; set; } = Color.FromArgb(50, 58, 73);
    public static Color MusicPlaylistLaneBg { get; set; } = Color.FromArgb(39, 43, 63);
    public static Color ExportPartGlow { get; set; } = Color.FromArgb(255, 0, 245, 255);
    public static Color SeekCyan { get; set; } = Color.FromArgb(0, 245, 255);
    public static Color SeekExit { get; set; } = Color.FromArgb(255, 220, 45, 45);
    public static Color SeekAnacrusis { get; set; } = Color.FromArgb(80, 220, 110);
    public static Color SeekFadeOut { get; set; } = Color.White;
    public static Color MouseGuide { get; set; } = Color.FromArgb(220, 255, 255, 255);
    public static Color WaveformScrollTrack { get; set; } = Color.FromArgb(18, 18, 18);
    public static Color WaveformScrollThumb { get; set; } = Color.FromArgb(72, 72, 72);
    public static Color WaveformScrollThumbHover { get; set; } = Color.FromArgb(91, 91, 91);

    // --- トランスポート ---
    public static Color TransportBack { get; set; } = Color.FromArgb(26, 27, 38);
    public static Color TransportBorder { get; set; } = Color.FromArgb(55, 55, 58);
    public static Color TransportFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color TransportSectionFore { get; set; } = Color.FromArgb(150, 150, 150);
    public static Color TransportHoverBack { get; set; } = Color.FromArgb(55, 57, 61);
    public static Color TransportPressedBack { get; set; } = Color.FromArgb(70, 72, 76);
    public static Color TransportBadgeBack { get; set; } = Color.FromArgb(38, 38, 38);

    // --- ログ ---
    public static Color LogBack { get; set; } = Color.FromArgb(30, 30, 30);
    public static Color LogDefault { get; set; } = Color.FromArgb(220, 220, 220);
    public static Color LogHeader { get; set; } = Color.FromArgb(110, 180, 255);
    public static Color LogWarning { get; set; } = Color.FromArgb(255, 180, 70);
    public static Color LogError { get; set; } = Color.FromArgb(255, 110, 110);
    public static Color LogMuted { get; set; } = Color.FromArgb(150, 150, 150);
    public static Color LogButtonBack { get; set; } = Color.FromArgb(30, 30, 30);
    public static Color LogButtonFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color LogButtonBorder { get; set; } = Color.FromArgb(55, 55, 58);
    public static Color LogButtonHoverBack { get; set; } = Color.FromArgb(39, 43, 63);

    // --- 共通オプションコントロール ---
    public static Color OptionGlyphBorder { get; set; } = Color.FromArgb(150, 150, 150);
    public static Color OptionGlyphChecked { get; set; } = Color.FromArgb(0, 245, 255);
    public static Color OptionGlyphHover { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color OptionGlyphDisabled { get; set; } = Color.FromArgb(90, 90, 94);
    public static Color OptionGlyphCheckMark { get; set; } = Color.FromArgb(26, 27, 38);

    // --- Transition Settings / Playlist ---
    public static Color PlaylistBack { get; set; } = Color.FromArgb(30, 30, 30);
    public static Color PlaylistDefaultFore { get; set; } = Color.FromArgb(150, 150, 150);
    public static Color PlaylistOptionFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color PlaylistHoverFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color PlaylistActiveFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color PlaylistButtonBorder { get; set; } = Color.FromArgb(58, 58, 58);
    public static Color PlaylistAutoBack { get; set; } = Color.FromArgb(0, 92, 98);
    public static Color PlaylistManualBack { get; set; } = Color.FromArgb(89, 89, 0);
    public static Color PlaylistHoverBorder { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color PlaylistTransitionBorder { get; set; } = Color.FromArgb(0, 245, 255);
    public static Color PlaylistManualBorder { get; set; } = Color.FromArgb(255, 255, 0);

    // --- 下部アクションバー ---
    public static Color ActionBarBack { get; set; } = Color.FromArgb(26, 27, 38);
    public static Color ActionOptionFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color ActionCopyrightFore { get; set; } = Color.FromArgb(150, 150, 150);
    public static Color ActionLinkFore { get; set; } = Color.FromArgb(110, 180, 255);
    public static Color ActionLinkHoverFore { get; set; } = Color.FromArgb(0, 245, 255);
    public static Color ActionButtonInnerBack { get; set; } = Color.FromArgb(26, 27, 38);
    public static Color ClearButtonFill { get; set; } = Color.FromArgb(43, 19, 22);
    public static Color ClearButtonHoverFill { get; set; } = Color.FromArgb(68, 26, 31);
    public static Color ClearButtonBack { get; set; } = Color.FromArgb(190, 50, 50);
    public static Color ClearButtonFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color ClearButtonHoverBack { get; set; } = Color.FromArgb(210, 70, 70);
    public static Color ClearButtonPressedBack { get; set; } = Color.FromArgb(160, 35, 35);
    public static Color ExportButtonFill { get; set; } = Color.FromArgb(15, 27, 45);
    public static Color ExportButtonHoverFill { get; set; } = Color.FromArgb(18, 40, 67);
    public static Color ExportButtonBack { get; set; } = Color.FromArgb(30, 110, 210);
    public static Color ExportButtonFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color ExportButtonHoverBack { get; set; } = Color.FromArgb(45, 130, 230);
    public static Color ExportButtonPressedBack { get; set; } = Color.FromArgb(20, 90, 180);
    public static Color ActionButtonDisabledBorder { get; set; } = Color.FromArgb(70, 70, 74);
    public static Color ActionButtonDisabledFore { get; set; } = Color.FromArgb(150, 150, 154);

    // --- WAAPI ステータスバー ---
    public static Color StatusBarBack { get; set; } = Color.FromArgb(22, 22, 24);
    public static Color StatusBarBorder { get; set; } = Color.FromArgb(55, 55, 58);
    public static Color StatusBarTitleFore { get; set; } = Color.FromArgb(150, 150, 150);
    public static Color StatusBarDetailFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color StatusBarSuccessFore { get; set; } = Color.FromArgb(0, 107, 215);
    public static Color StatusBarErrorFore { get; set; } = Color.FromArgb(190, 50, 50);

    // --- ダイアログ／色設定パネル ---
    public static Color DialogBodyBack { get; set; } = Color.FromArgb(40, 40, 42);
    public static Color DialogInputBack { get; set; } = Color.FromArgb(28, 28, 30);
    public static Color DialogFore { get; set; } = Color.FromArgb(235, 235, 235);
    public static Color DialogShadow { get; set; } = Color.Black;
    public static Color ColorPanelBack { get; set; } = Color.FromArgb(40, 40, 42);
    public static Color ColorPanelListBack { get; set; } = Color.FromArgb(32, 32, 34);
    public static Color ColorPanelInputBack { get; set; } = Color.FromArgb(28, 28, 30);
    public static Color ColorPanelInputFore { get; set; } = Color.FromArgb(235, 235, 235);

    /// <summary>パネル表示用の一覧（キー＝INI、日本語ラベル）。</summary>
    public static IReadOnlyList<UiColorEntry> Entries { get; } =
    [
        new("WindowBack", "ウィンドウ背景", () => WindowBack, c => WindowBack = c),
        new("WindowFore", "ウィンドウ文字", () => WindowFore, c => WindowFore = c),

        new("WaveformBack", "波形エリア背景", () => WaveformBack, c => WaveformBack = c),
        new("EmptyHint", "空状態ヒント", () => EmptyHint, c => EmptyHint = c),
        new("WaveRevealEdge", "波形・初回表示エッジ", () => WaveRevealEdge, c => WaveRevealEdge = c),
        new("BarNumberBg", "小節番号・背景", () => BarNumberBg, c => BarNumberBg = c),
        new("TempoBg", "テンポ・背景", () => TempoBg, c => TempoBg = c),
        new("SignatureBg", "拍子・背景", () => SignatureBg, c => SignatureBg = c),
        new("MarkerRowBg", "マーカー行・背景", () => MarkerRowBg, c => MarkerRowBg = c),
        new("WaveformInfoFg", "波形情報・文字", () => WaveformInfoFg, c => WaveformInfoFg = c),
        new("MarkerTriangle", "マーカー三角", () => MarkerTriangle, c => MarkerTriangle = c),
        new("BarLine", "小節線", () => BarLine, c => BarLine = c),
        new("BeatLine", "拍線", () => BeatLine, c => BeatLine = c),
        new("TempoChangeLine", "テンポ変更線", () => TempoChangeLine, c => TempoChangeLine = c),
        new("WaveFill", "波形", () => WaveFill, c => WaveFill = c),
        new("WaveCenter", "波形センター線", () => WaveCenter, c => WaveCenter = c),
        new("WaveformSourceMeterTrack", "波形メーター・トラック", () => WaveformSourceMeterTrack, c => WaveformSourceMeterTrack = c),
        new("WaveformSourceMeterMinimum", "波形メーター・最小", () => WaveformSourceMeterMinimum, c => WaveformSourceMeterMinimum = c),
        new("WaveformSourceMeterMaximum", "波形メーター・最大", () => WaveformSourceMeterMaximum, c => WaveformSourceMeterMaximum = c),
        new("RegionWaveFillGray", "波形リージョン塗り（通常）", () => RegionWaveFillGray, c => RegionWaveFillGray = c),
        new("RegionWaveFillExcluded", "波形リージョン塗り（-R）", () => RegionWaveFillExcluded, c => RegionWaveFillExcluded = c),
        new("RegionWaveFillLoop", "波形リージョン塗り（-L）", () => RegionWaveFillLoop, c => RegionWaveFillLoop = c),
        new("RegionWaveFillAnacrusis", "波形リージョン塗り（-A）", () => RegionWaveFillAnacrusis, c => RegionWaveFillAnacrusis = c),
        new("RegionWaveFillExit", "波形リージョン塗り（-E）", () => RegionWaveFillExit, c => RegionWaveFillExit = c),
        new("RegionBoundaryMarker", "リージョン境界マーカー", () => RegionBoundaryMarker, c => RegionBoundaryMarker = c),
        new("EntryCueMarker", "Entry Cue マーカー", () => EntryCueMarker, c => EntryCueMarker = c),
        new("ExitCueMarker", "Exit Cue マーカー", () => ExitCueMarker, c => ExitCueMarker = c),
        new("OutputPartFg", "出力パート名・文字", () => OutputPartFg, c => OutputPartFg = c),
        new("OutputPartShadow", "出力パート名・影", () => OutputPartShadow, c => OutputPartShadow = c),
        new("MusicSegmentLaneBg", "Music Segment Name・背景", () => MusicSegmentLaneBg, c => MusicSegmentLaneBg = c),
        new("MusicPlaylistLaneBg", "Music Playlist Name・背景", () => MusicPlaylistLaneBg, c => MusicPlaylistLaneBg = c),
        new("ExportPartGlow", "書き出し中パート枠", () => ExportPartGlow, c => ExportPartGlow = c),
        new("SeekCyan", "再生ヘッド", () => SeekCyan, c => SeekCyan = c),
        new("SeekExit", "Exit 二重再生ヘッド", () => SeekExit, c => SeekExit = c),
        new("SeekAnacrusis", "アウフタクト先行再生ヘッド", () => SeekAnacrusis, c => SeekAnacrusis = c),
        new("SeekFadeOut", "遷移元フェードアウトヘッド", () => SeekFadeOut, c => SeekFadeOut = c),
        new("MouseGuide", "マウスガイド", () => MouseGuide, c => MouseGuide = c),
        new("WaveformScrollTrack", "波形スクロール・トラック", () => WaveformScrollTrack, c => WaveformScrollTrack = c),
        new("WaveformScrollThumb", "波形スクロール・つまみ", () => WaveformScrollThumb, c => WaveformScrollThumb = c),
        new("WaveformScrollThumbHover", "波形スクロール・ホバー", () => WaveformScrollThumbHover, c => WaveformScrollThumbHover = c),

        new("TransportBack", "Transport・背景", () => TransportBack, c => TransportBack = c),
        new("TransportBorder", "Transport・境界線", () => TransportBorder, c => TransportBorder = c),
        new("TransportFore", "Transport・文字／アイコン", () => TransportFore, c => TransportFore = c),
        new("TransportSectionFore", "Transport・見出し文字", () => TransportSectionFore, c => TransportSectionFore = c),
        new("TransportHoverBack", "Transport・ホバー背景", () => TransportHoverBack, c => TransportHoverBack = c),
        new("TransportPressedBack", "Transport・押下背景", () => TransportPressedBack, c => TransportPressedBack = c),
        new("TransportBadgeBack", "Transport・ズーム記号背景", () => TransportBadgeBack, c => TransportBadgeBack = c),

        new("LogBack", "ログ背景", () => LogBack, c => LogBack = c),
        new("LogDefault", "ログ文字（既定）", () => LogDefault, c => LogDefault = c),
        new("LogHeader", "ログ文字（ヘッダ）", () => LogHeader, c => LogHeader = c),
        new("LogWarning", "ログ文字（警告）", () => LogWarning, c => LogWarning = c),
        new("LogError", "ログ文字（エラー）", () => LogError, c => LogError = c),
        new("LogMuted", "ログ文字（弱）", () => LogMuted, c => LogMuted = c),
        new("LogButtonBack", "ログボタン・背景", () => LogButtonBack, c => LogButtonBack = c),
        new("LogButtonFore", "ログボタン・アイコン", () => LogButtonFore, c => LogButtonFore = c),
        new("LogButtonBorder", "ログボタン・枠", () => LogButtonBorder, c => LogButtonBorder = c),
        new("LogButtonHoverBack", "ログボタン・ホバー背景", () => LogButtonHoverBack, c => LogButtonHoverBack = c),

        new("OptionGlyphBorder", "オプション・通常枠", () => OptionGlyphBorder, c => OptionGlyphBorder = c),
        new("OptionGlyphChecked", "オプション・選択色", () => OptionGlyphChecked, c => OptionGlyphChecked = c),
        new("OptionGlyphHover", "オプション・ホバー枠", () => OptionGlyphHover, c => OptionGlyphHover = c),
        new("OptionGlyphDisabled", "オプション・無効色", () => OptionGlyphDisabled, c => OptionGlyphDisabled = c),
        new("OptionGlyphCheckMark", "オプション・チェック線", () => OptionGlyphCheckMark, c => OptionGlyphCheckMark = c),

        new("PlaylistBack", "Settings／Playlist・背景", () => PlaylistBack, c => PlaylistBack = c),
        new("PlaylistDefaultFore", "Settings／Playlist・見出し／通常文字", () => PlaylistDefaultFore, c => PlaylistDefaultFore = c),
        new("PlaylistOptionFore", "Settings・選択肢文字", () => PlaylistOptionFore, c => PlaylistOptionFore = c),
        new("PlaylistHoverFore", "Playlist・波形ホバー文字", () => PlaylistHoverFore, c => PlaylistHoverFore = c),
        new("PlaylistActiveFore", "Playlist・再生中文字", () => PlaylistActiveFore, c => PlaylistActiveFore = c),
        new("PlaylistButtonBorder", "Playlist・ボタン枠", () => PlaylistButtonBorder, c => PlaylistButtonBorder = c),
        new("PlaylistAutoBack", "Playlist・自動再生開始フェード塗り", () => PlaylistAutoBack, c => PlaylistAutoBack = c),
        new("PlaylistManualBack", "Playlist・手動再生開始フェード塗り", () => PlaylistManualBack, c => PlaylistManualBack = c),
        new("PlaylistHoverBorder", "Playlist・波形ホバー枠", () => PlaylistHoverBorder, c => PlaylistHoverBorder = c),
        new("PlaylistTransitionBorder", "Playlist・自動再生中／遷移待機枠", () => PlaylistTransitionBorder, c => PlaylistTransitionBorder = c),
        new("PlaylistManualBorder", "Playlist・手動再生中枠", () => PlaylistManualBorder, c => PlaylistManualBorder = c),

        new("ActionBarBack", "Action Bar・背景", () => ActionBarBack, c => ActionBarBack = c),
        new("ActionOptionFore", "Action Bar・オプション文字", () => ActionOptionFore, c => ActionOptionFore = c),
        new("ActionCopyrightFore", "Action Bar・著作権文字", () => ActionCopyrightFore, c => ActionCopyrightFore = c),
        new("ActionLinkFore", "Action Bar・リンク文字", () => ActionLinkFore, c => ActionLinkFore = c),
        new("ActionLinkHoverFore", "Action Bar・リンク選択文字", () => ActionLinkHoverFore, c => ActionLinkHoverFore = c),
        new("ActionButtonInnerBack", "Action Button・内側背景", () => ActionButtonInnerBack, c => ActionButtonInnerBack = c),
        new("ClearButtonFill", "CLEAR・塗り", () => ClearButtonFill, c => ClearButtonFill = c),
        new("ClearButtonHoverFill", "CLEAR・ホバー塗り", () => ClearButtonHoverFill, c => ClearButtonHoverFill = c),
        new("ClearButtonBack", "CLEAR・枠", () => ClearButtonBack, c => ClearButtonBack = c),
        new("ClearButtonFore", "CLEAR・文字", () => ClearButtonFore, c => ClearButtonFore = c),
        new("ClearButtonHoverBack", "CLEAR・ホバー枠", () => ClearButtonHoverBack, c => ClearButtonHoverBack = c),
        new("ClearButtonPressedBack", "CLEAR・押下枠", () => ClearButtonPressedBack, c => ClearButtonPressedBack = c),
        new("ExportButtonFill", "EXPORT・塗り", () => ExportButtonFill, c => ExportButtonFill = c),
        new("ExportButtonHoverFill", "EXPORT・ホバー塗り", () => ExportButtonHoverFill, c => ExportButtonHoverFill = c),
        new("ExportButtonBack", "EXPORT・枠", () => ExportButtonBack, c => ExportButtonBack = c),
        new("ExportButtonFore", "EXPORT・文字", () => ExportButtonFore, c => ExportButtonFore = c),
        new("ExportButtonHoverBack", "EXPORT・ホバー枠", () => ExportButtonHoverBack, c => ExportButtonHoverBack = c),
        new("ExportButtonPressedBack", "EXPORT・押下枠", () => ExportButtonPressedBack, c => ExportButtonPressedBack = c),
        new("ActionButtonDisabledBorder", "Action Button・無効枠", () => ActionButtonDisabledBorder, c => ActionButtonDisabledBorder = c),
        new("ActionButtonDisabledFore", "Action Button・無効文字", () => ActionButtonDisabledFore, c => ActionButtonDisabledFore = c),

        new("StatusBarBack", "WAAPI Status・背景", () => StatusBarBack, c => StatusBarBack = c),
        new("StatusBarBorder", "WAAPI Status・上線", () => StatusBarBorder, c => StatusBarBorder = c),
        new("StatusBarTitleFore", "WAAPI Status・タイトル文字", () => StatusBarTitleFore, c => StatusBarTitleFore = c),
        new("StatusBarDetailFore", "WAAPI Status・詳細文字", () => StatusBarDetailFore, c => StatusBarDetailFore = c),
        new("StatusBarSuccessFore", "WAAPI Status・成功文字", () => StatusBarSuccessFore, c => StatusBarSuccessFore = c),
        new("StatusBarErrorFore", "WAAPI Status・エラー文字", () => StatusBarErrorFore, c => StatusBarErrorFore = c),

        new("DialogBodyBack", "Go To Measure・背景", () => DialogBodyBack, c => DialogBodyBack = c),
        new("DialogInputBack", "Go To Measure・入力背景", () => DialogInputBack, c => DialogInputBack = c),
        new("DialogFore", "Go To Measure・文字", () => DialogFore, c => DialogFore = c),
        new("DialogShadow", "Go To Measure・影", () => DialogShadow, c => DialogShadow = c),
        new("ColorPanelBack", "色設定パネル・背景", () => ColorPanelBack, c => ColorPanelBack = c),
        new("ColorPanelListBack", "色設定パネル・一覧背景", () => ColorPanelListBack, c => ColorPanelListBack = c),
        new("ColorPanelInputBack", "色設定パネル・入力背景", () => ColorPanelInputBack, c => ColorPanelInputBack = c),
        new("ColorPanelInputFore", "色設定パネル・入力文字", () => ColorPanelInputFore, c => ColorPanelInputFore = c),
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
            "PlaylistMeterMinimum",
            "PlaylistMeterMaximum",
            "ActionButtonDisabledBack",
            "TransportAccent",
            "WaveformScrollThumbPressed",
            "LogPlaylistScrollTrack",
            "LogPlaylistScrollThumb",
            "LogPlaylistScrollThumbHover",
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
