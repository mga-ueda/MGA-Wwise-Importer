namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 右ペイン下部の More Options パネル。
/// 折りたたみ内に Stream／Loudness Normalize／Marker Grid／Marker Comment をまとめる。
/// 行高はプレイリスト項目（30px）に合わせ、DPI スケールの影響を受けないよう
/// 子コントロールは固定ピクセルで配置する。
/// </summary>
internal sealed class MarkerOptionsPanel : UserControl
{
    private const int HeaderHeight = 26;
    private const int RowPitch = 32;
    private const int RowHeight = 30;
    private const int StreamMsMin = 0;
    private const int StreamMsMax = 9999;
    private const int StreamMsDefault = 500;
    private const double LoudnessTargetDefault = -24.0;
    private const double LoudnessTargetMin = -70.0;
    private const double LoudnessTargetMax = 0.0;

    private readonly Panel _leftSeparator = new() { Dock = DockStyle.Left, Width = 1, TabStop = false };
    private readonly DarkToolTip _toolTip = new()
    {
        AutoPopDelay = 12000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = true,
    };

    private readonly SectionHeaderLabel _streamHeaderLabel;
    private readonly FlatOptionCheckBox _streamEnabledCheckBox;
    private readonly Label _lookAheadLabel;
    private readonly TextBox _lookAheadTextBox;
    private readonly Label _prefetchLabel;
    private readonly TextBox _prefetchTextBox;

    private readonly SectionHeaderLabel _loudnessHeaderLabel;
    private readonly FlatOptionCheckBox _loudnessEnabledCheckBox;
    private readonly Label _loudnessTargetLabel;
    private readonly TextBox _loudnessTargetTextBox;
    private readonly Label _loudnessUnitLabel;
    private readonly FlatOptionCheckBox _loudnessGroupBalanceCheckBox;

    private readonly SectionHeaderLabel _moreOptionsHeaderLabel;
    private readonly SectionHeaderLabel _gridHeaderLabel;
    private readonly FlatOptionRadioButton _gridDefaultRadio;
    private readonly FlatOptionRadioButton _gridBarRadio;
    private readonly FlatOptionRadioButton _gridBeatRadio;

    private readonly SectionHeaderLabel _commentHeaderLabel;
    private readonly Label _digitsLabel;
    private readonly TextBox _digitsTextBox;
    private readonly FlatOptionCheckBox _zeroPadCheckBox;
    private readonly FlatOptionCheckBox _resetPerPartCheckBox;
    private readonly Label _previewLabel;

    private readonly Label _prefixLabel;
    private readonly TextBox _prefixTextBox;
    private readonly Label _suffixLabel;
    private readonly TextBox _suffixTextBox;
    private readonly Label _joinerLabel;
    private readonly TextBox _joinerTextBox;

    private readonly Control[] _moreOptionsBodyControls;
    private readonly int _collapsedHeight;
    private readonly int _expandedHeight;

    private MarkerSettings? _settings;
    private bool _updating;
    private bool _interactionLocked;
    private bool _streamEnabled = true;
    private int _lookAheadMs = StreamMsDefault;
    private int _prefetchLengthMs = StreamMsDefault;
    private bool _loudnessNormalizeEnabled;
    private double _loudnessTargetLkfs = LoudnessTargetDefault;
    private bool _loudnessPreserveGroupBalance = true;
    private bool _moreOptionsExpanded = true;

    /// <summary>設定値が UI 操作で変更された（保存・適用は購読側で行う）。</summary>
    public event EventHandler? SettingsChanged;

    /// <summary>TextBox 編集の開始／終了（ショートカット抑止用）。</summary>
    public event EventHandler<bool>? TextEditingChanged;

    /// <summary>More Options の開閉などで必要高さが変わった。</summary>
    public event EventHandler? RequiredHeightChanged;

    public MarkerOptionsPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        var baseFont = new Font("Yu Gothic UI", 8.5F);
        var headerFont = new Font("Yu Gothic UI", 8.5F, FontStyle.Bold);

        // 折りたたみ見出しの下に
        // 上段: Stream | Loudness Normalize
        // 下段: Marker Grid | Marker Comment
        var commentColumnGap = S(4);
        var streamPadL = S(12);
        var streamGap = S(6);
        var streamPadR = S(8);
        var streamLabelW = Math.Max(
            MeasureLabelWidth("Look-ahead Time", baseFont),
            MeasureLabelWidth("Prefetch Length", baseFont));
        var streamMsBoxW = Math.Max(S(36), MeasureLabelWidth("9999", baseFont) + S(14));
        var streamNeededW = streamPadL + streamLabelW + streamGap + streamMsBoxW + streamPadR;

        var gridContentW = Math.Max(
            MeasureLabelWidth("Timeline", baseFont),
            MeasureLabelWidth("Marker Grid", headerFont));
        var gridNeededW = S(12) + gridContentW + S(8);
        var leftColW = Math.Max(streamNeededW, gridNeededW);

        var loudnessPadL = S(12);
        var loudnessGap = S(6);
        var loudnessPadR = S(8);
        var loudnessTargetLabelW = MeasureLabelWidth("Target", baseFont);
        var loudnessTargetBoxW = Math.Max(S(44), MeasureLabelWidth("-24.0", baseFont) + S(14));
        var loudnessUnitW = MeasureLabelWidth("LKFS", baseFont);
        var loudnessCheckW = Math.Max(
            MeasureLabelWidth("Normalize", baseFont),
            MeasureLabelWidth("Preserve Group Balance", baseFont));
        var loudnessW = loudnessPadL
            + Math.Max(
                loudnessCheckW,
                loudnessTargetLabelW + loudnessGap + loudnessTargetBoxW + loudnessGap + loudnessUnitW)
            + loudnessPadR;

