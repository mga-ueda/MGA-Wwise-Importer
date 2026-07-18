namespace MgaWwiseIMImporter.UI;

/// <summary>
/// Playlist 行の左外側に置くグループ塗り分け用の四角枠。
/// Shift＋クリック／ドラッグでグループ塗り、Ctrl で解除塗りの起点になる。
/// </summary>
internal sealed class PlaylistGroupSwatch : Control
{
    private Color? _groupColor;

    public const int BoxSize = 12;
    public const int ControlWidth = 16;

    /// <summary>
    /// グループ枠の塗り色。null のときは空枠のみ表示する。
    /// </summary>
    public Color? GroupColor
    {
        get => _groupColor;
        set
        {
            if (_groupColor == value)
            {
                return;
            }

            _groupColor = value;
            Invalidate();
        }
    }

    public PlaylistGroupSwatch()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer,
            true);
        // クリックでフォーカスを奪わず、上下キーの波形拡縮を阻害しない。
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        Width = ControlWidth;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        var top = Math.Max(0, (ClientSize.Height - BoxSize) / 2);
        var bounds = new Rectangle(0, top, BoxSize, Math.Min(BoxSize, ClientSize.Height));
        if (_groupColor is Color groupColor)
        {
            using var fill = new SolidBrush(groupColor);
            e.Graphics.FillRectangle(fill, bounds);
            using var border = new Pen(ControlPaint.Dark(groupColor), 1f);
            e.Graphics.DrawRectangle(
                border,
                bounds.X,
                bounds.Y,
                bounds.Width - 1,
                bounds.Height - 1);
        }
        else
        {
            using var border = new Pen(UiColors.PlaylistButtonBorder, 1f);
            e.Graphics.DrawRectangle(
                border,
                bounds.X,
                bounds.Y,
                bounds.Width - 1,
                bounds.Height - 1);
        }
    }
}
