using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 書き出し／読み込み中にフォームのクライアント領域（WAAPI ステータスバーを除く）を覆うすりガラス。
/// ホスト Form の子コントロールとして載せるため、ウィンドウ移動に自動で追従する。
/// マウス入力はここで吸収する（ショートカットは Form1 のロックフラグ側で抑止）。
/// </summary>
internal sealed class ExportGlassOverlay : Control
{
    private const int MaxDots = 3;
    private const int FadeOutDelayMs = 1000;
    private const int FadeOutDurationMs = 300;
    private const int LogMargin = 18;

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
    private float _fadeStartOpacity = 1f;
    private float _paintOpacity = 1f;

    public ExportGlassOverlay()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw
            | ControlStyles.SupportsTransparentBackColor,
            true);
        BackColor = Color.Transparent;
        TabStop = false;
        Visible = false;

        _dotsTimer.Tick += (_, _) =>
        {
            _dotCount = _dotCount % MaxDots + 1;
            Invalidate();
        };
        _fadeDelayTimer.Tick += (_, _) => StartFadeOut();
        _fadeTimer.Tick += (_, _) => AdvanceFade();
    }

    /// <summary>フェード中でなく、忙しい表示として前面に出ているとき。</summary>
    public bool IsShowingBusy => Visible && !_fading && !_fadePending;

    /// <param name="host">載せる親（通常はメイン Form）。</param>
    /// <param name="coverBounds">覆う範囲（ホストのクライアント座標）。ステータスバーなどは含めない。</param>
    /// <param name="baseText">中央メッセージ本文（末尾ドットはアニメーションで付与）。</param>
    public void ShowOverlay(Control host, Rectangle coverBounds, string baseText)
    {
        CancelFade();
        EnsureParent(host);
        Bounds = coverBounds;
        BringToFront();

        _frostedSnapshot?.Dispose();
        _frostedSnapshot = CaptureFrostedSnapshot(host, coverBounds);
        _baseText = NormalizeMessage(baseText);
        _dotCount = 1;
        _logLines.Clear();
        _paintOpacity = 1f;

        Visible = true;
        Invalidate();
        Update();
        _dotsTimer.Start();
    }

    /// <summary>リサイズ時にカバー範囲だけ合わせる（スナップショットは引き伸ばし表示）。</summary>
    public void SyncBounds(Rectangle coverBounds)
    {
        if (IsDisposed || !IsShowingBusy)
        {
            return;
        }

        if (Bounds == coverBounds)
        {
            return;
        }

        Bounds = coverBounds;
        BringToFront();
        Invalidate();
    }

    private void EnsureParent(Control host)
    {
        if (ReferenceEquals(Parent, host))
        {
            return;
        }

        Parent?.Controls.Remove(this);
        host.Controls.Add(this);
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
                _logLines.Add(line.TrimEnd());
            }
        }

        Invalidate();
    }

    /// <summary>完了表示を 1 秒維持してから、描画不透明度を 0 まで落として非表示にする。</summary>
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

        // 完了ログは 1 秒間見せたあと消し、すりガラスをフェードアウトする。
        _logLines.Clear();
        Invalidate();
        Update();
        _fading = true;
        _fadeStartOpacity = _paintOpacity;
        _fadeStartTickMs = Environment.TickCount64;
        _fadeTimer.Start();
    }

    public void HideOverlay()
    {
        CancelFade();
        _dotsTimer.Stop();
        _paintOpacity = 1f;
        Visible = false;
        _frostedSnapshot?.Dispose();
        _frostedSnapshot = null;
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // 透明対応: 親を透かしてフェードで本体 UI が見えるようにする。
        if (_paintOpacity < 0.999f || _frostedSnapshot is null)
        {
            base.OnPaintBackground(e);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var opacity = Math.Clamp(_paintOpacity, 0f, 1f);
        if (_frostedSnapshot is { } snapshot)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            if (opacity >= 0.999f)
            {
                g.DrawImage(snapshot, ClientRectangle);
            }
            else
            {
                DrawImageWithOpacity(g, snapshot, ClientRectangle, opacity);
            }
        }
        else
        {
            var tint = UiColors.ForControlBack(UiColors.SurfaceBack);
            using var back = new SolidBrush(Color.FromArgb(
                (int)Math.Round(255 * opacity),
                tint.R,
                tint.G,
                tint.B));
            g.FillRectangle(back, ClientRectangle);
        }

        DrawLog(g, opacity);
        DrawMessage(g, opacity);
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
        _paintOpacity = _fadeStartOpacity * (1f - progress);
        Invalidate();
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
        _paintOpacity = 1f;
    }

    private void DrawMessage(Graphics g, float opacity)
    {
        _messageFont ??= new Font(Font.FontFamily, 15f, FontStyle.Bold, GraphicsUnit.Point);
        const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

        var baseSize = TextRenderer.MeasureText(g, _baseText, _messageFont, Size.Empty, flags);
        var maxDotsText = " " + new string('.', MaxDots);
        var maxDotsSize = TextRenderer.MeasureText(g, maxDotsText, _messageFont, Size.Empty, flags);
        var x = (Width - (baseSize.Width + maxDotsSize.Width)) / 2;
        var y = (Height - baseSize.Height) / 2;
        var dotsText = " " + new string('.', _dotCount);

        DrawTextWithOutline(g, _baseText, new Point(x, y), flags, opacity);
        DrawTextWithOutline(g, dotsText, new Point(x + baseSize.Width, y), flags, opacity);
    }

    private void DrawLog(Graphics g, float opacity)
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
        var availableHeight = Math.Max(0, maximumBottom - LogMargin);
        var visibleCount = Math.Min(_logLines.Count, availableHeight / lineHeight);
        var first = _logLines.Count - visibleCount;
        var y = maximumBottom - visibleCount * lineHeight;

        var section = Form1.LogColorSection.None;
        var alpha = (int)Math.Round(255 * opacity);
        for (var i = 0; i < _logLines.Count; i++)
        {
            section = Form1.AdvanceLogColorSection(_logLines[i], section);
            if (i < first)
            {
                continue;
            }

            var bounds = new Rectangle(LogMargin, y, maximumWidth, lineHeight);
            var fore = Form1.ColorForLogLine(_logLines[i], section);
            TextRenderer.DrawText(
                g,
                _logLines[i],
                _logFont,
                new Rectangle(bounds.X + 1, bounds.Y + 1, bounds.Width, bounds.Height),
                Color.FromArgb(Math.Min(210, alpha), Color.Black),
                flags);
            TextRenderer.DrawText(
                g,
                _logLines[i],
                _logFont,
                bounds,
                Color.FromArgb(alpha, fore),
                flags);
            y += lineHeight;
        }
    }

    private void DrawTextWithOutline(
        Graphics g,
        string text,
        Point location,
        TextFormatFlags flags,
        float opacity)
    {
        var font = _messageFont!;
        var alphaScale = Math.Clamp(opacity, 0f, 1f);

        foreach (var (dx, dy, alpha) in SoftShadowLayers)
        {
            TextRenderer.DrawText(
                g,
                text,
                font,
                new Point(location.X + dx, location.Y + dy),
                Color.FromArgb((int)Math.Round(alpha * alphaScale), 0, 0, 0),
                flags);
        }

        foreach (var (dx, dy) in OutlineOffsets)
        {
            TextRenderer.DrawText(
                g,
                text,
                font,
                new Point(location.X + dx, location.Y + dy),
                Color.FromArgb((int)Math.Round(255 * alphaScale), Color.Black),
                flags);
        }

        var fore = UiColors.PrimaryFore;
        TextRenderer.DrawText(
            g,
            text,
            font,
            location,
            Color.FromArgb((int)Math.Round(255 * alphaScale), fore),
            flags);
    }

    private static void DrawImageWithOpacity(
        Graphics g,
        Image image,
        Rectangle dest,
        float opacity)
    {
        var matrix = new ColorMatrix
        {
            Matrix33 = Math.Clamp(opacity, 0f, 1f),
        };
        using var attributes = new ImageAttributes();
        attributes.SetColorMatrix(matrix, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);
        g.DrawImage(
            image,
            dest,
            0,
            0,
            image.Width,
            image.Height,
            GraphicsUnit.Pixel,
            attributes);
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

    private static Bitmap? CaptureFrostedSnapshot(Control host, Rectangle coverBounds)
    {
        var size = coverBounds.Size;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        try
        {
            var form = host as Form;
            using var capture = form is { Opacity: >= 1d }
                ? CaptureViaScreen(form, coverBounds) ?? CaptureViaClientControls(host, coverBounds)
                : CaptureViaClientControls(host, coverBounds)
                    ?? (form is null ? null : CaptureViaScreen(form, coverBounds));
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

    private static Bitmap? CaptureViaClientControls(Control host, Rectangle coverBounds)
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
            if (child is ExportGlassOverlay
                || !child.Visible
                || child.Width <= 0
                || child.Height <= 0)
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
}