        var col2W = S(114);
        var col3PadL = S(12);
        var col3Gap = S(6);
        var col3PadR = S(8);
        var col3LabelW = Math.Max(
            MeasureLabelWidth("Prefix", baseFont),
            Math.Max(
                MeasureLabelWidth("Suffix", baseFont),
                MeasureLabelWidth("Separator", baseFont)));
        var col3EditorW = S(44);
        var commentW = col2W + commentColumnGap + col3PadL + col3LabelW + col3Gap + col3EditorW + col3PadR;

        var leftX = 1;
        var rightX = leftX + leftColW;
        var rightW = Math.Max(loudnessW, commentW);
        RequiredWidth = rightX + rightW + S(8);

        // 閉じた状態は More Options 見出しのみ。開くと直後に各セクションが続く。
        var moreOptionsHeaderY = 1;
        var row1HeaderY = moreOptionsHeaderY + S(HeaderHeight) + 1;
        var row1ContentTop = row1HeaderY + S(HeaderHeight) + 1;
        var primaryBottom = row1ContentTop + RowPitch * 2 + RowHeight;
        // 見出し帯の下マージン（SectionHeaderLabel）と同程度の間隔を空ける。
        var row2HeaderY = primaryBottom + S(8);
        var row2ContentTop = row2HeaderY + S(HeaderHeight) + 1;
        _collapsedHeight = moreOptionsHeaderY + S(HeaderHeight) + 2;
        _expandedHeight = row2ContentTop + RowPitch * 3 + RowHeight + 2;
        Height = _expandedHeight;

        _moreOptionsHeaderLabel = CreateHeader(
            FormatMoreOptionsHeader(expanded: true),
            headerFont,
            leftX,
            Math.Max(1, RequiredWidth - leftX),
            y: moreOptionsHeaderY);
        _moreOptionsHeaderLabel.Cursor = Cursors.Hand;
        _moreOptionsHeaderLabel.Click += (_, _) => ToggleMoreOptions();
        // 初期幅は RequiredWidth 基準。親が広いときは OnResize で右端まで伸ばす。

