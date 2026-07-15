using System.Text;

namespace MgaWwiseImporter.Wave;

/// <summary>
/// WAV 全体を一度だけ走査して作る min/max ピークの段階解像度（ミップ）階層。
/// 構築後は任意の表示窓を O(画面幅) で集計でき、ズーム操作でディスクへ戻らない。
/// </summary>
internal sealed class WavPeakPyramid
{
    // 基底レベルの最大バケット数。1 バケット 8 バイトなので最大でも約 16MB。
    private const int TargetBaseBuckets = 1 << 21;

    private readonly float[][] _minLevels;
    private readonly float[][] _maxLevels;

    private WavPeakPyramid(
        float[][] minLevels,
        float[][] maxLevels,
        long frameCount,
        int baseBucketFrames,
        uint sampleRate)
    {
        _minLevels = minLevels;
        _maxLevels = maxLevels;
        FrameCount = frameCount;
        BaseBucketFrames = baseBucketFrames;
        SampleRate = sampleRate;
    }

    public long FrameCount { get; }

    /// <summary>基底レベル 1 バケットあたりのフレーム数（1 なら全サンプル保持と等価）。</summary>
    public int BaseBucketFrames { get; }

    public uint SampleRate { get; }

    /// <summary>
    /// 要求粒度が基底レベル以上（1 ピクセル ≧ 1 基底バケット）なら true。
    /// false のときは生サンプル読みでないと粒度が足りない。
    /// </summary>
    public bool HasFullDetailFor(long startFrame, long endFrame, int peakCount)
    {
        if (BaseBucketFrames == 1)
        {
            return true;
        }

        var rangeFrames = Math.Max(0, endFrame - startFrame);
        return rangeFrames >= (long)Math.Max(1, peakCount) * BaseBucketFrames;
    }

    /// <summary>[startFrame, endFrame) を peakCount バケットに集計する（メモリのみ・高速）。</summary>
    public WavPeakData ReadRange(long startFrame, long endFrame, int peakCount)
    {
        startFrame = Math.Clamp(startFrame, 0, FrameCount);
        endFrame = Math.Clamp(endFrame, startFrame, FrameCount);
        var rangeFrames = endFrame - startFrame;
        if (rangeFrames <= 0 || peakCount <= 0)
        {
            return WavPeakData.Empty;
        }

        var buckets = (int)Math.Min(peakCount, rangeFrames);
        var framesPerBucket = rangeFrames / (double)buckets;

        // 1 出力バケットあたりの参照数が数個で済むレベルを選ぶ
        var level = 0;
        while (level + 1 < _minLevels.Length
            && ((long)BaseBucketFrames << (level + 1)) * 2 <= framesPerBucket)
        {
            level++;
        }

        var levelBucketFrames = (long)BaseBucketFrames << level;
        var levelMins = _minLevels[level];
        var levelMaxs = _maxLevels[level];
        var mins = new float[buckets];
        var maxs = new float[buckets];

        for (var i = 0; i < buckets; i++)
        {
            var f0 = startFrame + i * rangeFrames / buckets;
            var f1 = startFrame + (i + 1) * rangeFrames / buckets;
            if (f1 <= f0)
            {
                f1 = f0 + 1;
            }

            var b0 = (int)Math.Clamp(f0 / levelBucketFrames, 0, levelMins.Length - 1);
            var b1 = (int)Math.Clamp((f1 - 1) / levelBucketFrames, b0, levelMins.Length - 1);

            var min = float.MaxValue;
            var max = float.MinValue;
            for (var b = b0; b <= b1; b++)
            {
                if (levelMins[b] < min)
                {
                    min = levelMins[b];
                }

                if (levelMaxs[b] > max)
                {
                    max = levelMaxs[b];
                }
            }

            if (min > max)
            {
                min = 0;
                max = 0;
            }

            mins[i] = min;
            maxs[i] = max;
        }

        return new WavPeakData(mins, maxs, FrameCount, SampleRate);
    }

    /// <summary>ファイル全体を 1 回のシーケンシャル走査で読み、階層を構築する。</summary>
    public static WavPeakPyramid Build(WavFileInfo info)
    {
        if (info.FrameCount <= 0 || info.BlockAlign == 0 || info.Channels == 0)
        {
            throw new InvalidDataException("波形フォーマットが不正です。");
        }

        using var stream = new FileStream(
            info.Path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            1 << 16,
            FileOptions.SequentialScan);
        using var reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: false);

        if (!WavPeakReader.TryFindDataChunk(stream, reader, out var dataStart, out var dataSize))
        {
            throw new InvalidDataException("data チャンクが見つかりません。");
        }

        var blockAlign = info.BlockAlign;
        var frameCount = Math.Min(info.FrameCount, (long)(dataSize / blockAlign));
        if (frameCount <= 0)
        {
            throw new InvalidDataException("データが空です。");
        }

        var baseBucket = (int)Math.Max(1L, (frameCount + TargetBaseBuckets - 1) / TargetBaseBuckets);
        var baseCount = (int)((frameCount + baseBucket - 1) / baseBucket);
        var mins = new float[baseCount];
        var maxs = new float[baseCount];
        Array.Fill(mins, float.MaxValue);
        Array.Fill(maxs, float.MinValue);

        stream.Position = dataStart;
        const int ChunkFrames = 1 << 14;
        var buffer = new byte[blockAlign * ChunkFrames];
        var mono = new float[ChunkFrames];
        long frame = 0;
        // バケット境界は逐次カウンタで追う（フレームごとの除算を避ける）
        var bucket = 0;
        var framesLeftInBucket = baseBucket;
        while (frame < frameCount)
        {
            var framesToRead = (int)Math.Min(ChunkFrames, frameCount - frame);
            var framesGot = WavPeakReader.ReadFrames(stream, buffer, framesToRead, blockAlign);
            WavPeakReader.ConvertFramesToMono(buffer, framesGot, info, mono);

            var min = mins[bucket];
            var max = maxs[bucket];
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

                if (--framesLeftInBucket == 0)
                {
                    mins[bucket] = min;
                    maxs[bucket] = max;
                    bucket++;
                    framesLeftInBucket = baseBucket;
                    if (bucket < baseCount)
                    {
                        min = mins[bucket];
                        max = maxs[bucket];
                    }
                }
            }

            if (bucket < baseCount)
            {
                mins[bucket] = min;
                maxs[bucket] = max;
            }

            frame += framesGot;
            if (framesGot < framesToRead)
            {
                break;
            }
        }

        for (var i = 0; i < baseCount; i++)
        {
            if (mins[i] > maxs[i])
            {
                mins[i] = 0;
                maxs[i] = 0;
            }
        }

        // 上位レベル: 隣接 2 バケットを結合して半分に
        var minLevels = new List<float[]> { mins };
        var maxLevels = new List<float[]> { maxs };
        while (minLevels[^1].Length > 2048)
        {
            var prevMin = minLevels[^1];
            var prevMax = maxLevels[^1];
            var len = (prevMin.Length + 1) / 2;
            var nextMin = new float[len];
            var nextMax = new float[len];
            for (var i = 0; i < len; i++)
            {
                var a = i * 2;
                var b = Math.Min(a + 1, prevMin.Length - 1);
                nextMin[i] = Math.Min(prevMin[a], prevMin[b]);
                nextMax[i] = Math.Max(prevMax[a], prevMax[b]);
            }

            minLevels.Add(nextMin);
            maxLevels.Add(nextMax);
        }

        return new WavPeakPyramid(
            [.. minLevels],
            [.. maxLevels],
            frameCount,
            baseBucket,
            info.SampleRate);
    }
}
