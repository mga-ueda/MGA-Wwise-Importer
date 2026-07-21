using System.ComponentModel;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// UiColors に追従するダーク配色の ToolTip。
/// OwnerDraw で背景・枠・文字色を描き、複数行テキストにも対応する。
/// Popup でサイズを確定し、OS 既定（白背景）描画へ落ちないよう OwnerDraw を都度再確認する。
/// </summary>
internal sealed class DarkToolTip : ToolTip
{
    private const int MaxTipWidth = 420;
    private const int PadX = 8;
    private const int PadY = 6;

    public DarkToolTip() => Initialize();

    public DarkToolTip(IContainer container)
        : base(container) => Initialize();

    private void Initialize()
    {
        ApplyOwnerDrawMode();
        Popup += OnPopup;
        Draw += OnDraw;
    }

    /// <summary>テーマ色や OwnerDraw フラグを再適用する（言語切替・色変更後など）。</summary>
    public void ApplyTheme() => ApplyOwnerDrawMode();

    private void ApplyOwnerDrawMode()
    {
        // IsBalloon が true だと OwnerDraw より優先され OS 既定見た目になる。
        IsBalloon = false;
        OwnerDraw = true;
        // アニメーション／フェード中に OwnerDraw が効かない端末がある。
        UseAnimation = false;
        UseFading = false;
        BackColor = UiColors.ForControlBack(UiColors.ToolTipBack);
        ForeColor = UiColors.ToolTipFore;
    }

    private void OnPopup(object? sender, PopupEventArgs e)
    {
        ApplyOwnerDrawMode();

        var text = e.AssociatedControl is null
            ? string.Empty
            : GetToolTip(e.AssociatedControl);
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var font = SystemFonts.StatusFont;
        var ownsFont = font is null;
        font ??= new Font("Yu Gothic UI", 9F);
        try
        {
            var size = MeasureTip(text, font);
            e.ToolTipSize = new Size(size.Width + PadX * 2, size.Height + PadY * 2);
        }
        finally
        {
            if (ownsFont)
            {
                font.Dispose();
            }
        }
    }

    private void OnDraw(object? sender, DrawToolTipEventArgs e)
    {
        ApplyOwnerDrawMode();

        var backColor = UiColors.ForControlBack(UiColors.ToolTipBack);
        var borderColor = UiColors.ForControlBack(UiColors.ToolTipBorder);
        var foreColor = UiColors.ToolTipFore;

        using (var back = new SolidBrush(backColor))
        {
            e.Graphics.FillRectangle(back, e.Bounds);
        }

        using (var border = new Pen(borderColor))
        {
            e.Graphics.DrawRectangle(
                border,
                e.Bounds.X,
                e.Bounds.Y,
                e.Bounds.Width - 1,
                e.Bounds.Height - 1);
        }

        const TextFormatFlags flags =
            TextFormatFlags.Left
            | TextFormatFlags.Top
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.WordBreak
            | TextFormatFlags.TextBoxControl;

        var textBounds = new Rectangle(
            e.Bounds.X + PadX,
            e.Bounds.Y + PadY,
            Math.Max(0, e.Bounds.Width - PadX * 2),
            Math.Max(0, e.Bounds.Height - PadY * 2));

        TextRenderer.DrawText(
            e.Graphics,
            e.ToolTipText,
            e.Font ?? SystemFonts.StatusFont!,
            textBounds,
            foreColor,
            flags);
    }

    private static Size MeasureTip(string text, Font font)
    {
        const TextFormatFlags flags =
            TextFormatFlags.Left
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.WordBreak
            | TextFormatFlags.TextBoxControl;

        return TextRenderer.MeasureText(
            text,
            font,
            new Size(MaxTipWidth, int.MaxValue),
            flags);
    }
}