        _streamHeaderLabel = CreateHeader("Stream", headerFont, leftX, leftColW, y: row1HeaderY);
        _streamEnabledCheckBox = new FlatOptionCheckBox
        {
            AutoSize = false,
            Checked = true,
            Font = baseFont,
            Location = new Point(leftX + streamPadL, row1ContentTop),
            Size = new Size(leftColW - streamPadL - streamPadR, RowHeight),
            Text = "Stream",
        };
        _streamEnabledCheckBox.CheckedChanged += (_, _) => OnStreamUiChanged();
        _prefetchLabel = new Label
        {
            Font = baseFont,
            Location = new Point(leftX + streamPadL, row1ContentTop + RowPitch),
            Size = new Size(streamLabelW, RowHeight),
            Text = "Prefetch Length",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _prefetchTextBox = CreateStreamMsTextBox(
            baseFont,
            leftX + streamPadL + streamLabelW + streamGap,
            row1ContentTop + RowPitch,
            streamMsBoxW);
        _lookAheadLabel = new Label
        {
            Font = baseFont,
            Location = new Point(leftX + streamPadL, row1ContentTop + RowPitch * 2),
            Size = new Size(streamLabelW, RowHeight),
            Text = "Look-ahead Time",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _lookAheadTextBox = CreateStreamMsTextBox(
            baseFont,
            leftX + streamPadL + streamLabelW + streamGap,
            row1ContentTop + RowPitch * 2,
            streamMsBoxW);

        _loudnessHeaderLabel = CreateHeader("Loudness Normalize", headerFont, rightX, rightW, y: row1HeaderY);
        _loudnessEnabledCheckBox = new FlatOptionCheckBox
        {
            AutoSize = false,
            Font = baseFont,
            Location = new Point(rightX + loudnessPadL, row1ContentTop),
            Size = new Size(rightW - loudnessPadL - loudnessPadR, RowHeight),
            Text = "Normalize",
        };
        _loudnessEnabledCheckBox.CheckedChanged += (_, _) => OnLoudnessUiChanged();
        _loudnessTargetLabel = new Label
        {
            Font = baseFont,
            Location = new Point(rightX + loudnessPadL, row1ContentTop + RowPitch),
            Size = new Size(loudnessTargetLabelW, RowHeight),
            Text = "Target",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _loudnessTargetTextBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = baseFont,
            Size = new Size(loudnessTargetBoxW, 25),
            TextAlign = HorizontalAlignment.Center,
            MaxLength = 6,
            Text = LoudnessTargetDefault.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture),
        };
        _loudnessTargetTextBox.Location = new Point(
            rightX + loudnessPadL + loudnessTargetLabelW + loudnessGap,
            CenterInRow(row1ContentTop + RowPitch, _loudnessTargetTextBox.PreferredHeight));
        _loudnessTargetTextBox.KeyPress += LoudnessTargetTextBox_KeyPress;
        _loudnessTargetTextBox.Leave += LoudnessTargetTextBox_Leave;
        _loudnessTargetTextBox.TextChanged += (_, _) => OnLoudnessUiChanged();
        WireTextEditingFocus(_loudnessTargetTextBox);
        _loudnessUnitLabel = new Label
        {
            Font = baseFont,
            Location = new Point(
                _loudnessTargetTextBox.Right + loudnessGap,
                row1ContentTop + RowPitch),
            Size = new Size(loudnessUnitW, RowHeight),
            Text = "LKFS",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _loudnessGroupBalanceCheckBox = new FlatOptionCheckBox
        {
            AutoSize = false,
            Checked = true,
            Font = baseFont,
            Location = new Point(rightX + loudnessPadL, row1ContentTop + RowPitch * 2),
            Size = new Size(rightW - loudnessPadL - loudnessPadR, RowHeight),
            Text = "Preserve Group Balance",
        };
        _loudnessGroupBalanceCheckBox.CheckedChanged += (_, _) => OnLoudnessUiChanged();

        _gridHeaderLabel = CreateHeader("Marker Grid", headerFont, leftX, leftColW, y: row2HeaderY);
        _gridBarRadio = CreateGridRadio("Bar", MarkerGridOverrideMode.Bar, leftX, leftColW, row2ContentTop);
        _gridBeatRadio = CreateGridRadio("Beat", MarkerGridOverrideMode.Beat, leftX, leftColW, row2ContentTop + RowPitch);
        _gridDefaultRadio = CreateGridRadio(
            "Timeline",
            MarkerGridOverrideMode.Default,
            leftX,
            leftColW,
            row2ContentTop + RowPitch * 2);

        var commentDigitsX = rightX;
        var commentFieldsX = rightX + col2W + commentColumnGap;
        _commentHeaderLabel = CreateHeader("Marker Comment", headerFont, rightX, rightW, y: row2HeaderY);

        _digitsLabel = new Label
        {
            Font = baseFont,
            Location = new Point(commentDigitsX + S(12), row2ContentTop),
            Size = new Size(S(48), RowHeight),
            Text = "Digits",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        _digitsTextBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = baseFont,
            Size = new Size(S(46), 25),
            TextAlign = HorizontalAlignment.Center,
            MaxLength = 1,
            Text = "3",
        };
        _digitsTextBox.Location = new Point(
            commentDigitsX + S(12) + S(50),
            CenterInRow(row2ContentTop, _digitsTextBox.PreferredHeight));
        _digitsTextBox.KeyPress += DigitsTextBox_KeyPress;
        _digitsTextBox.TextChanged += (_, _) => OnUiChanged();
        WireTextEditingFocus(_digitsTextBox);

        _zeroPadCheckBox = CreateCheckBox(
            "Zero Pad",
            baseFont,
            commentDigitsX + S(12),
            row2ContentTop + RowPitch,
            col2W - S(16));
        _resetPerPartCheckBox = CreateCheckBox(
            "Reset Per Part",
            baseFont,
            commentDigitsX + S(12),
            row2ContentTop + RowPitch * 2,
            col2W - S(12));

        _previewLabel = new Label
        {
            AutoEllipsis = true,
            Font = baseFont,
            Location = new Point(commentDigitsX + S(12), row2ContentTop + RowPitch * 3),
            Size = new Size(rightW - S(12), RowHeight),
            Text = string.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        var commentFieldX = commentFieldsX + col3PadL;
        var commentEditorX = commentFieldX + col3LabelW + col3Gap;
        _prefixLabel = CreateFieldLabel("Prefix", baseFont, commentFieldX, row2ContentTop, col3LabelW);
        _prefixTextBox = CreateTextBox(baseFont, commentEditorX, row2ContentTop, col3EditorW);
        _suffixLabel = CreateFieldLabel("Suffix", baseFont, commentFieldX, row2ContentTop + RowPitch, col3LabelW);
        _suffixTextBox = CreateTextBox(baseFont, commentEditorX, row2ContentTop + RowPitch, col3EditorW);
        _joinerLabel = CreateFieldLabel("Separator", baseFont, commentFieldX, row2ContentTop + RowPitch * 2, col3LabelW);
        _joinerTextBox = CreateTextBox(baseFont, commentEditorX, row2ContentTop + RowPitch * 2, col3EditorW);

        _moreOptionsBodyControls =
        [
            _streamHeaderLabel,
            _streamEnabledCheckBox,
            _prefetchLabel,
            _prefetchTextBox,
            _lookAheadLabel,
            _lookAheadTextBox,
            _loudnessHeaderLabel,
            _loudnessEnabledCheckBox,
            _loudnessTargetLabel,
            _loudnessTargetTextBox,
            _loudnessUnitLabel,
            _loudnessGroupBalanceCheckBox,
            _gridHeaderLabel,
            _gridBarRadio,
            _gridBeatRadio,
            _gridDefaultRadio,
            _commentHeaderLabel,
            _digitsLabel,
            _digitsTextBox,
            _zeroPadCheckBox,
            _resetPerPartCheckBox,
            _previewLabel,
            _prefixLabel,
            _prefixTextBox,
            _suffixLabel,
            _suffixTextBox,
            _joinerLabel,
            _joinerTextBox,
        ];

        Controls.Add(_moreOptionsHeaderLabel);
        foreach (var control in _moreOptionsBodyControls)
        {
            Controls.Add(control);
        }

        Controls.Add(_leftSeparator);

        ApplyMoreOptionsVisibility();
        ApplyToolTips();
    }

    /// <summary>自前で DPI を考慮して配置するため、AutoScale を子へ伝播させない。</summary>
    protected override bool ScaleChildren => false;

    /// <summary>全カラムが収まるために必要な幅（DPI 反映済み）。</summary>
    public int RequiredWidth { get; }

    /// <summary>現在の開閉状態で必要な固定高さ（DPI 反映済み）。</summary>
    public int RequiredHeight => _moreOptionsExpanded ? _expandedHeight : _collapsedHeight;

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        SyncMoreOptionsHeaderWidth();
    }

    /// <summary>
    /// More Options 見出し帯をパネル右端（Music Playlist 列の右端）まで伸ばす。
    /// </summary>
    private void SyncMoreOptionsHeaderWidth()
    {
        // ctor 中に Height 設定で OnResize が走るため、生成前は無視する。
        if (_moreOptionsHeaderLabel is null)
        {
            return;
        }

        var left = _moreOptionsHeaderLabel.Left;
        var width = Math.Max(1, ClientSize.Width - left);
        if (_moreOptionsHeaderLabel.Width != width)
        {
            _moreOptionsHeaderLabel.Width = width;
        }
    }

    /// <summary>Music Track のストリーミング有効。</summary>
    public bool StreamEnabled => _streamEnabled;

    /// <summary>Look-ahead time（ms）。</summary>
    public int LookAheadMs => _lookAheadMs;

    /// <summary>Prefetch Length（ms）。</summary>
    public int PrefetchLengthMs => _prefetchLengthMs;

    /// <summary>EXPORT 時にラウドネス正規化するか。</summary>
    public bool LoudnessNormalizeEnabled => _loudnessNormalizeEnabled;

    /// <summary>正規化ターゲット（LKFS）。</summary>
    public double LoudnessTargetLkfs => _loudnessTargetLkfs;

    /// <summary>グループ内の相対バランスを保って正規化するか。</summary>
    public bool LoudnessPreserveGroupBalance => _loudnessPreserveGroupBalance;

    /// <summary>More Options が開いているか。</summary>
    public bool MoreOptionsExpanded => _moreOptionsExpanded;

    /// <summary>DPI スケール（96dpi 基準）を適用する。</summary>
    private int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);

