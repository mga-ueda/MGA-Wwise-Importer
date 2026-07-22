using System.Text;
using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Wave;

/// <summary>
/// ITU-R BS.1770-4 相当の Integrated Loudness（LKFS）測定と、パート単位の正規化ゲイン計算。
/// </summary>
internal static class LoudnessMeter
{
    private const double AbsoluteGateLkfs = -70.0;
    private const double RelativeGateDb = -10.0;
    private const double BlockSeconds = 0.4;
    private const double BlockOverlap = 0.75;

    /// <summary>
    /// ソース WAV の指定サンプル範囲の Integrated Loudness（LKFS）を返す。
    /// 無音などでゲート後にブロックが無い場合は <see cref="double.NegativeInfinity"/>。
    /// </summary>
    public static double MeasureIntegratedLkfs(
        string sourcePath,
        WavFileInfo info,
        long startSample,
        long endSample)
    {
        if (endSample <= startSample || info.SampleRate == 0 || info.Channels == 0)
        {
            return double.NegativeInfinity;
        }

        var samples = ReadFloatFrames(sourcePath, info, startSample, endSample);
        return MeasureIntegratedLkfs(samples, info.Channels, info.SampleRate);
    }

    /// <summary>
    /// 各パートのゲイン（線形）を計算する。
    /// Preserve Group Balance 時は、グループ内で最も大きい LKFS をターゲットへ合わせ、
    /// 同じゲインをメンバー全体へ適用する。
    /// </summary>
    public static Dictionary<int, float> ComputePartGains(
        string sourcePath,
        WavFileInfo info,
        IReadOnlyList<WaveformOutputPart> parts,
        IReadOnlyDictionary<int, int>? partGroupIds,
        double targetLkfs,
        bool preserveGroupBalance,
        Action<string>? log = null)
    {
        var loudnessByPart = new Dictionary<int, double>(parts.Count);
        foreach (var part in parts)
        {
            var partSource = part.ResolveSourcePath(sourcePath);
            var localStart = part.ResolveLocalStart();
            var localEnd = part.ResolveLocalEnd();
            var partInfo = part.HasDedicatedSource
                ? WavFileInfo.Read(partSource)
                : info;

            var lkfs = MeasureIntegratedLkfs(
                partSource,
                partInfo,
                localStart,
                localEnd);
            loudnessByPart[part.Number] = lkfs;
            log?.Invoke(
                double.IsInfinity(lkfs)
                    ? UiStrings.LogLoudnessPartSilence(part.Number)
                    : UiStrings.LogLoudnessPartValue(part.Number, lkfs));
        }

        var gains = new Dictionary<int, float>(parts.Count);
        if (!preserveGroupBalance || partGroupIds is null || partGroupIds.Count == 0)
        {
            foreach (var part in parts)
            {
                gains[part.Number] = GainToTarget(loudnessByPart[part.Number], targetLkfs);
            }

            return gains;
        }

        var membersByGroup = new Dictionary<int, List<int>>();
        var ungrouped = new List<int>();
        foreach (var part in parts)
        {
            if (partGroupIds.TryGetValue(part.Number, out var groupId) && groupId > 0)
            {
                if (!membersByGroup.TryGetValue(groupId, out var list))
                {
                    list = [];
                    membersByGroup[groupId] = list;
                }

                list.Add(part.Number);
            }
            else
            {
                ungrouped.Add(part.Number);
            }
        }

        foreach (var partNumber in ungrouped)
        {
            gains[partNumber] = GainToTarget(loudnessByPart[partNumber], targetLkfs);
        }

        foreach (var (groupId, members) in membersByGroup)
        {
            // 1 件だけの「グループ」は単独正規化と同じ。
            if (members.Count < 2)
            {
                foreach (var partNumber in members)
                {
                    gains[partNumber] = GainToTarget(loudnessByPart[partNumber], targetLkfs);
                }

                continue;
            }

            var maxLkfs = double.NegativeInfinity;
            foreach (var partNumber in members)
            {
                var lkfs = loudnessByPart[partNumber];
                if (lkfs > maxLkfs)
                {
                    maxLkfs = lkfs;
                }
            }

            var sharedGain = GainToTarget(maxLkfs, targetLkfs);
            log?.Invoke(
                double.IsInfinity(maxLkfs)
                    ? UiStrings.LogLoudnessGroupSilence(groupId, sharedGain)
                    : UiStrings.LogLoudnessGroupValue(groupId, maxLkfs, sharedGain));
            foreach (var partNumber in members)
            {
                gains[partNumber] = sharedGain;
            }
        }

        return gains;
    }

