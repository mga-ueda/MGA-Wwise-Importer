using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 書き出し／読み込み中にフォームのクライアント領域（WAAPI ステータスバーを除く）を覆うすりガラスオーバーレイ。
/// 所有フォーム上に枠なし子ウィンドウとして載せ、解除時は <see cref="Form.Opacity"/> でフェードアウトする。
/// マウス入力はここで吸収する（ショートカットは Form1 のロックフラグ側で抑止）。
/// </summary>
internal sealed class ExportGlassOverlay : Form
{
    private const int MaxDots = 3;
    private const int FadeOutDelayMs = 1000;
    private const int FadeOutDurationMs = 300;
    private const int LogMargin = 18;
    private const int WmMouseActivate = 0x0021;
    private const int WmNcActivate = 0x0086;
    private const int MaNoActivate = 3;
    private const int SwShowNoActivate = 4;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTop = new(0);

    private readonly System.Windows.Forms.Timer _dotsTimer = new() { Interval = 450 };
    private readonly System.Windows.Forms.Timer _fadeDelayTimer = new() { Interval = FadeOutDelayMs };
    private readonly System.Windows.Forms.Timer _fadeTimer = new() { Interval = 16 };
    private readonly List<string> _logLines = [];
    private Bitmap? _frostedSnapshot;
    private Font? _messageFont;
    private Font? _logFont;
    private string _baseText = UiStrings.OverlayExporting;
    private int _dotCount = 1;
    private bool _fadePending;
    private bool _fading;
    private long _fadeStartTickMs;
    private double _fadeStartOpacity = 1.0;

