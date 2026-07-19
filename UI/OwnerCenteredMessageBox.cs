using System.Runtime.InteropServices;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 所有フォームの中央に表示する標準 MessageBox／コモンダイアログ。
/// 表示直前に CBT フックでダイアログ位置を差し替える（見た目・操作は標準のまま）。
/// </summary>
internal static class OwnerCenteredMessageBox
{
    private const int WhCbt = 5;
    private const int HcbtActivate = 5;

    // GC に回収されないよう static に保持する。
    private static readonly HookProcDelegate HookProcInstance = HookProc;

    [ThreadStatic] private static IntPtr _hook;
    [ThreadStatic] private static IntPtr _ownerHandle;

    public static DialogResult Show(
        IWin32Window owner,
        string text,
        string caption,
        MessageBoxButtons buttons,
        MessageBoxIcon icon,
        MessageBoxDefaultButton defaultButton = MessageBoxDefaultButton.Button1)
    {
        _ownerHandle = owner.Handle;
        _hook = SetWindowsHookEx(WhCbt, HookProcInstance, IntPtr.Zero, GetCurrentThreadId());
        try
        {
            return MessageBox.Show(owner, text, caption, buttons, icon, defaultButton);
        }
        finally
        {
            Unhook();
        }
    }

    /// <summary>
    /// FolderBrowserDialog／SaveFileDialog／ColorDialog などのコモンダイアログを
    /// 所有フォームの中央に表示する。
    /// </summary>
    public static DialogResult ShowDialog(IWin32Window owner, CommonDialog dialog)
    {
        _ownerHandle = owner.Handle;
        _hook = SetWindowsHookEx(WhCbt, HookProcInstance, IntPtr.Zero, GetCurrentThreadId());
        try
        {
            return dialog.ShowDialog(owner);
        }
        finally
        {
            Unhook();
        }
    }

    private static IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam)
    {
        var hook = _hook;
        if (code == HcbtActivate)
        {
            CenterOnOwner(wParam);
            Unhook();
        }

        return CallNextHookEx(hook, code, wParam, lParam);
    }

    private static void CenterOnOwner(IntPtr dialogHandle)
    {
        if (_ownerHandle == IntPtr.Zero
            || !GetWindowRect(_ownerHandle, out var ownerRect)
            || !GetWindowRect(dialogHandle, out var dialogRect))
        {
            return;
        }

        var width = dialogRect.Right - dialogRect.Left;
        var height = dialogRect.Bottom - dialogRect.Top;
        var x = ownerRect.Left + (ownerRect.Right - ownerRect.Left - width) / 2;
        var y = ownerRect.Top + (ownerRect.Bottom - ownerRect.Top - height) / 2;

        // 画面外へはみ出さないよう作業領域内へ収める。
        var workingArea = Screen.FromHandle(_ownerHandle).WorkingArea;
        x = Math.Clamp(x, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - width));
        y = Math.Clamp(y, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - height));

        MoveWindow(dialogHandle, x, y, width, height, repaint: false);
    }

    private static void Unhook()
    {
        if (_hook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _ownerHandle = IntPtr.Zero;
    }

    private delegate IntPtr HookProcDelegate(int code, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook, HookProcDelegate lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(
        IntPtr hWnd, int x, int y, int nWidth, int nHeight, bool repaint);
}
