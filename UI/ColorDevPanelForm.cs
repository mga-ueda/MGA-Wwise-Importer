namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 開発者向け色調整パネル。開いたままメイン画面を見ながら変更できる。
/// アルファはコード既定を維持し、パネルでは RGB（#RRGGBB）のみ編集・コピペする。
/// </summary>
internal sealed class ColorDevPanelForm : Form
{
    private readonly Dictionary<string, Panel> _swatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _hexInputs = new(StringComparer.OrdinalIgnoreCase);
    private readonly TableLayoutPanel _root;
    private readonly Panel _scroll;
    private readonly TableLayoutPanel _list;
    private readonly FlowLayoutPanel _buttons;
    private bool _suppressHexEvents;

    public event EventHandler? ColorsChanged;

    public ColorDevPanelForm()
    {
        Text = "色調整（開発者）";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        StartPosition = FormStartPosition.Manual;
        MinimumSize = new Size(540, 320);
        Size = new Size(560, 640);
        ShowInTaskbar = false;
        KeyPreview = true;
        BackColor = UiColors.ForControlBack(UiColors.ColorPanelBack);
        ForeColor = UiColors.ColorPanelInputFore;
        Font = new Font("Yu Gothic UI", 9F);

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8),
            BackColor = BackColor,
        };
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

        _scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiColors.ForControlBack(UiColors.ColorPanelListBack),
        };

        _list = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(4),
            BackColor = _scroll.BackColor,
        };
        // 最長ラベル「波形リージョン塗り（通常）」などが切れない幅
        _list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250f));
        _list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48f));
        _list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        for (var i = 0; i < UiColors.Entries.Count; i++)
        {
            var entry = UiColors.Entries[i];
            _list.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            _list.RowCount = i + 1;

            var nameLabel = new Label
            {
                Text = entry.Label,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            var swatch = new Panel
            {
                Width = 40,
                Height = 22,
                Margin = new Padding(4, 4, 4, 4),
                Cursor = Cursors.Hand,
                BorderStyle = BorderStyle.FixedSingle,
                Tag = entry.Key,
            };
            swatch.Click += (_, _) => PickColor(entry.Key);
            _swatches[entry.Key] = swatch;

            var hex = new TextBox
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(4, 3, 4, 3),
                Font = new Font("Consolas", 9F),
                BackColor = UiColors.ForControlBack(UiColors.ColorPanelInputBack),
                ForeColor = UiColors.ColorPanelInputFore,
                BorderStyle = BorderStyle.FixedSingle,
                Tag = entry.Key,
            };
            hex.Leave += Hex_Leave;
            hex.KeyDown += Hex_KeyDown;
            _hexInputs[entry.Key] = hex;

            _list.Controls.Add(nameLabel, 0, i);
            _list.Controls.Add(swatch, 1, i);
            _list.Controls.Add(hex, 2, i);
        }

        _scroll.Controls.Add(_list);

        _buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
            BackColor = BackColor,
        };

        var closeButton = new Button { Text = "閉じる", AutoSize = true, FlatStyle = FlatStyle.System };
        closeButton.Click += (_, _) => Close();

        var resetButton = new Button { Text = "既定に戻す", AutoSize = true, FlatStyle = FlatStyle.System };
        resetButton.Click += (_, _) =>
        {
            UiColors.ResetToDefaults();
            ApplyColorChange();
        };

        _buttons.Controls.Add(closeButton);
        _buttons.Controls.Add(resetButton);

        _root.Controls.Add(_scroll, 0, 0);
        _root.Controls.Add(_buttons, 0, 1);
        Controls.Add(_root);

        RefreshRows();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    public void RefreshRows() => WithPreservedScroll(RefreshRowsCore);

    private void Hex_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || sender is not TextBox hex || hex.Tag is not string key)
        {
            return;
        }

        e.SuppressKeyPress = true;
        ApplyHexText(key, hex.Text);
    }

    private void Hex_Leave(object? sender, EventArgs e)
    {
        if (_suppressHexEvents || sender is not TextBox hex || hex.Tag is not string key)
        {
            return;
        }

        ApplyHexText(key, hex.Text);
    }

    private void ApplyHexText(string key, string text)
    {
        var entry = FindEntry(key);
        if (entry is null)
        {
            return;
        }

        var current = entry.Get();
        var expected = UiColors.FormatColor(current);
        if (string.Equals(text.Trim(), expected, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!UiColors.TryParseColor(text, out var parsed))
        {
            // 不正値は表示を戻す
            _suppressHexEvents = true;
            try
            {
                if (_hexInputs.TryGetValue(key, out var hex))
                {
                    hex.Text = expected;
                }
            }
            finally
            {
                _suppressHexEvents = false;
            }

            return;
        }

        var alpha = UiColors.GetDefaultAlpha(key);
        var next = Color.FromArgb(alpha, parsed.R, parsed.G, parsed.B);
        entry.Set(next);
        ApplyColorChange();
    }

    private void PickColor(string key)
    {
        var entry = FindEntry(key);
        if (entry is null)
        {
            return;
        }

        var current = entry.Get();
        using var dialog = new ColorDialog
        {
            AllowFullOpen = true,
            AnyColor = true,
            FullOpen = true,
            Color = Color.FromArgb(current.R, current.G, current.B),
        };

        // アプリ本体（Owner）の中央に表示する。単体表示時はパネル中央。
        if (OwnerCenteredMessageBox.ShowDialog(Owner ?? (IWin32Window)this, dialog) != DialogResult.OK)
        {
            return;
        }

        // アルファはコード既定を維持（パネルでは変更しない）
        var alpha = UiColors.GetDefaultAlpha(key);
        var next = Color.FromArgb(alpha, dialog.Color.R, dialog.Color.G, dialog.Color.B);
        entry.Set(next);
        ApplyColorChange();
    }

    private void ApplyColorChange()
    {
        var pos = _scroll.AutoScrollPosition;
        ApplyPanelColors();
        RefreshRowsCore();
        UiColors.SaveToIni();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
        // メイン側の色適用やレイアウト後も位置を維持する。
        RestoreScrollPosition(pos);
        BeginInvoke(() => RestoreScrollPosition(pos));
    }

    private static UiColorEntry? FindEntry(string key) =>
        UiColors.Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

    private void RefreshRowsCore()
    {
        _suppressHexEvents = true;
        try
        {
            foreach (var entry in UiColors.Entries)
            {
                var color = entry.Get();
                if (_swatches.TryGetValue(entry.Key, out var swatch))
                {
                    swatch.BackColor = Color.FromArgb(255, color.R, color.G, color.B);
                }

                if (_hexInputs.TryGetValue(entry.Key, out var hex))
                {
                    hex.Text = UiColors.FormatColor(color);
                }
            }
        }
        finally
        {
            _suppressHexEvents = false;
        }
    }

    private void WithPreservedScroll(Action action)
    {
        var pos = _scroll.AutoScrollPosition;
        action();
        RestoreScrollPosition(pos);
    }

    private void RestoreScrollPosition(Point autoScrollPosition)
    {
        // AutoScrollPosition の getter は負値、setter は正値を渡す必要がある。
        _scroll.AutoScrollPosition = new Point(-autoScrollPosition.X, -autoScrollPosition.Y);
    }

    private void ApplyPanelColors()
    {
        BackColor = UiColors.ForControlBack(UiColors.ColorPanelBack);
        ForeColor = UiColors.ColorPanelInputFore;
        _root.BackColor = BackColor;
        _scroll.BackColor = UiColors.ForControlBack(UiColors.ColorPanelListBack);
        _list.BackColor = _scroll.BackColor;
        _buttons.BackColor = BackColor;

        foreach (var label in _list.Controls.OfType<Label>())
        {
            label.ForeColor = ForeColor;
        }

        foreach (var input in _hexInputs.Values)
        {
            input.BackColor = UiColors.ForControlBack(UiColors.ColorPanelInputBack);
            input.ForeColor = UiColors.ColorPanelInputFore;
        }

        Invalidate(true);
    }
}