    private static float GainToTarget(double measuredLkfs, double targetLkfs)
    {
        if (double.IsInfinity(measuredLkfs) || double.IsNaN(measuredLkfs))
        {
            return 1f;
        }

        var db = targetLkfs - measuredLkfs;
        return (float)Math.Pow(10.0, db / 20.0);
    }

    private static double MeasureIntegratedLkfs(
        float[] interleaved,
        int channels,
        uint sampleRate)
    {
        var frameCount = interleaved.Length / channels;
        if (frameCount <= 0)
        {
            return double.NegativeInfinity;
        }

        var filters = new KWeightFilter[channels];
        for (var c = 0; c < channels; c++)
        {
            filters[c] = new KWeightFilter(sampleRate);
        }

        var weighted = new float[interleaved.Length];
        for (var i = 0; i < frameCount; i++)
        {
            for (var c = 0; c < channels; c++)
            {
                var index = i * channels + c;
                weighted[index] = (float)filters[c].Process(interleaved[index]);
            }
        }

        var blockSize = Math.Max(1, (int)Math.Round(sampleRate * BlockSeconds));
        var hop = Math.Max(1, (int)Math.Round(blockSize * (1.0 - BlockOverlap)));
        if (frameCount < blockSize)
        {
            // 短すぎる場合は全体を 1 ブロックとして扱う。
            blockSize = frameCount;
            hop = frameCount;
        }

        var channelWeights = BuildChannelWeights(channels);
        var blockPowers = new List<double>();
        for (var start = 0; start + blockSize <= frameCount; start += hop)
        {
            var power = 0.0;
            for (var c = 0; c < channels; c++)
            {
                var sumSq = 0.0;
                for (var i = 0; i < blockSize; i++)
                {
                    var sample = weighted[(start + i) * channels + c];
                    sumSq += sample * (double)sample;
                }

                power += channelWeights[c] * (sumSq / blockSize);
            }

            blockPowers.Add(power);
        }

        if (blockPowers.Count == 0)
        {
            return double.NegativeInfinity;
        }

        var absoluteThreshold = GatePowerFromLkfs(AbsoluteGateLkfs);
        var afterAbsolute = blockPowers.Where(p => p > absoluteThreshold).ToList();
        if (afterAbsolute.Count == 0)
        {
            return double.NegativeInfinity;
        }

        var absoluteMean = afterAbsolute.Average();
        var relativeThreshold = absoluteMean * Math.Pow(10.0, RelativeGateDb / 10.0);
        var afterRelative = afterAbsolute.Where(p => p > relativeThreshold).ToList();
        if (afterRelative.Count == 0)
        {
            return double.NegativeInfinity;
        }

        return LkfsFromPower(afterRelative.Average());
    }

    private static double[] BuildChannelWeights(int channels)
    {
        // ステレオ／モノは 1.0。5.1 想定では LFE(index 3) を除外しサラウンドを 1.41。
        var weights = new double[channels];
        for (var i = 0; i < channels; i++)
        {
            weights[i] = 1.0;
        }

        if (channels >= 5)
        {
            if (channels >= 6)
            {
                weights[3] = 0.0; // LFE
                weights[4] = 1.41;
                weights[5] = 1.41;
            }
            else
            {
                weights[3] = 1.41;
                weights[4] = 1.41;
            }
        }

        return weights;
    }

    private static double GatePowerFromLkfs(double lkfs) =>
        Math.Pow(10.0, (lkfs + 0.691) / 10.0);

