using System.Runtime.InteropServices;
using System.Text;

namespace MgaWwiseImporter.Wave;

/// <summary>
/// WAV の data チャンクから表示用ピーク（min/max）を読み取る。
/// </summary>
internal static class WavPeakReader
{
    public static WavPeakData Read(WavFileInfo info, int peakCount)
    {
        if (info.FrameCount <= 0)
        {
            return WavPeakData.Empty;
        }

        return ReadRange(info, startFrame: 0, endFrame: info.FrameCount, peakCount);
    }

    /// <summary>
    /// [startFrame, endFrame) の範囲だけを peakCount バケットにまとめて読む（時間軸ズーム用）。
    /// </summary>
    public static WavPeakData ReadRange(
        WavFileInfo info,
        long startFrame,
        long endFrame,
        int peakCount)
    {
        if (peakCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(peakCount));
        }

        if (info.FrameCount <= 0 || info.BlockAlign == 0 || info.Channels == 0)
        {
            return WavPeakData.Empty;
        }

        startFrame = Math.Clamp(startFrame, 0, info.FrameCount);
        endFrame = Math.Clamp(endFrame, startFrame, info.FrameCount);
        var rangeFrames = endFrame - startFrame;
        if (rangeFrames <= 0)
        {
            return WavPeakData.Empty;
        }

        using var stream = new FileStream(
            info.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1 << 16,
            FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        if (!TryFindDataChunk(stream, reader, out var dataStart, out var dataSize))
        {
            throw new InvalidDataException("data チャンクが見つかりません。");
        }

        var fileFrameCount = (long)Math.Min(info.FrameCount, dataSize / info.BlockAlign);
        endFrame = Math.Min(endFrame, fileFrameCount);
        startFrame = Math.Min(startFrame, endFrame);
        rangeFrames = endFrame - startFrame;
        if (rangeFrames <= 0)
        {
            return WavPeakData.Empty;
        }

        var buckets = (int)Math.Min(peakCount, rangeFrames);
        var channels = info.Channels;
        var bytesPerSample = info.BitsPerSample / 8;
        if (bytesPerSample <= 0)
        {
            throw new InvalidDataException("BitsPerSample が不正です。");
        }

        // 1 バケットあたりのフレーム数が少なければ、シークせず範囲全体を順次走査する方が圧倒的に速い
        var framesPerBucket = rangeFrames / (double)buckets;
        return framesPerBucket <= 4096
            ? ReadRangeSequential(
                stream, dataStart, info, startFrame, rangeFrames, buckets)
            : ReadRangeSampled(
                stream, dataStart, info, startFrame, rangeFrames, buckets);
    }

    /// <summary>範囲全体を 1 回の順次読みで走査（表示窓の詳細読み向け）。</summary>
    private static WavPeakData ReadRangeSequential(
        FileStream stream,
        long dataStart,
        WavFileInfo info,
        long startFrame,
        long rangeFrames,
        int buckets)
    {
        var mins = new float[buckets];
        var maxs = new float[buckets];
        Array.Fill(mins, float.MaxValue);
        Array.Fill(maxs, float.MinValue);

        var blockAlign = info.BlockAlign;

        stream.Position = dataStart + startFrame * blockAlign;
        const int ChunkFrames = 1 << 14;
        var buffer = new byte[blockAlign * ChunkFrames];
        var mono = new float[ChunkFrames];
        long local = 0;
        while (local < rangeFrames)
        {
            var framesToRead = (int)Math.Min(ChunkFrames, rangeFrames - local);
            var framesGot = ReadFrames(stream, buffer, framesToRead, blockAlign);
            ConvertFramesToMono(buffer, framesGot, info, mono);

            for (var i = 0; i < framesGot; i++)
            {
                var value = mono[i];
                var bucket = (int)((local + i) * buckets / rangeFrames);
                if (value < mins[bucket])
                {
                    mins[bucket] = value;
                }

                if (value > maxs[bucket])
                {
                    maxs[bucket] = value;
                }
            }

            local += framesGot;
            if (framesGot < framesToRead)
            {
                break;
            }
        }

        for (var i = 0; i < buckets; i++)
        {
            if (mins[i] > maxs[i])
            {
                mins[i] = 0;
                maxs[i] = 0;
            }
        }

        return new WavPeakData(mins, maxs, info.FrameCount, info.SampleRate);
    }