    public ExportGlassOverlay()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        AutoScaleMode = AutoScaleMode.None;
        DoubleBuffered = true;
        Opacity = 1.0;
        Visible = false;

        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint,
            true);

        _dotsTimer.Tick += (_, _) =>
        {
            _dotCount = _dotCount % MaxDots + 1;
            Invalidate();
        };
        _fadeDelayTimer.Tick += (_, _) => StartFadeOut();
        _fadeTimer.Tick += (_, _) => AdvanceFade();
    }

    /// <summary>所有フォームのアクティベーションを奪わずに表示する。</summary>
    protected override bool ShowWithoutActivation => true;

    /// <summary>フェード中でなく、忙しい表示として前面に出ているとき。</summary>
    public bool IsShowingBusy => Visible && !_fading && !_fadePending;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            // タスクバーに出さず、親のアクティブ状態も奪わない。
            cp.ExStyle |= WsExToolwindow | WsExNoActivate;
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmMouseActivate)
        {
            m.Result = MaNoActivate;
            return;
        }

        // このウィンドウが前面に来ても、親のタイトルバーはアクティブ見た目のままにする。
        if (m.Msg == WmNcActivate && m.WParam != IntPtr.Zero && Owner is { IsHandleCreated: true } owner)
        {
            base.WndProc(ref m);
            KeepOwnerLookingActive(owner);
            return;
        }

        base.WndProc(ref m);
    }

    /// <param name="coverBounds">覆う範囲（ホストのクライアント座標）。ステータスバーなどは含めない。</param>
    /// <param name="baseText">中央メッセージ本文（末尾ドットはアニメーションで付与）。</param>
    public void ShowOverlay(Form host, Rectangle coverBounds, string baseText)
    {
        CancelFade();
        Owner = host;
        TopMost = host.TopMost;
        Bounds = new Rectangle(host.PointToScreen(coverBounds.Location), coverBounds.Size);
        BackColor = UiColors.ForControlBack(UiColors.SurfaceBack);

        _frostedSnapshot?.Dispose();
        _frostedSnapshot = CaptureFrostedSnapshot(host, coverBounds);
        _baseText = NormalizeMessage(baseText);
        _dotCount = 1;
        _logLines.Clear();
        Opacity = 1.0;

        if (!Visible)
        {
            ShowWithoutActivating(host);
        }
        else
        {
            Invalidate();
        }

        Refresh();
        Update();
        KeepOwnerLookingActive(host);
        _dotsTimer.Start();
    }

    /// <summary>Show() は所有フォームを一瞬非アクティブにするため、SW_SHOWNOACTIVATE で出す。</summary>
    private void ShowWithoutActivating(Form host)
    {
        Owner = host;
        if (!IsHandleCreated)
        {
            CreateHandle();
        }

        ShowWindow(Handle, SwShowNoActivate);
        Visible = true;
        ReassertAboveOwner();
    }

    /// <summary>
    /// オーナーの Opacity 復帰や Activate で前面順が入れ替わったときに、
    /// アクティブを奪わずオーバーレイをオーナー直上へ戻す。
    /// フェード中／解除待ちは触らない（再表示で下の UI を隠し続けるのを防ぐ）。
    /// </summary>
    public void ReassertAboveOwner()
    {
        if (IsDisposed || !IsHandleCreated || !IsShowingBusy)
        {
            return;
        }

        ShowWindow(Handle, SwShowNoActivate);
        SetWindowPos(
            Handle,
            HwndTop,
            0,
            0,
            0,
            0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
        Refresh();
        Update();
    }

    /// <summary>表示を維持したまま中央メッセージだけ差し替える（起動→Last Session 継続用）。</summary>
    public void SetMessage(string baseText)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => SetMessage(baseText));
            return;
        }

        var next = NormalizeMessage(baseText);
        if (string.Equals(_baseText, next, StringComparison.Ordinal))
        {
            return;
        }

        _baseText = next;
        Invalidate();
    }

    private static string NormalizeMessage(string baseText) =>
        string.IsNullOrWhiteSpace(baseText) ? UiStrings.OverlayLoading : baseText.Trim();

    /// <summary>書き出し中の進行ログを左下へ追加する。</summary>
    public void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(text));
            return;
        }

        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        foreach (var line in lines)
        {
            if (line.Length > 0)
            {
                // セグメント明細など、既存ログのインデントは維持する。
                _logLines.Add(line.TrimEnd());
            }
        }

        Invalidate();
    }

    /// <summary>完了表示を 1 秒維持してから、Opacity を 0 まで落として非表示にする。</summary>
    public void BeginFadeOut()
    {
        if (!Visible || _fadePending || _fading)
        {
            return;
        }

        _dotsTimer.Stop();
        _fadePending = true;
        _fadeDelayTimer.Start();
    }

    private void StartFadeOut()
    {
        _fadeDelayTimer.Stop();
        _fadePending = false;
        if (!Visible)
        {
            return;
        }

        // 完了ログは 1 秒間見せたあと消し、すりガラスだけをフェードアウトする。
        // Form.Opacity なら下の実 UI がそのまま透けるので、終了時の点滅が起きない。
        _logLines.Clear();
        Invalidate();
        Update();
        _fading = true;
        _fadeStartOpacity = Opacity;
        _fadeStartTickMs = Environment.TickCount64;
        _fadeTimer.Start();
    }

    public void HideOverlay()
    {
        var owner = Owner as Form;
        CancelFade();
        _dotsTimer.Stop();
        Opacity = 1.0;
        Hide();
        _frostedSnapshot?.Dispose();
        _frostedSnapshot = null;
        if (owner is { IsHandleCreated: true, IsDisposed: false })
        {
            KeepOwnerLookingActive(owner);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        if (_frostedSnapshot is { } snapshot)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(snapshot, ClientRectangle);
        }
        else
        {
            using var back = new SolidBrush(UiColors.ForControlBack(UiColors.SurfaceBack));
            g.FillRectangle(back, ClientRectangle);
        }

        DrawLog(g);
        // 中央メッセージはログより手前に描く。
        DrawMessage(g);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dotsTimer.Dispose();
            _fadeDelayTimer.Dispose();
            _fadeTimer.Dispose();
            _frostedSnapshot?.Dispose();
            _frostedSnapshot = null;
            _messageFont?.Dispose();
            _messageFont = null;
            _logFont?.Dispose();
            _logFont = null;
        }

        base.Dispose(disposing);
    }

    private float FadeProgress() =>
        Math.Clamp((Environment.TickCount64 - _fadeStartTickMs) / (float)FadeOutDurationMs, 0f, 1f);

    private void AdvanceFade()
    {
        var progress = FadeProgress();
        Opacity = _fadeStartOpacity * (1.0 - progress);
        if (progress >= 1f)
        {
            HideOverlay();
        }
    }

    private void CancelFade()
    {
        _fadeDelayTimer.Stop();
        _fadeTimer.Stop();
        _fadePending = false;
        _fading = false;
    }

    private void DrawMessage(Graphics g)
    {
        _messageFont ??= new Font(Font.FontFamily, 15f, FontStyle.Bold, GraphicsUnit.Point);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        // ドット数が変わっても本文位置が動かないよう、最大幅基準でセンタリングする。
        var baseSize = TextRenderer.MeasureText(g, _baseText, _messageFont, Size.Empty, flags);
        var maxDotsText = " " + new string('.', MaxDots);
        var maxDotsSize = TextRenderer.MeasureText(g, maxDotsText, _messageFont, Size.Empty, flags);
        var x = (Width - (baseSize.Width + maxDotsSize.Width)) / 2;
        var y = (Height - baseSize.Height) / 2;
        var dotsText = " " + new string('.', _dotCount);

        DrawTextWithOutline(g, _baseText, new Point(x, y), flags);
        DrawTextWithOutline(g, dotsText, new Point(x + baseSize.Width, y), flags);
    }

    private void DrawLog(Graphics g)
    {
        if (_logLines.Count == 0)
        {
            return;
        }

        _logFont ??= AppFonts.CreateLogFont(8f);
        const TextFormatFlags flags =
            TextFormatFlags.NoPadding
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.EndEllipsis
            | TextFormatFlags.SingleLine;
        var lineHeight = TextRenderer.MeasureText(g, "Ag", _logFont, Size.Empty, flags).Height + 3;
        var maximumBottom = Height - LogMargin;
        var maximumWidth = Math.Max(1, Width - LogMargin * 2);
        // 行数を固定せず、画面内に入るだけ下端から上へ表示する。
        var availableHeight = Math.Max(0, maximumBottom - LogMargin);
        var visibleCount = Math.Min(_logLines.Count, availableHeight / lineHeight);
        var first = _logLines.Count - visibleCount;
        var y = maximumBottom - visibleCount * lineHeight;

        var section = Form1.LogColorSection.None;
        for (var i = 0; i < _logLines.Count; i++)
        {
            section = Form1.AdvanceLogColorSection(_logLines[i], section);
            if (i < first)
            {
                continue;
            }

            var bounds = new Rectangle(LogMargin, y, maximumWidth, lineHeight);
            TextRenderer.DrawText(
                g,
                _logLines[i],
                _logFont,
                new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width, bounds.Height),
                Color.FromArgb(210, Color.Black),
                flags);
            TextRenderer.DrawText(
                g,
                _logLines[i],
                _logFont,
                bounds,
                Form1.ColorForLogLine(_logLines[i], section),
                flags);
            y += lineHeight;
        }
    }

    private void DrawTextWithOutline(Graphics g, string text, Point location, TextFormatFlags flags)
    {
        var font = _messageFont!;

        // 柔らかいドロップシャドウ（縁より下に描く）。
        foreach (var (dx, dy, alpha) in SoftShadowLayers)
        {
            TextRenderer.DrawText(
                g,
                text,
                font,
                new Point(location.X + dx, location.Y + dy),
                Color.FromArgb(alpha, 0, 0, 0),
                flags);
        }

        // 周囲 8 方向に黒を描いて縁取りする。
        foreach (var (dx, dy) in OutlineOffsets)
        {
            TextRenderer.DrawText(
                g,
                text,
                font,
                new Point(location.X + dx, location.Y + dy),
                Color.Black,
                flags);
        }

        TextRenderer.DrawText(g, text, font, location, UiColors.PrimaryFore, flags);
    }

    private static readonly (int Dx, int Dy, int Alpha)[] SoftShadowLayers =
    [
        (2, 2, 40),
        (3, 3, 55),
        (4, 4, 40),
        (5, 5, 25),
    ];

    private static readonly (int Dx, int Dy)[] OutlineOffsets =
    [
        (-1, -1), (0, -1), (1, -1),
        (-1, 0), (1, 0),
        (-1, 1), (0, 1), (1, 1),
    ];

    /// <summary>
    /// 覆う範囲だけをキャプチャし、縮小→拡大の 2 段階でぼかした画像を作る。
    /// キャプチャできない場合は null（単色背景で代替）。
    /// </summary>
    private static Bitmap? CaptureFrostedSnapshot(Form host, Rectangle coverBounds)
    {
        var size = coverBounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        try
        {
            // Form.DrawToBitmap はタイトルバー込みのウィンドウ全体を描くため使わない。
            using var capture = host.Opacity >= 1d
                ? CaptureViaScreen(host, coverBounds) ?? CaptureViaClientControls(host, coverBounds)
                : CaptureViaClientControls(host, coverBounds) ?? CaptureViaScreen(host, coverBounds);
            if (capture is null)
            {
                return null;
            }

            return BuildFrosted(capture, size);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Bitmap? CaptureViaScreen(Form host, Rectangle coverBounds)
    {
        var size = coverBounds.Size;
        if (size.Width <= 0 || size.Height <= 0 || host.Opacity < 1d)
        {
            return null;
        }

        var capture = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(capture);
        g.CopyFromScreen(host.PointToScreen(coverBounds.Location), Point.Empty, size);
        return capture;
    }

    private static Bitmap? CaptureViaClientControls(Form host, Rectangle coverBounds)
    {
        var clientSize = host.ClientSize;
        var clip = Rectangle.Intersect(coverBounds, new Rectangle(Point.Empty, clientSize));
        if (clip.Width <= 0 || clip.Height <= 0)
        {
            return null;
        }

        var capture = new Bitmap(clip.Width, clip.Height);
        using var g = Graphics.FromImage(capture);
        g.Clear(host.BackColor);
        g.TranslateTransform(-clip.X, -clip.Y);

        var children = new Control[host.Controls.Count];
        host.Controls.CopyTo(children, 0);
        Array.Reverse(children);
        foreach (var child in children)
        {
            if (!child.Visible || child.Width <= 0 || child.Height <= 0)
            {
                continue;
            }

            if (!child.Bounds.IntersectsWith(clip))
            {
                continue;
            }

            using var childBmp = new Bitmap(child.Width, child.Height);
            child.DrawToBitmap(childBmp, new Rectangle(0, 0, child.Width, child.Height));
            g.DrawImageUnscaled(childBmp, child.Left, child.Top);
        }

        return capture;
    }

    private static Bitmap BuildFrosted(Bitmap source, Size size)
    {
        using var half = ScaleTo(source, size.Width / 6, size.Height / 6);
        using var tiny = ScaleTo(half, size.Width / 20, size.Height / 20);

        var frosted = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(frosted);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(tiny, new Rectangle(Point.Empty, size));

        var tintBase = UiColors.SurfaceBack;
        using var tint = new SolidBrush(Color.FromArgb(140, tintBase.R, tintBase.G, tintBase.B));
        g.FillRectangle(tint, new Rectangle(Point.Empty, size));
        return frosted;
    }

    private static Bitmap ScaleTo(Bitmap source, int width, int height)
    {
        var scaled = new Bitmap(Math.Max(1, width), Math.Max(1, height));
        using var g = Graphics.FromImage(scaled);
        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(source, new Rectangle(0, 0, scaled.Width, scaled.Height));
        return scaled;
    }

    private static void KeepOwnerLookingActive(Form owner)
    {
        if (!owner.IsHandleCreated || owner.IsDisposed)
        {
            return;
        }

        SetActiveWindow(owner.Handle);
        SendMessage(owner.Handle, WmNcActivate, (IntPtr)1, IntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr SetActiveWindow(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
