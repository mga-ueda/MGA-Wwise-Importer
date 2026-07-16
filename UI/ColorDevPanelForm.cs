namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 開発者向け色調整パネル。開いたままメイン画面を見ながら変更できる。
/// アルファはコード既定を維持し、パネルでは RGB（#RRGGBB）のみ編集・コピペする。
/// </summary>
internal sealed class ColorDevPanelForm : Form
{
    private readonly Dictionary<string, Panel> _swatches = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, TextBox> _hexInputs = new(StringComparer.OrdinalIgnoreCase);
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
        BackColor = Color.FromArgb(40, 40, 42);
        ForeColor = Color.FromArgb(230, 230, 230);
        Font = new Font("Yu Gothic UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(8),
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));

        var scroll = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(32, 32, 34),
        };

        var list = new TableLayoutPanel
        {
            ColumnCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Padding = new Padding(4),
        };
        // 最長ラベル「波形リージョン塗り（通常）」などが切れない幅
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250f));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 48f));
        list.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        for (var i = 0; i < UiColors.Entries.Count; i++)
        {
            var entry = UiColors.Entries[i];
            list.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
            list.RowCount = i + 1;

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
                BackColor = Color.FromArgb(28, 28, 30),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.FixedSingle,
                Tag = entry.Key,
            };
            hex.Leave += Hex_Leave;
            hex.KeyDown += Hex_KeyDown;
            _hexInputs[entry.Key] = hex;

            list.Controls.Add(nameLabel, 0, i);
            list.Controls.Add(swatch, 1, i);
            list.Controls.Add(hex, 2, i);
        }

        scroll.Controls.Add(list);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Padding = new Padding(0, 4, 0, 0),
        };

        var closeButton = new Button { Text = "閉じる", AutoSize = true, FlatStyle = FlatStyle.System };
        closeButton.Click += (_, _) => Close();

        var resetButton = new Button { Text = "既定に戻す", AutoSize = true, FlatStyle = FlatStyle.System };
        resetButton.Click += (_, _) =>
        {
            UiColors.ResetToDefaults();
            UiColors.SaveToIni();
            RefreshRows();
            ColorsChanged?.Invoke(this, EventArgs.Empty);
        };

        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(resetButton);

        root.Controls.Add(scroll, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);

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

    public void RefreshRows()
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
        RefreshRows();
        UiColors.SaveToIni();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
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

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // アルファはコード既定を維持（パネルでは変更しない）
        var alpha = UiColors.GetDefaultAlpha(key);
        var next = Color.FromArgb(alpha, dialog.Color.R, dialog.Color.G, dialog.Color.B);
        entry.Set(next);
        RefreshRows();
        UiColors.SaveToIni();
        ColorsChanged?.Invoke(this, EventArgs.Empty);
    }

    private static UiColorEntry? FindEntry(string key) =>
        UiColors.Entries.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}
