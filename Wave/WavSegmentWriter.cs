using System.Text;
using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Wave;

/// <summary>
/// ソース WAV の指定サンプル範囲だけを切り出して書き出す（メタデータ埋め込みなし）。
/// 線形ゲインおよびリージョン端フェード（サンプル単位）を適用できる。
/// </summary>
internal static class WavSegmentWriter
{
    /// <summary>
    /// ソース WAV の指定サンプル範囲を切り出して書き出す。
    /// <paramref name="regionEdgeFades"/> がある場合は絶対サンプル位置でゲインを乗算する（破壊編集）。
    /// </summary>
    public static void WriteSegment(
        string sourcePath,
        string destinationPath,
        long startSample,
        long endSample,
        ushort blockAlign,
        float gain = 1f,
        WavFileInfo? formatInfo = null,
        IReadOnlyList<RegionEdgeFade>? regionEdgeFades = null)
    {
        if (blockAlign == 0)
        {
            throw new InvalidDataException(UiStrings.ErrBlockAlignInvalid);
        }

        if (endSample <= startSample)
        {
            throw new ArgumentOutOfRangeException(nameof(endSample), UiStrings.ErrExportRangeEmpty);
        }

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
                throw new InvalidDataException(UiStrings.ErrChunkSizeInvalid(id));
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
            throw new InvalidDataException(UiStrings.ErrFmtChunkMissing);
        }

        if (dataStart < 0)
        {
            throw new InvalidDataException(UiStrings.ErrDataChunkMissing);
        }

        var startByte = checked(startSample * (long)blockAlign);
        var endByte = checked(endSample * (long)blockAlign);
        if (startByte < 0 || endByte > dataSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(endSample),
                UiStrings.ErrExportRangeBeforeData(startSample, endSample));
        }

        var segmentByteLength = checked((int)(endByte - startByte));
        var contentSize = 4
            + 8 + fmtData.Length + (fmtData.Length & 1)
            + 8 + segmentByteLength + (segmentByteLength & 1);

        using var dest = File.Create(destinationPath);
        using var writer = new BinaryWriter(dest, Encoding.ASCII, leaveOpen: true);

        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(contentSize);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));

        WriteChunk(writer, "fmt ", fmtData);

        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(segmentByteLength);
        source.Position = dataStart + startByte;

        var fades = regionEdgeFades is { Count: > 0 }
            ? regionEdgeFades.Where(f => f.HasAnyFade).ToArray()
            : [];
        var bakeRegionFade = fades.Length > 0
            && RegionEdgeFade.OverlapsRange(startSample, endSample, fades);
        var applyConstantGain = Math.Abs(gain - 1f) >= 0.000001f;

        if (!bakeRegionFade && !applyConstantGain)
        {
            CopyExact(source, dest, segmentByteLength);
        }
        else
        {
            var info = formatInfo ?? WavFileInfo.Read(sourcePath);
            CopyWithGainEnvelope(
                source,
                dest,
                segmentByteLength,
                info,
                startSample,
                gain,
                bakeRegionFade ? fades : null);
        }

        if ((segmentByteLength & 1) == 1)
        {
            writer.Write((byte)0);
        }
    }

    private static void CopyWithGainEnvelope(
        Stream source,
        Stream destination,
        int byteCount,
        WavFileInfo info,
        long startSample,
        float linearGain,
        IReadOnlyList<RegionEdgeFade>? regionEdgeFades)
    {
        var format = LoudnessMeter.ResolvePcmFormat(info);
        var bytesPerSample = info.BitsPerSample / 8;
        var channels = info.Channels;
        if (bytesPerSample <= 0 || info.BlockAlign != channels * bytesPerSample)
        {
            throw new InvalidDataException(UiStrings.ErrSampleFormatInvalid);
        }

        if (byteCount % info.BlockAlign != 0)
        {
            throw new InvalidDataException(UiStrings.ErrExportBytesNotBlockAligned);
        }

        var frame = new byte[info.BlockAlign];
        var frames = byteCount / info.BlockAlign;
        for (var i = 0; i < frames; i++)
        {
            var read = source.Read(frame, 0, frame.Length);
            if (read != frame.Length)
            {
                throw new EndOfStreamException(UiStrings.ErrDataChunkTruncated);
            }

            var sampleGain = linearGain;
            if (regionEdgeFades is not null)
            {
                sampleGain *= RegionEdgeFade.GainAt(startSample + i, regionEdgeFades);
            }

            for (var c = 0; c < channels; c++)
            {
                var sample = LoudnessMeter.DecodeSample(frame, c * bytesPerSample, format) * sampleGain;
                LoudnessMeter.EncodeSample(sample, frame, c * bytesPerSample, format);
            }

            destination.Write(frame, 0, frame.Length);
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
                throw new EndOfStreamException(UiStrings.ErrDataChunkTruncated);
            }

            destination.Write(buffer, 0, read);
            remaining -= read;
        }
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }
}
