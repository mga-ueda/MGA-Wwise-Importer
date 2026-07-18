namespace MgaWwiseIMImporter.UI;

/// <summary>
/// セクション見出しラベル。行の中へ上下左右にマージンを取った
/// 一段低いグレー帯（BarColor）を描き、隣接する列の帯どうしが接しないようにする。
/// </summary>
internal sealed class SectionHeaderLabel : Label
{
    private Color _barColor = UiColors.SectionHeaderBack;

    /// <summary>見出し帯の塗り色。周囲は BackColor で塗られる。</summary>
    public Color BarColor
    {
        get => _barColor;
        set
        {
            if (_barColor == value)
            {
                return;
            }

            _barColor = value;
            Invalidate();
        }
    }

    public SectionHeaderLabel()
    {
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.ResizeRedraw,
            true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        // 下の UI との間隔を広めに取るため、下側マージンを厚くして帯を低くする。
        var marginX = S(3);
        var marginTop = S(3);
        var marginBottom = S(7);
        var bar = new Rectangle(
            marginX,
            marginTop,
            Math.Max(0, Width - marginX * 2),
            Math.Max(0, Height - marginTop - marginBottom));
        using (var brush = new SolidBrush(_barColor))
        {
            e.Graphics.FillRectangle(brush, bar);
        }

        // テキスト位置は従来どおり Padding 基準（下の選択肢と左端を揃える）。
        var textBounds = Rectangle.FromLTRB(
            Padding.Left,
            bar.Top,
            Math.Max(Padding.Left, Width - Padding.Right),
            bar.Bottom);
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

    private int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);
}
