using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 表示言語切替（スペクトラム左）。JP／EN をトランスポート同様に画像描画し、薄い枠付きの正方形。
/// </summary>
internal sealed class LanguageFlagButton : Button
{
    private bool _hovered;
    private bool _pressed;

    public LanguageFlagButton()
    {
        AccessibleRole = AccessibleRole.PushButton;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Size = new Size(24, 24);
        Margin = new Padding(8, 0, 4, 0);
        TabStop = false;
        Cursor = Cursors.Hand;
        UseVisualStyleBackColor = false;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint,
            true);
        SetStyle(ControlStyles.Selectable, false);
        ApplyColors();
        RefreshAppearance();
    }

    public Color HoverBackColor { get; set; }
    public Color PressedBackColor { get; set; }
    public Color BorderColor { get; set; }

    public void RefreshAppearance()
    {
        AccessibleName = UiStrings.IsJapanese
            ? UiStrings.LanguageBadgeJapanese
            : UiStrings.LanguageBadgeEnglish;
        Invalidate();
    }

    public void ApplyColors()
    {
        BackColor = UiColors.ForControlBack(UiColors.ProjectBarBack);
        ForeColor = UiColors.LogButtonFore;
        HoverBackColor = UiColors.ForControlBack(UiColors.TransportHoverBack);
        PressedBackColor = UiColors.ForControlBack(UiColors.TransportPressedBack);
        BorderColor = UiColors.ForControlBack(UiColors.ChromeBorder);
        Invalidate();
    }

    /// <summary>
    /// <see cref="AutoScaleMode.Font"/> は縦横倍率が異なるため、正方形を維持する。
    /// </summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        var keepSquare = Width == Height;
        base.ScaleControl(factor, specified);
        if (keepSquare && Width != Height)
        {
            var side = Math.Min(Width, Height);
            Size = new Size(side, side);
        }
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
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = e.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.Clear(BackColor);

        var fill = _pressed
            ? PressedBackColor
            : _hovered
                ? HoverBackColor
                : BackColor;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using (var fillBrush = new SolidBrush(fill))
        {
            g.FillRectangle(fillBrush, bounds);
        }

        using (var borderPen = new Pen(BorderColor, 1f))
        {
            g.DrawRectangle(borderPen, 0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        }

        var label = UiStrings.IsJapanese
            ? UiStrings.LanguageBadgeJapanese
            : UiStrings.LanguageBadgeEnglish;
        using var font = new Font("Yu Gothic UI", 7.5F, FontStyle.Bold);
        var textSize = TextRenderer.MeasureText(
            g,
            label,
            font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        var textX = (Width - textSize.Width) / 2;
        var textY = (Height - textSize.Height) / 2;
        TextRenderer.DrawText(
            g,
            label,
            font,
            new Point(textX, textY),
            ForeColor,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}
