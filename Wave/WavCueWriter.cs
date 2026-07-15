using System.Text;

namespace MgaWwiseImporter.Wave;

/// <summary>
/// WAV セグメントへ cue / LIST adtl（リージョン）を書き出す。
/// </summary>
internal static class WavCueWriter
{
    // 日本語 Windows などでは系統 ANSI（ここでは CP932）で書くエディタが多い。
    private static Encoding TextEncoding => Encoding.GetEncoding(932);

    /// <summary>
    /// ソース WAV の指定サンプル範囲を切り出し、cue／リージョンを付与して書き出す。
    /// </summary>
    public static void WriteSegment(
        string sourcePath,
        string destinationPath,
        long startSample,
        long endSample,
        ushort blockAlign,
        IReadOnlyList<WavCueItem> cues)
    {
        if (blockAlign == 0)
        {
            throw new InvalidDataException("BlockAlign が不正です。");
        }

        if (endSample <= startSample)
        {
            throw new ArgumentOutOfRangeException(nameof(endSample), "書き出し範囲が空です。");
        }

        using var source = File.OpenRead(sourcePath);
        using var reader = new BinaryReader(source, Encoding.ASCII, leaveOpen: true);

        if (ReadFourCc(reader) != "RIFF")
        {
            throw new InvalidDataException("RIFF ヘッダーではありません。");
        }

        _ = reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE")
        {
            throw new InvalidDataException("WAVE 形式ではありません。");
        }

        byte[]? fmtData = null;
        long dataStart = -1;
        uint dataSize = 0;

        while (source.Position + 8 <= source.Length)
        {
            var id = ReadFourCc(reader);
            var size = reader.ReadUInt32();
            var chunkDataStart = source.Position;
            if (chunkDataStart + size > source.Length)
            {
                throw new InvalidDataException($"チャンクサイズが不正です: {id}");
            }

            if (id == "fmt ")
            {
                fmtData = reader.ReadBytes((int)size);
            }
            else if (id == "data")
            {
                dataStart = chunkDataStart;
                dataSize = size;
            }

            var paddedSize = size + (size & 1);
            source.Position = chunkDataStart + paddedSize;
        }

        if (fmtData is null)
        {
            throw new InvalidDataException("fmt チャンクが見つかりません。");
        }

        if (dataStart < 0)
        {
            throw new InvalidDataException("data チャンクが見つかりません。");
        }

        var startByte = checked(startSample * (long)blockAlign);
        var endByte = checked(endSample * (long)blockAlign);
        if (startByte < 0 || endByte > dataSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endSample),
                $"書き出し範囲が data 外です: samples=[{startSample}..{endSample})"
                + $" dataFrames={dataSize / blockAlign}");
        }

        var segmentByteLength = checked((int)(endByte - startByte));
        var cueChunk = cues.Count > 0 ? BuildCueChunkBytes(cues) : null;
        var adtlChunk = cues.Count > 0 ? BuildAdtlListChunkBytes(cues) : null;

        var contentSize = 4
            + 8 + fmtData.Length + (fmtData.Length & 1)
            + (cueChunk is null ? 0 : 8 + cueChunk.Length + (cueChunk.Length & 1))
            + (adtlChunk is null ? 0 : 8 + adtlChunk.Length + (adtlChunk.Length & 1))
            + 8 + segmentByteLength + (segmentByteLength & 1);

        using var dest = File.Create(destinationPath);
        using var writer = new BinaryWriter(dest, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(contentSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        WriteChunk(writer, "fmt ", fmtData);
        if (cueChunk is not null)
        {
            WriteChunk(writer, "cue ", cueChunk);
        }

        if (adtlChunk is not null)
        {
            WriteChunk(writer, "LIST", adtlChunk);
        }

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(segmentByteLength);
        source.Position = dataStart + startByte;
        CopyExact(source, dest, segmentByteLength);
        if ((segmentByteLength & 1) == 1)
        {
            writer.Write((byte)0);
        }
    }

    private static void WriteChunk(BinaryWriter writer, string id, byte[] data)
    {
        writer.Write(Encoding.ASCII.GetBytes(id));
        writer.Write(data.Length);
        writer.Write(data);
        if ((data.Length & 1) == 1)
        {
            writer.Write((byte)0);
        }
    }

    private static void CopyExact(Stream source, Stream destination, int byteCount)
    {
        var buffer = new byte[Math.Min(byteCount, 1024 * 256)];
        var remaining = byteCount;
        while (remaining > 0)
        {
            var toRead = Math.Min(buffer.Length, remaining);
            var read = source.Read(buffer, 0, toRead);
            if (read <= 0)
            {
                throw new EndOfStreamException("data チャンクの読み取りが途中で終わりました。");
            }

            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static byte[] BuildCueChunkBytes(IReadOnlyList<WavCueItem> cues)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(cues.Count);
            foreach (var cue in cues)
            {
                writer.Write(cue.Id);
                writer.Write(checked((uint)cue.SampleOffset));
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(0);
                writer.Write(0);
                writer.Write(checked((uint)cue.SampleOffset));
            }
        }

        return stream.ToArray();
    }

    private static byte[] BuildAdtlListChunkBytes(IReadOnlyList<WavCueItem> cues)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("adtl"));

            foreach (var cue in cues)
            {
                WriteAdtlTextChunk(writer, "labl", cue.Id, cue.Comment);
                if (cue.IsRegion)
                {
                    WriteLtxtChunk(writer, cue.Id, cue.SampleLength, cue.Comment);
                }
                else
                {
                    WriteAdtlTextChunk(writer, "note", cue.Id, cue.Comment);
                }
            }
        }

        return stream.ToArray();
    }

    private static void WriteAdtlTextChunk(BinaryWriter writer, string chunkId, uint cueId, string text)
    {
        var textBytes = TextEncoding.GetBytes(text ?? string.Empty);
        var payloadLength = 4 + textBytes.Length + 1;
        writer.Write(Encoding.ASCII.GetBytes(chunkId));
        writer.Write(payloadLength);
        writer.Write(cueId);
        writer.Write(textBytes);
        writer.Write((byte)0);
        WritePadIfOdd(writer, payloadLength);
    }

    private static void WriteLtxtChunk(
        BinaryWriter writer,
        uint cueId,
        long sampleLength,
        string text)
    {
        // dwName + dwSampleLength + dwPurpose + country/language/dialect/codepage + text
        var textBytes = TextEncoding.GetBytes(text ?? string.Empty);
        var payloadLength = 20 + textBytes.Length + 1;
        writer.Write(Encoding.ASCII.GetBytes("ltxt"));
        writer.Write(payloadLength);
        writer.Write(cueId);
        writer.Write(checked((uint)sampleLength));
        writer.Write(Encoding.ASCII.GetBytes("rgn "));
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(textBytes);
        writer.Write((byte)0);
        WritePadIfOdd(writer, payloadLength);
    }

    private static void WritePadIfOdd(BinaryWriter writer, int payloadLength)
    {
        if ((payloadLength & 1) == 1)
        {
            writer.Write((byte)0);
        }
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }
}
