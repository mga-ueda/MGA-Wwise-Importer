namespace MgaWwiseImporter.Wave;

internal sealed class WaveEmbedBuildResult
{
    public required IReadOnlyList<WavCueItem> Cues { get; init; }
    public required IReadOnlyList<string> Warnings { get; init; }
}