    private static double LkfsFromPower(double power) =>
        power <= 0 ? double.NegativeInfinity : -0.691 + 10.0 * Math.Log10(power);

    private static float[] ReadFloatFrames(
        string sourcePath,
        WavFileInfo info,
        long startSample,
        long endSample)
    {
        var frameCount = checked((int)(endSample - startSample));
        var channels = info.Channels;
        var bytesPerSample = info.BitsPerSample / 8;
        if (bytesPerSample <= 0 || info.BlockAlign != channels * bytesPerSample)
        {
            throw new InvalidDataException(UiStrings.ErrSampleFormatInvalid);
        }

        using var source = File.OpenRead(sourcePath);
        using var reader = new BinaryReader(source, Encoding.ASCII, leaveOpen: true);
        var dataStart = FindDataChunk(reader, source, out _);
        var startByte = checked(startSample * (long)info.BlockAlign);
        source.Position = dataStart + startByte;

        var interleaved = new float[frameCount * channels];
        var format = ResolvePcmFormat(info);
        var frameBytes = new byte[info.BlockAlign];
        for (var i = 0; i < frameCount; i++)
        {
            var read = source.Read(frameBytes, 0, frameBytes.Length);
            if (read != frameBytes.Length)
            {
                throw new EndOfStreamException(UiStrings.ErrDataChunkTruncated);
            }

            for (var c = 0; c < channels; c++)
            {
                interleaved[i * channels + c] = DecodeSample(frameBytes, c * bytesPerSample, format);
            }
        }

        return interleaved;
    }

    internal static long FindDataChunk(BinaryReader reader, Stream source, out uint dataSize)
    {
        source.Position = 0;
        if (ReadFourCc(reader) != "RIFF")
        {
            throw new InvalidDataException(UiStrings.ErrNotRiffHeader);
        }

        _ = reader.ReadUInt32();
        if (ReadFourCc(reader) != "WAVE")
        {
            throw new InvalidDataException(UiStrings.ErrNotWaveFormat);
        }

        while (source.Position + 8 <= source.Length)
        {
            var id = ReadFourCc(reader);
            var size = reader.ReadUInt32();
            var chunkDataStart = source.Position;
            if (chunkDataStart + size > source.Length)
            {
                throw new InvalidDataException(UiStrings.ErrChunkSizeInvalid(id));
            }

            if (id == "data")
            {
                dataSize = size;
                return chunkDataStart;
            }

            var paddedSize = size + (size & 1);
            source.Position = chunkDataStart + paddedSize;
        }

        throw new InvalidDataException(UiStrings.ErrDataChunkMissing);
    }

    internal static PcmSampleFormat ResolvePcmFormat(WavFileInfo info)
    {
        if (info.AudioFormat == 3 && info.BitsPerSample == 32)
        {
            return PcmSampleFormat.Float32;
        }

        if (info.AudioFormat is 1 or 65534)
        {
            return info.BitsPerSample switch
            {
                16 => PcmSampleFormat.Pcm16,
                24 => PcmSampleFormat.Pcm24,
                32 => PcmSampleFormat.Pcm32,
                _ => throw new NotSupportedException(
                    UiStrings.ErrUnsupportedBitDepth(info.BitsPerSample)),
            };
        }

        throw new NotSupportedException(UiStrings.ErrUnsupportedWavFormat(info.AudioFormatName));
    }

    internal static float DecodeSample(byte[] frame, int offset, PcmSampleFormat format) =>
        format switch
        {
            PcmSampleFormat.Pcm16 => BitConverter.ToInt16(frame, offset) / 32768f,
            PcmSampleFormat.Pcm24 =>
                (((frame[offset] | (frame[offset + 1] << 8) | (frame[offset + 2] << 16)) << 8) >> 8)
                / 8388608f,
            PcmSampleFormat.Pcm32 => BitConverter.ToInt32(frame, offset) / 2147483648f,
            PcmSampleFormat.Float32 => BitConverter.ToSingle(frame, offset),
            _ => 0f,
        };

