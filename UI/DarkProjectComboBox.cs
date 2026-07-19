using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// プロジェクト名の編集と選択に使うダークテーマ ComboBox。
/// WM_PAINT を自前で処理し、枠・矢印・項目をフラットに描画する
/// （ネイティブ描画との二度描きによるちらつきを避ける）。
/// </summary>
internal sealed class DarkProjectComboBox : ComboBox
{
    private const int WmEraseBkgnd = 0x0014;
    private const int WmPaint = 0x000F;
    private const int WmNcPaint = 0x0085;
    private const int EmSetSel = 0x00B1;
    private const int CbSetItemHeight = 0x0153;
    private const int CbGetItemHeight = 0x0154;

    private bool _hovered;
    private int? _controlHeightTarget;
    private DropDownBorderWindow? _dropDownBorderWindow;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr hWnd, out PaintStruct lpPaint);

    [DllImport("user32.dll")]
    private static extern bool EndPaint(IntPtr hWnd, ref PaintStruct lpPaint);

    [DllImport("user32.dll")]
    private static extern bool GetComboBoxInfo(IntPtr hWnd, ref ComboBoxInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly Rectangle ToRectangle() =>
            Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintStruct
    {
        public IntPtr Hdc;
        public int FErase;
        public NativeRect RcPaint;
        public int FRestore;
        public int FIncUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] RgbReserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo
    {
        public int CbSize;
        public NativeRect RcItem;
        public NativeRect RcButton;
        public int StateButton;
        public IntPtr HwndCombo;
        public IntPtr HwndItem;
        public IntPtr HwndList;
    }

    public DarkProjectComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDown;
        FlatStyle = FlatStyle.Flat;
        IntegralHeight = false;
        ItemHeight = 24;
        MaxDropDownItems = 12;
        ApplyColors();
    }

    public void ApplyColors()
    {
        BackColor = UiColors.ForControlBack(UiColors.ProjectBarInputBack);
        ForeColor = UiColors.ProjectBarInputFore;
        Invalidate();
    }

    /// <summary>
    /// 編集欄のテキスト選択ハイライトを解除する。
    /// ComboBox の子 EDIT にも EM_SETSEL を送り、見た目の全選択を確実に消す。
    /// </summary>
    public void ClearTextSelection()
    {
        SelectionLength = 0;
        SelectionStart = Text?.Length ?? 0;

        if (!IsHandleCreated)
        {
            return;
        }

        var info = new ComboBoxInfo { CbSize = Marshal.SizeOf<ComboBoxInfo>() };
        if (!GetComboBoxInfo(Handle, ref info) || info.HwndItem == IntPtr.Zero)
        {
            return;
        }

        // EM_SETSEL(hwnd, -1, 0) で選択解除（キャレットは末尾）。
        _ = SendMessage(info.HwndItem, EmSetSel, (IntPtr)(-1), IntPtr.Zero);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // ドロップダウン一覧のスクロールバーへ対応 OS のダークテーマを適用する。
        _ = SetWindowTheme(Handle, "DarkMode_CFD", null);
        ApplyControlHeightTarget();
    }

    /// <summary>
    /// コントロール全体の高さを指定値に合わせる。
    /// ItemHeight（ドロップダウン項目の高さ）は変えず、選択フィールドの高さ
    /// （CB_SETITEMHEIGHT の -1）だけを調整する。ハンドル再作成後も維持される。
    /// </summary>
    public void SetControlHeight(int targetHeight)
    {
        _controlHeightTarget = targetHeight;
        ApplyControlHeightTarget();
    }

    private void ApplyControlHeightTarget()
    {
        if (_controlHeightTarget is not int target || !IsHandleCreated)
        {
            return;
        }

        var fieldHeight = (int)SendMessage(Handle, CbGetItemHeight, (IntPtr)(-1), IntPtr.Zero);
        if (fieldHeight <= 0)
        {
            return;
        }

        // コントロール高 = 選択フィールド高 + 固定枠。枠分を実測して差し引く。
        var chrome = Height - fieldHeight;
        var desired = Math.Max(8, target - chrome);
        if (desired != fieldHeight)
        {
            _ = SendMessage(Handle, CbSetItemHeight, (IntPtr)(-1), (IntPtr)desired);
        }
    }

    /// <summary>
    /// 選択フィールド内の編集子ウィンドウ（テキスト表示領域）の矩形。
    /// このコントロールのクライアント座標。取得できないときは null。
    /// </summary>
    public Rectangle? GetEditItemBounds()
    {
        if (!IsHandleCreated)
        {
            return null;
        }

        var info = new ComboBoxInfo { CbSize = Marshal.SizeOf<ComboBoxInfo>() };
        if (!GetComboBoxInfo(Handle, ref info))
        {
            return null;
        }

        return info.RcItem.ToRectangle();
    }

    protected override void OnDropDown(EventArgs e)
    {
        AttachDropDownBorder();
        Invalidate();
        base.OnDropDown(e);
    }

    protected override void OnDropDownClosed(EventArgs e)
    {
        _dropDownBorderWindow?.ReleaseHandle();
        _dropDownBorderWindow = null;
        Invalidate();
        base.OnDropDownClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dropDownBorderWindow?.ReleaseHandle();
            _dropDownBorderWindow = null;
        }

        base.Dispose(disposing);
    }

    private void AttachDropDownBorder()
    {
        var info = new ComboBoxInfo { CbSize = Marshal.SizeOf<ComboBoxInfo>() };
        if (!GetComboBoxInfo(Handle, ref info) || info.HwndList == IntPtr.Zero)
        {
            return;
        }

        _dropDownBorderWindow?.ReleaseHandle();
        _dropDownBorderWindow = new DropDownBorderWindow(info.HwndList);
        PaintWindowBorder(info.HwndList);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        ClearTextSelection();
        base.OnLostFocus(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count)
        {
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        var itemText = GetItemText(Items[e.Index]);
        var back = selected
            ? UiColors.ForControlBack(UiColors.TransportHoverBack)
            : UiColors.ForControlBack(UiColors.ProjectBarInputBack);
        var fore = UiColors.ProjectBarInputFore;

        using var backBrush = new SolidBrush(back);
        e.Graphics.FillRectangle(backBrush, e.Bounds);

        var textBounds = new Rectangle(
            e.Bounds.Left + 8,
            e.Bounds.Top,
            Math.Max(0, e.Bounds.Width - 16),
            e.Bounds.Height);
        TextRenderer.DrawText(
            e.Graphics,
            itemText,
            Font,
            textBounds,
            fore,
            TextFormatFlags.Left
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.NoPrefix);
    }

    private static void PaintWindowBorder(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var rect))
        {
            return;
        }

        var hdc = GetWindowDC(handle);
        if (hdc == IntPtr.Zero)
        {
            return;
        }

        try
        {
            using var g = Graphics.FromHdc(hdc);
            using var pen = new Pen(UiColors.ProjectBarBorder);
            g.DrawRectangle(pen, 0, 0, rect.Right - rect.Left - 1, rect.Bottom - rect.Top - 1);
        }
        finally
        {
            _ = ReleaseDC(handle, hdc);
        }
    }

    private sealed class DropDownBorderWindow : NativeWindow
    {
        public DropDownBorderWindow(IntPtr handle)
        {
            AssignHandle(handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg is WmNcPaint or WmPaint)
            {
                PaintWindowBorder(Handle);
            }
        }
    }

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WmPaint:
                HandlePaint(ref m);
                return;
            case WmNcPaint:
                m.Result = IntPtr.Zero;
                return;
            case WmEraseBkgnd:
                // 全面を WM_PAINT で塗るため、消去はスキップしてちらつきを抑える。
                m.Result = (IntPtr)1;
                return;
        }

        base.WndProc(ref m);
    }

    private void HandlePaint(ref Message m)
    {
        var hdc = BeginPaint(m.HWnd, out var ps);
        try
        {
            using var g = Graphics.FromHdc(hdc);
            DrawFlatChrome(g);
        }
        finally
        {
            EndPaint(m.HWnd, ref ps);
        }

        m.Result = IntPtr.Zero;
    }

    private void DrawFlatChrome(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // 編集子ウィンドウの領域は塗らない（テキスト表示は EDIT 側が担当）。
        var info = new ComboBoxInfo { CbSize = Marshal.SizeOf<ComboBoxInfo>() };
        Rectangle? editBounds = null;
        Rectangle buttonBounds;
        if (GetComboBoxInfo(Handle, ref info))
        {
            editBounds = info.RcItem.ToRectangle();
            buttonBounds = info.RcButton.ToRectangle();
        }
        else
        {
            var buttonWidth = Math.Min(24, Width);
            buttonBounds = new Rectangle(Width - buttonWidth, 1, buttonWidth - 1, Height - 2);
        }

        var state = g.Save();
        if (editBounds is { } edit)
        {
            g.ExcludeClip(edit);
        }

        var inputBack = UiColors.ForControlBack(UiColors.ProjectBarInputBack);
        using (var backBrush = new SolidBrush(inputBack))
        {
            g.FillRectangle(backBrush, ClientRectangle);
        }

        if (Enabled && (_hovered || DroppedDown))
        {
            using var hoverBrush = new SolidBrush(
                UiColors.ForControlBack(UiColors.TransportHoverBack));
            g.FillRectangle(hoverBrush, buttonBounds);
        }

        var fore = Enabled ? UiColors.ProjectBarInputFore : UiColors.ChromeDim;
        var centerX = buttonBounds.Left + buttonBounds.Width / 2f;
        var centerY = buttonBounds.Top + buttonBounds.Height / 2f;
        var arrow = DroppedDown
            ? new[]
            {
                new PointF(centerX - 4f, centerY + 2f),
                new PointF(centerX, centerY - 2f),
                new PointF(centerX + 4f, centerY + 2f),
            }
            : new[]
            {
                new PointF(centerX - 4f, centerY - 2f),
                new PointF(centerX, centerY + 2f),
                new PointF(centerX + 4f, centerY - 2f),
            };
        using (var arrowPen = new Pen(fore, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        })
        {
            g.DrawLines(arrowPen, arrow);
        }

        g.Restore(state);

        using var borderPen = new Pen(
            Enabled ? UiColors.ProjectBarBorder : UiColors.ChromeMid);
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
    }
}
