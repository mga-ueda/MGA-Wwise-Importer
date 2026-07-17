namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 押下時にも文字位置を動かさない Playlist 専用フラットボタン。
/// 無効状態は Enabled ではなく Form1 側の文字色で表現する。
/// </summary>
internal sealed class FlatPlaylistButton : Button
{
    public FlatPlaylistButton()
    {
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        var borderSize = FlatAppearance.BorderSize;
        if (borderSize > 0)
        {
            using var pen = new Pen(FlatAppearance.BorderColor, borderSize);
            var inset = borderSize / 2f;
            e.Graphics.DrawRectangle(
                pen,
                inset,
                inset,
                Math.Max(0f, ClientSize.Width - borderSize),
                Math.Max(0f, ClientSize.Height - borderSize));
        }

        var textBounds = Rectangle.FromLTRB(
            Padding.Left,
            Padding.Top,
            Math.Max(Padding.Left, ClientSize.Width - Padding.Right),
            Math.Max(Padding.Top, ClientSize.Height - Padding.Bottom));
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            Font,
            textBounds,
            ForeColor,
            TextFormatFlags.Left
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine);
    }
}