    /// <summary>行（高さ 30px）の中に指定高さのコントロールを縦中央配置する Y を返す。</summary>
    private static int CenterInRow(int rowY, int controlHeight) =>
        rowY + Math.Max(0, (RowHeight - controlHeight) / 2);

    /// <summary>設定を UI へ反映し、以後の UI 操作でこの設定を書き換える。</summary>
    public void Bind(MarkerSettings settings)
    {
        _settings = settings;
        _updating = true;
        try
        {
            var gridRadio = settings.GridOverride switch
            {
                MarkerGridOverrideMode.Bar => _gridBarRadio,
                MarkerGridOverrideMode.Beat => _gridBeatRadio,
                _ => _gridDefaultRadio,
            };
            gridRadio.Checked = true;
            _digitsTextBox.Text = settings.CommentDigits <= 0
                ? string.Empty
                : Math.Clamp(
                    settings.CommentDigits,
                    MarkerSettings.CommentDigitsMin,
                    MarkerSettings.CommentDigitsMax).ToString();
            _zeroPadCheckBox.Checked = settings.CommentZeroPad;
            _resetPerPartCheckBox.Checked = settings.CommentResetPerPart;
            _prefixTextBox.Text = settings.CommentPrefix;
            _suffixTextBox.Text = settings.CommentSuffix;
            _joinerTextBox.Text = settings.CommentJoiner;
        }
        finally
        {
            _updating = false;
        }

        UpdateDependentStates();
        UpdatePreview();
    }

    /// <summary>Stream（有効／LookAhead／Prefetch）を UI へ反映する。</summary>
    public void BindStreaming(bool streamEnabled, int lookAheadMs, int prefetchLengthMs)
    {
        _updating = true;
        try
        {
            _streamEnabled = streamEnabled;
            _streamEnabledCheckBox.Checked = streamEnabled;
            _lookAheadMs = Math.Clamp(lookAheadMs, StreamMsMin, StreamMsMax);
            _prefetchLengthMs = Math.Clamp(prefetchLengthMs, StreamMsMin, StreamMsMax);
            _lookAheadTextBox.Text = _lookAheadMs.ToString();
            _prefetchTextBox.Text = _prefetchLengthMs.ToString();
        }
        finally
        {
            _updating = false;
        }

        UpdateDependentStates();
    }

    /// <summary>Loudness Normalize を UI へ反映する。</summary>
    public void BindLoudness(
        bool enabled,
        double targetLkfs,
        bool preserveGroupBalance)
    {
        _updating = true;
        try
        {
            _loudnessNormalizeEnabled = enabled;
            _loudnessEnabledCheckBox.Checked = enabled;
            _loudnessTargetLkfs = Math.Clamp(targetLkfs, LoudnessTargetMin, LoudnessTargetMax);
            _loudnessTargetTextBox.Text = FormatLoudnessTarget(_loudnessTargetLkfs);
            _loudnessPreserveGroupBalance = preserveGroupBalance;
            _loudnessGroupBalanceCheckBox.Checked = preserveGroupBalance;
        }
        finally
        {
            _updating = false;
        }

        UpdateDependentStates();
    }

