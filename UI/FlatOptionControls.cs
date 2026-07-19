using System.Drawing.Drawing2D;

namespace MgaWwiseIMImporter.UI;

internal sealed class FlatOptionRadioButton : RadioButton
{
    /// <summary>プレイリスト項目と同じ行高（AutoScale 後も固定）。</summary>
    public const int RowHeight = 30;

    private bool _hovered;

    public FlatOptionRadioButton()
    {
        AutoSize = false;
        Height = RowHeight;
        Margin = new Padding(3, 1, 3, 1);
        FlatStyle = FlatStyle.Flat;
        TabStop = false;
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

    public void ApplyColors() => Invalidate();

    public override Size GetPreferredSize(Size proposedSize)
    {
        var glyph = ScaleLogical(14);
        var gap = ScaleLogical(6);
        var text = TextRenderer.MeasureText(
            Text,
            Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return new Size(glyph + gap + text.Width + ScaleLogical(2), RowHeight);
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        base.ScaleControl(factor, specified);
        // ランタイム生成のプレイリスト行と行間を揃えるため、縦方向の AutoScale を打ち消す。
        Height = RowHeight;
        Margin = new Padding(3, 1, 3, 1);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        Invalidate();
        base.OnCheckedChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var glyphSize = ScaleLogical(14);
        var glyph = new RectangleF(
            ScaleLogical(1),
            (Height - glyphSize) / 2f,
            glyphSize - 1f,
            glyphSize - 1f);
        var borderColor = ResolveBorderColor();
        using (var border = new Pen(borderColor, ScaleLogical(1.4f)))
        {
            g.DrawEllipse(border, glyph);
        }

        if (Checked)
        {
            var inset = ScaleLogical(4f);
            var dot = RectangleF.Inflate(glyph, -inset, -inset);
            using var fill = new SolidBrush(UiColors.OptionGlyphChecked);
            g.FillEllipse(fill, dot);
        }

        DrawText(g, glyphSize);
    }

    private Color ResolveBorderColor()
    {
        if (!Enabled)
        {
            return UiColors.OptionGlyphDisabled;
        }

        if (Checked)
        {
            return UiColors.OptionGlyphChecked;
        }

        return _hovered ? UiColors.OptionGlyphHover : UiColors.OptionGlyphBorder;
    }

    private void DrawText(Graphics g, int glyphSize)
    {
        var textLeft = glyphSize + ScaleLogical(7);
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height),
            Enabled ? ForeColor : UiColors.OptionGlyphDisabled,
            TextFormatFlags.Left
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine);
    }

    private int ScaleLogical(int value) =>
        (int)Math.Round(value * DeviceDpi / 96f);

    private float ScaleLogical(float value) =>
        value * DeviceDpi / 96f;
}

internal sealed class FlatOptionCheckBox : CheckBox
{
    private const int LayoutGlyphSize = 14;
    private const int DrawnGlyphSize = 10;

    private bool _hovered;

    public FlatOptionCheckBox()
    {
        AutoSize = true;
        FlatStyle = FlatStyle.Flat;
        TabStop = false;
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

    public void ApplyColors() => Invalidate();

    public override Size GetPreferredSize(Size proposedSize)
    {
        // コントロール寸法とテキスト位置は従来どおりに保ち、枠だけを小さく描画する。
        var glyph = ScaleLogical(LayoutGlyphSize);
        var gap = ScaleLogical(6);
        var text = TextRenderer.MeasureText(
            Text,
            Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        return new Size(
            Padding.Horizontal + glyph + gap + text.Width + ScaleLogical(2),
            Padding.Vertical + Math.Max(glyph, text.Height) + ScaleLogical(4));
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnCheckedChanged(EventArgs e)
    {
        Invalidate();
        base.OnCheckedChanged(e);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var glyphSlotSize = ScaleLogical(LayoutGlyphSize);
        var glyphSize = ScaleLogical(DrawnGlyphSize);
        var glyph = new RectangleF(
            Padding.Left + ScaleLogical(1) + (glyphSlotSize - glyphSize) / 2f,
            (Height - glyphSize) / 2f,
            glyphSize - 1f,
            glyphSize - 1f);
        var borderColor = ResolveBorderColor();
        if (Checked)
        {
            using var fill = new SolidBrush(Enabled
                ? UiColors.OptionGlyphChecked
                : UiColors.OptionGlyphDisabled);
            g.FillRectangle(fill, glyph);
        }

        using (var border = new Pen(borderColor, ScaleLogical(1.4f)))
        {
            g.DrawRectangle(border, glyph.X, glyph.Y, glyph.Width, glyph.Height);
        }

        if (Checked)
        {
            using var check = new Pen(UiColors.OptionGlyphCheckMark, ScaleLogical(1.8f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };
            g.DrawLines(check,
            [
                new PointF(glyph.Left + glyph.Width * 0.22f, glyph.Top + glyph.Height * 0.52f),
                new PointF(glyph.Left + glyph.Width * 0.43f, glyph.Top + glyph.Height * 0.73f),
                new PointF(glyph.Left + glyph.Width * 0.80f, glyph.Top + glyph.Height * 0.29f),
            ]);
        }

        var textLeft = Padding.Left + glyphSlotSize + ScaleLogical(7);
        TextRenderer.DrawText(
            g,
            Text,
            Font,
            new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height),
            Enabled ? ForeColor : UiColors.OptionGlyphDisabled,
            TextFormatFlags.Left
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine);
    }

    private Color ResolveBorderColor()
    {
        if (!Enabled)
        {
            return UiColors.OptionGlyphDisabled;
        }

        if (Checked)
        {
            return UiColors.OptionGlyphChecked;
        }

        return _hovered ? UiColors.OptionGlyphHover : UiColors.OptionGlyphBorder;
    }

    private int ScaleLogical(int value) =>
        (int)Math.Round(value * DeviceDpi / 96f);

    private float ScaleLogical(float value) =>
        value * DeviceDpi / 96f;
}
