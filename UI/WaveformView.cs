namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 読み込んだ Wave のピーク波形と再生位置（シークバー）を描画する。
/// </summary>
internal sealed class WaveformView : Control
{
    private const int LabelWaveGapPx = 3;
    private const int LabelRowCount = 4;
    private const int InfoLanePadX = 6;
    private const int InfoLaneSeparatorPx = 2;
    private static readonly string[] InfoRowLabels = ["Measure", "Tempo", "Signature", "Marker"];

    // MGA-CineAudio-Reviewer (transport-timeline.js) と同じ残光パラメータ
    /// <summary>軌跡の目標長さ（画面ピクセル）。ズームによらず見た目を揃える。</summary>
    private const float TrailTargetLengthPx = 360f;
    /// <summary>サンプル保持の上限（壁時計）。描画フェードはズームで短くなり得る。</summary>
    private const int TrailSampleRetainMs = 10400;
    private const float TrailPeakAlpha = 0.15f;
    private const float TrailPlayheadGapPx = 2f;
    private const double TrailMinSecDelta = 0.02;
    private const int TrailSampleMinIntervalMs = 24;
    private const int TrailMaxSamples = 900;
    private const double TrailDiscontinuitySec = 1.25;

    // プレビュー初回表示の段階演出（オーバーラップ・フェード／ワイプ）
    private const int RevealTickMs = 16;
    private const int RevealTotalMs = 900;
    private static readonly (int StartMs, int DurationMs)[] RevealLayerWindows =
    [
        (0, 240),    // labels
        (70, 420),   // wave wipe
        (300, 260),  // bars
        (420, 240),  // markers
        (520, 300),  // captions
    ];

    private WavPeakData? _peaks;
    private WavFileInfo? _wavInfo;
    private string _sourceDisplayName = string.Empty;
    private int _infoLaneWidth = 164;
    private WavPeakData? _detailPeaks;
    private double _detailViewStart = double.NaN;
    private double _detailViewEnd = double.NaN;
    private int _detailPixelWidth = -1;
    private bool _detailIsApproximate;
    private WavPeakPyramid? _peakPyramid;
    private int _pyramidGeneration;
    private (double ViewStart, double ViewEnd, int Width)? _rawDetailWanted;
    private bool _rawDetailReading;
    private IReadOnlyList<WaveformBarMark> _bars = [];
    private IReadOnlyList<WaveformMarkerMark> _markers = [];
    private IReadOnlyList<WaveformCycleMark> _cycles = [];
    private IReadOnlyList<WaveformRegionMark> _regions = [];
    private IReadOnlyList<WaveformOutputPart> _outputParts = [];
    private int? _exportHighlightPartNumber;
    private readonly System.Windows.Forms.Timer _exportGlowTimer;

    // 時間軸ズーム（1=全体表示。既定より縮小しない）
    private const double TimeZoomMin = 1.0;
    private const double TimeZoomMax = 8192.0;
    // キーボード: ≈ 2^(1/8)。ホイールは少し大きめ ≈ 2^(1/4)
    private const double TimeZoomStep = 1.09050773267;
    private const double TimeZoomWheelStep = 1.189207115;
    private double _timeZoom = TimeZoomMin;
    private double _viewStart; // 表示左端の絶対進捗 0..1

    // 振幅ズーム（1=既定。既定より縮小しない）
    private const double AmpZoomMin = 1.0;
    private const double AmpZoomMax = 128.0;
    private const double AmpZoomStep = 1.09050773267;
    private const double AmpZoomWheelStep = 1.189207115;
    private double _ampZoom = AmpZoomMin;