    /// <summary>More Options の開閉を UI へ反映する。</summary>
    public void BindMoreOptions(bool expanded)
    {
        if (_moreOptionsExpanded == expanded)
        {
            return;
        }

        _moreOptionsExpanded = expanded;
        ApplyMoreOptionsVisibility();
        RequiredHeightChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ApplyColors()
    {
        var back = UiColors.ForControlBack(UiColors.PlaylistBack);
        var headerBack = UiColors.ForControlBack(UiColors.SectionHeaderBack);
        var headerFore = UiColors.PlaylistDefaultFore;
        var optionFore = UiColors.PlaylistOptionFore;
        var separator = UiColors.ForControlBack(UiColors.PlaylistButtonBorder);

        BackColor = back;
        _leftSeparator.BackColor = separator;
        foreach (var header in new[]
        {
            _streamHeaderLabel,
            _loudnessHeaderLabel,
            _moreOptionsHeaderLabel,
            _gridHeaderLabel,
            _commentHeaderLabel,
        })
        {
            header.BackColor = back;
            header.BarColor = headerBack;
            header.ForeColor = headerFore;
        }

        _lookAheadLabel.BackColor = back;
        _lookAheadLabel.ForeColor = optionFore;
        _prefetchLabel.BackColor = back;
        _prefetchLabel.ForeColor = optionFore;
        _loudnessTargetLabel.BackColor = back;
        _loudnessTargetLabel.ForeColor = optionFore;
        _loudnessUnitLabel.BackColor = back;
        _loudnessUnitLabel.ForeColor = optionFore;
        _digitsLabel.BackColor = back;
        _digitsLabel.ForeColor = optionFore;
        _prefixLabel.BackColor = back;
        _prefixLabel.ForeColor = optionFore;
        _suffixLabel.BackColor = back;
        _suffixLabel.ForeColor = optionFore;
        _joinerLabel.BackColor = back;
        _joinerLabel.ForeColor = optionFore;
        _previewLabel.BackColor = back;

        foreach (var radio in new[] { _gridBarRadio, _gridBeatRadio, _gridDefaultRadio })
        {
            radio.BackColor = back;
            radio.ForeColor = optionFore;
            radio.ApplyColors();
        }

        foreach (var checkBox in new[]
        {
            _streamEnabledCheckBox,
            _loudnessEnabledCheckBox,
            _loudnessGroupBalanceCheckBox,
            _zeroPadCheckBox,
            _resetPerPartCheckBox,
        })
        {
            checkBox.BackColor = back;
            checkBox.ForeColor = optionFore;
            checkBox.ApplyColors();
        }

        var inputBack = UiColors.ForControlBack(UiColors.DialogInputBack);
        foreach (var textBox in new[]
        {
            _lookAheadTextBox,
            _prefetchTextBox,
            _loudnessTargetTextBox,
            _digitsTextBox,
            _prefixTextBox,
            _suffixTextBox,
            _joinerTextBox,
        })
        {
            textBox.BackColor = inputBack;
        }

        ApplyDependentColors();
        UpdatePreview();
    }

    /// <summary>
    /// 書き出し中の操作ロック。TextBox は Enabled=false にせず ReadOnly＋色で無効化する。
    /// </summary>
    public void SetInteractionLocked(bool locked)
    {
        if (_interactionLocked == locked)
        {
            return;
        }

        _interactionLocked = locked;
        foreach (var radio in new[] { _gridBarRadio, _gridBeatRadio, _gridDefaultRadio })
        {
            radio.Enabled = !locked;
        }

        foreach (var checkBox in new[]
        {
            _streamEnabledCheckBox,
            _loudnessEnabledCheckBox,
            _loudnessGroupBalanceCheckBox,
            _zeroPadCheckBox,
            _resetPerPartCheckBox,
        })
        {
            checkBox.Enabled = !locked;
        }

        if (locked)
        {
            TextEditingChanged?.Invoke(this, false);
            var disabledFore = UiColors.OptionGlyphDisabled;
            foreach (var textBox in EnumerateEditableTextBoxes())
            {
                textBox.ReadOnly = true;
                textBox.ForeColor = disabledFore;
                textBox.Cursor = Cursors.Default;
            }

            return;
        }

        UpdateDependentStates();
    }

    private void ToggleMoreOptions()
    {
        _moreOptionsExpanded = !_moreOptionsExpanded;
        ApplyMoreOptionsVisibility();
        RequiredHeightChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyMoreOptionsVisibility()
    {
        _moreOptionsHeaderLabel.Text = FormatMoreOptionsHeader(_moreOptionsExpanded);
        foreach (var control in _moreOptionsBodyControls)
        {
            control.Visible = _moreOptionsExpanded;
        }
    }

    private static string FormatMoreOptionsHeader(bool expanded) =>
        expanded ? "▾ More Options" : "▸ More Options";

    private SectionHeaderLabel CreateHeader(string text, Font font, int x, int width, int y) => new()
    {
        AutoEllipsis = true,
        Font = font,
        Location = new Point(x, y),
        Padding = new Padding(S(10), 0, S(4), 0),
        Size = new Size(width, S(HeaderHeight)),
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private TextBox CreateStreamMsTextBox(Font font, int x, int rowY, int width)
    {
        var textBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = font,
            Size = new Size(width, 25),
            TextAlign = HorizontalAlignment.Center,
            MaxLength = 4,
            Text = StreamMsDefault.ToString(),
        };
        textBox.Location = new Point(x, CenterInRow(rowY, textBox.PreferredHeight));
        textBox.KeyPress += StreamMsTextBox_KeyPress;
        textBox.Leave += StreamMsTextBox_Leave;
        textBox.TextChanged += (_, _) => OnStreamUiChanged();
        WireTextEditingFocus(textBox);
        return textBox;
    }

    private FlatOptionRadioButton CreateGridRadio(
        string text,
        MarkerGridOverrideMode mode,
        int columnX,
        int columnWidth,
        int y)
    {
        var radio = new FlatOptionRadioButton
        {
            Font = new Font("Yu Gothic UI", 8.5F),
            Location = new Point(columnX + S(12), y),
            Size = new Size(columnWidth - S(16), RowHeight),
            Tag = mode,
            Text = text,
        };
        radio.CheckedChanged += (_, _) =>
        {
            if (radio.Checked)
            {
                OnUiChanged();
            }
        };
        return radio;
    }

    /// <summary>ラベル文字の描画幅を返す（WinForms Label と同じ GDI 計測）。</summary>
    private static int MeasureLabelWidth(string text, Font font)
    {
        var size = TextRenderer.MeasureText(
            text,
            font,
            Size.Empty,
            TextFormatFlags.NoPrefix);
        return Math.Max(1, size.Width);
    }

    private Label CreateFieldLabel(string text, Font font, int x, int y, int width) => new()
    {
        Font = font,
        Location = new Point(x, y),
        Size = new Size(width, RowHeight),
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
    };

    private FlatOptionCheckBox CreateCheckBox(string text, Font font, int x, int y, int width)
    {
        var checkBox = new FlatOptionCheckBox
        {
            AutoSize = false,
            Font = font,
            Location = new Point(x, y),
            Size = new Size(width, RowHeight),
            Text = text,
        };
        checkBox.CheckedChanged += (_, _) => OnUiChanged();
        return checkBox;
    }

    private TextBox CreateTextBox(Font font, int x, int rowY, int width)
    {
        var textBox = new TextBox
        {
            BorderStyle = BorderStyle.FixedSingle,
            Font = font,
            Size = new Size(width, 25),
            TextAlign = HorizontalAlignment.Center,
        };
        textBox.Location = new Point(x, CenterInRow(rowY, textBox.PreferredHeight));
        textBox.TextChanged += (_, _) => OnUiChanged();
        WireTextEditingFocus(textBox);
        return textBox;
    }

    private void WireTextEditingFocus(TextBox textBox)
    {
        textBox.Enter += (_, _) => TextEditingChanged?.Invoke(this, true);
        textBox.Leave += (_, _) =>
        {
            // 同パネル内の別 TextBox へ移る場合は抑止を維持する。
            BeginInvoke(() =>
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                if (!HasFocusedTextBox())
                {
                    TextEditingChanged?.Invoke(this, false);
                }
            });
        };
    }

    private bool HasFocusedTextBox()
    {
        foreach (var textBox in EnumerateEditableTextBoxes())
        {
            if (textBox.Focused)
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<TextBox> EnumerateEditableTextBoxes()
    {
        yield return _lookAheadTextBox;
        yield return _prefetchTextBox;
        yield return _loudnessTargetTextBox;
        yield return _digitsTextBox;
        yield return _prefixTextBox;
        yield return _suffixTextBox;
        yield return _joinerTextBox;
    }

    private void OnUiChanged()
    {
        if (_updating || _settings is null)
        {
            return;
        }

        _settings.GridOverride = _gridBarRadio.Checked
            ? MarkerGridOverrideMode.Bar
            : _gridBeatRadio.Checked
                ? MarkerGridOverrideMode.Beat
                : MarkerGridOverrideMode.Default;
        if (TryGetDigits(out var digits))
        {
            _settings.CommentDigits = digits;
        }
        _settings.CommentZeroPad = _zeroPadCheckBox.Checked;
        _settings.CommentResetPerPart = _resetPerPartCheckBox.Checked;
        _settings.CommentPrefix = _prefixTextBox.Text;
        _settings.CommentSuffix = _suffixTextBox.Text;
        _settings.CommentJoiner = _joinerTextBox.Text;
        _settings.SyncCommentOptionalEnabledFlags();

        UpdateDependentStates();
        UpdatePreview();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateDependentStates()
    {
        // Enabled=false だと OS の無効色（暗い背景で黒）になるため、
        // ReadOnly＋色で見た目の無効状態を表す。
        if (_interactionLocked)
        {
            return;
        }

        _digitsTextBox.ReadOnly = false;
        _lookAheadTextBox.ReadOnly = !_streamEnabled;
        _prefetchTextBox.ReadOnly = !_streamEnabled;
        _loudnessTargetTextBox.ReadOnly = !_loudnessNormalizeEnabled;
        _loudnessGroupBalanceCheckBox.Enabled = _loudnessNormalizeEnabled;
        _prefixTextBox.ReadOnly = false;
        _suffixTextBox.ReadOnly = false;
        _joinerTextBox.ReadOnly = false;
        ApplyDependentColors();
    }

    private void ApplyDependentColors()
    {
        var optionFore = UiColors.PlaylistOptionFore;
        var disabledFore = UiColors.OptionGlyphDisabled;
        var inputBack = UiColors.ForControlBack(UiColors.DialogInputBack);

        _digitsLabel.ForeColor = optionFore;
        _lookAheadLabel.ForeColor = _streamEnabled ? optionFore : disabledFore;
        _prefetchLabel.ForeColor = _streamEnabled ? optionFore : disabledFore;
        _loudnessTargetLabel.ForeColor = _loudnessNormalizeEnabled ? optionFore : disabledFore;
        _loudnessUnitLabel.ForeColor = _loudnessNormalizeEnabled ? optionFore : disabledFore;
        _loudnessGroupBalanceCheckBox.ForeColor = _loudnessNormalizeEnabled ? optionFore : disabledFore;
        _loudnessGroupBalanceCheckBox.ApplyColors();
        _prefixLabel.ForeColor = optionFore;
        _suffixLabel.ForeColor = optionFore;
        _joinerLabel.ForeColor = optionFore;
        ApplyInputAppearance(_lookAheadTextBox, enabled: _streamEnabled, optionFore, disabledFore, inputBack);
        ApplyInputAppearance(_prefetchTextBox, enabled: _streamEnabled, optionFore, disabledFore, inputBack);
        ApplyInputAppearance(
            _loudnessTargetTextBox,
            enabled: _loudnessNormalizeEnabled,
            optionFore,
            disabledFore,
            inputBack);
        ApplyInputAppearance(_digitsTextBox, enabled: true, optionFore, disabledFore, inputBack);
        ApplyInputAppearance(_prefixTextBox, enabled: true, optionFore, disabledFore, inputBack);
        ApplyInputAppearance(_suffixTextBox, enabled: true, optionFore, disabledFore, inputBack);
        ApplyInputAppearance(_joinerTextBox, enabled: true, optionFore, disabledFore, inputBack);
    }

    private static void ApplyInputAppearance(
        TextBox textBox,
        bool enabled,
        Color optionFore,
        Color disabledFore,
        Color inputBack)
    {
        textBox.BackColor = inputBack;
        textBox.ForeColor = enabled ? optionFore : disabledFore;
        textBox.Cursor = enabled ? Cursors.IBeam : Cursors.Default;
    }

    private void StreamMsTextBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar)
            && (e.KeyChar < '0' || e.KeyChar > '9'))
        {
            e.Handled = true;
        }
    }

    private void StreamMsTextBox_Leave(object? sender, EventArgs e)
    {
        if (_updating)
        {
            return;
        }

        if (sender == _lookAheadTextBox)
        {
            _lookAheadTextBox.Text = _lookAheadMs.ToString();
        }
        else if (sender == _prefetchTextBox)
        {
            _prefetchTextBox.Text = _prefetchLengthMs.ToString();
        }
    }

    private void LoudnessTargetTextBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar))
        {
            return;
        }

        if (e.KeyChar is '-' or '.' or ',')
        {
            return;
        }

        if (e.KeyChar < '0' || e.KeyChar > '9')
        {
            e.Handled = true;
        }
    }

    private void LoudnessTargetTextBox_Leave(object? sender, EventArgs e)
    {
        if (_updating)
        {
            return;
        }

        _loudnessTargetTextBox.Text = FormatLoudnessTarget(_loudnessTargetLkfs);
    }

    private void OnStreamUiChanged()
    {
        if (_updating || _interactionLocked)
        {
            return;
        }

        var streamEnabled = _streamEnabledCheckBox.Checked;
        var lookAheadOk = TryParseStreamMs(_lookAheadTextBox.Text, out var lookAhead);
        var prefetchOk = TryParseStreamMs(_prefetchTextBox.Text, out var prefetch);
        if (!lookAheadOk || !prefetchOk)
        {
            // チェックだけ変わった場合も保存する。
            if (streamEnabled == _streamEnabled)
            {
                return;
            }

            _streamEnabled = streamEnabled;
            UpdateDependentStates();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (streamEnabled == _streamEnabled
            && lookAhead == _lookAheadMs
            && prefetch == _prefetchLengthMs)
        {
            return;
        }

        _streamEnabled = streamEnabled;
        _lookAheadMs = lookAhead;
        _prefetchLengthMs = prefetch;
        UpdateDependentStates();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnLoudnessUiChanged()
    {
        if (_updating || _interactionLocked)
        {
            return;
        }

        var enabled = _loudnessEnabledCheckBox.Checked;
        var groupBalance = _loudnessGroupBalanceCheckBox.Checked;
        var targetOk = TryParseLoudnessTarget(_loudnessTargetTextBox.Text, out var target);
        if (!targetOk)
        {
            if (enabled == _loudnessNormalizeEnabled
                && groupBalance == _loudnessPreserveGroupBalance)
            {
                return;
            }

            _loudnessNormalizeEnabled = enabled;
            _loudnessPreserveGroupBalance = groupBalance;
            UpdateDependentStates();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (enabled == _loudnessNormalizeEnabled
            && Math.Abs(target - _loudnessTargetLkfs) < 0.0001
            && groupBalance == _loudnessPreserveGroupBalance)
        {
            return;
        }

        _loudnessNormalizeEnabled = enabled;
        _loudnessTargetLkfs = target;
        _loudnessPreserveGroupBalance = groupBalance;
        UpdateDependentStates();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static bool TryParseStreamMs(string text, out int milliseconds)
    {
        if (int.TryParse(text.Trim(), out milliseconds)
            && milliseconds >= StreamMsMin
            && milliseconds <= StreamMsMax)
        {
            return true;
        }

        milliseconds = 0;
        return false;
    }

    private static bool TryParseLoudnessTarget(string text, out double targetLkfs)
    {
        var trimmed = text.Trim().Replace(',', '.');
        if (double.TryParse(
                trimmed,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out targetLkfs)
            && targetLkfs >= LoudnessTargetMin
            && targetLkfs <= LoudnessTargetMax)
        {
            return true;
        }

        targetLkfs = LoudnessTargetDefault;
        return false;
    }

    private static string FormatLoudnessTarget(double value) =>
        value.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    private void DigitsTextBox_KeyPress(object? sender, KeyPressEventArgs e)
    {
        if (!char.IsControl(e.KeyChar)
            && (e.KeyChar < '0' || e.KeyChar > '6'))
        {
            e.Handled = true;
        }
    }

    private bool TryGetDigits(out int digits)
    {
        if (string.IsNullOrWhiteSpace(_digitsTextBox.Text))
        {
            digits = 0;
            return true;
        }

        return int.TryParse(_digitsTextBox.Text, out digits)
            && digits >= MarkerSettings.CommentDigitsMin
            && digits <= MarkerSettings.CommentDigitsMax;
    }

    private void ApplyToolTips()
    {
        SetToolTip(_streamHeaderLabel,
            "Wwise Music Track のストリーミング関連設定です。");
        SetToolTip(_streamEnabledCheckBox,
            "オンの場合、Music Track をストリーミング有効で作成します（既定オン）。"
            + " オフのときは Look-ahead Time／Prefetch Length は適用されません。");
        SetToolTip(_lookAheadLabel,
            "2 番目以降のセグメントの Look-ahead Time（ms、0〜9999。既定 500）。"
            + " Stream オン時のみ有効。先頭セグメントは Zero latency のため 0 固定です。");
        SetToolTip(_lookAheadTextBox,
            "Look-ahead Time（ms）。0〜9999。既定は 500 です。Stream オン時のみ有効。");
        SetToolTip(_prefetchLabel,
            "Playlist 先頭セグメント先頭トラックの Prefetch Length（ms、0〜9999。既定 500）。Stream オン時のみ有効。");
        SetToolTip(_prefetchTextBox,
            "Prefetch Length（ms）。0〜9999。既定は 500 です。"
            + " Playlist 先頭セグメント先頭トラックにだけ反映されます。Stream オン時のみ有効。");
        SetToolTip(_loudnessHeaderLabel,
            "このアプリ独自のラウドネス正規化です（Wwise の非破壊 Loudness Normalize とは無関係）。"
            + " EXPORT 時に分割 WAV へ破壊編集でゲインを焼き込みます。");
        SetToolTip(_loudnessEnabledCheckBox,
            "オンの場合、EXPORT で分割した各 WAV の音量を Target LKFS へ破壊的に正規化します"
            + "（既定オフ。Wwise 標準機能ではなく、このアプリ独自の処理です）。"
            + " 元の連続波形は変更せず、書き出すセパレート WAV のみを書き換えます。");
        SetToolTip(_loudnessTargetLabel,
            "正規化の目標ラウドネス（LKFS、−70〜0。既定 −24）。Normalize オン時のみ有効。");
        SetToolTip(_loudnessTargetTextBox,
            "目標ラウドネス（LKFS）。−70〜0。既定は −24 です。Normalize オン時のみ有効。");
        SetToolTip(_loudnessUnitLabel,
            "単位は LKFS（ITU-R BS.1770 / LUFS と同値）です。");
        SetToolTip(_loudnessGroupBalanceCheckBox,
            "オンの場合、グループ内で最も大きい音量のファイルを Target に合わせ、"
            + "他メンバーは相対バランスを保ったまま同じゲインを破壊編集で適用します（既定オン）。"
            + " オフでは各ファイルを個別に Target へ正規化します。");
        SetToolTip(_moreOptionsHeaderLabel,
            "Stream／Loudness Normalize／Marker Grid／Marker Comment を開閉します（既定は開いた状態）。"
            + " 開閉状態はプロジェクト設定へ自動保存されます。"
            + " 開閉しても Music Playlist の高さは変わりません。");
        SetToolTip(_gridHeaderLabel,
            "マーカーをドラッグで付与するときのスナップ間隔を指定します。縦線の描画には影響しません。");
        SetToolTip(_gridDefaultRadio,
            "現在タイムラインに表示されているグリッドへスナップします。従来と同じ動作です。");
        SetToolTip(_gridBarRadio,
            "タイムラインの表示倍率に関係なく、必ず小節単位でマーカーを付与します。");
        SetToolTip(_gridBeatRadio,
            "タイムラインの表示倍率に関係なく、必ず拍単位でマーカーを付与します。");
        SetToolTip(_commentHeaderLabel,
            "追加マーカーから生成する Wwise Custom Cue 名の規則を設定します。");
        SetToolTip(_digitsLabel,
            "連番の桁数を 1～6 で指定します。空欄または 0 の場合は連番自体を付けません。"
            + " 1 以上のときは、その桁で表せる最大値までしかマーカーを追加できません（例: 3 → 999 件）。");
        SetToolTip(_digitsTextBox,
            "連番の桁数です。空欄または 0 で連番なし、1～6 で連番ありになります。"
            + " 桁数を超える連番は追加できません。");
        SetToolTip(_zeroPadCheckBox,
            "オンの場合、Digits の桁数まで常に 0 で埋めます"
            + "（例: Digits=2 → 01、Digits=3 → 001、Digits=4 → 0001）。"
            + "オフのときは桁埋めせず 1, 2, 3… と表示します。");
        SetToolTip(_resetPerPartCheckBox,
            "オンの場合、Music Playlist の各パート（書き出しファイル）ごとに連番を 1 へ戻します。");
        SetToolTip(_prefixLabel,
            "入力がある場合、連番の前に接頭語を追加します。Digits が空欄または 0 のときは必須です。");
        SetToolTip(_prefixTextBox,
            "Custom Cue 名の先頭に付ける文字列を入力します。空欄なら接頭語なし。"
            + " Digits が空欄または 0 のときは必須です。");
        SetToolTip(_suffixLabel,
            "入力がある場合、連番の後ろに接尾語を追加します。");
        SetToolTip(_suffixTextBox,
            "Custom Cue 名の連番より後ろに付ける文字列を入力します。空欄なら接尾語なし。Unicode 文字を使用できます。");
        SetToolTip(_joinerLabel,
            "入力がある場合、接頭語／接尾語と連番の間に区切り文字を追加します。");
        SetToolTip(_joinerTextBox,
            "接頭語／接尾語と連番を繋ぐ文字列を入力します（例: _ または -）。空欄なら区切りなし。");
        SetToolTip(_previewLabel,
            "生成される Wwise Custom Cue 名の例と、名前が有効かどうかを表示します。");
    }

    private void SetToolTip(Control control, string text) => _toolTip.SetToolTip(control, text);

    private void UpdatePreview()
    {
        if (_settings is null)
        {
            _previewLabel.Text = string.Empty;
            return;
        }

        var rule = _settings.ToCommentRule();
        var example = rule.Format(1);
        var validationError = ValidateWwiseCustomCueName(_settings, example);
        if (validationError is null)
        {
            _previewLabel.Text = $"e.g. {example}";
            _previewLabel.ForeColor = UiColors.PlaylistDefaultFore;
        }
        else
        {
            _previewLabel.Text = validationError;
            _previewLabel.ForeColor = UiColors.MarkerCommentErrorFore;
        }
    }

    /// <summary>
    /// Wwise Help では Custom Cue を含む一般オブジェクト名に Unicode 文字を使用できる。
    /// 名前として表示できない空白名・制御文字だけを、このアプリ側で NG とする。
    /// </summary>
    private static string? ValidateWwiseCustomCueName(MarkerSettings settings, string name)
    {
        // 連番なし（Digits が 0）の場合は Prefix が無いと名前が空になるため必須。
        if (settings.CommentDigits <= 0
            && string.IsNullOrWhiteSpace(settings.CommentPrefix))
        {
            return "Digits が 0 のときは Prefix を入力してください";
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return "名前が空です";
        }

        if (name.Any(char.IsControl))
        {
            return "制御文字は使用できません";
        }

        return null;
    }
}
