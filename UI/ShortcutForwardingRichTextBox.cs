using System.Runtime.InteropServices;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// ProcessCmdKey を親フォームに先渡しする RichTextBox。
/// 既定の RTF は Ctrl+Home/End などを自己消費するため、ショートカットがフォームまで届かない。
/// 左にスクロールバー幅相当の余白を付け、右端のバーとバランスを取る。
/// </summary>
internal sealed class ShortcutForwardingRichTextBox : RichTextBox
{
    private const int EmGetRect = 0x00B2;
    private const int EmSetRect = 0x00B3;

    public Func<Keys, bool>? ShortcutHandler { get; set; }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (ShortcutHandler?.Invoke(keyData) == true)
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTextMargin();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ApplyTextMargin();
    }

    /// <summary>テキスト描画領域の左をスクロールバー幅ぶん空ける。</summary>
    private void ApplyTextMargin()
    {
        if (!IsHandleCreated || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }

        var margin = SystemInformation.VerticalScrollBarWidth;
        var rect = new NativeRect();
        _ = SendMessage(Handle, EmGetRect, IntPtr.Zero, ref rect);

        // 取得に失敗／未初期化のときはクライアント全体を基準にする
        if (rect.Right <= rect.Left || rect.Bottom <= rect.Top)
        {
            rect.Left = 0;
            rect.Top = 0;
            rect.Right = ClientSize.Width;
            rect.Bottom = ClientSize.Height;
        }

        rect.Left = margin;
        if (rect.Right <= rect.Left)
        {
            rect.Right = rect.Left + 1;
        }

        _ = SendMessage(Handle, EmSetRect, IntPtr.Zero, ref rect);
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref NativeRect lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
