namespace MgaWwiseImporter.UI;

/// <summary>
/// 読み込んだ Wave のピーク波形と再生位置（シークバー）を描画する。
/// </summary>
internal sealed class WaveformView : Control
{
    private const int LabelWaveGapPx = 3;
    private const int LabelRowCount = 5;

    // MGA-CineAudio-Reviewer (transport-timeline.js) と同じ残光パラメータ
    private const int TrailFadeMs = 10400;
    private const float TrailPeakAlpha = 0.15f;
    private const float TrailPlayheadGapPx = 2f;
    private const double TrailMinSecDelta = 0.02;
    private const int TrailSampleMinIntervalMs = 24;
    private const int TrailMaxSamples = 900;
    private const double TrailDiscontinuitySec = 1.25;

    // プレビュー初回表示の段階演出（オーバーラップ・フェード／ワイプ）
    private const int RevealTickMs = 16;
    private const int RevealTotalMs = 980;
    private static readonly (int StartMs, int DurationMs)[] RevealLayerWindows =
    [
        (0, 240),    // labels
        (70, 420),   // wave wipe
        (300, 260),  // bars
        (420, 240),  // markers
        (520, 260),  // cycles
        (640, 300),  // captions
    ];

    private WavPeakData? _peaks;
    private string? _sourcePath;
    private IReadOnlyList<WaveformBarMark> _bars = [];
    private IReadOnlyList<WaveformMarkerMark> _markers = [];
    private IReadOnlyList<WaveformCycleMark> _cycles = [];
    private IReadOnlyList<WaveformRegionMark> _regions = [];
    private IReadOnlyList<WaveformOutputPart> _outputParts = [];
    private int? _exportHighlightPartNumber;
    private readonly System.Windows.Forms.Timer _exportGlowTimer;
    private double? _playheadProgress;
    private readonly List<(double Progress, long TickMs)> _trailSamples = [];
    private float[]? _trailColumnAlpha;
    private bool _trailActive;
    private bool _isDraggingSeek;
    private float? _mouseGuideX;
    private Bitmap? _staticLayer;
    private bool _staticLayerDirty = true;
    private bool _revealActive;
    private bool _holdScaffold;
    private TaskCompletionSource? _revealCompleted;
    private long _revealStartTickMs;
    private readonly System.Windows.Forms.Timer _revealTimer;
    private readonly Bitmap?[] _revealLayers = new Bitmap?[5];
    private Size _revealLayerSize;
    private bool _revealLayersDirty = true;
    private Rectangle _revealWaveRect;
    private bool _revealRebuildQueued;

    private const int WmEraseBkgnd = 0x0014;

