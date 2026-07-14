namespace MgaWwiseImporter.Wave;

internal sealed class WavCueItem
{
    public required uint Id { get; init; }
    public required long SampleOffset { get; init; }
    public required long SampleLength { get; init; }
    public required string Name { get; init; }
    public required string Comment { get; init; }
    public required bool IsRegion { get; init; }
}
