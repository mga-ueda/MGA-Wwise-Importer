namespace MgaWwiseIMImporter.UI;

/// <summary>
/// エディタ下の WAAPI / Wwise 接続ステータス表示。
/// </summary>
internal sealed class WaapiStatusBar : Panel
{
    private readonly Label _titleLabel;
    private readonly Label _badgeLabel;
    private readonly Label _detailLabel;

    public WaapiStatusBar()
    {
        Height = 30;
        Dock = DockStyle.Bottom;
        Padding = new Padding(10, 0, 10, 0);
        TabStop = false;

        _titleLabel = new Label
        {
            AutoSize = true,
            Text = "WAAPI",
            Font = new Font("MS Gothic", 9F, FontStyle.Bold),
            Location = new Point(10, 7),
            TabStop = false,
        };

        _badgeLabel = new Label
        {
            AutoSize = true,
            Text = "—",
            Font = new Font("MS Gothic", 9F, FontStyle.Bold),
            Location = new Point(58, 7),
            TabStop = false,
        };

        _detailLabel = new Label
        {
            AutoEllipsis = true,
            Text = string.Empty,
            Font = new Font("MS Gothic", 9F),
            Location = new Point(100, 7),
            TabStop = false,
        };

        Controls.Add(_detailLabel);
        Controls.Add(_badgeLabel);
        Controls.Add(_titleLabel);
        Resize += (_, _) => LayoutLabels();
        Paint += OnPaintSeparator;
        ApplyColors();
        SetPending();
    }

    public void ApplyColors()
    {
        BackColor = UiColors.ForControlBack(UiColors.StatusBarBack);
        _titleLabel.ForeColor = UiColors.LogMuted;
        _titleLabel.BackColor = BackColor;
        _badgeLabel.BackColor = BackColor;
        _detailLabel.BackColor = BackColor;

        if (_badgeLabel.Text == "OK")
        {
            _badgeLabel.ForeColor = UiColors.SeekCyan;
            _detailLabel.ForeColor = UiColors.LogDefault;
        }
        else if (_badgeLabel.Text is "NG")
        {
            _badgeLabel.ForeColor = UiColors.LogError;
            _detailLabel.ForeColor = UiColors.LogError;
        }
        else
        {
            _badgeLabel.ForeColor = UiColors.LogMuted;
            _detailLabel.ForeColor = UiColors.LogMuted;
        }
    }

    public void SetPending()
    {
        _badgeLabel.Text = "…";
        _badgeLabel.ForeColor = UiColors.LogMuted;
        _detailLabel.Text = "確認中…";
        _detailLabel.ForeColor = UiColors.LogMuted;
        LayoutLabels();
    }

    public void SetSkipped()
    {
        _badgeLabel.Text = "—";
        _badgeLabel.ForeColor = UiColors.LogMuted;
        _detailLabel.Text = "起動時チェックオフ";
        _detailLabel.ForeColor = UiColors.LogMuted;
        LayoutLabels();
    }

    public void SetResult(WaapiProbeResult result)
    {
        if (result.Ok)
        {
            _badgeLabel.Text = "OK";
            _badgeLabel.ForeColor = UiColors.SeekCyan;
            _detailLabel.ForeColor = UiColors.LogDefault;
        }
        else
        {
            _badgeLabel.Text = "NG";
            _badgeLabel.ForeColor = UiColors.LogError;
            _detailLabel.ForeColor = UiColors.LogError;
        }

        _detailLabel.Text = result.FormatStatusDetail();
        LayoutLabels();
    }

    /// <summary>接続維持中に選択パスだけ差し替える。</summary>
    public void UpdateSelection(string wwiseVersion, string projectName, string selectedPath)
    {
        _badgeLabel.Text = "OK";
        _badgeLabel.ForeColor = UiColors.SeekCyan;
        _detailLabel.ForeColor = UiColors.LogDefault;

        var parts = new List<string>();
        if (wwiseVersion.Length > 0)
        {
            parts.Add(wwiseVersion);
        }

        if (projectName.Length > 0)
        {
            parts.Add(projectName);
        }

        parts.Add(string.IsNullOrEmpty(selectedPath) ? "（未選択）" : selectedPath);
        _detailLabel.Text = string.Join("  ·  ", parts);
        LayoutLabels();
    }

    private void LayoutLabels()
    {
        var midY = Math.Max(0, (ClientSize.Height - _titleLabel.PreferredHeight) / 2);
        _titleLabel.Location = new Point(Padding.Left, midY);
        _badgeLabel.Location = new Point(_titleLabel.Right + 8, midY);
        var detailX = _badgeLabel.Right + 12;
        _detailLabel.Location = new Point(detailX, midY);
        _detailLabel.Width = Math.Max(0, ClientSize.Width - detailX - Padding.Right);
        _detailLabel.Height = _detailLabel.PreferredHeight;
    }

    private void OnPaintSeparator(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(UiColors.StatusBarBorder);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);
    }
}
