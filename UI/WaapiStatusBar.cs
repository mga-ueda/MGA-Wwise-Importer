namespace MgaWwiseIMImporter.UI;

/// <summary>
/// エディタ下の WAAPI / Wwise 接続ステータス表示。
/// </summary>
internal sealed class WaapiStatusBar : Panel
{
    private readonly Label _titleLabel;
    private readonly Label _detailLabel;
    private readonly Font _badgeFont = new("Yu Gothic UI", 9F, FontStyle.Bold);

    private string _badgeText = "—";
    private Color _badgeBack = Color.Transparent;
    private Color _badgeFore = Color.Gray;
    private bool _badgeFilled;
    private Rectangle _badgeFillBounds;
    private Rectangle _badgeTextBounds;

    public WaapiStatusBar()
    {
        Height = 30;
        Dock = DockStyle.Bottom;
        Padding = new Padding(10, 0, 10, 0);
        TabStop = false;
        DoubleBuffered = true;

        _titleLabel = new Label
        {
            AutoSize = true,
            Text = "WAAPI",
            Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold),
            Location = new Point(10, 7),
            TabStop = false,
        };

        _detailLabel = new Label
        {
            AutoEllipsis = true,
            Text = string.Empty,
            Font = new Font("Yu Gothic UI", 9F),
            Location = new Point(100, 7),
            TabStop = false,
        };

        Controls.Add(_detailLabel);
        Controls.Add(_titleLabel);
        Resize += (_, _) => LayoutLabels();
        Paint += OnPaint;
        ApplyColors();
        SetPending();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _badgeFont.Dispose();
        }

        base.Dispose(disposing);
    }

    public void ApplyColors()
    {
        BackColor = UiColors.ForControlBack(UiColors.StatusBarBack);
        _titleLabel.ForeColor = UiColors.StatusBarTitleFore;
        _titleLabel.BackColor = BackColor;
        _detailLabel.BackColor = BackColor;

        if (_badgeText == "CONNECT")
        {
            SetBadgeConnected();
            _detailLabel.ForeColor = UiColors.StatusBarDetailFore;
        }
        else if (_badgeText == "DISCONNECT")
        {
            SetBadgeDisconnected();
            _detailLabel.ForeColor = UiColors.StatusBarErrorFore;
        }
        else
        {
            SetBadgeNeutral();
            _detailLabel.ForeColor = UiColors.StatusBarTitleFore;
        }

        Invalidate();
    }

    private void SetBadgeConnected()
    {
        _badgeText = "CONNECT";
        _badgeBack = UiColors.StatusBarSuccessFore;
        _badgeFore = Color.White;
        _badgeFilled = true;
    }

    private void SetBadgeDisconnected()
    {
        _badgeText = "DISCONNECT";
        _badgeBack = UiColors.StatusBarErrorFore;
        _badgeFore = Color.White;
        _badgeFilled = true;
    }

    private void SetBadgeNeutral()
    {
        _badgeBack = BackColor;
        _badgeFore = UiColors.StatusBarTitleFore;
        _badgeFilled = false;
    }

    public void SetPending()
    {
        _badgeText = "…";
        SetBadgeNeutral();
        _detailLabel.Text = "確認中…";
        _detailLabel.ForeColor = UiColors.StatusBarTitleFore;
        LayoutLabels();
    }

    public void SetSkipped()
    {
        _badgeText = "—";
        SetBadgeNeutral();
        _detailLabel.Text = "起動時チェックオフ";
        _detailLabel.ForeColor = UiColors.StatusBarTitleFore;
        LayoutLabels();
    }

    public void SetResult(WaapiProbeResult result)
    {
        if (result.Ok)
        {
            SetBadgeConnected();
            _detailLabel.ForeColor = UiColors.StatusBarDetailFore;
        }
        else
        {
            SetBadgeDisconnected();
            _detailLabel.ForeColor = UiColors.StatusBarErrorFore;
        }

        _detailLabel.Text = result.FormatStatusDetail();
        LayoutLabels();
    }

    /// <summary>接続維持中に選択パスだけ差し替える。</summary>
    public void UpdateSelection(string wwiseVersion, string projectName, string selectedPath)
    {
        SetBadgeConnected();
        _detailLabel.ForeColor = UiColors.StatusBarDetailFore;

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
        var titleMidY = Math.Max(0, (ClientSize.Height - _titleLabel.PreferredHeight) / 2);
        _titleLabel.Location = new Point(Padding.Left, titleMidY);

        const int padX = 8;
        const int padY = 3;
        // Yu Gothic UI はメトリクス上の中央より文字が下に見えるため、塗りだけ少し下げる。
        const int fillNudgeY = 2;
        var textSize = TextRenderer.MeasureText(
            _badgeText,
            _badgeFont,
            Size.Empty,
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        var textTop = titleMidY
            + Math.Max(0, (_titleLabel.PreferredHeight - textSize.Height) / 2);
        var badgeWidth = textSize.Width + padX * 2;
        var badgeLeft = _titleLabel.Right + 8;
        _badgeTextBounds = new Rectangle(badgeLeft, textTop, badgeWidth, textSize.Height);
        _badgeFillBounds = new Rectangle(
            badgeLeft,
            textTop - padY + fillNudgeY,
            badgeWidth,
            textSize.Height + padY * 2);

        var detailX = _badgeFillBounds.Right + 12;
        _detailLabel.Location = new Point(detailX, titleMidY);
        _detailLabel.Width = Math.Max(0, ClientSize.Width - detailX - Padding.Right);
        _detailLabel.Height = _detailLabel.PreferredHeight;
        Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(UiColors.StatusBarBorder);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);

        if (_badgeFilled)
        {
            using var brush = new SolidBrush(_badgeBack);
            e.Graphics.FillRectangle(brush, _badgeFillBounds);
        }

        TextRenderer.DrawText(
            e.Graphics,
            _badgeText,
            _badgeFont,
            _badgeTextBounds,
            _badgeFore,
            TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine);
    }
}
