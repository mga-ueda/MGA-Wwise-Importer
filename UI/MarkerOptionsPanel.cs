namespace MgaWwiseIMImporter.UI;

/// <summary>
/// マーカー付与オプション（Marker Grid／Marker Comment）を表示する下部パネル。
/// 行高はプレイリスト項目（30px）に合わせ、DPI スケールの影響を受けないよう
/// 子コントロールは固定ピクセルで配置する。
/// </summary>
internal sealed class MarkerOptionsPanel : UserControl
{
    private const int HeaderHeight = 26;
    private const int RowPitch = 32;
    private const int RowHeight = 30;

    private readonly Panel _leftSeparator = new() { Dock = DockStyle.Left, Width = 1, TabStop = false };
    private readonly DarkToolTip _toolTip = new()
    {
        AutoPopDelay = 12000,
        InitialDelay = 350,
        ReshowDelay = 100,
        ShowAlways = true,
    };

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

    private readonly FlatOptionCheckBox _prefixCheckBox;
    private readonly TextBox _prefixTextBox;
    private readonly FlatOptionCheckBox _suffixCheckBox;
    private readonly TextBox _suffixTextBox;
    private readonly FlatOptionCheckBox _joinerCheckBox;
    private readonly TextBox _joinerTextBox;

    private MarkerSettings? _settings;
    private bool _updating;
    private bool _interactionLocked;

    /// <summary>設定値が UI 操作で変更された（保存・適用は購読側で行う）。</summary>
    public event EventHandler? SettingsChanged;

    public MarkerOptionsPanel()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        var baseFont = new Font("Yu Gothic UI", 8.5F);
        var headerFont = new Font("Yu Gothic UI", 8.5F, FontStyle.Bold);

        // フォントは DPI に追従して大きく描画されるため、
        // 横方向の配置・幅も DPI に合わせて拡縮する（行高だけはプレイリストと同じ 30px 固定）。
        // 各列は内容に必要な幅だけ確保する。均等幅にはせず、列間も小さく詰める。
        // Fade In / Fade Out 間と同じく、セクション同士を隙間なく並べる。
        var sectionGap = 0;
        var commentColumnGap = S(4);
        var col1X = 1;
        var col1W = S(92);
        var col2X = col1X + col1W + sectionGap;
        var col2W = S(114);
        var col3X = col2X + col2W + commentColumnGap;
        var col3W = S(136);
        // Fade In などの遷移セクションと同じ「ヘッダー（26px 相当）＋1px」で行を開始する。
        var contentTop = 1 + S(HeaderHeight) + 1;
        RequiredWidth = col3X + col3W + S(8);

        _gridHeaderLabel = CreateHeader("Marker Grid", headerFont, col1X, col1W);
        _gridBarRadio = CreateGridRadio("Bar", MarkerGridOverrideMode.Bar, col1X, col1W, contentTop);
        _gridBeatRadio = CreateGridRadio("Beat", MarkerGridOverrideMode.Beat, col1X, col1W, contentTop + RowPitch);
        _gridDefaultRadio = CreateGridRadio("Timeline", MarkerGridOverrideMode.Default, col1X, col1W, contentTop + RowPitch * 2);

        // 見出し背景がコメント設定の全列（col2＋col3）を覆うよう幅を広げる。
        _commentHeaderLabel = CreateHeader("Marker Comment", headerFont, col2X, col3X + col3W - col2X);

        _digitsLabel = new Label
        {
            Font = baseFont,
            Location = new Point(col2X + S(12), contentTop),
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
            col2X + S(12) + S(50),
            CenterInRow(contentTop, _digitsTextBox.PreferredHeight));
        _digitsTextBox.KeyPress += DigitsTextBox_KeyPress;
        _digitsTextBox.TextChanged += (_, _) => OnUiChanged();

        _zeroPadCheckBox = CreateCheckBox("Zero Pad", baseFont, col2X + S(12), contentTop + RowPitch, col2W - S(16));
        _resetPerPartCheckBox = CreateCheckBox("Reset Per Part", baseFont, col2X + S(12), contentTop + RowPitch * 2, col2W - S(12));

        _previewLabel = new Label
        {
            AutoEllipsis = true,
            Font = baseFont,
            Location = new Point(col2X + S(12), contentTop + RowPitch * 3),
            Size = new Size(col3X + col3W - (col2X + S(12)), RowHeight),
            Text = string.Empty,
            TextAlign = ContentAlignment.MiddleLeft,
        };

