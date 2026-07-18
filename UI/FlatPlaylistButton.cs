namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 押下時にも文字位置を動かさない Playlist 専用フラットボタン。
/// 無効状態は Enabled ではなく Form1 側の文字色で表現する。
/// </summary>
internal sealed class FlatPlaylistButton : Button
{
    private Color? _indicatorColor;
    private double _indicatorGlowLevel;

    /// <summary>
    /// 左端の状態インジケーター色。null のときは表示しない。
    /// </summary>
    public Color? IndicatorColor
    {
        get => _indicatorColor;
        set
        {
            if (_indicatorColor == value)
            {
                return;
            }

            _indicatorColor = value;
            Invalidate();
        }
    }

    /// <summary>遷移時にインジケーターの周囲へ加えるフェード強度。</summary>
    public double IndicatorGlowLevel
    {
        get => _indicatorGlowLevel;
        set
        {
            var level = Math.Clamp(value, 0d, 1d);
            if (Math.Abs(_indicatorGlowLevel - level) < 0.001d)
            {
                return;
            }

            _indicatorGlowLevel = level;
            Invalidate();
        }
    }

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

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        const int indicatorWidth = 6;
        const int indicatorHeight = 16;
        const int indicatorLeft = 2;
        if (_indicatorColor is Color indicatorColor)
        {
            var indicatorTop = Math.Max(0, (ClientSize.Height - indicatorHeight) / 2);
            if (_indicatorGlowLevel > 0d)
            {
                var glowAlpha = (int)Math.Round(110d * _indicatorGlowLevel);
                using var glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, indicatorColor));
                e.Graphics.FillRectangle(
                    glowBrush,
                    indicatorLeft - 1,
                    Math.Max(0, indicatorTop - 2),
                    indicatorWidth + 2,
                    Math.Min(indicatorHeight + 4, ClientSize.Height));
            }

            using var brush = new SolidBrush(indicatorColor);
            e.Graphics.FillRectangle(
                brush,
                indicatorLeft,
                indicatorTop,
                indicatorWidth,
                Math.Min(indicatorHeight, ClientSize.Height));
        }

        var textBounds = Rectangle.FromLTRB(
            Padding.Left + indicatorLeft + indicatorWidth + 5,
            Padding.Top,
            Math.Max(
                Padding.Left + indicatorLeft + indicatorWidth + 5,
                ClientSize.Width - Padding.Right),
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
