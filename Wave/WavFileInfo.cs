using System.Text;

namespace MgaWwiseImporter.Wave;

internal sealed class WavFileInfo
{
    public required string Path { get; init; }
    public long FileSizeBytes { get; init; }
    public ushort AudioFormat { get; init; }
    public ushort Channels { get; init; }
    public uint SampleRate { get; init; }
    public uint ByteRate { get; init; }
    public ushort BlockAlign { get; init; }
    public ushort BitsPerSample { get; init; }
    public uint DataSizeBytes { get; init; }
    public bool HasBroadcastExtension { get; init; }
    public ulong TimeReferenceSamples { get; init; }

    public string AudioFormatName => AudioFormat switch
    {
        1 => "PCM",
        3 => "IEEE Float",
        6 => "A-law",
        7 => "μ-law",
        65534 => "Extensible",
        _ => $"Unknown ({AudioFormat})",
    };

    public long FrameCount => BlockAlign == 0 ? 0 : DataSizeBytes / BlockAlign;

    public TimeSpan Duration => SampleRate == 0
        ? TimeSpan.Zero
        : TimeSpan.FromSeconds(FrameCount / (double)SampleRate);

    public static WavFileInfo Read(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        var riff = ReadFourCc(reader);
        if (riff != "RIFF")
        {
            throw new InvalidDataException("RIFF ヘッダーではありません。");
        }

        _ = reader.ReadUInt32();
        var wave = ReadFourCc(reader);
        if (wave != "WAVE")
        {
            throw new InvalidDataException("WAVE 形式ではありません。");
        }

        ushort? audioFormat = null;
        ushort channels = 0;
        uint sampleRate = 0;
        uint byteRate = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        uint dataSize = 0;
        var hasBext = false;
        ulong timeReferenceSamples = 0;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = ReadFourCc(reader);
            var chunkSize = reader.ReadUInt32();
            var chunkDataStart = stream.Position;

            if (chunkId == "fmt ")
            {
                if (chunkSize < 16)
                {
                    throw new InvalidDataException("fmt チャンクが不正です。");
                }

                audioFormat = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadUInt32();
                byteRate = reader.ReadUInt32();
                blockAlign = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
            }
            else if (chunkId == "data")
            {
                dataSize = chunkSize;
                break;
            }
            else if (chunkId == "bext")
            {
                hasBext = true;
                // Description(256) + Originator(32) + OriginatorReference(32)
                // + OriginationDate(10) + OriginationTime(8) + TimeReference(8)
                if (chunkSize >= 346)
                {
                    stream.Position = chunkDataStart + 338;
                    var low = reader.ReadUInt32();
                    var high = reader.ReadUInt32();
                    timeReferenceSamples = low + ((ulong)high << 32);
                }
            }

            var paddedSize = chunkSize + (chunkSize & 1);
            stream.Position = chunkDataStart + paddedSize;
        }

        if (audioFormat is null)
        {
            throw new InvalidDataException("fmt チャンクが見つかりません。");
        }

        return new WavFileInfo
        {
            Path = path,
            FileSizeBytes = new FileInfo(path).Length,
            AudioFormat = audioFormat.Value,
            Channels = channels,
            SampleRate = sampleRate,
            ByteRate = byteRate,
            BlockAlign = blockAlign,
            BitsPerSample = bitsPerSample,
            DataSizeBytes = dataSize,
            HasBroadcastExtension = hasBext,
            TimeReferenceSamples = timeReferenceSamples,
        };
    }

    public string ToDisplayText()
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Wave ===");
        sb.AppendLine($"Path           : {Path}");
        sb.AppendLine($"File Size      : {FileSizeBytes:N0} bytes");
        sb.AppendLine($"Format         : {AudioFormatName} ({AudioFormat})");
        sb.AppendLine($"Channels       : {Channels}");
        sb.AppendLine($"Sample Rate    : {SampleRate} Hz");
        sb.AppendLine($"Bit Depth      : {BitsPerSample} bit");
        sb.AppendLine($"Block Align    : {BlockAlign} bytes");
        sb.AppendLine($"Byte Rate      : {ByteRate:N0} bytes/sec");
        sb.AppendLine($"Data Size      : {DataSizeBytes:N0} bytes");
        sb.AppendLine($"Frames         : {FrameCount:N0}");
        sb.AppendLine($"Duration       : {FormatDuration(Duration)}");
        sb.AppendLine($"Broadcast Ext  : {(HasBroadcastExtension ? "Yes (bext)" : "No")}");
        sb.AppendLine($"Time Reference : {TimeReferenceSamples:N0} samples");
        return sb.ToString();
    }

    private static string FormatDuration(TimeSpan duration)
    {
        return $"{(int)duration.TotalMinutes:00}:{duration.Seconds:00}.{duration.Milliseconds:000}"
            + $" ({duration.TotalSeconds:0.000} sec)";
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }
}
