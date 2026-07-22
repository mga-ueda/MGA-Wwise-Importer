using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Wave;

/// <summary>波形上に重ねる小節線／テンポ変更マーク 1 本。</summary>
/// <param name="SampleOffset">波形先頭基準のサンプル位置。</param>
/// <param name="BarNumber">
/// 波形先頭基準の相対小節番号（1 始まり）。
/// アウフタクト時は先頭半端小節が 1、直後の小節線が 2。
/// テンポ変更のみのマークでは 0。
/// </param>
/// <param name="Bpm">その位置のテンポ（ランプ中は補間値）。</param>
/// <param name="Numerator">その位置の拍子の分子。</param>
/// <param name="Denominator">その位置の拍子の分母。</param>
/// <param name="IsTempoChangeOnly">
/// true なら小節線ではなく、小節途中のテンポ変更位置。
/// テンポ行のラベル（と短いガイド線）のみ描画する。
/// </param>
internal readonly record struct WaveformBarMark(
    long SampleOffset,
    int BarNumber,
    double Bpm,
    int Numerator,
    int Denominator,
    bool IsTempoChangeOnly = false);

/// <summary>波形上の単発マーカーコメント 1 件。</summary>
/// <param name="SampleOffset">波形先頭基準のサンプル位置。</param>
/// <param name="Comment">マーカー名（コメント）。</param>
/// <param name="IsSharedProjection">グループの基準 Playlist から同期先へ投影された表示なら true。</param>
/// <param name="IsFromWaveEmbedded">WAV 埋め込みから読み取ったマーカーなら true（アプリ追加は false）。</param>
internal readonly record struct WaveformMarkerMark(
    long SampleOffset,
    string Comment,
    bool IsSharedProjection = false,
    bool IsFromWaveEmbedded = false);

/// <summary>波形上のサイクル（範囲）マーカー 1 件。</summary>
/// <param name="StartSampleOffset">範囲開始（波形先頭基準サンプル）。</param>
/// <param name="EndSampleOffset">範囲終了（波形先頭基準サンプル、排他的ではなく描画上の右端）。</param>
/// <param name="Comment">マーカー名（任意表示用）。</param>
internal readonly record struct WaveformCycleMark(
    long StartSampleOffset,
    long EndSampleOffset,
    string Comment);

/// <summary>
/// プレビュー上の分割リージョン（隣接のみ・入れ子なし）。出力分割の計画にも使う。
/// <paramref name="EndSampleOffset"/> は排他的終端（次リージョン開始と一致し得る）。
/// </summary>
/// <param name="StartSampleOffset">開始サンプル（波形先頭基準・含む）。</param>
/// <param name="EndSampleOffset">終了サンプル（波形先頭基準・含まない）。</param>
/// <param name="IsExcluded">
/// true なら -R 範囲内など、出力計画から除外する区画（番号なし・別色）。
/// </param>
/// <param name="NameSuffix">
/// リージョン名に添える接尾辞（例: <c>-L</c> / <c>-E</c> / <c>-A</c>）。空なら無し。
/// 除外（-R）は <see cref="IsExcluded"/> で表し、ここには含めない。
/// </param>
/// <param name="IsAutoNameSuffix">
/// true ならアプリが自動付与した接尾辞（例: アウフタクトの -A、 -L 直後の -E）。
/// 波形リージョン塗りなどに反映する。
/// </param>
internal readonly record struct WaveformRegionMark(
    long StartSampleOffset,
    long EndSampleOffset,
    bool IsExcluded = false,
    string NameSuffix = "",
    bool IsAutoNameSuffix = false);

/// <summary>
/// 連続する着色リージョンをまとめた、1 つの出力波形ファイル候補（表示用の計画ラベル）。
/// </summary>
/// <param name="Number">1 始まりの通し（ファイル名の _n）。</param>
/// <param name="StartSampleOffset">開始サンプル（含む）。仮想タイムライン座標。</param>
/// <param name="EndSampleOffset">終了サンプル（含まない）。仮想タイムライン座標。</param>
/// <param name="FileName">例: a_1.wav（ディレクトリ無し）。</param>
/// <param name="SourcePath">
/// 切り出し元 WAV。空ならプレビューの単一 <c>SourcePath</c> を使う（単体／XML 互換）。
/// </param>
/// <param name="LocalStartSample">
/// ソース WAV ローカル開始。負なら <see cref="StartSampleOffset"/> をローカルとみなす。
/// </param>
/// <param name="LocalEndSample">
/// ソース WAV ローカル終了（含まない）。負なら <see cref="EndSampleOffset"/> をローカルとみなす。
/// </param>
internal readonly record struct WaveformOutputPart(
    int Number,
    long StartSampleOffset,
    long EndSampleOffset,
    string FileName,
    string SourcePath = "",
    long LocalStartSample = -1,
    long LocalEndSample = -1)
{
    /// <summary>パート固有のソースとローカル範囲が付いているか。</summary>
    public bool HasDedicatedSource =>
        !string.IsNullOrEmpty(SourcePath)
        && LocalStartSample >= 0
        && LocalEndSample > LocalStartSample;

    public string ResolveSourcePath(string fallback) =>
        string.IsNullOrEmpty(SourcePath) ? fallback : SourcePath;

    public long ResolveLocalStart() => HasDedicatedSource ? LocalStartSample : StartSampleOffset;

    public long ResolveLocalEnd() => HasDedicatedSource ? LocalEndSample : EndSampleOffset;

    /// <summary>仮想タイムライン座標 → ソースローカル座標。</summary>
    public long VirtualToLocal(long virtualSample)
    {
        if (!HasDedicatedSource)
        {
            return virtualSample;
        }

        return virtualSample - (StartSampleOffset - LocalStartSample);
    }
}