        // 3つのエディタは、最長ラベル（Separator）の直後で左端を揃える。
        var checkLeft = col3X + S(12);
        var glyphAndGap = S(14) + S(7);
        var editorGap = S(4);
        var prefixTextW = MeasureLabelWidth("Prefix", baseFont);
        var suffixTextW = MeasureLabelWidth("Suffix", baseFont);
        var joinerTextW = MeasureLabelWidth("Separator", baseFont);
        var editorX = checkLeft
            + glyphAndGap
            + Math.Max(prefixTextW, Math.Max(suffixTextW, joinerTextW))
            + editorGap;

        _prefixCheckBox = CreateCheckBox(
            "Prefix", baseFont, checkLeft, contentTop, glyphAndGap + prefixTextW + S(2));
        _prefixTextBox = CreateTextBox(baseFont, editorX, contentTop, S(44));
        _suffixCheckBox = CreateCheckBox(
            "Suffix", baseFont, checkLeft, contentTop + RowPitch, glyphAndGap + suffixTextW + S(2));
        _suffixTextBox = CreateTextBox(baseFont, editorX, contentTop + RowPitch, S(44));
        _joinerCheckBox = CreateCheckBox(
            "Separator", baseFont, checkLeft, contentTop + RowPitch * 2, glyphAndGap + joinerTextW + S(2));
        _joinerTextBox = CreateTextBox(baseFont, editorX, contentTop + RowPitch * 2, S(32));

        RequiredHeight = contentTop + RowPitch * 3 + RowHeight + 2;
        Height = RequiredHeight;

        Controls.Add(_gridHeaderLabel);
        Controls.Add(_gridBarRadio);
        Controls.Add(_gridBeatRadio);
        Controls.Add(_gridDefaultRadio);
        Controls.Add(_commentHeaderLabel);
        Controls.Add(_digitsLabel);
        Controls.Add(_digitsTextBox);
        Controls.Add(_zeroPadCheckBox);
        Controls.Add(_resetPerPartCheckBox);
        Controls.Add(_previewLabel);
        Controls.Add(_prefixCheckBox);
        Controls.Add(_prefixTextBox);
        Controls.Add(_suffixCheckBox);
        Controls.Add(_suffixTextBox);
        Controls.Add(_joinerCheckBox);
        Controls.Add(_joinerTextBox);
        Controls.Add(_leftSeparator);

