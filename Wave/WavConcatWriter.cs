using System.Text;
using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Wave;

/// <summary>
/// 複数 WAV を再生用に一時ファイルへ順連結する（プレビュー再生専用。Export の切り出し元には使わない）。
/// </summary>
internal static class WavConcatWriter
{
    public static string WriteTempConcat(IReadOnlyList<WaveformSourceSpan> spans)
    {
        if (spans.Count == 0)
        {
            throw new ArgumentException(UiStrings.ErrMultiWaveOnlyNoSpans);
        }

        var template = spans[0].WavInfo;
        var totalFrames = spans.Sum(span => span.FrameCount);
        var totalBytes = checked(totalFrames * template.BlockAlign);
        if (totalBytes > int.MaxValue)
        {
            throw new InvalidDataException(UiStrings.ErrMultiWaveOnlyTooLong);
        }

        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"MgaWwiseIMImporter-multi-{Guid.NewGuid():N}.wav");

        try
        {
            WriteConcat(spans, template, (int)totalBytes, tempPath);
            return tempPath;
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void WriteConcat(
        IReadOnlyList<WaveformSourceSpan> spans,
        WavFileInfo template,
        int totalDataBytes,
        string destinationPath)
    {
        using var dest = File.Create(destinationPath);
        using var writer = new BinaryWriter(dest, Encoding.ASCII, leaveOpen: true);

        byte[]? fmtData = null;
        using (var probe = File.OpenRead(spans[0].Path))
        using (var reader = new BinaryReader(probe, Encoding.ASCII, leaveOpen: true))
        {
            if (ReadFourCc(reader) != "RIFF")
            {
                throw new InvalidDataException(UiStrings.ErrNotRiffHeader);
            }

            _ = reader.ReadUInt32();
            if (ReadFourCc(reader) != "WAVE")
            {
                throw new InvalidDataException(UiStrings.ErrNotWaveFormat);
            }

            while (probe.Position + 8 <= probe.Length)
            {
                var id = ReadFourCc(reader);
                var size = reader.ReadUInt32();
                var chunkDataStart = probe.Position;
                if (chunkDataStart + size > probe.Length)
                {
                    break;
                }

                if (id == "fmt ")
                {
                    fmtData = reader.ReadBytes((int)size);
                    break;
                }

                var paddedSize = size + (size & 1);
                probe.Position = chunkDataStart + paddedSize;
            }
        }

        if (fmtData is null)
        {
            throw new InvalidDataException(UiStrings.ErrFmtChunkMissing);
        }

        var fmtPadded = fmtData.Length + (fmtData.Length & 1);
        var dataPadded = totalDataBytes + (totalDataBytes & 1);
        var riffSize = 4 + (8 + fmtPadded) + (8 + dataPadded);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(riffSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(fmtData.Length);
        writer.Write(fmtData);
        if ((fmtData.Length & 1) != 0)
        {
            writer.Write((byte)0);
        }

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(totalDataBytes);

        var buffer = new byte[Math.Min(1024 * 1024, Math.Max((int)template.BlockAlign, 4096))];
        foreach (var span in spans)
        {
            CopyDataChunk(span.Path, span.FrameCount * template.BlockAlign, buffer, dest);
        }

        if ((totalDataBytes & 1) != 0)
        {
            writer.Write((byte)0);
        }
    }

    private static void CopyDataChunk(
        string sourcePath,
        long byteCount,
        byte[] buffer,
        Stream dest)
    {
        using var source = File.OpenRead(sourcePath);
        using var reader = new BinaryReader(source, Encoding.ASCII, leaveOpen: true);

        if (ReadFourCc(reader) != "RIFF")
        {
            throw new InvalidDataException(UiStrings.ErrNotRiffHeader);
        }

        _ = reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE")
        {
            throw new InvalidDataException(UiStrings.ErrNotWaveFormat);
        }

        long dataStart = -1;
        uint dataSize = 0;
        while (source.Position + 8 <= source.Length)
        {
            var id = ReadFourCc(reader);
            var size = reader.ReadUInt32();
            var chunkDataStart = source.Position;
            if (chunkDataStart + size > source.Length)
            {
                break;
            }

            if (id == "data")
            {
                dataStart = chunkDataStart;
                dataSize = size;
                break;
            }

            var paddedSize = size + (size & 1);
            source.Position = chunkDataStart + paddedSize;
        }

        if (dataStart < 0)
        {
            throw new InvalidDataException(UiStrings.ErrDataChunkMissing);
        }

        if (byteCount > dataSize)
        {
            throw new InvalidDataException(UiStrings.ErrMultiWaveOnlyConcatRange);
        }

        source.Position = dataStart;
        var remaining = byteCount;
        while (remaining > 0)
        {
            var toRead = (int)Math.Min(buffer.Length, remaining);
            var read = source.Read(buffer, 0, toRead);
            if (read <= 0)
            {
                throw new EndOfStreamException(UiStrings.ErrMultiWaveOnlyConcatRange);
            }

            dest.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static string ReadFourCc(BinaryReader reader) =>
        Encoding.ASCII.GetString(reader.ReadBytes(4));

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // best-effort
        }
    }
}