    /// <summary>
    /// 広い範囲向け: バケットごとに連続ミニブロックを数か所だけ読む
    /// （フレーム単位のシークを繰り返すと数十万 syscall になり非常に遅い）。
    /// </summary>
    private static WavPeakData ReadRangeSampled(
        FileStream stream,
        long dataStart,
        WavFileInfo info,
        long startFrame,
        long rangeFrames,
        int buckets)
    {
        const int BlockFrames = 256;
        const int MaxBlocksPerBucket = 4;

        var mins = new float[buckets];
        var maxs = new float[buckets];
        var blockAlign = info.BlockAlign;
        var buffer = new byte[blockAlign * BlockFrames];
        var mono = new float[BlockFrames];

        for (var bucket = 0; bucket < buckets; bucket++)
        {
            var localStart = bucket * rangeFrames / buckets;
            var localEnd = (bucket + 1) * rangeFrames / buckets;
            if (localEnd <= localStart)
            {
                localEnd = localStart + 1;
            }

            var frameStart = startFrame + localStart;
            var framesInBucket = localEnd - localStart;
            var blocks = (int)Math.Clamp(framesInBucket / BlockFrames, 1L, MaxBlocksPerBucket);
            var min = float.MaxValue;
            var max = float.MinValue;

            for (var block = 0; block < blocks; block++)
            {
                // バケット内に等間隔でブロックを配置する
                var blockStart = frameStart + block * framesInBucket / blocks;
                var blockFrames = (int)Math.Min(BlockFrames, frameStart + framesInBucket - blockStart);
                if (blockFrames <= 0)
                {
                    continue;
                }

                stream.Position = dataStart + blockStart * blockAlign;
                var framesGot = ReadFrames(stream, buffer, blockFrames, blockAlign);
                ConvertFramesToMono(buffer, framesGot, info, mono);
                for (var i = 0; i < framesGot; i++)
                {
                    var value = mono[i];
                    if (value < min)
                    {
                        min = value;
                    }

                    if (value > max)
                    {
                        max = value;
                    }
                }
            }

            if (min > max)
            {
                min = 0;
                max = 0;
            }

            mins[bucket] = min;
            maxs[bucket] = max;
        }

        return new WavPeakData(mins, maxs, info.FrameCount, info.SampleRate);
    }

    /// <summary>指定フレーム数を読み切る（EOF なら得られた分だけ）。戻り値は読めたフレーム数。</summary>
    internal static int ReadFrames(Stream stream, byte[] buffer, int frames, int blockAlign)
    {
        var bytesNeeded = frames * blockAlign;
        var got = 0;
        while (got < bytesNeeded)
        {
            var n = stream.Read(buffer, got, bytesNeeded - got);
            if (n == 0)
            {
                break;
            }

            got += n;
        }

        return got / blockAlign;
    }

    /// <summary>
    /// フレーム列を一括でモノラル float へ変換する。
    /// per-sample デリゲート呼び出しを避けた形式別タイトループ（巨大ファイルの走査で支配的なコスト）。
    /// </summary>
    internal static void ConvertFramesToMono(
        ReadOnlySpan<byte> source,
        int frames,
        WavFileInfo info,
        Span<float> mono)
    {
        var channels = info.Channels;
        var blockAlign = info.BlockAlign;
        if (channels == 0 || frames <= 0)
        {
            return;
        }

        var inv = 1f / channels;
        var packed = blockAlign == channels * (info.BitsPerSample / 8);

        if (info.AudioFormat == 3 && info.BitsPerSample == 32)
        {
            if (packed)
            {
                var samples = MemoryMarshal.Cast<byte, float>(source);
                for (var i = 0; i < frames; i++)
                {
                    float sum = 0;
                    var baseIndex = i * channels;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        sum += samples[baseIndex + ch];
                    }

                    mono[i] = sum * inv;
                }

                return;
            }

            for (var i = 0; i < frames; i++)
            {
                float sum = 0;
                var offset = i * blockAlign;
                for (var ch = 0; ch < channels; ch++)
                {
                    sum += MemoryMarshal.Read<float>(source[(offset + ch * 4)..]);
                }

                mono[i] = sum * inv;
            }

            return;
        }

        if (info.AudioFormat is not (1 or 65534))
        {
            throw new NotSupportedException($"AudioFormat={info.AudioFormat} は波形表示未対応です。");
        }

        switch (info.BitsPerSample)
        {
            case 8:
            {
                var scale = inv / 128f;
                for (var i = 0; i < frames; i++)
                {
                    var offset = i * blockAlign;
                    var sum = 0;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        sum += source[offset + ch] - 128;
                    }

                    mono[i] = sum * scale;
                }

                return;
            }

