namespace MgaWwiseIMImporter.Wave;

internal sealed class WaveformPreviewData
{
    public WaveformPreviewData(
        WavPeakData peaks,
        string sourcePath,
        WavFileInfo wavInfo,
        IReadOnlyList<WaveformBarMark>? bars = null,
        IReadOnlyList<WaveformMarkerMark>? markers = null,
        IReadOnlyList<WaveformCycleMark>? cycles = null,
        IReadOnlyList<WaveformRegionMark>? regions = null,
        IReadOnlyList<WaveformOutputPart>? outputParts = null,
        bool allowsSessionMarkerEdit = false)
    {
        Peaks = peaks;
        SourcePath = sourcePath;
        WavInfo = wavInfo;
        Bars = bars ?? [];
        Markers = markers ?? [];
        Cycles = cycles ?? [];
        Regions = regions ?? [];
        OutputParts = outputParts ?? [];
        AllowsSessionMarkerEdit = allowsSessionMarkerEdit;
    }

    public WavPeakData Peaks { get; }
    public string SourcePath { get; }
    public WavFileInfo WavInfo { get; }
    public IReadOnlyList<WaveformBarMark> Bars { get; }
    public IReadOnlyList<WaveformMarkerMark> Markers { get; }
    public IReadOnlyList<WaveformCycleMark> Cycles { get; }
    public IReadOnlyList<WaveformRegionMark> Regions { get; }
    public IReadOnlyList<WaveformOutputPart> OutputParts { get; }

    /// <summary>
    /// Wave 単体（マーカーのみ／無し、または smpl ループ）で、アプリ上のマーカー編集（コメント／削除）を許す。
    /// WAV ファイル自体は書き換えない。
    /// </summary>
    public bool AllowsSessionMarkerEdit { get; }
}
