using System.ComponentModel;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// UiColors に追従するダーク配色の ToolTip。
/// OwnerDraw で背景・枠・文字色を描き、複数行テキストにも対応する。
/// </summary>
internal sealed class DarkToolTip : ToolTip
{
    public DarkToolTip() => Initialize();

    public DarkToolTip(IContainer container)
        : base(container) => Initialize();

    private void Initialize()
    {
        OwnerDraw = true;
        Draw += static (_, e) =>
        {
            using var back = new SolidBrush(UiColors.ForControlBack(UiColors.ToolTipBack));
            e.Graphics.FillRectangle(back, e.Bounds);
            using var border = new Pen(UiColors.ForControlBack(UiColors.ToolTipBorder));
            e.Graphics.DrawRectangle(
                border,
                e.Bounds.X,
                e.Bounds.Y,
                e.Bounds.Width - 1,
                e.Bounds.Height - 1);

            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.NoPrefix;
            var size = TextRenderer.MeasureText(
                e.Graphics, e.ToolTipText, e.Font, e.Bounds.Size, flags);
            var textBounds = new Rectangle(
                e.Bounds.X + 3,
                e.Bounds.Y + Math.Max(0, (e.Bounds.Height - size.Height) / 2),
                Math.Max(0, e.Bounds.Width - 6),
                size.Height);
            TextRenderer.DrawText(
                e.Graphics, e.ToolTipText, e.Font, textBounds, UiColors.ToolTipFore, flags);
        };
    }
}
