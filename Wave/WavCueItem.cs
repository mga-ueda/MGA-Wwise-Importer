namespace MgaWwiseImporter.Wave;

/// <summary>書き出し WAV に埋め込む cue／リージョン／マーカー 1 件。</summary>
internal sealed class WavCueItem
{
    public required uint Id { get; init; }
    public required long SampleOffset { get; init; }
    /// <summary>リージョンの長さ。単発マーカーは 0。</summary>
    public required long SampleLength { get; init; }
    /// <summary>labl（およびリージョン時は ltxt）に出す表示名。</summary>
    public required string Comment { get; init; }
    /// <summary>true=範囲リージョン（ltxt+rgn）、false=単発マーカー（note）。</summary>
    public required bool IsRegion { get; init; }
}
