using System.Runtime.InteropServices;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// FixedSingle の枠（WS_BORDER、システム色で明るい）を
/// ダークテーマの任意色で上書き描画する TextBox。
/// </summary>
internal sealed class DarkBorderTextBox : TextBox
{
    private const int WmNcPaint = 0x0085;
    private const int WmPaint = 0x000F;

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    private Color _borderColor = Color.FromArgb(55, 55, 58);

    /// <summary>枠の色。変更時は枠を再描画する。</summary>
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            if (_borderColor == value)
            {
                return;
            }

            _borderColor = value;
            PaintDarkBorder();
        }
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        // 枠は非クライアント領域に描かれるため、システム描画の直後に塗り重ねる。
        if ((m.Msg == WmNcPaint || m.Msg == WmPaint)
            && BorderStyle == BorderStyle.FixedSingle)
        {
            PaintDarkBorder();
        }
    }

    private void PaintDarkBorder()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var hdc = GetWindowDC(Handle);
        if (hdc == IntPtr.Zero)
        {
            return;
        }

        try
        {
            using var g = Graphics.FromHdc(hdc);
            // 高 DPI では枠が 2px 以上になり得るため、フレーム厚ぶんを塗る。
            var frameThickness = Math.Max(1, (Width - ClientSize.Width) / 2);
            using var pen = new Pen(_borderColor);
            for (var i = 0; i < frameThickness; i++)
            {
                g.DrawRectangle(pen, i, i, Width - 1 - i * 2, Height - 1 - i * 2);
            }
        }
        finally
        {
            _ = ReleaseDC(Handle, hdc);
        }
    }
}