    public WaveformView()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint
            | ControlStyles.Opaque,
            true);
        BackColor = UiColors.WaveformBack;
        Font = new Font("MS Gothic", 8.5F);
        Height = 210;
        TabStop = false;
        Cursor = Cursors.Default;
        _revealTimer = new System.Windows.Forms.Timer { Interval = RevealTickMs };
        _revealTimer.Tick += OnRevealTick;
        _exportGlowTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _exportGlowTimer.Tick += (_, _) => Invalidate();
    }

    public void SetPreview(
        WavPeakData peaks,
        string sourcePath,
        IReadOnlyList<WaveformBarMark>? bars = null,
        IReadOnlyList<WaveformMarkerMark>? markers = null,
        IReadOnlyList<WaveformCycleMark>? cycles = null,
        IReadOnlyList<WaveformRegionMark>? regions = null,
        IReadOnlyList<WaveformOutputPart>? outputParts = null)
    {
        StopRevealAnimation();
        _peaks = peaks;
        _sourcePath = sourcePath;
        _bars = bars ?? [];
        _markers = markers ?? [];
        _cycles = cycles ?? [];
        _regions = regions ?? [];
        _outputParts = outputParts ?? [];
        ClearExportHighlight();
        ClearPlayhead();
        _mouseGuideX = null;
        Cursor = Cursors.Hand;

        // 重いレイヤ生成の前にダークな足場だけ先に出す（白フラッシュ防止）
        DisposeStaticLayer();
        InvalidateRevealLayers();
        _holdScaffold = true;
        Invalidate();
        Update();

        var bounds = ClientRectangle;
        if (bounds.Width > 2 && bounds.Height > 2)
        {
            EnsureRevealLayers(bounds);
        }

        _holdScaffold = false;
        StartRevealAnimation();
    }

    /// <summary>
    /// ファイル解析など重い処理の直前に呼び、現状の暗い描画を画面へ確定する。
    /// </summary>
    public void CommitDarkFrame()
    {
        Invalidate();
        Update();
    }

    /// <summary>
    /// UiColors 変更後に背景・静的レイヤを作り直す。
    /// </summary>
    public void RefreshAppearance()
    {
        BackColor = UiColors.WaveformBack;
        DisposeStaticLayer();
        InvalidateRevealLayers();

        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }

        var bounds = ClientRectangle;
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            Invalidate();
            return;
        }

        if (_revealActive)
        {
            EnsureRevealLayers(bounds);
        }
        else if (_peaks is not null && !_peaks.IsEmpty)
        {
            BuildStaticLayer(bounds);
        }

        Invalidate();
    }

    public void ClearPreview()
    {
        StopRevealAnimation();
        _holdScaffold = false;
        _peaks = null;
        _sourcePath = null;
        _bars = [];
        _markers = [];
        _cycles = [];
        _regions = [];
        _outputParts = [];
        ClearExportHighlight();
        _isDraggingSeek = false;
        _mouseGuideX = null;
        ClearPlayhead();
        Cursor = Cursors.Default;
        DisposeStaticLayer();
        InvalidateRevealLayers();
        Invalidate();
        Update();
    }

    /// <summary>
    /// 再生位置を更新する。progress は 0〜1。null で非表示。
    /// recordTrail が false のとき残光を消す（停止時など）。
    /// </summary>
    public void SetPlayhead(double? progress, bool recordTrail = false)
    {
        if (progress is null)
        {
            ClearPlayhead();
            Invalidate();
            return;
        }

        var clamped = Math.Clamp(progress.Value, 0d, 1d);
        _playheadProgress = clamped;
        _trailActive = recordTrail;
        if (!recordTrail)
        {
            ClearTrailSamples();
        }
        else
        {
            RecordTrailSample(clamped);
        }

        Invalidate();
    }

    /// <summary>
    /// 書き出し中の出力パート枠を発光表示する。null で解除。
    /// </summary>
    public void SetExportHighlight(int? partNumber)
    {
        if (_exportHighlightPartNumber == partNumber)
        {
            if (partNumber is not null && !_exportGlowTimer.Enabled)
            {
                _exportGlowTimer.Start();
            }

            return;
        }

        _exportHighlightPartNumber = partNumber;
        if (partNumber is null)
        {
            _exportGlowTimer.Stop();
        }
        else if (!_exportGlowTimer.Enabled)
        {
            _exportGlowTimer.Start();
        }

        Invalidate();
    }

    public void ClearExportHighlight() => SetExportHighlight(null);

    /// <summary>クリック／ドラッグでシーク（0〜1）。</summary>
    public event EventHandler<double>? SeekRequested;

    private void ClearPlayhead()
    {
        _playheadProgress = null;
        _trailActive = false;
        ClearTrailSamples();
    }

    private void ClearTrailSamples()
    {
        _trailSamples.Clear();
    }

    private void DisposeStaticLayer()
    {
        _staticLayerDirty = true;
        _staticLayer?.Dispose();
        _staticLayer = null;
    }

    private void InvalidateRevealLayers()
    {
        _revealLayersDirty = true;
        for (var i = 0; i < _revealLayers.Length; i++)
        {
            _revealLayers[i]?.Dispose();
            _revealLayers[i] = null;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        DisposeStaticLayer();
        InvalidateRevealLayers();
        if (!IsHandleCreated)
        {
            return;
        }

        if (_revealActive && !_revealRebuildQueued)
        {
            _revealRebuildQueued = true;
            BeginInvoke(RebuildRevealLayersAfterResize);
            return;
        }

        if (!_revealActive && _peaks is not null && !_peaks.IsEmpty && !_revealRebuildQueued)
        {
            _revealRebuildQueued = true;
            BeginInvoke(RebuildStaticLayerAfterResize);
        }
    }

    private void RebuildRevealLayersAfterResize()
    {
        _revealRebuildQueued = false;
        if (!_revealActive || IsDisposed)
        {
            return;
        }

        var bounds = ClientRectangle;
        if (bounds.Width > 2 && bounds.Height > 2)
        {
            EnsureRevealLayers(bounds);
        }

        Invalidate();
    }

    private void RebuildStaticLayerAfterResize()
    {
        _revealRebuildQueued = false;
        if (_revealActive || IsDisposed || _peaks is null || _peaks.IsEmpty)
        {
            return;
        }

        var bounds = ClientRectangle;
        if (bounds.Width > 2 && bounds.Height > 2)
        {
            BuildStaticLayer(bounds);
        }

        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        pevent.Graphics.Clear(UiColors.WaveformBack);
    }

    protected override void WndProc(ref Message m)
    {
        // 既定のウィンドウブラシは白。消去をダークで上書きしてフラッシュを防ぐ。
        if (m.Msg == WmEraseBkgnd)
        {
            if (m.WParam != IntPtr.Zero)
            {
                using var g = Graphics.FromHdc(m.WParam);
                g.Clear(UiColors.WaveformBack);
            }

            m.Result = 1;
            return;
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopRevealAnimation();
            _exportGlowTimer.Stop();
            _exportGlowTimer.Dispose();
            _revealTimer.Dispose();
            DisposeStaticLayer();
            InvalidateRevealLayers();
        }

        base.Dispose(disposing);
    }

    private bool IsRevealing => _revealActive;

    private void StartRevealAnimation()
    {
        DisposeStaticLayer();
        _revealActive = true;
        _revealStartTickMs = Environment.TickCount64;
        _revealCompleted?.TrySetCanceled();
        _revealCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _revealTimer.Start();
        Invalidate();
    }

    private void StopRevealAnimation()
    {
        _revealTimer.Stop();
        _revealActive = false;
        _revealCompleted?.TrySetCanceled();
        _revealCompleted = null;
    }

    private void FinishRevealAnimation()
    {
        _revealTimer.Stop();
        _revealActive = false;

        // OnPaint 外で静的レイヤを焼き、次フレームを即座に出せるようにする
        var bounds = ClientRectangle;
        if (bounds.Width > 2 && bounds.Height > 2)
        {
            BuildStaticLayer(bounds);
        }

        InvalidateRevealLayers();
        Invalidate();
        _revealCompleted?.TrySetResult();
    }

    /// <summary>
    /// 読み込み演出が終わるまで待つ。演出中でなければ即座に完了する。
    /// </summary>
    public async Task WaitForRevealAsync()
    {
        var tcs = _revealCompleted;
        if (!_revealActive || tcs is null)
        {
            return;
        }

        try
        {
            await tcs.Task.ConfigureAwait(true);
        }
        catch (TaskCanceledException)
        {
            // キャンセル（再読み込みなど）は待ち解除のみ
        }
    }

    private void OnRevealTick(object? sender, EventArgs e)
    {
        if (!_revealActive)
        {
            return;
        }

        if (Environment.TickCount64 - _revealStartTickMs >= RevealTotalMs)
        {
            FinishRevealAnimation();
            return;
        }

        Invalidate();
    }

    private static float EaseOutCubic(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var u = 1f - t;
        return 1f - u * u * u;
    }

    private static float EaseOutQuint(float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        var u = 1f - t;
        return 1f - u * u * u * u * u;
    }

    private static float LayerLocalT(int layerIndex, long elapsedMs)
    {
        var (startMs, durationMs) = RevealLayerWindows[layerIndex];
        if (elapsedMs < startMs)
        {
            return 0f;
        }

        return Math.Clamp((elapsedMs - startMs) / (float)durationMs, 0f, 1f);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        UpdateMouseGuide(e.X);
        if (e.Button != MouseButtons.Left || !TryGetProgressFromX(e.X, out var progress))
        {
            return;
        }

        _isDraggingSeek = true;
        Capture = true;
        SeekRequested?.Invoke(this, progress);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        UpdateMouseGuide(e.X);

        if (!_isDraggingSeek || !TryGetProgressFromX(e.X, out var progress))
        {
            return;
        }

        SeekRequested?.Invoke(this, progress);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        UpdateMouseGuide(e.X);
        if (e.Button != MouseButtons.Left || !_isDraggingSeek)
        {
            return;
        }

        _isDraggingSeek = false;
        Capture = false;
        if (TryGetProgressFromX(e.X, out var progress))
        {
            SeekRequested?.Invoke(this, progress);
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_isDraggingSeek)
        {
            return;
        }

        if (_mouseGuideX is not null)
        {
            _mouseGuideX = null;
            Invalidate();
        }
    }

    private void UpdateMouseGuide(int mouseX)
    {
        if (_peaks is null || _peaks.IsEmpty)
        {
            return;
        }

        var content = Rectangle.Inflate(ClientRectangle, -4, -4);
        if (content.Width <= 0)
        {
            return;
        }

        var x = Math.Clamp(mouseX, content.Left, content.Right);
        if (_mouseGuideX is float prev && Math.Abs(prev - x) < 0.25f)
        {
            return;
        }

        _mouseGuideX = x;
        Invalidate();
    }

    private bool TryGetProgressFromX(int mouseX, out double progress)
    {
        progress = 0;
        if (_peaks is null || _peaks.IsEmpty)
        {
            return false;
        }

        var content = Rectangle.Inflate(ClientRectangle, -4, -4);
        if (content.Width <= 0)
        {
            return false;
        }

        progress = Math.Clamp((mouseX - content.Left) / (double)content.Width, 0d, 1d);
        return true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.Clear(UiColors.WaveformBack);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var bounds = ClientRectangle;
        if (bounds.Width <= 2 || bounds.Height <= 2)
        {
            return;
        }

        if (_peaks is null || _peaks.IsEmpty || _holdScaffold)
        {
            DrawEmptyScaffold(g, bounds);
            return;
        }

        if (IsRevealing)
        {
            // レイヤ未準備なら足場だけ（OnPaint 内で重い生成をしない）
            if (_revealLayersDirty || _revealLayers[0] is null)
            {
                DrawEmptyScaffold(g, bounds);
            }
            else
            {
                DrawRevealFrame(g, bounds);
            }
        }
        else
        {
            if (_staticLayerDirty || _staticLayer is null)
            {
                DrawEmptyScaffold(g, bounds);
            }
            else
            {
                g.DrawImageUnscaled(_staticLayer, 0, 0);
            }
        }

        var content = Rectangle.Inflate(bounds, -4, -4);
        DrawExportPartGlow(g, content);
        DrawPlayhead(g, content);
        DrawMouseGuide(g, content);
    }

    private void DrawEmptyScaffold(Graphics g, Rectangle bounds)
    {
        var content = Rectangle.Inflate(bounds, -4, -4);
        var (labels, wave, rowHeight) = GetLayout(content, g);
        DrawLabelRows(g, labels, rowHeight, LabelRowCount);

        if (_peaks is not null && !_peaks.IsEmpty)
        {
            return;
        }

        using var brush = new SolidBrush(UiColors.EmptyHint);
        const string message = "Wave / XML をドロップすると波形と小節線を表示します";
        var size = g.MeasureString(message, Font);
        var centerY = wave.Height > 0
            ? wave.Top + (wave.Height - size.Height) / 2f
            : (bounds.Height - size.Height) / 2f;
        g.DrawString(
            message,
            Font,
            brush,
            (bounds.Width - size.Width) / 2f,
            centerY);
    }

    /// <summary>
    /// レイヤを時間軸上でオーバーラップさせ、フェード／ワイプ／スライドで出現。
    /// </summary>
    private void DrawRevealFrame(Graphics g, Rectangle bounds)
    {
        var elapsed = Environment.TickCount64 - _revealStartTickMs;

        // 0: ラベル行（カスケード）
        var labelsT = LayerLocalT(0, elapsed);
        if (labelsT > 0f)
        {
            var rows = Math.Clamp((int)Math.Floor(EaseOutCubic(labelsT) * LabelRowCount) + 1, 1, LabelRowCount);
            var content = Rectangle.Inflate(bounds, -4, -4);
            var (labels, _, rowHeight) = GetLayout(content, g);
            DrawLabelRows(g, labels, rowHeight, rows);
        }

        // 1: 波形（左→右ソフトワイプ）
        var waveT = LayerLocalT(1, elapsed);
        if (waveT > 0f)
        {
            DrawWaveLayerWiped(g, _revealLayers[0], EaseOutQuint(waveT), bounds);
        }

        // 2: 小節（フェード）
        DrawLayerFaded(g, _revealLayers[1], EaseOutCubic(LayerLocalT(2, elapsed)));

        // 3: マーカー（フェード＋わずかに上から落下）
        var markersT = LayerLocalT(3, elapsed);
        if (markersT > 0f)
        {
            var eased = EaseOutCubic(markersT);
            DrawLayerFaded(g, _revealLayers[2], eased, offsetY: (1f - eased) * -10f);
        }

        // 4: サイクル（フェード）
        DrawLayerFaded(g, _revealLayers[3], EaseOutCubic(LayerLocalT(4, elapsed)));

        // 5: キャプション（フェード＋下からスライド）
        var captionsT = LayerLocalT(5, elapsed);
        if (captionsT > 0f)
        {
            var eased = EaseOutCubic(captionsT);
            DrawLayerFaded(g, _revealLayers[4], eased, offsetY: (1f - eased) * 12f);
        }
    }

    private void EnsureRevealLayers(Rectangle bounds)
    {
        var size = bounds.Size;
        if (!_revealLayersDirty
            && _revealLayerSize == size
            && _revealLayers[0] is not null)
        {
            return;
        }

        InvalidateRevealLayers();
        _revealLayerSize = size;
        _revealLayersDirty = false;

        var content = Rectangle.Inflate(bounds, -4, -4);
        using var probeBmp = new Bitmap(1, 1);
        using var probe = Graphics.FromImage(probeBmp);
        var (labels, wave, rowHeight) = GetLayout(content, probe);
        _revealWaveRect = wave;

        _revealLayers[0] = CreateTransparentLayer(size, g => DrawWaveform(g, wave));
        _revealLayers[1] = CreateTransparentLayer(size, g => DrawBars(g, labels, wave, rowHeight));
        _revealLayers[2] = CreateTransparentLayer(size, g => DrawMarkers(g, labels, rowHeight));
        _revealLayers[3] = CreateTransparentLayer(size, g => DrawCycles(g, labels, rowHeight));
        _revealLayers[4] = CreateTransparentLayer(size, g =>
        {
            DrawOutputPartLabels(g, wave);
            DrawTitle(g, wave);
        });
    }

    private Bitmap CreateTransparentLayer(Size size, Action<Graphics> paint)
    {
        var bmp = new Bitmap(size.Width, size.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceOver;
        paint(g);
        return bmp;
    }

    private static void DrawLayerFaded(Graphics g, Bitmap? layer, float alpha, float offsetY = 0f)
    {
        if (layer is null || alpha <= 0.01f)
        {
            return;
        }

        var clamped = Math.Clamp(alpha, 0f, 1f);
        if (clamped >= 0.995f && Math.Abs(offsetY) < 0.5f)
        {
            g.DrawImageUnscaled(layer, 0, 0);
            return;
        }

        var matrix = new System.Drawing.Imaging.ColorMatrix
        {
            Matrix33 = clamped,
        };
        using var attrs = new System.Drawing.Imaging.ImageAttributes();
        attrs.SetColorMatrix(matrix);
        var dest = new Rectangle(0, (int)MathF.Round(offsetY), layer.Width, layer.Height);
        g.DrawImage(
            layer,
            dest,
            0,
            0,
            layer.Width,
            layer.Height,
            GraphicsUnit.Pixel,
            attrs);
    }

    private void DrawWaveLayerWiped(Graphics g, Bitmap? layer, float wipeT, Rectangle bounds)
    {
        if (layer is null || wipeT <= 0.01f)
        {
            return;
        }

        var wave = _revealWaveRect;
        if (wave.Width <= 0 || wave.Height <= 0)
        {
            DrawLayerFaded(g, layer, wipeT);
            return;
        }

        var wipeWidth = Math.Max(1, (int)MathF.Round(wave.Width * wipeT));
        var clipRight = wave.Left + wipeWidth;
        var state = g.Save();
        g.SetClip(new Rectangle(0, 0, clipRight, bounds.Height));
        // ワイプ前半はわずかにフェードイン
        var alpha = Math.Clamp(wipeT * 2.2f, 0f, 1f);
        DrawLayerFaded(g, layer, alpha);
        g.Restore(state);

        if (wipeT < 0.995f)
        {
            DrawWipeEdge(g, clipRight, wave);
        }
    }

    private static void DrawWipeEdge(Graphics g, int edgeX, Rectangle wave)
    {
        const int soft = 18;
        var left = Math.Max(wave.Left, edgeX - soft);
        if (left >= edgeX || wave.Height <= 0)
        {
            return;
        }

        var rect = new Rectangle(left, wave.Top, edgeX - left, wave.Height);
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new Rectangle(rect.Left - 1, rect.Top, rect.Width + 2, rect.Height),
            Color.FromArgb(0, 200, 240, 255),
            Color.FromArgb(90, 180, 240, 255),
            0f);
        g.FillRectangle(brush, rect);

        using var pen = new Pen(Color.FromArgb(160, 140, 230, 255), 1.5f);
        g.DrawLine(pen, edgeX, wave.Top, edgeX, wave.Bottom);
    }

    /// <summary>
    /// 上部: 小節／テンポ／拍子／マーカー／サイクルの5行、下部: 波形エリア（リージョン着色）。
    /// </summary>
    private (Rectangle Labels, Rectangle Wave, float RowHeight) GetLayout(Rectangle content, Graphics g)
    {
        var rowHeight = Font.GetHeight(g) + 2f;
        var labelsHeight = (int)Math.Ceiling(rowHeight * LabelRowCount);
        var labels = new Rectangle(content.Left, content.Top, content.Width, labelsHeight);
        var waveTop = content.Top + labelsHeight + LabelWaveGapPx;
        var waveHeight = Math.Max(0, content.Bottom - waveTop);
        var wave = new Rectangle(content.Left, waveTop, content.Width, waveHeight);
        return (labels, wave, rowHeight);
    }

    private void BuildStaticLayer(Rectangle bounds)
    {
        DisposeStaticLayer();
        _staticLayer = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var g = Graphics.FromImage(_staticLayer);
        g.Clear(UiColors.WaveformBack);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var content = Rectangle.Inflate(bounds, -4, -4);
        var (labels, wave, rowHeight) = GetLayout(content, g);
        DrawLabelRows(g, labels, rowHeight, LabelRowCount);
        DrawWaveform(g, wave);
        DrawBars(g, labels, wave, rowHeight);
        DrawMarkers(g, labels, rowHeight);
        DrawCycles(g, labels, rowHeight);
        DrawOutputPartLabels(g, wave);
        DrawTitle(g, wave);
        _staticLayerDirty = false;
    }

    private void DrawTitle(Graphics g, Rectangle wave)
    {
        if (string.IsNullOrEmpty(_sourcePath) || wave.Height <= 0)
        {
            return;
        }

        var text = Path.GetFileName(_sourcePath);
        using var brush = new SolidBrush(Color.White);
        var y = wave.Bottom - Font.GetHeight(g);
        g.DrawString(text, Font, brush, wave.Left, y);
    }

    private void DrawLabelRows(Graphics g, Rectangle labels, float rowHeight, int visibleRowCount)
    {
        if (labels.Width <= 0 || labels.Height <= 0 || visibleRowCount <= 0)
        {
            return;
        }

        ReadOnlySpan<Color> rowColors =
        [
            UiColors.BarNumberBg,
            UiColors.TempoBg,
            UiColors.SignatureBg,
            UiColors.MarkerRowBg,
            UiColors.CycleRowBg,
        ];

        var count = Math.Min(visibleRowCount, rowColors.Length);
        for (var i = 0; i < count; i++)
        {
            using var brush = new SolidBrush(rowColors[i]);
            g.FillRectangle(brush, labels.Left, labels.Top + i * rowHeight, labels.Width, rowHeight);
        }
    }

    private void DrawWaveform(Graphics g, Rectangle wave)
    {
        if (wave.Width <= 0 || wave.Height <= 0)
        {
            return;
        }

        DrawRegionBackgrounds(g, wave);

        var peaks = _peaks!;
        var midY = wave.Top + wave.Height / 2f;
        using (var centerPen = new Pen(UiColors.WaveCenter))
        {
            g.DrawLine(centerPen, wave.Left, midY, wave.Right, midY);
        }

        if (peaks.Mins.Length == 0)
        {
            return;
        }

        // ±1.0 で波形エリアの上下端いっぱいに届く
        var amplitude = wave.Height * 0.5f;
        using var wavePen = new Pen(UiColors.WaveFill, 1f);
        var bucketWidth = wave.Width / (float)peaks.Mins.Length;

        for (var i = 0; i < peaks.Mins.Length; i++)
        {
            var x = wave.Left + i * bucketWidth + bucketWidth * 0.5f;
            var y1 = midY - peaks.Maxs[i] * amplitude;
            var y2 = midY - peaks.Mins[i] * amplitude;
            if (Math.Abs(y2 - y1) < 1f)
            {
                y2 = y1 + 1f;
            }

            g.DrawLine(wavePen, x, y1, x, y2);
        }
    }

    private void DrawRegionBackgrounds(Graphics g, Rectangle wave)
    {
        if (_peaks is null || _peaks.FrameCount <= 0 || _regions.Count == 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        using var red = new SolidBrush(UiColors.RegionWaveFillRed);
        using var blue = new SolidBrush(UiColors.RegionWaveFillBlue);
        using var excluded = new SolidBrush(UiColors.RegionWaveFillExcluded);

        var coloredIndex = 0;
        foreach (var region in _regions)
        {
            var t0 = Math.Clamp(region.StartSampleOffset / (double)frameCount, 0d, 1d);
            var t1 = Math.Clamp(region.EndSampleOffset / (double)frameCount, 0d, 1d);
            var x0 = wave.Left + (float)(t0 * wave.Width);
            var x1 = wave.Left + (float)(t1 * wave.Width);
            var width = Math.Max(1f, x1 - x0);
            Brush fill;
            if (region.IsExcluded)
            {
                fill = excluded;
            }
            else
            {
                coloredIndex++;
                fill = coloredIndex % 2 == 1 ? red : blue;
            }

            g.FillRectangle(fill, x0, wave.Top, width, wave.Height);
        }
    }

    private void DrawOutputPartLabels(Graphics g, Rectangle wave)
    {
        if (_peaks is null || _peaks.FrameCount <= 0 || _outputParts.Count == 0 || wave.Height <= 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        var fontSize = Math.Clamp(wave.Height * 0.22f, 13f, 28f);
        using var labelFont = new Font(Font.FontFamily, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(UiColors.OutputPartFg);
        using var shadow = new SolidBrush(UiColors.OutputPartShadow);
        var labelHeight = labelFont.GetHeight(g);
        // 下端より少し上げ、ソースファイル名とも重なりにくくする
        var y = wave.Bottom - labelHeight - Math.Max(10f, wave.Height * 0.12f);

        foreach (var part in _outputParts)
        {
            var t0 = Math.Clamp(part.StartSampleOffset / (double)frameCount, 0d, 1d);
            var t1 = Math.Clamp(part.EndSampleOffset / (double)frameCount, 0d, 1d);
            var x0 = wave.Left + (float)(t0 * wave.Width);
            var x1 = wave.Left + (float)(t1 * wave.Width);
            var width = x1 - x0;
            if (width < 1f)
            {
                continue;
            }

            var size = g.MeasureString(part.FileName, labelFont);
            // 理想はパート中央。幅不足／右端はみ出し時は左へ寄せつつ必ず描画する
            var preferredX = x0 + (width - size.Width) * 0.5f;
            var x = ClampLabelX(preferredX, size.Width, wave.Left, wave.Right);
            DrawLabeledShadow(g, part.FileName, labelFont, brush, shadow, x, y);
        }
    }

    /// <summary>
    /// ラベルの理想 X を、描画範囲内に収まるよう補正する（右はみ出しは左寄せ）。
    /// </summary>
    private static float ClampLabelX(float preferredX, float textWidth, float leftBound, float rightBound)
    {
        var maxWidth = rightBound - leftBound;
        if (maxWidth <= 0f)
        {
            return leftBound;
        }

        if (textWidth >= maxWidth)
        {
            return leftBound;
        }

        var x = preferredX;
        if (x + textWidth > rightBound)
        {
            x = rightBound - textWidth;
        }

        if (x < leftBound)
        {
            x = leftBound;
        }

        return x;
    }

    /// <summary>
    /// 書き出し中パートの枠をパルス発光させる（進行中の見た目用）。
    /// </summary>
    private void DrawExportPartGlow(Graphics g, Rectangle content)
    {
        if (_exportHighlightPartNumber is not int partNumber
            || _peaks is null
            || _peaks.FrameCount <= 0
            || content.Width <= 0)
        {
            return;
        }

        WaveformOutputPart? target = null;
        foreach (var part in _outputParts)
        {
            if (part.Number == partNumber)
            {
                target = part;
                break;
            }
        }

        if (target is not WaveformOutputPart highlight)
        {
            return;
        }

        var (_, wave, _) = GetLayout(content, g);
        if (wave.Width <= 0 || wave.Height <= 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        var t0 = Math.Clamp(highlight.StartSampleOffset / (double)frameCount, 0d, 1d);
        var t1 = Math.Clamp(highlight.EndSampleOffset / (double)frameCount, 0d, 1d);
        var x0 = wave.Left + (float)(t0 * wave.Width);
        var x1 = wave.Left + (float)(t1 * wave.Width);
        var width = Math.Max(2f, x1 - x0);
        var rect = new RectangleF(x0, wave.Top, width, wave.Height);

        // 約 1.1 秒周期で明滅（巨大ファイル書き出し中も動き続ける）
        var phase = (Environment.TickCount64 % 1100) / 1100f;
        var pulse = 0.40f + 0.60f * (0.5f + 0.5f * MathF.Sin(phase * MathF.PI * 2f));
        var baseColor = UiColors.ExportPartGlow;

        // 外側ほど薄いハロー（発光）
        for (var i = 5; i >= 1; i--)
        {
            var alpha = (int)(28 * pulse * i);
            using var glowPen = new Pen(Color.FromArgb(Math.Clamp(alpha, 0, 255), baseColor), i * 2.2f);
            g.DrawRectangle(glowPen, rect.X, rect.Y, rect.Width, rect.Height);
        }

        using var corePen = new Pen(
            Color.FromArgb((int)(230 * pulse), baseColor),
            2.2f);
        g.DrawRectangle(corePen, rect.X, rect.Y, rect.Width, rect.Height);

        // 内側の薄い塗りで「今この固まり」を強調
        using var fill = new SolidBrush(Color.FromArgb((int)(36 * pulse), baseColor));
        g.FillRectangle(fill, rect);
    }

    private static void DrawLabeledShadow(
        Graphics g,
        string text,
        Font font,
        Brush textBrush,
        Brush shadowBrush,
        float x,
        float y)
    {
        ReadOnlySpan<(float Dx, float Dy)> offsets =
        [
            (-2f, 0f), (2f, 0f), (0f, -2f), (0f, 2f),
            (-2f, -2f), (2f, -2f), (-2f, 2f), (2f, 2f),
            (3f, 2f), (2f, 3f), (3f, 3f),
        ];

        foreach (var (dx, dy) in offsets)
        {
            g.DrawString(text, font, shadowBrush, x + dx, y + dy);
        }

        g.DrawString(text, font, textBrush, x, y);
    }

    private void DrawBars(Graphics g, Rectangle labels, Rectangle wave, float rowHeight)
    {
        if (_peaks is null || _peaks.FrameCount <= 0 || _bars.Count == 0 || labels.Width <= 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        var barRowTop = labels.Top;
        var tempoRowTop = barRowTop + rowHeight;
        var signatureRowTop = tempoRowTop + rowHeight;
        // 小節線はラベル帯〜波形まで通す（隙間は地の色のまま）
        var lineTop = labels.Top;
        var lineBottom = wave.Height > 0 ? wave.Bottom : labels.Bottom;

        using var barPen = new Pen(UiColors.BarLine, 1f);
        using var anacrusisPen = new Pen(UiColors.AnacrusisLine, 1f)
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
            DashPattern = [4f, 3f],
        };
        using var tempoChangePen = new Pen(UiColors.TempoChangeLine, 1f)
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
            DashPattern = [3f, 3f],
        };
        using var barBrush = new SolidBrush(UiColors.BarNumberFg);
        using var tempoBrush = new SolidBrush(UiColors.TempoFg);
        using var signatureBrush = new SolidBrush(UiColors.SignatureFg);

        var barLabelY = barRowTop + 1f;
        var tempoLabelY = tempoRowTop + 1f;
        var signatureLabelY = signatureRowTop + 1f;
        var lastBarLabelX = float.NegativeInfinity;
        var lastTempoLabelX = float.NegativeInfinity;
        var lastSignatureLabelX = float.NegativeInfinity;
        // 表示比較用（丸め後 BPM / 拍子）。内部の mark.Bpm・Numerator/Denominator は各位置に保持済み。
        int? lastShownTempo = null;
        int? lastShownNumerator = null;
        int? lastShownDenominator = null;
        const float minLabelGap = 22f;

        foreach (var bar in _bars)
        {
            var t = Math.Clamp(bar.SampleOffset / (double)frameCount, 0d, 1d);
            var x = labels.Left + (float)(t * labels.Width);
            var tempoRounded = (int)Math.Round(bar.Bpm, MidpointRounding.AwayFromZero);
            var tempoLabel = tempoRounded.ToString(System.Globalization.CultureInfo.InvariantCulture);

            if (bar.IsTempoChangeOnly)
            {
                g.DrawLine(tempoChangePen, x, tempoRowTop, x, tempoRowTop + rowHeight);
                TryDrawTempoLabel(g, tempoLabel, tempoRounded, x, tempoLabelY, tempoBrush,
                    ref lastTempoLabelX, ref lastShownTempo, minLabelGap);
                continue;
            }

            var pen = bar.IsAnacrusis ? anacrusisPen : barPen;
            g.DrawLine(pen, x, lineTop, x, lineBottom);

            if (x - lastBarLabelX >= minLabelGap)
            {
                g.DrawString(bar.BarNumber.ToString(), Font, barBrush, x + 3f, barLabelY);
                lastBarLabelX = x;
            }

            TryDrawTempoLabel(g, tempoLabel, tempoRounded, x, tempoLabelY, tempoBrush,
                ref lastTempoLabelX, ref lastShownTempo, minLabelGap);

            if (lastShownNumerator != bar.Numerator || lastShownDenominator != bar.Denominator)
            {
                if (x - lastSignatureLabelX >= minLabelGap)
                {
                    var signatureLabel = $"{bar.Numerator}/{bar.Denominator}";
                    g.DrawString(signatureLabel, Font, signatureBrush, x + 3f, signatureLabelY);
                    lastSignatureLabelX = x;
                    lastShownNumerator = bar.Numerator;
                    lastShownDenominator = bar.Denominator;
                }
            }
        }
    }

    private void TryDrawTempoLabel(
        Graphics g,
        string tempoLabel,
        int tempoRounded,
        float x,
        float y,
        Brush brush,
        ref float lastTempoLabelX,
        ref int? lastShownTempo,
        float minLabelGap)
    {
        if (lastShownTempo == tempoRounded)
        {
            return;
        }

        if (x - lastTempoLabelX < minLabelGap)
        {
            return;
        }

        g.DrawString(tempoLabel, Font, brush, x + 3f, y);
        lastTempoLabelX = x;
        lastShownTempo = tempoRounded;
    }

    private void DrawMarkers(Graphics g, Rectangle labels, float rowHeight)
    {
        if (_peaks is null || _peaks.FrameCount <= 0 || _markers.Count == 0 || labels.Width <= 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        var markerRowTop = labels.Top + rowHeight * 3f;
        var markerRowBottom = markerRowTop + rowHeight;
        // ▼ の先端をマーカー時刻の X に厳密に合わせる（下向き）
        var tipY = markerRowBottom - 1f;
        var triHalfW = Math.Min(5f, rowHeight * 0.35f);
        var triH = Math.Min(rowHeight - 3f, 9f);

        using var triangleBrush = new SolidBrush(UiColors.MarkerTriangle);
        using var textBrush = new SolidBrush(UiColors.MarkerFg);

        // 三角は時刻順に描画
        foreach (var marker in _markers)
        {
            var t = Math.Clamp(marker.SampleOffset / (double)frameCount, 0d, 1d);
            var x = labels.Left + (float)(t * labels.Width);

            PointF[] triangle =
            [
                new(x, tipY),
                new(x - triHalfW, tipY - triH),
                new(x + triHalfW, tipY - triH),
            ];
            g.FillPolygon(triangleBrush, triangle);
        }

        // コメントは右から着地優先（後のマーカーを隠しにくい）。重なり時は左へ寄せて必ず描く。
        var nextOccupiedLeft = (float)labels.Right;
        for (var i = _markers.Count - 1; i >= 0; i--)
        {
            var marker = _markers[i];
            if (string.IsNullOrEmpty(marker.Comment))
            {
                continue;
            }

            var t = Math.Clamp(marker.SampleOffset / (double)frameCount, 0d, 1d);
            var x = labels.Left + (float)(t * labels.Width);
            var size = g.MeasureString(marker.Comment, Font);
            var preferredX = x + triHalfW + 2f;
            var rightLimit = Math.Min(labels.Right, nextOccupiedLeft - 2f);
            if (rightLimit <= labels.Left)
            {
                rightLimit = labels.Right;
            }

            var textX = ClampLabelX(preferredX, size.Width, labels.Left, rightLimit);
            var textY = markerRowTop + Math.Max(0f, (rowHeight - size.Height) * 0.5f);
            g.DrawString(marker.Comment, Font, textBrush, textX, textY);
            nextOccupiedLeft = textX;
        }
    }

    private void DrawCycles(Graphics g, Rectangle labels, float rowHeight)
    {
        if (_peaks is null || _peaks.FrameCount <= 0 || _cycles.Count == 0 || labels.Width <= 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        var cycleRowTop = labels.Top + rowHeight * 4f;
        var labelY = cycleRowTop + 1f;

        using var rangeBrush = new SolidBrush(UiColors.CycleRangeFill);
        using var textBrush = new SolidBrush(UiColors.CycleFg);

        foreach (var cycle in _cycles)
        {
            var t0 = Math.Clamp(cycle.StartSampleOffset / (double)frameCount, 0d, 1d);
            var t1 = Math.Clamp(cycle.EndSampleOffset / (double)frameCount, 0d, 1d);
            var x0 = labels.Left + (float)(t0 * labels.Width);
            var x1 = labels.Left + (float)(t1 * labels.Width);
            var width = Math.Max(1f, x1 - x0);
            g.FillRectangle(rangeBrush, x0, cycleRowTop, width, rowHeight);

            if (string.IsNullOrEmpty(cycle.Comment))
            {
                continue;
            }

            var size = g.MeasureString(cycle.Comment, Font);
            var preferredX = x0 + 3f;
            var textX = ClampLabelX(preferredX, size.Width, labels.Left, labels.Right);
            g.DrawString(cycle.Comment, Font, textBrush, textX, labelY);
        }
    }

    private void DrawPlayhead(Graphics g, Rectangle content)
    {
        if (_playheadProgress is null || content.Width <= 0)
        {
            return;
        }

        var x = content.Left + (float)(_playheadProgress.Value * content.Width);
        DrawSeekPlaybackTrail(g, content, x);

        // ソフトグロー（細め）
        using (var glowOuter = new Pen(Color.FromArgb(40, UiColors.SeekCyan), 3f))
        {
            g.DrawLine(glowOuter, x, content.Top, x, content.Bottom);
        }

        using (var glowInner = new Pen(Color.FromArgb(90, UiColors.SeekCyan), 1.5f))
        {
            g.DrawLine(glowInner, x, content.Top, x, content.Bottom);
        }

        // コア線
        using var corePen = new Pen(UiColors.SeekCyan, 1f);
        g.DrawLine(corePen, x, content.Top, x, content.Bottom);
    }

    /// <summary>
    /// MGA-CineAudio-Reviewer の drawSeekPlaybackTrail 相当。
    /// サンプル列→列アルファを合成し、連続ランを FillRectangle で描く（GDI+ グラデ大量生成を避けてハングしない）。
    /// </summary>
    private void DrawSeekPlaybackTrail(Graphics g, Rectangle content, float playheadX)
    {
        var now = Environment.TickCount64;
        PruneTrailSamplesByAge(now);
        if (_trailSamples.Count < 2 || content.Width <= 0)
        {
            return;
        }

        var trailRightX = playheadX - TrailPlayheadGapPx;
        if (trailRightX <= content.Left)
        {
            return;
        }

        EnsureTrailColumnAlpha(content.Width);
        var cols = _trailColumnAlpha!;
        Array.Clear(cols, 0, content.Width);

        for (var i = 1; i < _trailSamples.Count; i++)
        {
            RasterizeTrailSegment(content, _trailSamples[i - 1], _trailSamples[i], now, trailRightX, cols);
        }

        // 同じアルファの連続列をまとめて塗る（毎ピクセル Pen 生成を避ける）
        using var brush = new SolidBrush(Color.Transparent);
        var runStart = -1;
        var runAlpha = 0;

        void FlushRun(int endExclusive)
        {
            if (runStart < 0 || runAlpha <= 0)
            {
                runStart = -1;
                return;
            }

            brush.Color = Color.FromArgb(runAlpha, UiColors.SeekCyan);
            g.FillRectangle(
                brush,
                content.Left + runStart,
                content.Top,
                endExclusive - runStart,
                content.Height);
            runStart = -1;
        }

        for (var col = 0; col < content.Width; col++)
        {
            var a = ToByteAlpha(cols[col]);
            if (a <= 0)
            {
                FlushRun(col);
                continue;
            }

            if (runStart < 0)
            {
                runStart = col;
                runAlpha = a;
                continue;
            }

            // 近いアルファは同一ランにまとめて帯の縞を抑える
            if (Math.Abs(a - runAlpha) <= 2)
            {
                continue;
            }

            FlushRun(col);
            runStart = col;
            runAlpha = a;
        }

        FlushRun(content.Width);
    }

    private void RasterizeTrailSegment(
        Rectangle content,
        (double Progress, long TickMs) older,
        (double Progress, long TickMs) newer,
        long now,
        float trailRightX,
        float[] cols)
    {
        var x0 = content.Left + (float)(older.Progress * content.Width);
        var x1 = content.Left + (float)(newer.Progress * content.Width);
        var segL = Math.Min(x0, x1);
        var segR = Math.Max(x0, x1);
        if (segL >= trailRightX)
        {
            return;
        }

        segR = Math.Min(segR, trailRightX);
        var segW = segR - segL;
        if (segW <= 0.5f)
        {
            return;
        }

        var i0 = Math.Max(0, (int)Math.Floor(segL - content.Left));
        var i1 = Math.Min(cols.Length - 1, (int)Math.Ceiling(segR - content.Left));
        var atSpan = newer.TickMs - older.TickMs;

        for (var i = i0; i <= i1; i++)
        {
            var px = content.Left + i + 0.5f;
            if (px < segL || px > segR)
            {
                continue;
            }

            var f = (px - segL) / segW;
            var at = older.TickMs + (long)(atSpan * f);
            var alpha = TrailAlphaForAgeMs(now - at);
            if (alpha > cols[i])
            {
                cols[i] = alpha;
            }
        }
    }

    private void RecordTrailSample(double progress)
    {
        if (!_trailActive)
        {
            return;
        }

        var now = Environment.TickCount64;
        var durationSec = _peaks?.DurationSeconds ?? 0;
        if (_trailSamples.Count > 0)
        {
            var last = _trailSamples[^1];
            var secDelta = ProgressToSec(Math.Abs(progress - last.Progress), durationSec);
            if (secDelta >= DiscontinuitySec(durationSec))
            {
                _trailSamples.Clear();
            }
        }

        if (_trailSamples.Count > 0)
        {
            var last = _trailSamples[^1];
            var secDelta = ProgressToSec(Math.Abs(progress - last.Progress), durationSec);
            if (now - last.TickMs < TrailSampleMinIntervalMs && secDelta < TrailMinSecDelta)
            {
                return;
            }

            var width = contentWidthForTrail();
            if (durationSec > 0 && width > 0 && secDelta > TrailMinSecDelta * 1.5)
            {
                var pxDelta = (float)(secDelta / durationSec) * width;
                var insertN = Math.Min(8, Math.Max(1, (int)Math.Ceiling(pxDelta / 8f) - 1));
                for (var j = 1; j <= insertN; j++)
                {
                    var f = j / (double)(insertN + 1);
                    _trailSamples.Add((
                        last.Progress + (progress - last.Progress) * f,
                        last.TickMs + (long)((now - last.TickMs) * f)));
                }
            }
        }

        _trailSamples.Add((progress, now));
        PruneTrailSamplesByAge(now);
        if (_trailSamples.Count > TrailMaxSamples)
        {
            _trailSamples.RemoveRange(0, _trailSamples.Count - TrailMaxSamples);
            PruneTrailSamplesByAge(now);
        }
    }

    private float contentWidthForTrail()
    {
        var content = Rectangle.Inflate(ClientRectangle, -4, -4);
        return Math.Max(0, content.Width);
    }

    private void PruneTrailSamplesByAge(long now)
    {
        var remove = 0;
        while (remove < _trailSamples.Count && now - _trailSamples[remove].TickMs >= TrailFadeMs)
        {
            remove++;
        }

        if (remove > 0)
        {
            _trailSamples.RemoveRange(0, remove);
        }
    }

    private static double ProgressToSec(double progressDelta, double durationSec)
    {
        if (durationSec > 0)
        {
            return progressDelta * durationSec;
        }

        // 尺不明時は progress 差分を秒に見立てないで時間ゲートだけ効かせる
        return progressDelta * 60d;
    }

    private static double DiscontinuitySec(double durationSec)
    {
        if (durationSec <= 0)
        {
            return TrailDiscontinuitySec;
        }

        return Math.Max(TrailDiscontinuitySec, durationSec * 0.025);
    }

    private static float TrailAlphaForAgeMs(long ageMs)
    {
        if (ageMs >= TrailFadeMs)
        {
            return 0f;
        }

        var t = ageMs / (float)TrailFadeMs;
        var fade = 1f - t;
        return TrailPeakAlpha * fade * fade;
    }

    private void EnsureTrailColumnAlpha(int width)
    {
        if (_trailColumnAlpha is null || _trailColumnAlpha.Length != width)
        {
            _trailColumnAlpha = new float[width];
        }
    }

    private static int ToByteAlpha(float a)
    {
        return Math.Clamp((int)MathF.Round(a * 255f), 0, 255);
    }

    private void DrawMouseGuide(Graphics g, Rectangle content)
    {
        if (_mouseGuideX is not float mx)
        {
            return;
        }

        using var pen = new Pen(UiColors.MouseGuide, 1f);
        g.DrawLine(pen, mx, content.Top, mx, content.Bottom);
    }
}