            case 16:
            {
                var scale = inv / 32768f;
                if (packed)
                {
                    var samples = MemoryMarshal.Cast<byte, short>(source);
                    for (var i = 0; i < frames; i++)
                    {
                        var sum = 0;
                        var baseIndex = i * channels;
                        for (var ch = 0; ch < channels; ch++)
                        {
                            sum += samples[baseIndex + ch];
                        }

                        mono[i] = sum * scale;
                    }

                    return;
                }

                for (var i = 0; i < frames; i++)
                {
                    var offset = i * blockAlign;
                    var sum = 0;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        sum += MemoryMarshal.Read<short>(source[(offset + ch * 2)..]);
                    }

                    mono[i] = sum * scale;
                }

                return;
            }

            case 24:
            {
                var scale = inv / 8388608f;
                for (var i = 0; i < frames; i++)
                {
                    var offset = i * blockAlign;
                    long sum = 0;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        var o = offset + ch * 3;
                        var value = source[o] | (source[o + 1] << 8) | (source[o + 2] << 16);
                        if ((value & 0x800000) != 0)
                        {
                            value |= unchecked((int)0xFF000000);
                        }

                        sum += value;
                    }

                    mono[i] = sum * scale;
                }

                return;
            }

            case 32:
            {
                var scale = inv / 2147483648f;
                if (packed)
                {
                    var samples = MemoryMarshal.Cast<byte, int>(source);
                    for (var i = 0; i < frames; i++)
                    {
                        long sum = 0;
                        var baseIndex = i * channels;
                        for (var ch = 0; ch < channels; ch++)
                        {
                            sum += samples[baseIndex + ch];
                        }

                        mono[i] = sum * scale;
                    }

                    return;
                }

                for (var i = 0; i < frames; i++)
                {
                    var offset = i * blockAlign;
                    long sum = 0;
                    for (var ch = 0; ch < channels; ch++)
                    {
                        sum += MemoryMarshal.Read<int>(source[(offset + ch * 4)..]);
                    }

                    mono[i] = sum * scale;
                }

                return;
            }

            default:
                throw new NotSupportedException($"{info.BitsPerSample} bit PCM は未対応です。");
        }
    }

    internal static Func<byte[], int, float> CreateSampleReader(ushort audioFormat, ushort bitsPerSample)
    {
        if (audioFormat == 3 && bitsPerSample == 32)
        {
            return (buffer, offset) => BitConverter.ToSingle(buffer, offset);
        }

        if (audioFormat is 1 or 65534)
        {
            return bitsPerSample switch
            {
                8 => (buffer, offset) => (buffer[offset] - 128) / 128f,
                16 => (buffer, offset) => BitConverter.ToInt16(buffer, offset) / 32768f,
                24 => (buffer, offset) =>
                {
                    var value = buffer[offset]
                        | (buffer[offset + 1] << 8)
                        | (buffer[offset + 2] << 16);
                    if ((value & 0x800000) != 0)
                    {
                        value |= unchecked((int)0xFF000000);
                    }

                    return value / 8388608f;
                },
                32 => (buffer, offset) => BitConverter.ToInt32(buffer, offset) / 2147483648f,
                _ => throw new NotSupportedException($"{bitsPerSample} bit PCM は未対応です。"),
            };
        }

        throw new NotSupportedException($"AudioFormat={audioFormat} は波形表示未対応です。");
    }

    internal static bool TryFindDataChunk(
        Stream stream,
        BinaryReader reader,
        out long dataStart,
        out uint dataSize)
    {
        dataStart = 0;
        dataSize = 0;

        stream.Position = 0;
        if (ReadFourCc(reader) != "RIFF")
        {
            return false;
        }

        _ = reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE")
        {
            return false;
        }

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = ReadFourCc(reader);
            var chunkSize = reader.ReadUInt32();
            var chunkDataStart = stream.Position;

            if (chunkId == "data")
            {
                dataStart = chunkDataStart;
                dataSize = chunkSize;
                return true;
            }

            var paddedSize = chunkSize + (chunkSize & 1);
            stream.Position = chunkDataStart + paddedSize;
        }

        return false;
    }

    private static string ReadFourCc(BinaryReader reader)
    {
        return Encoding.ASCII.GetString(reader.ReadBytes(4));
    }
}

internal sealed class WavPeakData
{
    public static WavPeakData Empty { get; } = new([], [], 0, 0);

    public WavPeakData(float[] mins, float[] maxs, long frameCount, uint sampleRate = 0)
    {
        Mins = mins;
        Maxs = maxs;
        FrameCount = frameCount;
        SampleRate = sampleRate;
    }

    public float[] Mins { get; }
    public float[] Maxs { get; }
    public long FrameCount { get; }
    public uint SampleRate { get; }
    public double DurationSeconds => SampleRate == 0 ? 0 : FrameCount / (double)SampleRate;
    public bool IsEmpty => Mins.Length == 0 || FrameCount <= 0;
}