/// <summary>Wwise に作られる予定の Music Segment 名と、ソース波形上の範囲。</summary>
/// <param name="Name">例: song_a / part_1_a。</param>
/// <param name="StartSampleOffset">開始サンプル（ソース波形ローカル）。</param>
/// <param name="EndSampleOffset">終了サンプル（含まない）。</param>
internal readonly record struct WaveformSegmentNameMark(
    string Name,
    long StartSampleOffset,
    long EndSampleOffset);

/// <summary>
/// 波形タイムライン範囲外のため描画・出力計画の対象外としたマーカー／サイクル。
/// </summary>
internal readonly record struct WaveformIgnoredOutsideMark(
    string Kind,
    string Name,
    double StartPpq,
    double EndPpq,
    string Reason);

/// <summary>小節オーバーレイ構築結果（ログ用の診断情報付き）。</summary>
internal sealed class WaveformBarOverlayResult
{
    public required IReadOnlyList<WaveformBarMark> Marks { get; init; }
    public required IReadOnlyList<WaveformMarkerMark> Markers { get; init; }
    public required IReadOnlyList<WaveformCycleMark> Cycles { get; init; }
    public required IReadOnlyList<WaveformRegionMark> Regions { get; init; }
    public required IReadOnlyList<WaveformOutputPart> OutputParts { get; init; }
    public required IReadOnlyList<WaveformIgnoredOutsideMark> IgnoredOutsideMarks { get; init; }
    public required bool HasIXml { get; init; }
    public required ulong TimeReferenceSamples { get; init; }
    public required double WaveStartPpq { get; init; }
    public required double WaveEndPpq { get; init; }
    public required double? PreviousBarPpqAtWaveStart { get; init; }
    public required bool HasAnacrusis { get; init; }
}

/// <summary>
/// Nuendo XML のテンポ／拍子と WAV の iXML TimeReference から、
/// 波形ローカル座標の小節線を構築する。
/// <para>
/// 小節番号はプロジェクト絶対番号ではなく、波形先頭を 1 小節目とする相対番号。
/// 小節番号にはマーカー名を使わない。接尾辞（例: -A）も付けない。
/// 小節途中のテンポ変更は別マークとしてテンポ値のみ載せる。
/// マーカー名は別レイヤ（拍子行の下）にコメントとして表示する。
/// サイクルマーカーはさらにその下の行に範囲として表示する。
/// </para>
/// </summary>
internal static class WaveformBarOverlayBuilder
{
    private const double PpqEpsilon = 1e-6;

