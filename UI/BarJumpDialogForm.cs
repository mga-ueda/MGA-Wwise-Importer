using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 小節番号を入力してジャンプする簡易ダイアログ。Enter で確定、Esc でキャンセル。
/// 枠線なし。影は同一ウィンドウの半透明レイヤーに描画（別窓にしないのでフォーカスで消えない）。
/// </summary>
internal sealed class BarJumpDialogForm : Form
{
    private const int CornerRadius = 8;
    private const int InputCornerRadius = 4;
    private const int ContentWidth = 220;
    private const int ContentHeight = 88;
    private const int ShadowBlur = 25;
    private const int ShadowOffsetX = 0;
    private const int ShadowOffsetY = 5;
    private const int ShadowMaxAlpha = 10;
    private readonly int _pad;
    private readonly Rectangle _contentBounds;
    private readonly Rectangle _inputBounds;
    private readonly TextBox _barNumberBox;
    private readonly Form _inputHost;
    private Bitmap? _frameBitmap;

    public int? BarNumber { get; private set; }

    public BarJumpDialogForm(int? initialBarNumber = null)
    {
        _pad = ShadowBlur + Math.Max(Math.Abs(ShadowOffsetX), Math.Abs(ShadowOffsetY));
        _contentBounds = new Rectangle(_pad, _pad, ContentWidth, ContentHeight);
        const int inputWidth = 170;
        const int inputHeight = 34;
        _inputBounds = new Rectangle(
            _contentBounds.X + (ContentWidth - inputWidth) / 2,
            _contentBounds.Y + 36,
            inputWidth,
            inputHeight);

        Text = UiStrings.DialogBarJumpTitle;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        KeyPreview = true;
        AutoScaleMode = AutoScaleMode.None;
        RightToLeft = RightToLeft.No;
        BackColor = UiColors.ForControlBack(UiColors.DialogShadow);
        ClientSize = new Size(ContentWidth + _pad * 2, ContentHeight + _pad * 2);

        _barNumberBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 14F),
            BackColor = UiColors.ForControlBack(UiColors.DialogInputBack),
            ForeColor = UiColors.DialogFore,
            BorderStyle = BorderStyle.None,
            TextAlign = HorizontalAlignment.Center,
            RightToLeft = RightToLeft.No,
            Margin = Padding.Empty,
            AutoSize = false,
        };
        if (initialBarNumber is int initial && initial > 0)
        {
            _barNumberBox.Text = initial.ToString();
        }

        _barNumberBox.KeyDown += OnInputKeyDown;
        _barNumberBox.HandleCreated += (_, _) => ApplyInputTextMargins();

        // UpdateLayeredWindow では子コントロールが見えないため、入力だけ前面の所有ウィンドウに載せる
        _inputHost = new Form
        {
            FormBorderStyle = FormBorderStyle.None,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.Manual,
            AutoScaleMode = AutoScaleMode.None,
            RightToLeft = RightToLeft.No,
            BackColor = UiColors.ForControlBack(UiColors.DialogInputBack),
            Size = new Size(_inputBounds.Width - 4, _inputBounds.Height - 4),
            Padding = Padding.Empty,
        };
        _inputHost.Controls.Add(_barNumberBox);
    }

    protected override bool ShowWithoutActivation => false;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x00080000; // WS_EX_LAYERED
            return cp;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (Owner is { TopMost: true })
        {
            TopMost = true;
        }

        ApplyFrameBitmap();
        ShowInputHost();
        ApplyInputTextMargins();
        _barNumberBox.Focus();
        _barNumberBox.SelectAll();
    }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        SyncInputHostPosition();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_inputHost is { IsDisposed: false })
        {
            _inputHost.Close();
            _inputHost.Dispose();
        }

        _frameBitmap?.Dispose();
        _frameBitmap = null;
        base.OnFormClosed(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter)
        {
            AcceptIfValid();
            return true;
        }

        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            AcceptIfValid();
            return;
        }

        if (e.KeyCode == Keys.Escape)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }

    private void AcceptIfValid()
    {
        var text = _barNumberBox.Text.Trim();
        if (!int.TryParse(text, out var bar) || bar < 1)
        {
            _barNumberBox.Focus();
            _barNumberBox.SelectAll();
            return;
        }

        BarNumber = bar;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void ApplyInputTextMargins()
    {
        if (!_barNumberBox.IsHandleCreated)
        {
            return;
        }

        // BorderStyle.None の EDIT は左右余白が非対称になりやすいので均等化する。
        const int emSetMargins = 0x00D3;
        const int ecLeftMargin = 0x0001;
        const int ecRightMargin = 0x0002;
        const int margin = 0;
        var value = (IntPtr)((margin & 0xFFFF) | ((margin & 0xFFFF) << 16));
        _ = SendMessage(
            _barNumberBox.Handle,
            emSetMargins,
            (IntPtr)(ecLeftMargin | ecRightMargin),
            value);
        _barNumberBox.TextAlign = HorizontalAlignment.Center;
    }

    private void ShowInputHost()
    {
        if (_inputHost.Visible)
        {
            SyncInputHostPosition();
            return;
        }

        _inputHost.TopMost = TopMost;
        _inputHost.Show(this);
        SyncInputHostPosition();
    }

    private void SyncInputHostPosition()
    {
        if (!_inputHost.IsHandleCreated || !IsHandleCreated)
        {
            return;
        }

        // 入力ウェル中央に 2px インセット
        var local = new Point(_inputBounds.X + 2, _inputBounds.Y + 2);
        var screen = PointToScreen(local);
        _inputHost.Location = screen;
    }

    private void ApplyFrameBitmap()
    {
        _frameBitmap?.Dispose();
        _frameBitmap = BuildFrameBitmap();

        using var premul = new Bitmap(_frameBitmap.Width, _frameBitmap.Height, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(premul))
        {
            g.Clear(Color.Transparent);
            g.DrawImageUnscaled(_frameBitmap, 0, 0);
        }

        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var hBitmap = premul.GetHbitmap(Color.FromArgb(0));
        var oldBitmap = SelectObject(memDc, hBitmap);

        var size = new SizeStruct { Cx = premul.Width, Cy = premul.Height };
        var pointSource = new PointStruct { X = 0, Y = 0 };
        var topPos = new PointStruct { X = Left, Y = Top };
        var blend = new BlendFunction
        {
            BlendOp = 0,
            BlendFlags = 0,
            SourceConstantAlpha = 255,
            AlphaFormat = 1,
        };

        UpdateLayeredWindow(
            Handle,
            screenDc,
            ref topPos,
            ref size,
            memDc,
            ref pointSource,
            0,
            ref blend,
            0x00000002);

        SelectObject(memDc, oldBitmap);
        DeleteObject(hBitmap);
        DeleteDC(memDc);
        ReleaseDC(IntPtr.Zero, screenDc);
    }

    private Bitmap BuildFrameBitmap()
    {
        var bmp = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        g.CompositingMode = CompositingMode.SourceOver;

        DrawSoftShadow(g, _contentBounds);

        // 枠線なし：本体と入力ウェルを塗りだけ
        using (var bodyPath = CreateRoundedRectanglePath(_contentBounds, CornerRadius))
        using (var bodyBrush = new SolidBrush(UiColors.DialogBodyBack))
        {
            g.FillPath(bodyBrush, bodyPath);
        }

        using (var inputPath = CreateRoundedRectanglePath(_inputBounds, InputCornerRadius))
        using (var inputBrush = new SolidBrush(UiColors.DialogInputBack))
        {
            g.FillPath(inputBrush, inputPath);
        }

        var titleRect = new Rectangle(_contentBounds.X, _contentBounds.Y + 8, ContentWidth, 22);
        using var titleFont = new Font("Yu Gothic UI", 9F);
        using var titleBrush = new SolidBrush(UiColors.DialogFore);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        g.DrawString(UiStrings.LabelGoToMeasure, titleFont, titleBrush, titleRect, format);

        return bmp;
    }

    private static void DrawSoftShadow(Graphics g, Rectangle content)
    {
        var shadowBase = content;
        shadowBase.Offset(ShadowOffsetX, ShadowOffsetY);

        for (var i = ShadowBlur; i >= 1; i--)
        {
            var t = i / (float)ShadowBlur;
            // 外側ほど薄く、内側ほど濃い半透明黒
            var alpha = (int)(ShadowMaxAlpha * (1f - t) * (1f - t));
            if (alpha <= 0)
            {
                continue;
            }

            var bounds = Rectangle.Inflate(shadowBase, i, i);
            using var path = CreateRoundedRectanglePath(bounds, CornerRadius + i);
            using var brush = new SolidBrush(Color.FromArgb(alpha, UiColors.DialogShadow));
            g.FillPath(brush, path);
        }
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return path;
        }

        var diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SizeStruct
    {
        public int Cx;
        public int Cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr hdcDst,
        ref PointStruct pptDst,
        ref SizeStruct psize,
        IntPtr hdcSrc,
        ref PointStruct pptSrc,
        int crKey,
        ref BlendFunction pblend,
        int dwFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}
