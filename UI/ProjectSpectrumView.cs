using MgaWwiseIMImporter.Wave;

namespace MgaWwiseIMImporter.UI;

/// <summary>
/// プロジェクトバー右端の小型スペクトラムアナライザ。
/// 再生中の出力（モノラルミックス）を FFT し、RME 風の 1/3 オクターブ帯域で表示する。
/// 停止・一時停止中はバーが減衰してゼロへ戻る。
/// </summary>
internal sealed class ProjectSpectrumView : Control
{
    private const int FftSize = 2048;
    private const int BarWidth = 2;
    private const int BarGap = 2;
    private const float FloorDb = -60f;
    private const float CeilingDb = 0f;
    private const double RiseSeconds = 0.001d;
    private const double FallSeconds = 0.7d;
    private const double BlurSigma = 0.45d;
    private const float PeakSoftKneeDb = -6f;
    private const double PeakSoftGamma = 1.24d;

    // MGA-Layer-Music-Checker と同じ RME 風 1/3 オクターブ中心周波数。
    private static readonly double[] BandCenters =
    [
        20d, 25d, 31.5d, 40d, 50d, 63d, 80d, 100d,
        125d, 160d, 200d, 250d, 315d, 400d, 500d, 630d,
        800d, 1000d, 1250d, 1600d, 2000d, 2500d, 3150d,
        4000d, 5000d, 6300d, 8000d, 10000d, 12500d, 16000d, 20000d,
    ];

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 33 };
    private readonly float[] _samples = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];
    private readonly double[] _bandPower = new double[BandCenters.Length];
    private readonly double[] _blurredPower = new double[BandCenters.Length];
    private readonly float[] _envelopeDb = new float[BandCenters.Length];
    private readonly float[] _levels = new float[BandCenters.Length];
    private readonly float _windowSum;
    private bool _idle = true;

    public WaveAudioPlayer? Player { get; set; }

    public ProjectSpectrumView()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;

        var windowSum = 0f;
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5f - 0.5f * (float)Math.Cos(2d * Math.PI * i / (FftSize - 1));
            windowSum += _window[i];
        }

        _windowSum = windowSum;
        Array.Fill(_envelopeDb, FloorDb);
        _timer.Tick += (_, _) => UpdateLevels();
        _timer.Start();
    }

    /// <summary>枠 1px ＋ 余白 1px。</summary>
    private const int EdgeInset = 2;

    private Rectangle InnerBounds =>
        Rectangle.Inflate(ClientRectangle, -EdgeInset, -EdgeInset);

    /// <summary>全バンドが丁度収まる幅（バー＋間隔＋両側余白）。</summary>
    public static int RequiredWidth =>
        BandCenters.Length * BarWidth
        + (BandCenters.Length - 1) * BarGap
        + EdgeInset * 2;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // DPI スケーリングで拡大されてもバー描画は固定 px のため、
        // グラフが丁度収まる幅へ自分でサイズを固定する。
        Width = RequiredWidth;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateLevels()
    {
        if (!Visible || !IsHandleCreated || IsDisposed)
        {
            return;
        }

        var player = Player;
        var active = player is { IsPlaying: true };
        if (active)
        {
            _ = player!.ReadRecentOutputSamples(_samples);
            ComputeBandTargets(player.OutputSampleRate);
            _idle = false;
        }
        else
        {
            // 参照実装と同じ約 0.7 秒の指数下降でフロアへ戻す。
            var anyVisible = false;
            var fall = 1d - Math.Exp(-_timer.Interval / 1000d / FallSeconds);
            for (var i = 0; i < _levels.Length; i++)
            {
                _envelopeDb[i] += (FloorDb - _envelopeDb[i]) * (float)fall;
                _levels[i] = DbToLevel(_envelopeDb[i]);
                if (_levels[i] > 0.004f)
                {
                    anyVisible = true;
                }
                else
                {
                    _levels[i] = 0f;
                }
            }

            if (!anyVisible)
            {
                if (_idle)
                {
                    return;
                }

                _idle = true;
            }
        }

        Invalidate();
    }

    private void ComputeBandTargets(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            sampleRate = 48000;
        }

        for (var i = 0; i < FftSize; i++)
        {
            _re[i] = _samples[i] * _window[i];
            _im[i] = 0d;
        }

        Fft(_re, _im);

        Array.Clear(_bandPower);
        var binHz = sampleRate / (double)FftSize;
        var nyquist = sampleRate / 2d;

        // 各 FFT ビンの線形パワーを、帯域との周波数重なり率に応じて積算する。
        // 単純な「帯域内の最大ビン」ではないため、狭い低域だけが過大に見えにくい。
        for (var bin = 1; bin < FftSize / 2; bin++)
        {
            var binLow = bin * binHz;
            var binHigh = Math.Min(nyquist, (bin + 1) * binHz);
            var magnitude = 2d
                * Math.Sqrt(_re[bin] * _re[bin] + _im[bin] * _im[bin])
                / _windowSum;
            var power = magnitude * magnitude;
            for (var band = 0; band < BandCenters.Length; band++)
            {
                GetBandEdges(band, nyquist, out var bandLow, out var bandHigh);
                var overlap = Math.Min(binHigh, bandHigh) - Math.Max(binLow, bandLow);
                if (overlap > 0d)
                {
                    _bandPower[band] += power * overlap / binHz;
                }
            }
        }

        BlurBandPower();

        var dt = _timer.Interval / 1000d;
        var rise = 1d - Math.Exp(-dt / RiseSeconds);
        var fall = 1d - Math.Exp(-dt / FallSeconds);
        for (var band = 0; band < BandCenters.Length; band++)
        {
            var rawDb = _blurredPower[band] > 1e-18
                ? (float)(10d * Math.Log10(_blurredPower[band]))
                : FloorDb;
            var targetDb = SoftenDisplayPeak(Math.Clamp(rawDb, FloorDb, CeilingDb));
            var coefficient = targetDb >= _envelopeDb[band] ? rise : fall;
            _envelopeDb[band] +=
                (targetDb - _envelopeDb[band]) * (float)coefficient;
            _levels[band] = DbToLevel(_envelopeDb[band]);
        }
    }

    private static void GetBandEdges(
        int band,
        double nyquist,
        out double low,
        out double high)
    {
        low = band == 0
            ? BandCenters[0]
            : Math.Sqrt(BandCenters[band - 1] * BandCenters[band]);
        high = band == BandCenters.Length - 1
            ? Math.Min(nyquist * 0.995d, BandCenters[^1] * Math.Pow(2d, 1d / 6d))
            : Math.Sqrt(BandCenters[band] * BandCenters[band + 1]);
        high = Math.Min(high, nyquist * 0.995d);
    }

    /// <summary>参照実装と同じ、線形パワー領域での狭いガウス平滑化。</summary>
    private void BlurBandPower()
    {
        var radius = (int)Math.Ceiling(BlurSigma * 4d);
        for (var band = 0; band < BandCenters.Length; band++)
        {
            var weighted = 0d;
            var weightSum = 0d;
            for (var offset = -radius; offset <= radius; offset++)
            {
                var source = band + offset;
                if (source < 0 || source >= BandCenters.Length)
                {
                    continue;
                }

                var weight = Math.Exp(
                    -(offset * offset) / (2d * BlurSigma * BlurSigma));
                weighted += _bandPower[source] * weight;
                weightSum += weight;
            }

            _blurredPower[band] = weightSum > 0d ? weighted / weightSum : 0d;
        }
    }

    private static float SoftenDisplayPeak(float db)
    {
        if (db <= PeakSoftKneeDb)
        {
            return db;
        }

        var span = CeilingDb - PeakSoftKneeDb;
        var normalized = (db - PeakSoftKneeDb) / span;
        return PeakSoftKneeDb
            + span * (float)Math.Pow(normalized, PeakSoftGamma);
    }

    private static float DbToLevel(float db) =>
        Math.Clamp((db - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);

    /// <summary>基数2の反復 FFT（実部・虚部を上書き）。</summary>
    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            var bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
            {
                j &= ~bit;
            }

            j |= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2d * Math.PI / length;
            var wRe = Math.Cos(angle);
            var wIm = Math.Sin(angle);
            for (var start = 0; start < n; start += length)
            {
                var curRe = 1d;
                var curIm = 0d;
                for (var k = 0; k < length / 2; k++)
                {
                    var evenIndex = start + k;
                    var oddIndex = start + k + length / 2;
                    var oddRe = re[oddIndex] * curRe - im[oddIndex] * curIm;
                    var oddIm = re[oddIndex] * curIm + im[oddIndex] * curRe;
                    re[oddIndex] = re[evenIndex] - oddRe;
                    im[oddIndex] = im[evenIndex] - oddIm;
                    re[evenIndex] += oddRe;
                    im[evenIndex] += oddIm;
                    var nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var backBrush = new SolidBrush(
            UiColors.ForControlBack(UiColors.SpectrumBack)))
        {
            g.FillRectangle(backBrush, ClientRectangle);
        }

        var inner = InnerBounds;
        if (inner.Width > 0 && inner.Height > 0)
        {
            using var baseBrush = new SolidBrush(UiColors.SpectrumBaseline);
            using var barBrush = new SolidBrush(UiColors.SpectrumBar);
            var bandCount = Math.Min(
                _levels.Length,
                (inner.Width + BarGap) / (BarWidth + BarGap));
            var graphWidth = bandCount * BarWidth
                + Math.Max(0, bandCount - 1) * BarGap;
            var graphLeft = inner.Left + Math.Max(0, (inner.Width - graphWidth) / 2);
            for (var band = 0; band < bandCount; band++)
            {
                // 2px バー＋1px 間隔で固定し、全バンドを中央配置する。
                var x = graphLeft + band * (BarWidth + BarGap);
                // ゼロレベルでも 1px のベースラインを描いて存在を示す。
                g.FillRectangle(
                    baseBrush,
                    x,
                    inner.Bottom - 1,
                    BarWidth,
                    1);
                var barHeight = (int)Math.Round(_levels[band] * inner.Height);
                if (barHeight > 0)
                {
                    g.FillRectangle(
                        barBrush,
                        x,
                        inner.Bottom - barHeight,
                        BarWidth,
                        barHeight);
                }
            }
        }

        using var borderPen = new Pen(UiColors.SpectrumBorder);
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
    }
}