    public static WaveformBarOverlayResult Build(
        NuendoTracklistInfo tracklist,
        WavFileInfo wavInfo)
    {
        var tempoMap = new TempoMap(tracklist.TempoEvents, tracklist.SignatureEvents);
        var sampleRate = wavInfo.SampleRate;
        var timelineOffset = (long)wavInfo.TimeReferenceSamples;
        var frameCount = wavInfo.FrameCount;
        if (frameCount <= 0 || sampleRate == 0)
        {
            return new WaveformBarOverlayResult
            {
                Marks = [],
                Markers = [],
                Cycles = [],
                Regions = [],
                OutputParts = [],
                IgnoredOutsideMarks = [],
                HasIXml = wavInfo.HasIXml,
                TimeReferenceSamples = wavInfo.TimeReferenceSamples,
                WaveStartPpq = 0,
                WaveEndPpq = 0,
                PreviousBarPpqAtWaveStart = null,
                HasAnacrusis = false,
            };
        }

        var waveStartPpq = tempoMap.FindPpqForSamples(timelineOffset, sampleRate);
        var waveEndPpq = tempoMap.FindPpqForSamples(timelineOffset + frameCount, sampleRate);
        var boundaries = BarGrid.GetBarBoundaries(tracklist.SignatureEvents, waveEndPpq);

        // 小節番号はマーカーを見ない。XML 小節グリッド + iXML TimeReference でアウフタクト判別。
        var barStartAtWave = BarGrid.FindPreviousBarPpq(boundaries, waveStartPpq);
        var hasAnacrusis = barStartAtWave is not null
            && waveStartPpq > barStartAtWave.Value + PpqEpsilon;

        var marks = new List<WaveformBarMark>();

        // 相対番号: 波形先頭を「曲の 1 小節目」にシフトする。
        // アウフタクト → 1、直後の小節線 → 2、以降連番。
        var nextBarNumber = 1;
        if (hasAnacrusis)
        {
            var sig = tempoMap.GetSignatureAt(waveStartPpq);
            marks.Add(new WaveformBarMark(
                0,
                BarNumber: 1,
                Bpm: tempoMap.GetBpmAt(waveStartPpq),
                Numerator: sig.Numerator,
                Denominator: sig.Denominator));
            nextBarNumber = 2;
        }

        foreach (var barPpq in boundaries)
        {
            if (barPpq < waveStartPpq - PpqEpsilon || barPpq > waveEndPpq + PpqEpsilon)
            {
                continue;
            }

            if (hasAnacrusis && Math.Abs(barPpq - waveStartPpq) <= PpqEpsilon)
            {
                continue;
            }

            var localSample = tempoMap.PpqToSampleIndex(barPpq, sampleRate) - timelineOffset;
            if (localSample < 0 || localSample > frameCount)
            {
                continue;
            }

            if (localSample == frameCount)
            {
                continue;
            }

            if (localSample == 0 && hasAnacrusis)
            {
                continue;
            }

            var signature = tempoMap.GetSignatureAt(barPpq);
            marks.Add(new WaveformBarMark(
                localSample,
                nextBarNumber,
                Bpm: tempoMap.GetBpmAt(barPpq),
                Numerator: signature.Numerator,
                Denominator: signature.Denominator));
            nextBarNumber++;
        }

        AddMidBarTempoMarks(
            marks,
            tracklist.TempoEvents,
            tempoMap,
            sampleRate,
            timelineOffset,
            frameCount,
            waveStartPpq,
            waveEndPpq,
            boundaries);

        marks.Sort((a, b) => a.SampleOffset.CompareTo(b.SampleOffset));

        var ignoredOutside = new List<WaveformIgnoredOutsideMark>();
        var markers = BuildMarkers(
            tracklist.MarkerEvents,
            tempoMap,
            sampleRate,
            timelineOffset,
            frameCount,
            waveStartPpq,
            waveEndPpq,
            ignoredOutside);

        var cycles = BuildCycles(
            tracklist.MarkerEvents,
            tempoMap,
            sampleRate,
            timelineOffset,
            frameCount,
            waveStartPpq,
            waveEndPpq,
            ignoredOutside);

        var regions = WaveformRegionBuilder.Build(
            tracklist,
            tempoMap,
            sampleRate,
            timelineOffset,
            frameCount,
            waveStartPpq,
            waveEndPpq,
            boundaries,
            cycles);

        var outputParts = WaveformRegionBuilder.BuildOutputParts(regions, wavInfo.Path);

        return new WaveformBarOverlayResult
        {
            Marks = marks,
            Markers = markers,
            Cycles = cycles,
            Regions = regions,
            OutputParts = outputParts,
            IgnoredOutsideMarks = ignoredOutside,
            HasIXml = wavInfo.HasIXml,
            TimeReferenceSamples = wavInfo.TimeReferenceSamples,
            WaveStartPpq = waveStartPpq,
            WaveEndPpq = waveEndPpq,
            PreviousBarPpqAtWaveStart = barStartAtWave,
            HasAnacrusis = hasAnacrusis,
        };
    }

    private static List<WaveformMarkerMark> BuildMarkers(
        IReadOnlyList<NuendoMarkerEvent> markerEvents,
        TempoMap tempoMap,
        uint sampleRate,
        long timelineOffset,
        long frameCount,
        double waveStartPpq,
        double waveEndPpq,
        List<WaveformIgnoredOutsideMark> ignoredOutside)
    {
        var markers = new List<WaveformMarkerMark>();
        foreach (var marker in markerEvents)
        {
            if (marker.Kind != NuendoMarkerKind.Marker)
            {
                continue;
            }

            var comment = marker.Name?.Trim() ?? string.Empty;
            var markerPpq = marker.StartPpq;
            if (markerPpq < waveStartPpq - PpqEpsilon || markerPpq > waveEndPpq + PpqEpsilon)
            {
                ignoredOutside.Add(new WaveformIgnoredOutsideMark(
                    "Marker",
                    comment.Length == 0 ? UiStrings.LabelUnnamedMarker : comment,
                    markerPpq,
                    markerPpq,
                    UiStrings.ReasonOutsideTimeline));
                continue;
            }

            var localSample = tempoMap.PpqToSampleIndex(markerPpq, sampleRate) - timelineOffset;
            if (localSample < 0 || localSample >= frameCount)
            {
                ignoredOutside.Add(new WaveformIgnoredOutsideMark(
                    "Marker",
                    comment.Length == 0 ? UiStrings.LabelUnnamedMarker : comment,
                    markerPpq,
                    markerPpq,
                    UiStrings.ReasonOutsideSamples));
                continue;
            }

            if (comment.Length == 0)
            {
                continue;
            }

            markers.Add(new WaveformMarkerMark(localSample, comment));
        }

        markers.Sort((a, b) => a.SampleOffset.CompareTo(b.SampleOffset));
        return markers;
    }

