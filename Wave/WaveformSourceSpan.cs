namespace MgaWwiseIMImporter.Wave;

/// <summary>
/// 複数波形モードの仮想タイムライン上で、1 本のソース WAV が占める区間。
/// </summary>
/// <param name="Path">ソース WAV フルパス。</param>
/// <param name="WavInfo">当該ファイルのフォーマット情報。</param>
/// <param name="VirtualStartSample">仮想タイムライン上の開始（含む）。</param>
/// <param name="FrameCount">このファイルのフレーム数（長さ）。</param>
internal readonly record struct WaveformSourceSpan(
    string Path,
    WavFileInfo WavInfo,
    long VirtualStartSample,
    long FrameCount)
{
    public long VirtualEndSample => VirtualStartSample + FrameCount;
}