    private double? _playheadProgress;
    private readonly List<(double Progress, long TickMs)> _trailSamples = [];
    private bool _trailActive;
    private double? _exitPlayheadProgress;
    private readonly List<(double Progress, long TickMs)> _exitTrailSamples = [];
    private bool _exitTrailActive;
    private bool _isDraggingSeek;
    private float? _mouseGuideX;
    private Bitmap? _staticLayer;
    private bool _staticLayerDirty = true;
    private bool _revealActive;
    private bool _holdScaffold;
    private TaskCompletionSource? _revealCompleted;
    private long _revealStartTickMs;
    private readonly System.Windows.Forms.Timer _revealTimer;
    private readonly Bitmap?[] _revealLayers = new Bitmap?[4];
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
        BackColor = UiColors.ForControlBack(UiColors.WaveformBack);
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
        WavFileInfo? wavInfo = null,
        IReadOnlyList<WaveformBarMark>? bars = null,
        IReadOnlyList<WaveformMarkerMark>? markers = null,
        IReadOnlyList<WaveformCycleMark>? cycles = null,
        IReadOnlyList<WaveformRegionMark>? regions = null,
        IReadOnlyList<WaveformOutputPart>? outputParts = null)
    {
        StopRevealAnimation();
        _peaks = peaks;
        _wavInfo = wavInfo;
        _sourceDisplayName = string.IsNullOrWhiteSpace(sourcePath)
            ? string.Empty
            : Path.GetFileName(sourcePath);
        ClearDetailPeaks();
        StartPeakPyramidBuild(wavInfo);
        _bars = bars ?? [];
        _markers = markers ?? [];
        _cycles = cycles ?? [];
        _regions = regions ?? [];
        _outputParts = outputParts ?? [];
        ResetTimeZoom(refresh: false);
        ResetAmpZoom(refresh: false);
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
        BackColor = UiColors.ForControlBack(UiColors.WaveformBack);
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
        _wavInfo = null;
        _sourceDisplayName = string.Empty;
        ClearDetailPeaks();
        _peakPyramid = null;
        _pyramidGeneration++;
        _bars = [];
        _markers = [];
        _cycles = [];
        _regions = [];
        _outputParts = [];
        ResetTimeZoom(refresh: false);
        ResetAmpZoom(refresh: false);
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
    /// recordTrail が true（再生中）のときは、ズーム表示で画面外へ出たらページめくり追従する。
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
            RecordTrailSample(clamped, _trailSamples, ref _trailActive);
            FollowPlayheadPaged(clamped);
        }

        Invalidate();
    }

    /// <summary>
    /// -E 二重再生ヘッド（赤）。progress は 0〜1。null で非表示。
    /// </summary>
    public void SetExitPlayhead(double? progress, bool recordTrail = false)
    {
        if (progress is null)
        {
            ClearExitPlayhead();
            Invalidate();
            return;
        }

        var clamped = Math.Clamp(progress.Value, 0d, 1d);
        _exitPlayheadProgress = clamped;
        _exitTrailActive = recordTrail;
        if (!recordTrail)
        {
            _exitTrailSamples.Clear();
        }
        else
        {
            RecordTrailSample(clamped, _exitTrailSamples, ref _exitTrailActive);
        }

        Invalidate();
    }

    /// <summary>
    /// 再生ヘッドが表示窓の外へ出たとき、ページ単位で表示窓を進める／戻す。
    /// 右はみ出し: 新ページの左端にプレイヘッド。左はみ出し: 新ページの右端付近に。
    /// </summary>
    private void FollowPlayheadPaged(double progress)
    {
        if (_peaks is null || _peaks.IsEmpty || _timeZoom <= TimeZoomMin + 1e-9)
        {
            return;
        }

        var span = ViewSpan;
        if (span >= 1.0 - 1e-12)
        {
            return;
        }

        var viewEnd = _viewStart + span;
        double newStart;
        if (progress >= viewEnd)
        {
            // ページ送り: はみ出した位置を次ページの左端に
            newStart = progress;
        }
        else if (progress < _viewStart)
        {
            // ページ戻し: プレイヘッドが新ページ右端に来るようずらす
            newStart = progress - span;
        }
        else
        {
            return;
        }

        var previous = _viewStart;
        _viewStart = newStart;
        ClampTimeViewWindow();
        if (Math.Abs(_viewStart - previous) < 1e-12)
        {
            return;
        }

        NotifyTimeViewChanged();
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

    /// <summary>時間軸を拡大（既定より縮小しない）。</summary>
    public void ZoomTimeIn() => AdjustTimeZoom(TimeZoomStep, AnchorProgressForKeyboardZoom());

    /// <summary>時間軸を縮小（既定未満にはしない）。</summary>
    public void ZoomTimeOut() => AdjustTimeZoom(1.0 / TimeZoomStep, AnchorProgressForKeyboardZoom());

    /// <summary>時間軸ズームを既定（全体表示）に戻す。</summary>
    public void ResetTimeZoom() => ResetTimeZoom(refresh: true);

    /// <summary>時間軸を最大倍率にする。</summary>
    public void ZoomTimeToMax() => SetTimeZoomAbsolute(TimeZoomMax, AnchorProgressForKeyboardZoom());

    /// <summary>振幅を拡大（既定より縮小しない）。</summary>
    public void ZoomAmpIn() => AdjustAmpZoom(AmpZoomStep);

    /// <summary>振幅を縮小（既定未満にはしない）。</summary>
    public void ZoomAmpOut() => AdjustAmpZoom(1.0 / AmpZoomStep);

    /// <summary>振幅ズームを既定に戻す。</summary>
    public void ResetAmpZoom() => ResetAmpZoom(refresh: true);

    /// <summary>振幅を最大倍率にする。</summary>
    public void ZoomAmpToMax() => SetAmpZoomAbsolute(AmpZoomMax);

    /// <summary>表示窓を波形先頭へ。</summary>
    public void PanTimeToStart()
    {
        if (_peaks is null || _peaks.IsEmpty)
        {
            return;
        }

        _viewStart = 0;
        NotifyTimeViewChanged();
    }

    /// <summary>表示窓を波形末尾へ。</summary>
    public void PanTimeToEnd()
    {
        if (_peaks is null || _peaks.IsEmpty)
        {
            return;
        }

        _viewStart = Math.Max(0d, 1.0 - ViewSpan);
        NotifyTimeViewChanged();
    }

    /// <summary>再生位置を直前の小節線へ。成功したら true。</summary>
    public bool SeekToPreviousBar() => TrySeekAlongSamples(CollectBarSamples(), previous: true);

    /// <summary>再生位置を直後の小節線へ。成功したら true。</summary>
    public bool SeekToNextBar() => TrySeekAlongSamples(CollectBarSamples(), previous: false);

    /// <summary>
    /// 相対小節番号（1 始まり）の小節頭へシーク。成功したら true。
    /// </summary>
    public bool TrySeekToBarNumber(int barNumber)
    {
        if (barNumber < 1 || _peaks is null || _peaks.IsEmpty || _peaks.FrameCount <= 0)
        {
            return false;
        }

        foreach (var bar in _bars)
        {
            if (bar.IsTempoChangeOnly || bar.BarNumber != barNumber)
            {
                continue;
            }

            var frameCount = _peaks.FrameCount;
            var sample = Math.Clamp(bar.SampleOffset, 0L, frameCount);
            var progress = Math.Clamp(sample / (double)frameCount, 0d, 1d);
            _playheadProgress = progress;
            EnsureAbsoluteVisible(progress);
            SeekRequested?.Invoke(this, progress);
            return true;
        }

        return false;
    }

    /// <summary>現在の再生位置に最も近い（直前を含む）相対小節番号。無ければ null。</summary>
    public int? GetNearestBarNumber()
    {
        if (_peaks is null || _peaks.IsEmpty || _peaks.FrameCount <= 0 || _bars.Count == 0)
        {
            return null;
        }

        var frameCount = _peaks.FrameCount;
        var currentSample = (long)Math.Round((_playheadProgress ?? 0d) * frameCount);
        currentSample = Math.Clamp(currentSample, 0L, frameCount);

        int? best = null;
        long bestSample = long.MinValue;
        foreach (var bar in _bars)
        {
            if (bar.IsTempoChangeOnly || bar.BarNumber < 1)
            {
                continue;
            }

            var sample = Math.Clamp(bar.SampleOffset, 0L, frameCount);
            if (sample <= currentSample && sample >= bestSample)
            {
                bestSample = sample;
                best = bar.BarNumber;
            }
        }

        return best;
    }

    /// <summary>再生位置を直前のリージョン分割点へ。成功したら true。</summary>
    public bool SeekToPreviousRegionSplit() =>
        TrySeekAlongSamples(CollectRegionSplitSamples(), previous: true);

    /// <summary>再生位置を直後のリージョン分割点へ。成功したら true。</summary>
    public bool SeekToNextRegionSplit() =>
        TrySeekAlongSamples(CollectRegionSplitSamples(), previous: false);

    private List<long> CollectBarSamples()
    {
        var result = new List<long>();
        if (_peaks is null || _peaks.IsEmpty || _peaks.FrameCount <= 0)
        {
            return result;
        }

        var frameCount = _peaks.FrameCount;
        var seen = new HashSet<long>();
        foreach (var bar in _bars)
        {
            if (bar.IsTempoChangeOnly)
            {
                continue;
            }

            var sample = Math.Clamp(bar.SampleOffset, 0L, frameCount);
            if (seen.Add(sample))
            {
                result.Add(sample);
            }
        }

        return result;
    }

    private List<long> CollectRegionSplitSamples()
    {
        var result = new List<long>();
        if (_peaks is null || _peaks.IsEmpty || _peaks.FrameCount <= 0 || _regions.Count == 0)
        {
            return result;
        }

        var frameCount = _peaks.FrameCount;
        var seen = new HashSet<long>();
        foreach (var region in _regions)
        {
            var start = Math.Clamp(region.StartSampleOffset, 0L, frameCount);
            if (seen.Add(start))
            {
                result.Add(start);
            }

            var end = Math.Clamp(region.EndSampleOffset, 0L, frameCount);
            if (seen.Add(end))
            {
                result.Add(end);
            }
        }

        return result;
    }

    private bool TrySeekAlongSamples(List<long> samples, bool previous)
    {
        if (_peaks is null || _peaks.IsEmpty || _peaks.FrameCount <= 0 || samples.Count == 0)
        {
            return false;
        }

        samples.Sort();
        var frameCount = _peaks.FrameCount;
        // 再生エンジンは時間ベースで僅かに手前へ着地し得るため、表示上の位置でサンプルを出す
        var currentSample = (long)Math.Round((_playheadProgress ?? 0d) * frameCount);
        currentSample = Math.Clamp(currentSample, 0L, frameCount);

        long? targetSample = null;
        if (previous)
        {
            for (var i = samples.Count - 1; i >= 0; i--)
            {
                if (samples[i] < currentSample)
                {
                    targetSample = samples[i];
                    break;
                }
            }
        }
        else
        {
            for (var i = 0; i < samples.Count; i++)
            {
                if (samples[i] > currentSample)
                {
                    targetSample = samples[i];
                    break;
                }
            }
        }

        if (targetSample is not long sample)
        {
            return false;
        }

        var progress = Math.Clamp(sample / (double)frameCount, 0d, 1d);
        // エンジン着地誤差の前に表示位置を確定し、連続ジャンプを可能にする
        _playheadProgress = progress;
        EnsureAbsoluteVisible(progress);
        SeekRequested?.Invoke(this, progress);
        return true;
    }

    /// <summary>指定の絶対進捗が見えるよう表示窓をずらす（既に見えていれば何もしない）。</summary>
    private void EnsureAbsoluteVisible(double absoluteProgress)
    {
        if (_peaks is null || _peaks.IsEmpty || _timeZoom <= TimeZoomMin + 1e-9)
        {
            return;
        }

        absoluteProgress = Math.Clamp(absoluteProgress, 0d, 1d);
        var span = ViewSpan;
        var margin = span * 0.05d;
        if (absoluteProgress >= _viewStart + margin && absoluteProgress <= ViewEnd - margin)
        {
            return;
        }

        _viewStart = absoluteProgress - span * 0.5d;
        ClampTimeViewWindow();
        NotifyTimeViewChanged();
    }

    /// <summary>
    /// マウスホイールによる時間軸ズーム。
    /// <paramref name="mouseX"/> は本コントロール座標系。アンカーはその X の絶対進捗。
    /// </summary>
    public void ZoomTimeByWheel(int wheelDelta, int mouseX)
    {
        if (_peaks is null || _peaks.IsEmpty || wheelDelta == 0)
        {
            return;
        }

        // ノッチに応じた連続倍率（ホイールは 1/4 oct 刻み）
        var notches = Math.Max(1.0, Math.Abs(wheelDelta) / 120.0);
        var factor = Math.Pow(TimeZoomWheelStep, notches);
        if (wheelDelta < 0)
        {
            factor = 1.0 / factor;
        }

        var anchor = TryGetProgressFromX(mouseX, out var progress)
            ? progress
            : AnchorProgressForKeyboardZoom();
        AdjustTimeZoom(factor, anchor);
    }

    /// <summary>マウスホイールによる縦方向（振幅）ズーム（Shift+ホイール）。</summary>
    public void ZoomAmpByWheel(int wheelDelta)
    {
        if (_peaks is null || _peaks.IsEmpty || wheelDelta == 0)
        {
            return;
        }

        var notches = Math.Max(1.0, Math.Abs(wheelDelta) / 120.0);
        var factor = Math.Pow(AmpZoomWheelStep, notches);
        if (wheelDelta < 0)
        {
            factor = 1.0 / factor;
        }

        AdjustAmpZoom(factor);
    }

    private double AnchorProgressForKeyboardZoom()
    {
        if (_playheadProgress is double playhead
            && playhead >= _viewStart
            && playhead <= ViewEnd)
        {
            return playhead;
        }

        return _viewStart + ViewSpan * 0.5d;
    }

    private double ViewSpan => 1.0 / Math.Max(_timeZoom, TimeZoomMin);

    private double ViewEnd => Math.Min(1.0, _viewStart + ViewSpan);

    private void ResetTimeZoom(bool refresh)
    {
        _timeZoom = TimeZoomMin;
        _viewStart = 0;
        if (refresh)
        {
            NotifyTimeViewChanged();
        }
    }

    private void ResetAmpZoom(bool refresh)
    {
        _ampZoom = AmpZoomMin;
        if (refresh)
        {
            NotifyAmpViewChanged();
        }
    }

    private void SetTimeZoomAbsolute(double zoom, double anchorAbsolute)
    {
        if (_peaks is null || _peaks.IsEmpty)
        {
            return;
        }

        zoom = Math.Clamp(zoom, TimeZoomMin, TimeZoomMax);
        var oldSpan = ViewSpan;
        var rel = oldSpan > 1e-12
            ? Math.Clamp((anchorAbsolute - _viewStart) / oldSpan, 0d, 1d)
            : 0.5d;
        _timeZoom = zoom;
        _viewStart = anchorAbsolute - rel * ViewSpan;
        ClampTimeViewWindow();
        NotifyTimeViewChanged();
    }

    private void SetAmpZoomAbsolute(double zoom)
    {
        if (_peaks is null || _peaks.IsEmpty)
        {
            return;
        }

        _ampZoom = Math.Clamp(zoom, AmpZoomMin, AmpZoomMax);
        NotifyAmpViewChanged();
    }

    private void AdjustAmpZoom(double factor)
    {
        if (_peaks is null || _peaks.IsEmpty)
        {
            return;
        }

        var newZoom = Math.Clamp(_ampZoom * factor, AmpZoomMin, AmpZoomMax);
        if (Math.Abs(newZoom - _ampZoom) < 1e-9)
        {
            return;
        }

        _ampZoom = newZoom;
        NotifyAmpViewChanged();
    }

    private void AdjustTimeZoom(double factor, double anchorAbsolute)
    {
        if (_peaks is null || _peaks.IsEmpty)
        {
            return;
        }

        var newZoom = Math.Clamp(_timeZoom * factor, TimeZoomMin, TimeZoomMax);
        if (Math.Abs(newZoom - _timeZoom) < 1e-9
            && (newZoom <= TimeZoomMin || newZoom >= TimeZoomMax))
        {
            if (newZoom <= TimeZoomMin)
            {
                _viewStart = 0;
                NotifyTimeViewChanged();
            }

            return;
        }

        SetTimeZoomAbsolute(newZoom, anchorAbsolute);
    }

    private void ClampTimeViewWindow()
    {
        if (_timeZoom <= TimeZoomMin + 1e-9)
        {
            _timeZoom = TimeZoomMin;
            _viewStart = 0;
            return;
        }

        var span = ViewSpan;
        _viewStart = Math.Clamp(_viewStart, 0d, Math.Max(0d, 1.0 - span));
    }

    private void ClearDetailPeaks()
    {
        _detailPeaks = null;
        _detailViewStart = double.NaN;
        _detailViewEnd = double.NaN;
        _detailPixelWidth = -1;
        _detailIsApproximate = false;
        _rawDetailWanted = null;
    }

    private void NotifyAmpViewChanged() => RebuildPresentationLayers(clearDetailPeaks: false);

    private void NotifyTimeViewChanged() => RebuildPresentationLayers(clearDetailPeaks: true);

    private void RebuildPresentationLayers(bool clearDetailPeaks)
    {
        if (clearDetailPeaks)
        {
            ClearDetailPeaks();
        }

        // Bitmap は破棄せずダーティ化のみ（直後の BuildStaticLayer で同サイズなら再利用）
        _staticLayerDirty = true;
        InvalidateRevealLayers();

        if (!IsHandleCreated || IsDisposed)
        {
            Invalidate();
            return;
        }

        var bounds = ClientRectangle;
        if (bounds.Width > 2 && bounds.Height > 2 && _peaks is not null && !_peaks.IsEmpty)
        {
            if (_revealActive)
            {
                EnsureRevealLayers(bounds);
            }
            else
            {
                BuildStaticLayer(bounds);
            }
        }

        Invalidate();
    }

    private static double SampleToAbsolute(long sampleOffset, long frameCount)
    {
        if (frameCount <= 0)
        {
            return 0;
        }

        return Math.Clamp(sampleOffset / (double)frameCount, 0d, 1d);
    }

    private float AbsoluteToX(double absoluteProgress, Rectangle area)
    {
        var t = (absoluteProgress - _viewStart) / ViewSpan;
        return area.Left + (float)(t * area.Width);
    }

    private bool TryMapAbsoluteRange(
        double absoluteStart,
        double absoluteEnd,
        Rectangle area,
        out float x0,
        out float x1)
    {
        x0 = 0;
        x1 = 0;
        if (absoluteEnd < _viewStart || absoluteStart > ViewEnd)
        {
            return false;
        }

        var a0 = Math.Clamp(absoluteStart, _viewStart, ViewEnd);
        var a1 = Math.Clamp(absoluteEnd, _viewStart, ViewEnd);
        x0 = AbsoluteToX(a0, area);
        x1 = AbsoluteToX(a1, area);
        if (x1 < x0)
        {
            (x0, x1) = (x1, x0);
        }

        return true;
    }

    /// <summary>クリック／ドラッグでシーク（0〜1）。</summary>
    public event EventHandler<double>? SeekRequested;

    private void ClearPlayhead()
    {
        _playheadProgress = null;
        _trailActive = false;
        ClearTrailSamples();
        ClearExitPlayhead();
    }

    private void ClearExitPlayhead()
    {
        _exitPlayheadProgress = null;
        _exitTrailActive = false;
        _exitTrailSamples.Clear();
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

        var timeline = GetTimelineContentRect();
        if (timeline.Width <= 0)
        {
            return;
        }

        var x = Math.Clamp(mouseX, timeline.Left, timeline.Right);
        if (mouseX < timeline.Left)
        {
            if (_mouseGuideX is not null)
            {
                _mouseGuideX = null;
                Invalidate();
            }

            return;
        }

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

        var timeline = GetTimelineContentRect();
        if (timeline.Width <= 0 || mouseX < timeline.Left)
        {
            return false;
        }

        var local = Math.Clamp((mouseX - timeline.Left) / (double)timeline.Width, 0d, 1d);
        progress = Math.Clamp(_viewStart + local * ViewSpan, 0d, 1d);
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

        if (_revealActive)
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
        var timeline = GetTimelineRect(content);
        DrawExportPartGlow(g, timeline);
        DrawPlayhead(g, timeline, _playheadProgress, _trailSamples, UiColors.SeekCyan);
        DrawPlayhead(g, timeline, _exitPlayheadProgress, _exitTrailSamples, UiColors.SeekExit);
        DrawMouseGuide(g, timeline);
    }

    private void DrawEmptyScaffold(Graphics g, Rectangle bounds)
    {
        var content = Rectangle.Inflate(bounds, -4, -4);
        var (info, labels, wave, rowHeight) = GetLayout(content, g);
        DrawInfoLane(g, info, labels, wave, rowHeight, LabelRowCount);
        DrawLabelRows(g, labels, rowHeight, LabelRowCount);

        if (_peaks is not null && !_peaks.IsEmpty)
        {
            return;
        }

        using var brush = new SolidBrush(UiColors.EmptyHint);
        const string message = "Wave / XML をドロップすると波形と小節線を表示します";
        var size = g.MeasureString(message, Font);
        var centerX = wave.Width > 0
            ? wave.Left + (wave.Width - size.Width) / 2f
            : (bounds.Width - size.Width) / 2f;
        var centerY = wave.Height > 0
            ? wave.Top + (wave.Height - size.Height) / 2f
            : (bounds.Height - size.Height) / 2f;
        g.DrawString(message, Font, brush, centerX, centerY);
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
            var (info, labels, wave, rowHeight) = GetLayout(content, g);
            DrawInfoLane(g, info, labels, wave, rowHeight, rows);
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

        // 4: キャプション（フェード＋下からスライド）
        var captionsT = LayerLocalT(4, elapsed);
        if (captionsT > 0f)
        {
            var eased = EaseOutCubic(captionsT);
            DrawLayerFaded(g, _revealLayers[3], eased, offsetY: (1f - eased) * 12f);
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
        var (_, labels, wave, rowHeight) = GetLayout(content, probe);
        _revealWaveRect = wave;

        _revealLayers[0] = CreateTransparentLayer(size, g => DrawWaveform(g, wave));
        _revealLayers[1] = CreateTransparentLayer(size, g => DrawBars(g, labels, wave, rowHeight));
        _revealLayers[2] = CreateTransparentLayer(size, g => DrawMarkers(g, labels, rowHeight));
        _revealLayers[3] = CreateTransparentLayer(size, g => DrawOutputPartLabels(g, wave));
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
    /// 左: 行ラベル／波形名、右上: 小節／テンポ／拍子／マーカーの4行、右下: 波形エリア。
    /// </summary>
    private (Rectangle Info, Rectangle Labels, Rectangle Wave, float RowHeight) GetLayout(
        Rectangle content,
        Graphics g)
    {
        var rowHeight = Font.GetHeight(g) + 2f;
        var labelsHeight = (int)Math.Ceiling(rowHeight * LabelRowCount);
        _infoLaneWidth = MeasureInfoLaneWidth(g, content.Width);
        var mainLeft = content.Left + _infoLaneWidth + InfoLaneSeparatorPx;
        var mainWidth = Math.Max(0, content.Width - _infoLaneWidth - InfoLaneSeparatorPx);

        var info = new Rectangle(content.Left, content.Top, _infoLaneWidth, content.Height);
        var labels = new Rectangle(mainLeft, content.Top, mainWidth, labelsHeight);
        var waveTop = content.Top + labelsHeight + LabelWaveGapPx;
        var waveHeight = Math.Max(0, content.Bottom - waveTop);
        var wave = new Rectangle(mainLeft, waveTop, mainWidth, waveHeight);
        return (info, labels, wave, rowHeight);
    }

    private int MeasureInfoLaneWidth(Graphics g, int contentWidth)
    {
        float maxText = 0f;
        foreach (var label in InfoRowLabels)
        {
            maxText = Math.Max(maxText, g.MeasureString(label, Font).Width);
        }

        // ラベル幅＋余白の 2 倍（ファイル名の折り返し用）
        var width = ((int)Math.Ceiling(maxText) + InfoLanePadX * 2) * 2;
        var maxAllowed = Math.Max(96, contentWidth / 2);
        return Math.Clamp(width, 128, maxAllowed);
    }

    private Rectangle GetTimelineContentRect()
    {
        var content = Rectangle.Inflate(ClientRectangle, -4, -4);
        return GetTimelineRect(content);
    }

    private Rectangle GetTimelineRect(Rectangle content)
    {
        var inset = _infoLaneWidth + InfoLaneSeparatorPx;
        var mainLeft = content.Left + inset;
        var mainWidth = Math.Max(0, content.Width - inset);
        return new Rectangle(mainLeft, content.Top, mainWidth, content.Height);
    }

    private void BuildStaticLayer(Rectangle bounds)
    {
        // サイズが同じなら Bitmap を作り直さず再利用する（ズーム連打時の GC 圧を抑える）
        if (_staticLayer is null
            || _staticLayer.Width != bounds.Width
            || _staticLayer.Height != bounds.Height)
        {
            DisposeStaticLayer();
            _staticLayer = new Bitmap(
                bounds.Width,
                bounds.Height,
                System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        }

        using var g = Graphics.FromImage(_staticLayer);
        g.Clear(UiColors.WaveformBack);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        var content = Rectangle.Inflate(bounds, -4, -4);
        var (info, labels, wave, rowHeight) = GetLayout(content, g);
        DrawInfoLane(g, info, labels, wave, rowHeight, LabelRowCount);
        DrawLabelRows(g, labels, rowHeight, LabelRowCount);
        DrawWaveform(g, wave);
        DrawBars(g, labels, wave, rowHeight);
        DrawMarkers(g, labels, rowHeight);
        DrawOutputPartLabels(g, wave);
        _staticLayerDirty = false;
    }

    private void DrawInfoLane(
        Graphics g,
        Rectangle info,
        Rectangle labels,
        Rectangle wave,
        float rowHeight,
        int visibleRowCount)
    {
        if (info.Width <= 0 || info.Height <= 0 || visibleRowCount <= 0)
        {
            return;
        }

        ReadOnlySpan<Color> rowColors =
        [
            UiColors.BarNumberBg,
            UiColors.TempoBg,
            UiColors.SignatureBg,
            UiColors.MarkerRowBg,
        ];

        using var textBrush = new SolidBrush(UiColors.WaveformInfoFg);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Far,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        var count = Math.Min(visibleRowCount, InfoRowLabels.Length);
        for (var i = 0; i < count; i++)
        {
            var top = labels.Top + i * rowHeight;
            using var bg = new SolidBrush(rowColors[i]);
            g.FillRectangle(bg, info.Left, top, info.Width, rowHeight);
            g.DrawString(
                InfoRowLabels[i],
                Font,
                textBrush,
                new RectangleF(
                    info.Left + InfoLanePadX,
                    top,
                    Math.Max(0, info.Width - InfoLanePadX * 2),
                    rowHeight),
                format);
        }

        // 情報レーンとタイムラインの区切り（小節線色・2px）
        using (var sepBrush = new SolidBrush(UiColors.BarLine))
        {
            g.FillRectangle(
                sepBrush,
                info.Right,
                info.Top,
                InfoLaneSeparatorPx,
                info.Height);
        }

        if (_sourceDisplayName.Length == 0 || wave.Height <= 0)
        {
            return;
        }

        // マーカー行の下＝波形エリア左。上下左右中央。長い名前は改行
        var nameWidth = Math.Max(0, info.Width - InfoLanePadX * 2);
        var nameHeight = Math.Max(0, wave.Height - 4f);
        if (nameWidth <= 0 || nameHeight <= 0)
        {
            return;
        }

        var wrappedName = WrapTextToWidth(g, _sourceDisplayName, Font, nameWidth);
        using var nameFormat = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
        };
        g.DrawString(
            wrappedName,
            Font,
            textBrush,
            new RectangleF(info.Left + InfoLanePadX, wave.Top + 2f, nameWidth, nameHeight),
            nameFormat);
    }

    /// <summary>スペース無しのファイル名でも幅内で改行する。</summary>
    private static string WrapTextToWidth(Graphics g, string text, Font font, float maxWidth)
    {
        if (text.Length == 0 || maxWidth <= 1f)
        {
            return text;
        }

        if (g.MeasureString(text, font).Width <= maxWidth)
        {
            return text;
        }

        var lines = new List<string>();
        var line = new System.Text.StringBuilder();
        foreach (var ch in text)
        {
            line.Append(ch);
            if (g.MeasureString(line.ToString(), font).Width <= maxWidth)
            {
                continue;
            }

            if (line.Length <= 1)
            {
                lines.Add(line.ToString());
                line.Clear();
                continue;
            }

            line.Length--;
            lines.Add(line.ToString());
            line.Clear();
            line.Append(ch);
        }

        if (line.Length > 0)
        {
            lines.Add(line.ToString());
        }

        return string.Join("\n", lines);
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

        // -L / -A / -E / 通常グレーは下塗り。-R は後で波形上へ重ねる。
        DrawRegionBackgrounds(g, wave);

        var peaks = _peaks!;
        var midY = wave.Top + wave.Height / 2f;
        using (var centerPen = new Pen(UiColors.WaveCenter))
        {
            g.DrawLine(centerPen, wave.Left, midY, wave.Right, midY);
        }

        if (peaks.Mins.Length == 0)
        {
            DrawExcludedRegionOverlays(g, wave);
            return;
        }

        // 縦ズーム込み。±1.0 が既定で波形上下端、それ以上はクリップ
        var amplitude = wave.Height * 0.5f * (float)_ampZoom;
        using var wavePen = new Pen(UiColors.WaveFill, 1f);

        // 縦 1px 線に AA は不要。無効化すると列描画が大幅に速くなる
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        try
        {
            var detail = EnsureDetailPeaks(wave);
            if (detail is not null && !detail.IsEmpty)
            {
                var bucketCount = detail.Mins.Length;
                for (var px = 0; px < wave.Width; px++)
                {
                    var bucket = bucketCount == wave.Width
                        ? Math.Clamp(px, 0, bucketCount - 1)
                        : (int)Math.Clamp(
                            Math.Floor((px + 0.5d) / wave.Width * bucketCount),
                            0,
                            bucketCount - 1);
                    DrawPeakColumn(g, wavePen, wave, midY, amplitude, wave.Left + px + 0.5f,
                        detail.Mins[bucket], detail.Maxs[bucket]);
                }
            }
            else
            {
                // フォールバック: 全体概要ピークを表示窓に写像
                var overviewCount = peaks.Mins.Length;
                for (var px = 0; px < wave.Width; px++)
                {
                    var abs = _viewStart + ((px + 0.5d) / wave.Width) * ViewSpan;
                    var bucket = (int)Math.Clamp(Math.Floor(abs * overviewCount), 0, overviewCount - 1);
                    DrawPeakColumn(g, wavePen, wave, midY, amplitude, wave.Left + px + 0.5f,
                        peaks.Mins[bucket], peaks.Maxs[bucket]);
                }
            }
        }
        finally
        {
            g.SmoothingMode = smoothing;
        }

        // -R だけ波形の上に重ねる（境界線・Cue 線は別描画）
        DrawExcludedRegionOverlays(g, wave);

        // 連続リージョン固まりの境界（白）→ Entry/Exit Cue（ライム／赤・白より手前）
        DrawContiguousRegionCueMarkers(g, wave);
    }

    /// <summary>
    /// 除外で区切られた連続リージョン固まりごとに:
    /// <list type="bullet">
    /// <item>白: 固まりの頭（開始形）と末尾（終了形）</item>
    /// <item>ライム Entry: 頭。ただし先頭が -A ならその直後（開始形）。白より手前</item>
    /// <item>赤 Exit: 末尾。ただし末尾が -E ならその直前（終了形）。白より手前</item>
    /// </list>
    /// </summary>
    private void DrawContiguousRegionCueMarkers(Graphics g, Rectangle wave)
    {
        if (_peaks is null || _peaks.FrameCount <= 0 || _regions.Count == 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        var white = UiColors.ForControlBack(UiColors.RegionBoundaryMarker);
        var entryColor = UiColors.ForControlBack(UiColors.EntryCueMarker);
        var exitColor = UiColors.ForControlBack(UiColors.ExitCueMarker);

        using var whitePen = new Pen(white, 2f);
        using var whiteBrush = new SolidBrush(white);
        using var entryPen = new Pen(entryColor, 2f);
        using var entryBrush = new SolidBrush(entryColor);
        using var exitPen = new Pen(exitColor, 2f);
        using var exitBrush = new SolidBrush(exitColor);

        foreach (var run in CollectNonExcludedRuns(_regions))
        {
            if (run.Count == 0)
            {
                continue;
            }

            var first = run[0];
            var last = run[^1];

            // 白: 固まりの頭と末尾（先に描画＝奥）。下部の半四角
            if (TryGetWaveX(first.StartSampleOffset, frameCount, wave, out var xHead))
            {
                DrawRegionEdgeGlyph(g, whitePen, whiteBrush, wave, xHead, isStart: true, atBottom: true, square: true);
            }

            if (TryGetWaveX(last.EndSampleOffset, frameCount, wave, out var xTail))
            {
                DrawRegionEdgeGlyph(g, whitePen, whiteBrush, wave, xTail, isStart: false, atBottom: true, square: true);
            }

            // Entry: 頭。先頭 -A ならその後（上部の半三角）
            var entrySample = IsAnacrusisSuffix(first) && run.Count > 1
                ? run[1].StartSampleOffset
                : first.StartSampleOffset;
            if (TryGetWaveX(entrySample, frameCount, wave, out var xEntry))
            {
                DrawRegionEdgeGlyph(g, entryPen, entryBrush, wave, xEntry, isStart: true);
            }

            // Exit: 末尾。末尾が -E ならその前（上部の半三角）
            var exitSample = IsExitSuffix(last) && run.Count > 1
                ? last.StartSampleOffset
                : last.EndSampleOffset;
            if (TryGetWaveX(exitSample, frameCount, wave, out var xExit))
            {
                DrawRegionEdgeGlyph(g, exitPen, exitBrush, wave, xExit, isStart: false);
            }
        }
    }

    private bool TryGetWaveX(long sampleOffset, long frameCount, Rectangle wave, out float x)
    {
        var abs = SampleToAbsolute(sampleOffset, frameCount);
        if (abs < _viewStart - 1e-9 || abs > ViewEnd + 1e-9)
        {
            x = 0f;
            return false;
        }

        x = AbsoluteToX(abs, wave);
        return true;
    }

    /// <summary>
    /// DAW 風エッジ: 縦線＋半欠けグリフ（開始=右半分、終了=左半分）。
    /// 既定は上部の半三角。境界マーカーは下部の半四角。
    /// </summary>
    private static void DrawRegionEdgeGlyph(
        Graphics g,
        Pen pen,
        Brush brush,
        Rectangle wave,
        float x,
        bool isStart,
        bool atBottom = false,
        bool square = false)
    {
        const float halfW = 18f;

        g.DrawLine(pen, x, wave.Top, x, wave.Bottom);

        if (square)
        {
            var side = halfW * 2f / 3f;
            var top = atBottom ? wave.Bottom - side : wave.Top;
            var left = isStart ? x : x - side;
            g.FillRectangle(brush, left, top, side, side);
            return;
        }

        // 半欠けの ▼ は「正三角形の半分」(高さ=半幅×√3) だと縦長に見えるため、
        // 見かけのバランスを優先して高さ = 半幅 × √3/2 にする。
        var triH = halfW * MathF.Sqrt(3f) / 2f;
        var baseY = atBottom ? wave.Bottom : wave.Top;
        var tipY = atBottom ? wave.Bottom - triH : wave.Top + triH;
        PointF[] triangle = isStart
            ?
            [
                new(x, baseY),
                new(x + halfW, baseY),
                new(x, tipY),
            ]
            :
            [
                new(x - halfW, baseY),
                new(x, baseY),
                new(x, tipY),
            ];
        g.FillPolygon(brush, triangle);
    }

    private static List<List<WaveformRegionMark>> CollectNonExcludedRuns(
        IReadOnlyList<WaveformRegionMark> regions)
    {
        var runs = new List<List<WaveformRegionMark>>();
        List<WaveformRegionMark>? current = null;
        foreach (var region in regions)
        {
            if (region.IsExcluded)
            {
                current = null;
                continue;
            }

            if (current is null)
            {
                current = [];
                runs.Add(current);
            }

            current.Add(region);
        }

        return runs;
    }

    private static bool IsAnacrusisSuffix(WaveformRegionMark region) =>
        region.NameSuffix.Equals(WaveformRegionBuilder.AnacrusisSuffix, StringComparison.OrdinalIgnoreCase);

    private static bool IsExitSuffix(WaveformRegionMark region) =>
        region.NameSuffix.Equals(WaveformRegionBuilder.LoopEndSuffix, StringComparison.OrdinalIgnoreCase);

    private static void DrawPeakColumn(
        Graphics g,
        Pen wavePen,
        Rectangle wave,
        float midY,
        float amplitude,
        float x,
        float min,
        float max)
    {
        var y1 = Math.Clamp(midY - max * amplitude, wave.Top, wave.Bottom);
        var y2 = Math.Clamp(midY - min * amplitude, wave.Top, wave.Bottom);
        if (Math.Abs(y2 - y1) < 1f)
        {
            y2 = Math.Min(wave.Bottom, y1 + 1f);
        }

        g.DrawLine(wavePen, x, y1, x, y2);
    }

    /// <summary>
    /// 表示窓の範囲を画面幅分のピークに集計する（ズーム時の粒度を確保）。
    /// UI スレッドではメモリ上のピーク階層だけを使い、ディスク読みは一切しない。
    /// 階層の粒度が足りない深いズームでは、まず近似を即描画し、
    /// 生サンプルの精密ピークをバックグラウンドで読んで完成後に差し替える。
    /// </summary>
    private WavPeakData? EnsureDetailPeaks(Rectangle wave)
    {
        if (_wavInfo is null || _peaks is null || _peaks.IsEmpty || wave.Width <= 0)
        {
            return null;
        }

        var viewEnd = ViewEnd;
        if (_detailPeaks is not null
            && !_detailPeaks.IsEmpty
            && _detailPixelWidth == wave.Width
            && Math.Abs(_detailViewStart - _viewStart) < 1e-12
            && Math.Abs(_detailViewEnd - viewEnd) < 1e-12)
        {
            if (_detailIsApproximate)
            {
                RequestRawDetail(_viewStart, viewEnd, wave.Width);
            }

            return _detailPeaks;
        }

        var frameCount = _peaks.FrameCount;
        var startFrame = (long)Math.Floor(_viewStart * frameCount);
        var endFrame = (long)Math.Ceiling(viewEnd * frameCount);
        startFrame = Math.Clamp(startFrame, 0, frameCount);
        endFrame = Math.Clamp(endFrame, startFrame, frameCount);
        if (endFrame <= startFrame)
        {
            ClearDetailPeaks();
            return null;
        }

        var pyramid = _peakPyramid;
        if (pyramid is not null)
        {
            _detailPeaks = pyramid.ReadRange(startFrame, endFrame, wave.Width);
            _detailViewStart = _viewStart;
            _detailViewEnd = viewEnd;
            _detailPixelWidth = wave.Width;
            _detailIsApproximate = !pyramid.HasFullDetailFor(startFrame, endFrame, wave.Width);
            if (_detailIsApproximate)
            {
                RequestRawDetail(_viewStart, viewEnd, wave.Width);
            }

            return _detailPeaks;
        }

        // 階層構築中: 概要ピークのフォールバック描画で即応答する。
        // 概要の粒度で足りない深さなら精密読みだけ背景に依頼する。
        var rangeFrames = endFrame - startFrame;
        var overviewFramesPerBucket = frameCount / (double)Math.Max(1, _peaks.Mins.Length);
        if (rangeFrames / (double)wave.Width < overviewFramesPerBucket)
        {
            RequestRawDetail(_viewStart, viewEnd, wave.Width);
        }

        return null;
    }

    /// <summary>
    /// 生サンプルからの精密ピーク読みを背景スレッドへ依頼する（最新の要求のみ実行）。
    /// </summary>
    private void RequestRawDetail(double viewStart, double viewEnd, int width)
    {
        _rawDetailWanted = (viewStart, viewEnd, width);
        PumpRawDetailRead();
    }

    private void PumpRawDetailRead()
    {
        if (_rawDetailReading || _rawDetailWanted is not { } wanted)
        {
            return;
        }

        var info = _wavInfo;
        var peaks = _peaks;
        if (info is null || peaks is null || peaks.IsEmpty)
        {
            _rawDetailWanted = null;
            return;
        }

        _rawDetailWanted = null;

        // 既に同じ表示窓の精密ピークを持っているなら読み直さない
        if (!_detailIsApproximate
            && _detailPeaks is not null
            && !_detailPeaks.IsEmpty
            && _detailPixelWidth == wanted.Width
            && Math.Abs(_detailViewStart - wanted.ViewStart) < 1e-12
            && Math.Abs(_detailViewEnd - wanted.ViewEnd) < 1e-12)
        {
            return;
        }

        _rawDetailReading = true;
        var generation = _pyramidGeneration;
        var frameCount = peaks.FrameCount;
        var startFrame = Math.Clamp((long)Math.Floor(wanted.ViewStart * frameCount), 0, frameCount);
        var endFrame = Math.Clamp((long)Math.Ceiling(wanted.ViewEnd * frameCount), startFrame, frameCount);

        Task.Run(() =>
        {
            WavPeakData? data = null;
            try
            {
                data = WavPeakReader.ReadRange(info, startFrame, endFrame, wanted.Width);
            }
            catch
            {
                // 読み失敗時は近似のまま表示を続ける
            }

            try
            {
                BeginInvoke(() =>
                {
                    _rawDetailReading = false;
                    if (IsDisposed)
                    {
                        return;
                    }

                    if (generation == _pyramidGeneration)
                    {
                        ApplyRawDetail(wanted, data);
                    }

                    PumpRawDetailRead();
                });
            }
            catch (InvalidOperationException)
            {
                // ハンドル破棄後などは無視（次回 SetPreview で再構築）
                _rawDetailReading = false;
            }
        });
    }

    private void ApplyRawDetail((double ViewStart, double ViewEnd, int Width) wanted, WavPeakData? data)
    {
        if (data is null || data.IsEmpty)
        {
            return;
        }

        // ズーム連打中に届いた古い結果は捨てる（表示窓が一致するときだけ反映）
        if (Math.Abs(wanted.ViewStart - _viewStart) > 1e-12
            || Math.Abs(wanted.ViewEnd - ViewEnd) > 1e-12)
        {
            return;
        }

        _detailPeaks = data;
        _detailViewStart = wanted.ViewStart;
        _detailViewEnd = wanted.ViewEnd;
        _detailPixelWidth = wanted.Width;
        _detailIsApproximate = false;
        RebuildPresentationLayers(clearDetailPeaks: false);
    }

    /// <summary>ピーク階層をバックグラウンドで構築し、完成したら差し替える。</summary>
    private void StartPeakPyramidBuild(WavFileInfo? wavInfo)
    {
        _peakPyramid = null;
        var generation = ++_pyramidGeneration;
        if (wavInfo is null)
        {
            return;
        }

        Task.Run(() =>
        {
            WavPeakPyramid pyramid;
            try
            {
                pyramid = WavPeakPyramid.Build(wavInfo);
            }
            catch
            {
                return;
            }

            try
            {
                BeginInvoke(() =>
                {
                    if (generation != _pyramidGeneration || IsDisposed)
                    {
                        return;
                    }

                    _peakPyramid = pyramid;
                    // 初回表示は概要の間引きピークのまま焼き付いていることがある。
                    // 階層完成後に再構築しないと、初回ズーム時に初めて真のピークへ切り替わり
                    // 「縦が少し大きくなった」ように見える。
                    RebuildPresentationLayers(clearDetailPeaks: true);
                });
            }
            catch (InvalidOperationException)
            {
                // ハンドル破棄後などは無視（次回 SetPreview で再構築）
            }
        });
    }

    private void DrawRegionBackgrounds(Graphics g, Rectangle wave)
    {
        if (_peaks is null || _peaks.FrameCount <= 0 || _regions.Count == 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        using var gray = new SolidBrush(UiColors.RegionWaveFillGray);
        using var loop = new SolidBrush(UiColors.RegionWaveFillLoop);
        using var anacrusis = new SolidBrush(UiColors.RegionWaveFillAnacrusis);
        using var exit = new SolidBrush(UiColors.RegionWaveFillExit);

        foreach (var region in _regions)
        {
            if (region.IsExcluded)
            {
                continue;
            }

            var a0 = SampleToAbsolute(region.StartSampleOffset, frameCount);
            var a1 = SampleToAbsolute(region.EndSampleOffset, frameCount);
            if (!TryMapAbsoluteRange(a0, a1, wave, out var x0, out var x1))
            {
                continue;
            }

            var width = Math.Max(1f, x1 - x0);
            Brush fill;
            if (TryGetSuffixRegionBrush(region.NameSuffix, loop, anacrusis, exit, out var suffixFill))
            {
                fill = suffixFill;
            }
            else
            {
                fill = gray;
            }

            g.FillRectangle(fill, x0, wave.Top, width, wave.Height);
        }
    }

    /// <summary>
    /// -R 範囲を波形の上に重ねる。
    /// </summary>
    private void DrawExcludedRegionOverlays(Graphics g, Rectangle wave)
    {
        if (_peaks is null || _peaks.FrameCount <= 0 || _regions.Count == 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        using var excluded = new SolidBrush(UiColors.RegionWaveFillExcluded);

        foreach (var region in _regions)
        {
            if (!region.IsExcluded)
            {
                continue;
            }

            var a0 = SampleToAbsolute(region.StartSampleOffset, frameCount);
            var a1 = SampleToAbsolute(region.EndSampleOffset, frameCount);
            if (!TryMapAbsoluteRange(a0, a1, wave, out var x0, out var x1))
            {
                continue;
            }

            var width = Math.Max(1f, x1 - x0);
            g.FillRectangle(excluded, x0, wave.Top, width, wave.Height);
        }
    }

    /// <summary>-L=シアン、-A=ライム、-E=赤。該当しなければ false。</summary>
    private static bool TryGetSuffixRegionBrush(
        string nameSuffix,
        Brush loop,
        Brush anacrusis,
        Brush exit,
        out Brush fill)
    {
        if (nameSuffix.Equals(WaveformRegionBuilder.LoopLeftSuffix, StringComparison.OrdinalIgnoreCase))
        {
            fill = loop;
            return true;
        }

        if (nameSuffix.Equals(WaveformRegionBuilder.AnacrusisSuffix, StringComparison.OrdinalIgnoreCase))
        {
            fill = anacrusis;
            return true;
        }

        if (nameSuffix.Equals(WaveformRegionBuilder.LoopEndSuffix, StringComparison.OrdinalIgnoreCase))
        {
            fill = exit;
            return true;
        }

        fill = null!;
        return false;
    }

    private void DrawOutputPartLabels(Graphics g, Rectangle wave)
    {
        if (_peaks is null || _peaks.FrameCount <= 0 || _outputParts.Count == 0 || wave.Height <= 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        var idealFontSize = Math.Clamp(wave.Height * 0.22f, 13f, 28f);
        const float minFontSize = 8f;
        // 隣ラベルとのすき間は約2文字分（重なり無しでも密に見えないように）
        float gap;
        using (var gapProbe = new Font(Font.FontFamily, idealFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            gap = g.MeasureString("WW", gapProbe).Width;
        }

        var parts = new List<(string Text, float X0, float X1, float Mid)>(_outputParts.Count);
        foreach (var part in _outputParts)
        {
            var a0 = SampleToAbsolute(part.StartSampleOffset, frameCount);
            var a1 = SampleToAbsolute(part.EndSampleOffset, frameCount);
            if (!TryMapAbsoluteRange(a0, a1, wave, out var x0, out var x1))
            {
                continue;
            }

            if (x1 - x0 < 1f)
            {
                continue;
            }

            parts.Add((part.FileName, x0, x1, (x0 + x1) * 0.5f));
        }

        if (parts.Count == 0)
        {
            return;
        }

        using var brush = new SolidBrush(UiColors.OutputPartFg);
        using var shadow = new SolidBrush(UiColors.OutputPartShadow);
        var bottomMargin = Math.Max(10f, wave.Height * 0.12f);

        for (var i = 0; i < parts.Count; i++)
        {
            // 隣パートとの中間までをこのラベルの領域とする（リージョン外はみ出し可・他文字とは被らない）
            var slotLeft = i == 0
                ? wave.Left
                : (parts[i - 1].Mid + parts[i].Mid) * 0.5f + gap * 0.5f;
            var slotRight = i + 1 >= parts.Count
                ? wave.Right
                : (parts[i].Mid + parts[i + 1].Mid) * 0.5f - gap * 0.5f;
            var slotWidth = Math.Max(1f, slotRight - slotLeft);

            var fontSize = idealFontSize;
            using (var probe = new Font(Font.FontFamily, idealFontSize, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                var idealWidth = g.MeasureString(parts[i].Text, probe).Width;
                if (idealWidth > slotWidth)
                {
                    fontSize = Math.Max(minFontSize, idealFontSize * slotWidth / idealWidth);
                }
            }

            using var labelFont = new Font(Font.FontFamily, fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            var textWidth = g.MeasureString(parts[i].Text, labelFont).Width;
            var preferredX = parts[i].X0 + (parts[i].X1 - parts[i].X0 - textWidth) * 0.5f;
            var x = ClampLabelX(preferredX, textWidth, slotLeft, slotRight);
            var labelHeight = labelFont.GetHeight(g);
            var y = wave.Bottom - labelHeight - bottomMargin;

            // スロット外に描画しない（隣ラベルとの重なり防止）。狭いときは文字が切れる。
            var clip = g.Clip;
            g.SetClip(new RectangleF(slotLeft, y - 4f, slotWidth, labelHeight + 8f), System.Drawing.Drawing2D.CombineMode.Intersect);
            try
            {
                DrawLabeledShadow(g, parts[i].Text, labelFont, brush, shadow, x, y);
            }
            finally
            {
                g.Clip = clip;
            }
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

        var (_, _, wave, _) = GetLayout(content, g);
        if (wave.Width <= 0 || wave.Height <= 0)
        {
            return;
        }

        var frameCount = _peaks.FrameCount;
        var a0 = SampleToAbsolute(highlight.StartSampleOffset, frameCount);
        var a1 = SampleToAbsolute(highlight.EndSampleOffset, frameCount);
        if (!TryMapAbsoluteRange(a0, a1, wave, out var x0, out var x1))
        {
            return;
        }

        var width = Math.Max(2f, x1 - x0);
        var rect = new RectangleF(x0, wave.Top, width, wave.Height);

        // 約 1.1 秒周期で明滅（巨大ファイル書き出し中も動き続ける）
        var phase = (Environment.TickCount64 % 1100) / 1100f;
        var pulse = 0.40f + 0.60f * (0.5f + 0.5f * MathF.Sin(phase * MathF.PI * 2f));
        var baseColor = UiColors.ExportPartGlow;

        // 内側を半透明で塗り、「今この固まり」をはっきり見せる
        using (var fill = new SolidBrush(Color.FromArgb((int)(72 * pulse), baseColor)))
        {
            g.FillRectangle(fill, rect);
        }

        // 細い外光（太くしすぎない）
        using (var softPen = new Pen(Color.FromArgb((int)(55 * pulse), baseColor), 2f))
        {
            g.DrawRectangle(softPen, rect.X, rect.Y, rect.Width, rect.Height);
        }

        // コアの細線
        using var corePen = new Pen(Color.FromArgb((int)(220 * pulse), baseColor), 1f);
        g.DrawRectangle(corePen, rect.X, rect.Y, rect.Width, rect.Height);
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
        var lineTop = labels.Top;
        var lineBottom = wave.Height > 0 ? wave.Bottom : labels.Bottom;

        // 表示窓内の隣接小節頭の平均ピクセル間隔から、間引き段階を決める。
        // 番号幅は常に 3 桁想定（"000"）で、拡大時に桁が増えても重ならないようにする。
        var averageGapPx = EstimateVisibleBarGapPx(labels, frameCount);
        var threeDigitWidth = g.MeasureString("000", Font).Width;
        var minBarNumberGap = threeDigitWidth + 6f; // 描画オフセット分の余白
        var barStep = ChooseBarThinningStep(averageGapPx, minBarNumberGap);

        using var barPen = new Pen(UiColors.BarLine, 1f);
        using var tempoChangePen = new Pen(UiColors.TempoChangeLine, 1f)
        {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
            DashPattern = [3f, 3f],
        };
        using var barBrush = new SolidBrush(UiColors.WaveformInfoFg);
        using var tempoBrush = new SolidBrush(UiColors.WaveformInfoFg);
        using var signatureBrush = new SolidBrush(UiColors.WaveformInfoFg);

        var barLabelY = barRowTop + 1f;
        var tempoLabelY = tempoRowTop + 1f;
        var signatureLabelY = signatureRowTop + 1f;
        var lastTempoLabelX = float.NegativeInfinity;
        int? prevBarTempo = null;
        int? prevBarNumerator = null;
        int? prevBarDenominator = null;
        int? lastShownTempo = null;

        foreach (var bar in _bars)
        {
            var tempoRounded = (int)Math.Round(bar.Bpm, MidpointRounding.AwayFromZero);
            var tempoLabel = tempoRounded.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var abs = SampleToAbsolute(bar.SampleOffset, frameCount);
            var inView = abs >= _viewStart - 1e-9 && abs <= ViewEnd + 1e-9;

            if (bar.IsTempoChangeOnly)
            {
                if (inView)
                {
                    var tempoX = AbsoluteToX(abs, labels);
                    g.DrawLine(tempoChangePen, tempoX, tempoRowTop, tempoX, tempoRowTop + rowHeight);
                    TryDrawTempoLabel(g, tempoLabel, tempoRounded, tempoX, tempoLabelY, tempoBrush,
                        ref lastTempoLabelX, ref lastShownTempo, minLabelGap: 0f, force: true);
                }

                continue;
            }

            var tempoChanged = prevBarTempo is int pt && pt != tempoRounded;
            var signatureChanged = prevBarNumerator is int pn
                && prevBarDenominator is int pd
                && (pn != bar.Numerator || pd != bar.Denominator);
            var isStructural = tempoChanged || signatureChanged || prevBarTempo is null;

            if (!inView)
            {
                prevBarTempo = tempoRounded;
                prevBarNumerator = bar.Numerator;
                prevBarDenominator = bar.Denominator;
                continue;
            }

            var x = AbsoluteToX(abs, labels);
            var onGrid = IsBarOnThinningGrid(bar.BarNumber, barStep);
            var drawLine = isStructural || onGrid;
            if (drawLine)
            {
                g.DrawLine(barPen, x, lineTop, x, lineBottom);
            }

            var drawNumber = isStructural || onGrid;
            if (drawNumber)
            {
                g.DrawString(bar.BarNumber.ToString(), Font, barBrush, x + 3f, barLabelY);
            }

            // 拍子／テンポ変化（および先頭）では必ずテンポ・拍子ラベルも出す
            TryDrawTempoLabel(
                g,
                tempoLabel,
                tempoRounded,
                x,
                tempoLabelY,
                tempoBrush,
                ref lastTempoLabelX,
                ref lastShownTempo,
                minLabelGap: minBarNumberGap,
                force: isStructural);

            if (isStructural)
            {
                var signatureLabel = $"{bar.Numerator}/{bar.Denominator}";
                g.DrawString(signatureLabel, Font, signatureBrush, x + 3f, signatureLabelY);
            }

            prevBarTempo = tempoRounded;
            prevBarNumerator = bar.Numerator;
            prevBarDenominator = bar.Denominator;
        }
    }

    /// <summary>
    /// 十分な間隔がある限り密に。足りなければ 2→4→8→16→32→64 のグリッドへ間引く。
    /// グリッドは「1 と N の倍数」（例: N=8 → 1,8,16,24…／N=2 → 1,2,4,6…）。
    /// </summary>
    private static int ChooseBarThinningStep(double averageGapPx, float minGapPx)
    {
        ReadOnlySpan<int> steps = [1, 2, 4, 8, 16, 32, 64];
        foreach (var step in steps)
        {
            if (averageGapPx * step >= minGapPx)
            {
                return step;
            }
        }

        return 64;
    }

    /// <summary>
    /// step=1 は全小節。それ以外は 1 と step の倍数（2 → 1,2,4,6…／4 → 1,4,8,12…）。
    /// </summary>
    private static bool IsBarOnThinningGrid(int barNumber, int step)
    {
        if (step <= 1)
        {
            return true;
        }

        return barNumber == 1 || barNumber % step == 0;
    }

    /// <summary>表示窓内の隣接する小節頭の平均 X 間隔（無い／1 本だけなら十分広い値）。</summary>
    private double EstimateVisibleBarGapPx(Rectangle labels, long frameCount)
    {
        float? prevX = null;
        double sum = 0;
        var count = 0;
        foreach (var bar in _bars)
        {
            if (bar.IsTempoChangeOnly)
            {
                continue;
            }

            var abs = SampleToAbsolute(bar.SampleOffset, frameCount);
            if (abs < _viewStart - 1e-9 || abs > ViewEnd + 1e-9)
            {
                continue;
            }

            var x = AbsoluteToX(abs, labels);
            if (prevX is float px)
            {
                var gap = x - px;
                if (gap > 0.5f)
                {
                    sum += gap;
                    count++;
                }
            }

            prevX = x;
        }

        return count > 0 ? sum / count : labels.Width;
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
        float minLabelGap,
        bool force = false)
    {
        if (!force && lastShownTempo == tempoRounded)
        {
            return;
        }

        if (!force && x - lastTempoLabelX < minLabelGap)
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
        using var textBrush = new SolidBrush(UiColors.WaveformInfoFg);

        // 三角は時刻順に描画
        foreach (var marker in _markers)
        {
            var abs = SampleToAbsolute(marker.SampleOffset, frameCount);
            if (abs < _viewStart - 1e-9 || abs > ViewEnd + 1e-9)
            {
                continue;
            }

            var x = AbsoluteToX(abs, labels);

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

            var abs = SampleToAbsolute(marker.SampleOffset, frameCount);
            if (abs < _viewStart - 1e-9 || abs > ViewEnd + 1e-9)
            {
                continue;
            }

            var x = AbsoluteToX(abs, labels);
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

    private void DrawPlayhead(
        Graphics g,
        Rectangle content,
        double? playheadProgress,
        List<(double Progress, long TickMs)> trailSamples,
        Color color)
    {
        if (playheadProgress is null || content.Width <= 0)
        {
            return;
        }

        var abs = playheadProgress.Value;
        if (abs < _viewStart - 1e-9 || abs > ViewEnd + 1e-9)
        {
            return;
        }

        var x = AbsoluteToX(abs, content);
        DrawSeekPlaybackTrail(g, content, x, trailSamples, color);

        // ソフトグロー（細め）
        using (var glowOuter = new Pen(Color.FromArgb(40, color), 3f))
        {
            g.DrawLine(glowOuter, x, content.Top, x, content.Bottom);
        }

        using (var glowInner = new Pen(Color.FromArgb(90, color), 1.5f))
        {
            g.DrawLine(glowInner, x, content.Top, x, content.Bottom);
        }

        // コア線
        using var corePen = new Pen(color, 1f);
        g.DrawLine(corePen, x, content.Top, x, content.Bottom);
    }

    /// <summary>
    /// シーク軌跡。ピクセル距離ベースの線形グラデで描き、ズームで縦縞が出ないようにする。
    /// 長さはおおよそ <see cref="TrailTargetLengthPx"/>（サンプルで到達範囲を決める）。
    /// </summary>
    private void DrawSeekPlaybackTrail(
        Graphics g,
        Rectangle content,
        float playheadX,
        List<(double Progress, long TickMs)> trailSamples,
        Color color)
    {
        var now = Environment.TickCount64;
        PruneTrailSamplesByAge(now, trailSamples);
        if (trailSamples.Count < 2 || content.Width <= 0)
        {
            return;
        }

        var trailRightX = playheadX - TrailPlayheadGapPx;
        var trailLeftLimit = playheadX - TrailTargetLengthPx;
        if (trailRightX <= content.Left || trailRightX <= trailLeftLimit)
        {
            return;
        }

        var fadeMs = TrailFadeMsForView(content.Width);
        float? coveredLeft = null;
        foreach (var sample in trailSamples)
        {
            if (now - sample.TickMs >= fadeMs)
            {
                continue;
            }

            var x = AbsoluteToX(sample.Progress, content);
            if (x > trailRightX)
            {
                continue;
            }

            coveredLeft = coveredLeft is float left ? Math.Min(left, x) : x;
        }

        if (coveredLeft is null)
        {
            return;
        }

        var drawLeft = Math.Max(content.Left, Math.Max(trailLeftLimit, coveredLeft.Value));
        var drawRight = Math.Min(content.Right, trailRightX);
        var drawW = drawRight - drawLeft;
        if (drawW < 1f)
        {
            return;
        }

        // 再生ヘッド基準の距離グラデ（列ラスタの量子化縞を避ける）
        var gradLeft = playheadX - TrailTargetLengthPx;
        var gradRight = playheadX;
        if (gradRight - gradLeft < 1f)
        {
            return;
        }

        var peak = Color.FromArgb(ToByteAlpha(TrailPeakAlpha), color);
        var mid = Color.FromArgb(ToByteAlpha(TrailPeakAlpha * 0.25f), color);
        var clear = Color.FromArgb(0, color);
        using var brush = new System.Drawing.Drawing2D.LinearGradientBrush(
            new PointF(gradLeft, content.Top),
            new PointF(gradRight, content.Top),
            clear,
            peak)
        {
            InterpolationColors = new System.Drawing.Drawing2D.ColorBlend
            {
                Positions = [0f, 0.55f, 1f],
                Colors = [clear, mid, peak],
            },
        };

        var oldMode = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
        g.FillRectangle(brush, drawLeft, content.Top, drawW, content.Height);
        g.SmoothingMode = oldMode;
    }

    private void RecordTrailSample(
        double progress,
        List<(double Progress, long TickMs)> trailSamples,
        ref bool trailActive)
    {
        if (!trailActive)
        {
            return;
        }

        var now = Environment.TickCount64;
        var durationSec = _peaks?.DurationSeconds ?? 0;
        if (trailSamples.Count > 0)
        {
            var last = trailSamples[^1];
            var secDelta = ProgressToSec(Math.Abs(progress - last.Progress), durationSec);
            if (secDelta >= DiscontinuitySec(durationSec))
            {
                trailSamples.Clear();
            }
        }

        if (trailSamples.Count > 0)
        {
            var last = trailSamples[^1];
            var secDelta = ProgressToSec(Math.Abs(progress - last.Progress), durationSec);
            if (now - last.TickMs < TrailSampleMinIntervalMs && secDelta < TrailMinSecDelta)
            {
                return;
            }
        }

        trailSamples.Add((progress, now));
        PruneTrailSamplesByAge(now, trailSamples);
        if (trailSamples.Count > TrailMaxSamples)
        {
            trailSamples.RemoveRange(0, trailSamples.Count - TrailMaxSamples);
            PruneTrailSamplesByAge(now, trailSamples);
        }
    }

    private float contentWidthForTrail()
    {
        var timeline = GetTimelineContentRect();
        return Math.Max(0, timeline.Width);
    }

    private void PruneTrailSamplesByAge(long now, List<(double Progress, long TickMs)> trailSamples)
    {
        var retainMs = Math.Max(TrailSampleRetainMs, TrailFadeMsForView(contentWidthForTrail()));
        var remove = 0;
        while (remove < trailSamples.Count && now - trailSamples[remove].TickMs >= retainMs)
        {
            remove++;
        }

        if (remove > 0)
        {
            trailSamples.RemoveRange(0, remove);
        }
    }

    /// <summary>
    /// 表示窓で <see cref="TrailTargetLengthPx"/> 相当になるフェード時間（ms）。
    /// </summary>
    private double TrailFadeMsForView(float contentWidth)
    {
        var durationSec = _peaks?.DurationSeconds ?? 0;
        if (durationSec <= 0 || contentWidth <= 1f)
        {
            return TrailSampleRetainMs;
        }

        var viewDurationSec = durationSec * ViewSpan;
        var fadeSec = TrailTargetLengthPx / contentWidth * viewDurationSec;
        // 極端なズームでも帯が消え／伸びすぎないようクランプ（上限は長い曲の全体表示用）
        fadeSec = Math.Clamp(fadeSec, 0.2, 60.0);
        return fadeSec * 1000.0;
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