    private static List<WaveformCycleMark> BuildCycles(
        IReadOnlyList<NuendoMarkerEvent> markerEvents,
        TempoMap tempoMap,
        uint sampleRate,
        long timelineOffset,
        long frameCount,
        double waveStartPpq,
        double waveEndPpq,
        List<WaveformIgnoredOutsideMark> ignoredOutside)
    {
        var cycles = new List<WaveformCycleMark>();
        foreach (var marker in markerEvents)
        {
            if (marker.Kind != NuendoMarkerKind.CycleRegion)
            {
                continue;
            }

            var name = marker.Name?.Trim() ?? string.Empty;
            var startPpq = marker.StartPpq;
            var endPpq = marker.EndPpq;
            if (endPpq < startPpq)
            {
                (startPpq, endPpq) = (endPpq, startPpq);
            }

            // 波形範囲と完全に食い違うものは除外
            if (endPpq < waveStartPpq - PpqEpsilon || startPpq > waveEndPpq + PpqEpsilon)
            {
                ignoredOutside.Add(new WaveformIgnoredOutsideMark(
                    "Cycle",
                    name.Length == 0 ? UiStrings.LabelUnnamedMarker : name,
                    startPpq,
                    endPpq,
                    UiStrings.ReasonOutsideTimeline));
                continue;
            }

            var startSample = tempoMap.PpqToSampleIndex(startPpq, sampleRate) - timelineOffset;
            var endSample = tempoMap.PpqToSampleIndex(endPpq, sampleRate) - timelineOffset;
            startSample = Math.Clamp(startSample, 0, frameCount);
            endSample = Math.Clamp(endSample, 0, frameCount);
            if (endSample <= startSample)
            {
                ignoredOutside.Add(new WaveformIgnoredOutsideMark(
                    "Cycle",
                    name.Length == 0 ? UiStrings.LabelUnnamedMarker : name,
                    startPpq,
                    endPpq,
                    UiStrings.ReasonNoOverlap));
                continue;
            }

            cycles.Add(new WaveformCycleMark(
                startSample,
                endSample,
                name));
        }

        cycles.Sort((a, b) => a.StartSampleOffset.CompareTo(b.StartSampleOffset));
        return cycles;
    }

    /// <summary>
    /// 小節線と一致しないテンポイベントを、テンポ行専用マークとして追加する。
    /// </summary>
    private static void AddMidBarTempoMarks(
        List<WaveformBarMark> marks,
        IReadOnlyList<NuendoTempoEvent> tempoEvents,
        TempoMap tempoMap,
        uint sampleRate,
        long timelineOffset,
        long frameCount,
        double waveStartPpq,
        double waveEndPpq,
        IReadOnlyList<double> barBoundaries)
    {
        foreach (var tempoEvent in tempoEvents)
        {
            var tempoPpq = tempoEvent.Ppq;
            if (tempoPpq < waveStartPpq - PpqEpsilon || tempoPpq > waveEndPpq + PpqEpsilon)
            {
                continue;
            }

            // 小節線上のテンポは小節マーク側の BPM で既に出る
            if (BarGrid.IsNearAny(barBoundaries, tempoPpq) || Math.Abs(tempoPpq - waveStartPpq) <= PpqEpsilon)
            {
                continue;
            }

            var localSample = tempoMap.PpqToSampleIndex(tempoPpq, sampleRate) - timelineOffset;
            if (localSample < 0 || localSample >= frameCount)
            {
                continue;
            }

            if (marks.Exists(m => Math.Abs(m.SampleOffset - localSample) <= 1))
            {
                continue;
            }

            var signature = tempoMap.GetSignatureAt(tempoPpq);
            marks.Add(new WaveformBarMark(
                localSample,
                BarNumber: 0,
                Bpm: tempoEvent.Bpm,
                Numerator: signature.Numerator,
                Denominator: signature.Denominator,
                IsTempoChangeOnly: true));
        }
    }
}
