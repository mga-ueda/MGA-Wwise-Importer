namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 押下時にも文字位置を動かさない Playlist 専用フラットボタン。
/// 無効状態は Enabled ではなく Form1 側の文字色で表現する。
/// </summary>
internal sealed class FlatPlaylistButton : Button
{
    /// <summary>
    /// OnPaint と同じ条件で文字幅を測る。
    /// （NoPadding 無し。計測を描画と揃えないと省略記号が出る）
    /// </summary>
    public static int MeasureDisplayTextWidth(string text, Font font) =>
        TextRenderer.MeasureText(
            text,
            font,
            Size.Empty,
            TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width;

    public FlatPlaylistButton()
    {
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        TabStop = false;
        UseVisualStyleBackColor = false;
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);
        // クリックでフォーカスを奪わず、上下キーの波形拡縮を阻害しない。
        SetStyle(ControlStyles.Selectable, false);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        Invalidate();
        base.OnEnabledChanged(e);
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
            Enabled ? ForeColor : UiColors.ActionButtonDisabledFore,
            TextFormatFlags.Left
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine);
    }
}
