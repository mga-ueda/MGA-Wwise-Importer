namespace MgaWwiseIMImporter.Wave;

/// <summary>リージョン端フェードのカーブ形状（Wwise MusicClip FadeIn/OutShape 相当）。</summary>
internal enum RegionFadeCurveKind
{
    LogarithmicBase3,
    SineConstantPowerFadeIn,
    LogarithmicBase141,
    InvertedSCurve,
    Linear,
    SCurve,
    ExponentialBase141,
    SineConstantPowerFadeOut,
    ExponentialBase3,
}

/// <summary>
/// 連続リージョン固まり（非除外ラン）のイン／アウト端フェード。
/// ソース WAV は非破壊。プレビュー表示・再生に適用し、EXPORT 時は分割 WAV へ焼き込む。
/// </summary>
internal readonly record struct RegionEdgeFade(
    long InSample,
    long OutSample,
    long? FadeInEndSample,
    long? FadeOutStartSample,
    RegionFadeCurveKind FadeInCurve = RegionFadeCurveKind.SCurve,
    RegionFadeCurveKind FadeOutCurve = RegionFadeCurveKind.SCurve)
{
    /// <summary>Wwise CurveIn ドロップダウン順（上→下）。</summary>
    public static IReadOnlyList<RegionFadeCurveKind> MenuOrderFadeIn { get; } =
    [
        RegionFadeCurveKind.LogarithmicBase3,
        RegionFadeCurveKind.SineConstantPowerFadeIn,
        RegionFadeCurveKind.LogarithmicBase141,
        RegionFadeCurveKind.InvertedSCurve,
        RegionFadeCurveKind.Linear,
        RegionFadeCurveKind.SCurve,
        RegionFadeCurveKind.ExponentialBase141,
        RegionFadeCurveKind.SineConstantPowerFadeOut,
        RegionFadeCurveKind.ExponentialBase3,
    ];

    /// <summary>
    /// Wwise CurveOut ドロップダウン順（上→下）。
    /// 山なり（Exp3）→ 谷なり（Log3）。中央の InvS / Linear / S は CurveIn と同じ相対順。
    /// </summary>
    public static IReadOnlyList<RegionFadeCurveKind> MenuOrderFadeOut { get; } =
    [
        RegionFadeCurveKind.ExponentialBase3,
        RegionFadeCurveKind.SineConstantPowerFadeOut,
        RegionFadeCurveKind.ExponentialBase141,
        RegionFadeCurveKind.InvertedSCurve,
        RegionFadeCurveKind.Linear,
        RegionFadeCurveKind.SCurve,
        RegionFadeCurveKind.LogarithmicBase141,
        RegionFadeCurveKind.SineConstantPowerFadeIn,
        RegionFadeCurveKind.LogarithmicBase3,
    ];

    public long EffectiveFadeInEnd =>
        FadeInEndSample is { } end && end > InSample ? end : InSample;

    public long EffectiveFadeOutStart =>
        FadeOutStartSample is { } start && start < OutSample ? start : OutSample;

    public bool HasFadeIn => EffectiveFadeInEnd > InSample;

    public bool HasFadeOut => EffectiveFadeOutStart < OutSample;

    public bool HasAnyFade => HasFadeIn || HasFadeOut;

    /// <summary>食い込みを解消した正規化済みフェードを返す。</summary>
    public RegionEdgeFade Normalized()
    {
        if (OutSample <= InSample)
        {
            return new RegionEdgeFade(InSample, InSample, null, null, FadeInCurve, FadeOutCurve);
        }

        var fadeInEnd = EffectiveFadeInEnd;
        var fadeOutStart = EffectiveFadeOutStart;
        if (fadeInEnd > fadeOutStart)
        {
            var mid = InSample + (OutSample - InSample) / 2;
            fadeInEnd = Math.Min(fadeInEnd, mid);
            fadeOutStart = Math.Max(fadeOutStart, mid);
        }

        fadeInEnd = Math.Clamp(fadeInEnd, InSample, OutSample);
        fadeOutStart = Math.Clamp(fadeOutStart, InSample, OutSample);
        if (fadeInEnd > fadeOutStart)
        {
            fadeInEnd = fadeOutStart;
        }

        return new RegionEdgeFade(
            InSample,
            OutSample,
            fadeInEnd > InSample ? fadeInEnd : null,
            fadeOutStart < OutSample ? fadeOutStart : null,
            FadeInCurve,
            FadeOutCurve);
    }

    public RegionEdgeFade WithCurves(RegionFadeCurveKind fadeInCurve, RegionFadeCurveKind fadeOutCurve) =>
        new(InSample, OutSample, FadeInEndSample, FadeOutStartSample, fadeInCurve, fadeOutCurve);

    /// <summary>
    /// [startSample, endSample) がいずれかのフェード区間と重なるか。
    /// </summary>
    public static bool OverlapsRange(
        long startSample,
        long endSample,
        IReadOnlyList<RegionEdgeFade> fades)
    {
        if (endSample <= startSample || fades.Count == 0)
        {
            return false;
        }

        foreach (var fade in fades)
        {
            if (!fade.HasAnyFade)
            {
                continue;
            }

            if (fade.HasFadeIn
                && startSample < fade.EffectiveFadeInEnd
                && endSample > fade.InSample)
            {
                return true;
            }

            if (fade.HasFadeOut
                && startSample < fade.OutSample
                && endSample > fade.EffectiveFadeOutStart)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// カーブ形状に応じたゲイン。範囲外は 1。
    /// </summary>
    public float GainAt(long sample)
    {
        if (sample < InSample || sample >= OutSample)
        {
            return 1f;
        }

        var gain = 1f;
        var fadeInEnd = EffectiveFadeInEnd;
        if (fadeInEnd > InSample && sample < fadeInEnd)
        {
            var t = (sample - InSample) / (double)(fadeInEnd - InSample);
            gain *= EvaluateRising(FadeInCurve, t);
        }

        var fadeOutStart = EffectiveFadeOutStart;
        if (fadeOutStart < OutSample && sample >= fadeOutStart)
        {
            var t = (sample - fadeOutStart) / (double)(OutSample - fadeOutStart);
            // フェードアウト: 立ち上がり式を 1-f(t) で 1→0（Wwise CurveOut アイコンと同じ形）
            gain *= EvaluateFalling(FadeOutCurve, t);
        }

        return gain;
    }

    /// <summary>
    /// t∈[0,1] の立ち下がり（フェードアウト／CurveOut アイコン）。1 - EvaluateRising。
    /// </summary>
    public static float EvaluateFalling(RegionFadeCurveKind kind, double t) =>
        1f - EvaluateRising(kind, t);

    /// <summary>
    /// t∈[0,1] の立ち上がりカーブ（メニューアイコン左下→右上／イン側）。
    /// Wwise Authoring の補間に合わせる（いわゆる Base N は対数ではなく冪 N）。
    /// </summary>
    public static float EvaluateRising(RegionFadeCurveKind kind, double t)
    {
        t = Math.Clamp(t, 0d, 1d);
        return kind switch
        {
            // Logarithmic (Base 3) = 1-(1-t)^3 … 最も急な立ち上がり
            RegionFadeCurveKind.LogarithmicBase3 => (float)(1d - Math.Pow(1d - t, 3d)),
            // Sine (Constant Power Fade In)
            RegionFadeCurveKind.SineConstantPowerFadeIn => SinRising(t),
            // Logarithmic (Base 1.41) = 1-(1-t)^1.41
            RegionFadeCurveKind.LogarithmicBase141 => (float)(1d - Math.Pow(1d - t, 1.41d)),
            RegionFadeCurveKind.InvertedSCurve => InvertedSCurve(t),
            RegionFadeCurveKind.Linear => (float)t,
            RegionFadeCurveKind.SCurve => SCurve(t),
            // Exponential (Base 1.41) = t^1.41
            RegionFadeCurveKind.ExponentialBase141 => (float)Math.Pow(t, 1.41d),
            // Sine (Constant Power Fade Out) = Reciprocal Sine
            RegionFadeCurveKind.SineConstantPowerFadeOut => (float)(1d - Math.Cos(t * (Math.PI * 0.5))),
            // Exponential (Base 3) = t^3 … 最も遅い立ち上がり
            RegionFadeCurveKind.ExponentialBase3 => (float)Math.Pow(t, 3d),
            _ => SinRising(t),
        };
    }

    /// <summary>Constant Power Fade In（凸の弧）。</summary>
    private static float SinRising(double t) =>
        (float)Math.Sin(t * (Math.PI * 0.5));

    /// <summary>
    /// S-Curve: Hermite smoothstep（端で傾き 0＝水平に出入りする典型的な S）。
    /// </summary>
    private static float SCurve(double t) =>
        (float)(t * t * (3d - 2d * t));

    /// <summary>
    /// Inverted S-Curve: 両端が急・中央がゆるい（smoothstep とは逆の曲率）。
    /// </summary>
    private static float InvertedSCurve(double t) =>
        (float)(t * (2d - 3d * t + 2d * t * t));

    /// <summary>複数フェードがあるとき、sample が属する最初の固まりのゲイン。</summary>
    public static float GainAt(long sample, IReadOnlyList<RegionEdgeFade> fades)
    {
        foreach (var fade in fades)
        {
            if (sample >= fade.InSample && sample < fade.OutSample)
            {
                return fade.GainAt(sample);
            }
        }

        return 1f;
    }

    /// <summary>除外で区切られた連続リージョン固まりの (In, Out) 一覧。</summary>
    public static IReadOnlyList<(long InSample, long OutSample)> CollectRunBounds(
        IReadOnlyList<WaveformRegionMark> regions)
    {
        var bounds = new List<(long, long)>();
        long? runIn = null;
        long runOut = 0;
        foreach (var region in regions)
        {
            if (region.IsExcluded)
            {
                if (runIn is { } inSample && runOut > inSample)
                {
                    bounds.Add((inSample, runOut));
                }

                runIn = null;
                continue;
            }

            if (runIn is null)
            {
                runIn = region.StartSampleOffset;
            }

            runOut = region.EndSampleOffset;
        }

        if (runIn is { } lastIn && runOut > lastIn)
        {
            bounds.Add((lastIn, runOut));
        }

        return bounds;
    }

    /// <summary>
    /// 現在の固まり境界に合わせてフェードを再マップする。
    /// In/Out が一致するものだけ残し、それ以外は破棄。
    /// </summary>
    public static IReadOnlyList<RegionEdgeFade> RemapToRuns(
        IReadOnlyList<RegionEdgeFade> existing,
        IReadOnlyList<WaveformRegionMark> regions)
    {
        if (existing.Count == 0)
        {
            return [];
        }

        var runs = CollectRunBounds(regions);
        if (runs.Count == 0)
        {
            return [];
        }

        var runSet = runs.ToHashSet();
        var kept = new List<RegionEdgeFade>();
        foreach (var fade in existing)
        {
            if (!runSet.Contains((fade.InSample, fade.OutSample)))
            {
                continue;
            }

            var normalized = fade.Normalized();
            if (normalized.HasAnyFade)
            {
                kept.Add(normalized);
            }
        }

        return kept;
    }

    public static RegionEdgeFade WithFadeInEnd(
        long inSample,
        long outSample,
        long fadeInEnd,
        long? fadeOutStart,
        RegionFadeCurveKind fadeInCurve = RegionFadeCurveKind.SCurve,
        RegionFadeCurveKind fadeOutCurve = RegionFadeCurveKind.SCurve)
    {
        return new RegionEdgeFade(
            inSample,
            outSample,
            fadeInEnd,
            fadeOutStart,
            fadeInCurve,
            fadeOutCurve).Normalized();
    }

    public static RegionEdgeFade WithFadeOutStart(
        long inSample,
        long outSample,
        long? fadeInEnd,
        long fadeOutStart,
        RegionFadeCurveKind fadeInCurve = RegionFadeCurveKind.SCurve,
        RegionFadeCurveKind fadeOutCurve = RegionFadeCurveKind.SCurve)
    {
        return new RegionEdgeFade(
            inSample,
            outSample,
            fadeInEnd,
            fadeOutStart,
            fadeInCurve,
            fadeOutCurve).Normalized();
    }
}

/// <summary>リージョン端フェードの Undo / Redo スナップショット。</summary>
internal sealed class RegionEdgeFadeHistory
{
    private readonly Stack<IReadOnlyList<RegionEdgeFade>> _undo = new();
    private readonly Stack<IReadOnlyList<RegionEdgeFade>> _redo = new();

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    public void PushBeforeChange(IReadOnlyList<RegionEdgeFade> before)
    {
        _undo.Push(Clone(before));
        _redo.Clear();
    }

    public bool TryUndo(
        IReadOnlyList<RegionEdgeFade> current,
        out IReadOnlyList<RegionEdgeFade> restored)
    {
        if (_undo.Count == 0)
        {
            restored = [];
            return false;
        }

        _redo.Push(Clone(current));
        restored = _undo.Pop();
        return true;
    }

    public bool TryRedo(
        IReadOnlyList<RegionEdgeFade> current,
        out IReadOnlyList<RegionEdgeFade> restored)
    {
        if (_redo.Count == 0)
        {
            restored = [];
            return false;
        }

        _undo.Push(Clone(current));
        restored = _redo.Pop();
        return true;
    }

    private static IReadOnlyList<RegionEdgeFade> Clone(IReadOnlyList<RegionEdgeFade> source) =>
        source.ToArray();
}