    internal static void EncodeSample(float sample, byte[] frame, int offset, PcmSampleFormat format)
    {
        sample = Math.Clamp(sample, -1f, 1f);
        switch (format)
        {
            case PcmSampleFormat.Pcm16:
            {
                var value = (short)Math.Round(sample * 32767f, MidpointRounding.AwayFromZero);
                frame[offset] = (byte)(value & 0xFF);
                frame[offset + 1] = (byte)((value >> 8) & 0xFF);
                break;
            }
            case PcmSampleFormat.Pcm24:
            {
                var value = (int)Math.Round(sample * 8388607f, MidpointRounding.AwayFromZero);
                frame[offset] = (byte)(value & 0xFF);
                frame[offset + 1] = (byte)((value >> 8) & 0xFF);
                frame[offset + 2] = (byte)((value >> 16) & 0xFF);
                break;
            }
            case PcmSampleFormat.Pcm32:
            {
                var value = (int)Math.Round(sample * 2147483647f, MidpointRounding.AwayFromZero);
                var bytes = BitConverter.GetBytes(value);
                Buffer.BlockCopy(bytes, 0, frame, offset, 4);
                break;
            }
            case PcmSampleFormat.Float32:
            {
                var bytes = BitConverter.GetBytes(sample);
                Buffer.BlockCopy(bytes, 0, frame, offset, 4);
                break;
            }
        }
    }

    private static string ReadFourCc(BinaryReader reader) =>
        Encoding.ASCII.GetString(reader.ReadBytes(4));

    internal enum PcmSampleFormat
    {
        Pcm16,
        Pcm24,
        Pcm32,
        Float32,
    }

    /// <summary>BS.1770 K-weighting（pre-filter + RLB）。</summary>
    private sealed class KWeightFilter
    {
        private readonly Biquad _pre;
        private readonly Biquad _rlb;

        public KWeightFilter(uint sampleRate)
        {
            _pre = DesignHighShelf(sampleRate, 1681.974450955533, 3.999843853973347, 0.7071752369554196);
            _rlb = DesignHighPass(sampleRate, 38.13547087602444, 0.5003270373238773);
        }

        public double Process(double input) => _rlb.Process(_pre.Process(input));

        // libebur128 と同じ係数設計（任意サンプルレート）。
        private static Biquad DesignHighShelf(uint sampleRate, double f0, double dbGain, double q)
        {
            var k = Math.Tan(Math.PI * f0 / sampleRate);
            var vh = Math.Pow(10.0, dbGain / 20.0);
            var vb = Math.Pow(vh, 0.4996667741545416);
            var a0 = 1.0 + k / q + k * k;
            var b0 = (vh + vb * k / q + k * k) / a0;
            var b1 = 2.0 * (k * k - vh) / a0;
            var b2 = (vh - vb * k / q + k * k) / a0;
            var a1 = 2.0 * (k * k - 1.0) / a0;
            var a2 = (1.0 - k / q + k * k) / a0;
            return new Biquad(b0, b1, b2, a1, a2);
        }

        private static Biquad DesignHighPass(uint sampleRate, double f0, double q)
        {
            var k = Math.Tan(Math.PI * f0 / sampleRate);
            var a0 = 1.0 + k / q + k * k;
            var b0 = 1.0 / a0;
            var b1 = -2.0 / a0;
            var b2 = 1.0 / a0;
            var a1 = 2.0 * (k * k - 1.0) / a0;
            var a2 = (1.0 - k / q + k * k) / a0;
            return new Biquad(b0, b1, b2, a1, a2);
        }
    }

    private sealed class Biquad
    {
        private readonly double _b0;
        private readonly double _b1;
        private readonly double _b2;
        private readonly double _a1;
        private readonly double _a2;
        private double _z1;
        private double _z2;

        public Biquad(double b0, double b1, double b2, double a1, double a2)
        {
            _b0 = b0;
            _b1 = b1;
            _b2 = b2;
            _a1 = a1;
            _a2 = a2;
        }

        public double Process(double input)
        {
            var output = _b0 * input + _z1;
            _z1 = _b1 * input - _a1 * output + _z2;
            _z2 = _b2 * input - _a2 * output;
            return output;
        }
    }
}