        ApplyToolTips();
    }

    /// <summary>自前で DPI を考慮して配置するため、AutoScale を子へ伝播させない。</summary>
    protected override bool ScaleChildren => false;

    /// <summary>全カラムが収まるために必要な幅（DPI 反映済み）。</summary>
    public int RequiredWidth { get; }

    /// <summary>全項目が収まる固定高さ（DPI 反映済み）。</summary>
    public int RequiredHeight { get; }

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
            _prefixCheckBox.Checked = settings.CommentPrefixEnabled;
            _prefixTextBox.Text = settings.CommentPrefix;
            _suffixCheckBox.Checked = settings.CommentSuffixEnabled;
            _suffixTextBox.Text = settings.CommentSuffix;
            _joinerCheckBox.Checked = settings.CommentJoinerEnabled;
            _joinerTextBox.Text = settings.CommentJoiner;
        }
        finally
        {
            _updating = false;
        }

        UpdateDependentStates();
        UpdatePreview();
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
        _gridHeaderLabel.BackColor = back;
        _gridHeaderLabel.BarColor = headerBack;
        _gridHeaderLabel.ForeColor = headerFore;
        _commentHeaderLabel.BackColor = back;
        _commentHeaderLabel.BarColor = headerBack;
        _commentHeaderLabel.ForeColor = headerFore;
        _digitsLabel.BackColor = back;
        _digitsLabel.ForeColor = optionFore;
        _previewLabel.BackColor = back;

        foreach (var radio in new[] { _gridBarRadio, _gridBeatRadio, _gridDefaultRadio })
        {
            radio.BackColor = back;
            radio.ForeColor = optionFore;
            radio.ApplyColors();
        }

        foreach (var checkBox in new[]
        {
            _zeroPadCheckBox,
            _resetPerPartCheckBox,
            _prefixCheckBox,
            _suffixCheckBox,
            _joinerCheckBox,
        })
        {
            checkBox.BackColor = back;
            checkBox.ForeColor = optionFore;
            checkBox.ApplyColors();
        }

        var inputBack = UiColors.ForControlBack(UiColors.DialogInputBack);
        foreach (var textBox in new[] { _digitsTextBox, _prefixTextBox, _suffixTextBox, _joinerTextBox })
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
            _zeroPadCheckBox,
            _resetPerPartCheckBox,
            _prefixCheckBox,
            _suffixCheckBox,
            _joinerCheckBox,
        })
        {
            checkBox.Enabled = !locked;
        }

        if (locked)
        {
            var disabledFore = UiColors.OptionGlyphDisabled;
            foreach (var textBox in new[] { _digitsTextBox, _prefixTextBox, _suffixTextBox, _joinerTextBox })
            {
                textBox.ReadOnly = true;
                textBox.ForeColor = disabledFore;
                textBox.Cursor = Cursors.Default;
            }

            return;
        }

        UpdateDependentStates();
    }

    private SectionHeaderLabel CreateHeader(string text, Font font, int x, int width) => new()
    {
        AutoEllipsis = true,
        Font = font,
        Location = new Point(x, 1),
        Padding = new Padding(S(10), 0, S(4), 0),
        Size = new Size(width, S(HeaderHeight)),
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
    };

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

    /// <summary>ラベル文字の描画幅を返す（余白を含まない Typographic 計測）。</summary>
    private int MeasureLabelWidth(string text, Font font)
    {
        using var image = new Bitmap(1, 1);
        using var g = Graphics.FromImage(image);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var size = g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic);
        return Math.Max(1, (int)Math.Ceiling(size.Width));
    }

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
        return textBox;
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
        _settings.CommentPrefixEnabled = _prefixCheckBox.Checked;
        _settings.CommentPrefix = _prefixTextBox.Text;
        _settings.CommentSuffixEnabled = _suffixCheckBox.Checked;
        _settings.CommentSuffix = _suffixTextBox.Text;
        _settings.CommentJoinerEnabled = _joinerCheckBox.Checked;
        _settings.CommentJoiner = _joinerTextBox.Text;

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
        _prefixTextBox.ReadOnly = !_prefixCheckBox.Checked;
        _suffixTextBox.ReadOnly = !_suffixCheckBox.Checked;
        _joinerTextBox.ReadOnly = !_joinerCheckBox.Checked;
        ApplyDependentColors();
    }

    private void ApplyDependentColors()
    {
        var optionFore = UiColors.PlaylistOptionFore;
        var disabledFore = UiColors.OptionGlyphDisabled;
        var inputBack = UiColors.ForControlBack(UiColors.DialogInputBack);

        _digitsLabel.ForeColor = optionFore;
        ApplyInputAppearance(_digitsTextBox, enabled: true, optionFore, disabledFore, inputBack);
        ApplyInputAppearance(_prefixTextBox, enabled: _prefixCheckBox.Checked, optionFore, disabledFore, inputBack);
        ApplyInputAppearance(_suffixTextBox, enabled: _suffixCheckBox.Checked, optionFore, disabledFore, inputBack);
        ApplyInputAppearance(_joinerTextBox, enabled: _joinerCheckBox.Checked, optionFore, disabledFore, inputBack);
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
        SetToolTip(_prefixCheckBox,
            "オンの場合、連番の前に接頭語を追加します。Digits が空欄または 0 のときは必須です。");
        SetToolTip(_prefixTextBox,
            "Custom Cue 名の先頭に付ける文字列を入力します。Digits が空欄または 0 のときは必須です。");
        SetToolTip(_suffixCheckBox,
            "オンの場合、連番の後ろに接尾語を追加します。");
        SetToolTip(_suffixTextBox,
            "Custom Cue 名の連番より後ろに付ける文字列を入力します。Unicode 文字を使用できます。");
        SetToolTip(_joinerCheckBox,
            "オンの場合、接頭語／接尾語と連番の間に区切り文字を追加します。");
        SetToolTip(_joinerTextBox,
            "接頭語／接尾語と連番を繋ぐ文字列を入力します（例: _ または -）。");
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
            && (!settings.CommentPrefixEnabled
                || string.IsNullOrWhiteSpace(settings.CommentPrefix)))
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
