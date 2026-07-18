using System.Drawing.Drawing2D;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 角丸ボタン。標準 Button のフォーカス枠／矩形ボーダーを描かず、親背景で角を埋める。
/// </summary>
internal sealed class RoundedButton : Button
{
    private bool _hover;
    private bool _pressed;

    public int CornerRadius { get; set; } = 8;

    public Color HoverBackColor { get; set; }

    public Color PressedBackColor { get; set; }

    public Color DisabledBackColor { get; set; } = UiColors.ForControlBack(UiColors.ActionButtonInnerBack);

    public Color DisabledForeColor { get; set; } = UiColors.ActionButtonDisabledFore;

    public Color BorderColor { get; set; }

    public Color HoverBorderColor { get; set; }

    public Color PressedBorderColor { get; set; }

    public Color DisabledBorderColor { get; set; }

    public int BorderSize { get; set; }

    public RoundedButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TabStop = false;
        UseVisualStyleBackColor = false;
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
        // クリックでフォーカスを奪わず、↑↓ 等の波形ショートカットを阻害しない。
        SetStyle(ControlStyles.Selectable, false);
    }

    protected override bool ShowFocusCues => false;

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        // 角の外側は親色で塗り、非アクティブ時の白い矩形枠残りを防ぐ
        g.Clear(Parent?.BackColor ?? SystemColors.Control);

        var fill = ResolveFillColor();
        var textColor = Enabled ? ForeColor : DisabledForeColor;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var path = CreateRoundedRectanglePath(bounds, CornerRadius);
        using var brush = new SolidBrush(fill);
        g.FillPath(brush, path);

        var borderColor = ResolveBorderColor();
        if (BorderSize > 0 && !borderColor.IsEmpty)
        {
            var inset = BorderSize / 2f;
            var borderBounds = new RectangleF(
                inset,
                inset,
                Math.Max(0f, Width - 1f - BorderSize),
                Math.Max(0f, Height - 1f - BorderSize));
            using var borderPath = CreateRoundedRectanglePath(
                Rectangle.Round(borderBounds),
                Math.Max(0, CornerRadius - (int)Math.Ceiling(inset)));
            using var pen = new Pen(borderColor, BorderSize);
            g.DrawPath(pen, borderPath);
        }

        TextRenderer.DrawText(
            g,
            Text,
            Font,
            ClientRectangle,
            textColor,
            TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }

    private Color ResolveFillColor()
    {
        if (!Enabled)
        {
            return DisabledBackColor;
        }

        if (_pressed && !PressedBackColor.IsEmpty)
        {
            return PressedBackColor;
        }

        if (_hover && !HoverBackColor.IsEmpty)
        {
            return HoverBackColor;
        }

        return BackColor;
    }

    private Color ResolveBorderColor()
    {
        if (!Enabled && !DisabledBorderColor.IsEmpty)
        {
            return DisabledBorderColor;
        }

        if (_pressed && !PressedBorderColor.IsEmpty)
        {
            return PressedBorderColor;
        }

        if (_hover && !HoverBorderColor.IsEmpty)
        {
            return HoverBorderColor;
        }

        return BorderColor;
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        if (diameter <= 0 || diameter > bounds.Width || diameter > bounds.Height)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}
