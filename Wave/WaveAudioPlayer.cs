using NAudio.Wave;

namespace MgaWwiseIMImporter.Wave;

/// <summary>
/// Wave ファイルの再生。位置は Position で取得する。
/// 変換は自前で行い ACM ドライバに依存しない（マルチチャンネル／Extensible 対応）。
/// <c>-L</c> 連続区間は <see cref="SetLoopPlans"/> で登録し、
/// <see cref="ArmLoopAtProgress"/> で有効化した区間だけ末尾→先頭へシームレス折り返す。
/// 直後が <c>-E</c> のときは、ループ末端で頭へ戻る瞬間に Exit をワンショット二重再生する（Wwise 相当）。
/// シークで区間外へ出るとループ／Exit とも直ちに解除される。
/// </summary>
internal sealed class WaveAudioPlayer : IDisposable
{
    private WaveFileReader? _reader;
    private WaveFileReader? _exitReader;
    private WaveOutEvent? _output;
    private StereoFloatWaveProvider? _provider;
    private string? _path;
    private bool _isPlaying;
    private bool _disposed;
    private LoopPlaybackPlan[] _loopPlans = [];
    private LoopPlaybackPlan? _activePlan;

    public event EventHandler? PlaybackEnded;

    public bool IsPlaying => _isPlaying;

    public bool HasSource => !string.IsNullOrEmpty(_path);

    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    /// <summary>0〜1。長さ不明時は 0。</summary>
    public double Progress
    {
        get
        {
            var duration = Duration;
            if (duration <= TimeSpan.Zero)
            {
                return 0;
            }

            return Math.Clamp(Position.TotalSeconds / duration.TotalSeconds, 0d, 1d);
        }
    }

    /// <summary>
    /// <c>-L</c> 連続区間と、直後の連続 <c>-E</c>（あれば）を再生プランにする。
    /// </summary>
    public static LoopPlaybackPlan[] BuildLoopPlans(IReadOnlyList<WaveformRegionMark> regions)
    {
        if (regions.Count == 0)
        {
            return [];
        }

        var plans = new List<LoopPlaybackPlan>();
        long? runStart = null;
        long runEnd = 0;
        var runEndIndex = -1;

        void FlushLoopRun()
        {
            if (runStart is not long start || runEnd <= start || runEndIndex < 0)
            {
                runStart = null;
                runEndIndex = -1;
                return;
            }

            long? exitEnd = null;
            var expectedStart = runEnd;
            for (var j = runEndIndex + 1; j < regions.Count; j++)
            {
                var region = regions[j];
                if (region.IsExcluded
                    || !region.NameSuffix.Equals(
                        WaveformRegionBuilder.LoopEndSuffix,
                        StringComparison.OrdinalIgnoreCase)
                    || region.StartSampleOffset != expectedStart)
                {
                    break;
                }

                exitEnd = region.EndSampleOffset;
                expectedStart = region.EndSampleOffset;
            }

            plans.Add(new LoopPlaybackPlan(start, runEnd, exitEnd));
            runStart = null;
            runEndIndex = -1;
        }

        for (var i = 0; i < regions.Count; i++)
        {
            var region = regions[i];
            var isLoop = !region.IsExcluded
                && region.NameSuffix.Equals(
                    WaveformRegionBuilder.LoopLeftSuffix,
                    StringComparison.OrdinalIgnoreCase);
            if (isLoop)
            {
                if (runStart is null)
                {
                    runStart = region.StartSampleOffset;
                }

                runEnd = region.EndSampleOffset;
                runEndIndex = i;
                continue;
            }

            FlushLoopRun();
        }

        FlushLoopRun();
        return plans.ToArray();
    }

    /// <summary>互換: ループ本体だけが必要な場合。</summary>
    public static LoopSampleRange[] BuildLoopRanges(IReadOnlyList<WaveformRegionMark> regions) =>
        BuildLoopPlans(regions)
            .Select(p => new LoopSampleRange(p.LoopStartSample, p.LoopEndSample))
            .ToArray();

    public void SetLoopPlans(IReadOnlyList<LoopPlaybackPlan> plans)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _loopPlans = plans.Count == 0 ? [] : plans.ToArray();
        _activePlan = null;
        PushActivePlanToProvider();
    }

    public void SetLoopRanges(IReadOnlyList<LoopSampleRange> ranges) =>
        SetLoopPlans(ranges.Select(r => new LoopPlaybackPlan(r.StartSample, r.EndSample, null)).ToArray());

    public void ClearLoopRanges() => SetLoopPlans([]);

    /// <summary>
    /// 現在位置がループ区間内ならその区間だけを有効化。外ならループ／Exit 解除。
    /// シークで別位置へ飛んだときは必ず呼び、区間外なら二重再生の Exit も直ちに止める。
    /// </summary>
    public void ArmLoopAtProgress(double progress)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _activePlan = FindPlanAtProgress(progress);
        if (_activePlan is { } plan)
        {
            // アーム時点では Exit は始めない。ループ末端→頭の折り返しで開始する。
            _provider?.SetActivePlan(plan);
        }
        else
        {
            _provider?.SetActivePlan(null);
            _provider?.StopExitLayer();
        }
    }

    public void ClearActiveLoop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _activePlan = null;
        _provider?.SetActivePlan(null);
        _provider?.StopExitLayer();
    }

    /// <summary>有効中のループ区間（進捗 0〜1）。未アームなら false。</summary>
    public bool TryGetActiveLoopProgress(out double start, out double end)
    {
        start = 0;
        end = 0;
        if (_reader is null || _activePlan is not { } plan)
        {
            return false;
        }

        var frameCount = FrameCount;
        if (frameCount <= 0 || plan.LoopEndSample <= plan.LoopStartSample)
        {
            return false;
        }

        start = plan.LoopStartSample / (double)frameCount;
        end = plan.LoopEndSample / (double)frameCount;
        return end > start;
    }

    /// <summary>
    /// カタログ上、<paramref name="progress"/> が含まれるループ区間があるか（アーム状態は問わない）。
    /// </summary>
    public bool TryGetLoopProgress(double progress, out double start, out double end)
    {
        start = 0;
        end = 0;
        if (FindPlanAtProgress(progress) is not { } plan)
        {
            return false;
        }

        var frameCount = FrameCount;
        if (frameCount <= 0)
        {
            return false;
        }

        start = plan.LoopStartSample / (double)frameCount;
        end = plan.LoopEndSample / (double)frameCount;
        return end > start;
    }

    /// <summary>
    /// Exit 二重再生ヘッドの位置（0〜1）。再生していなければ false。
    /// </summary>
    public bool TryGetExitPlaybackProgress(out double progress)
    {
        progress = 0;
        if (_provider is null || _reader is null)
        {
            return false;
        }

        var frameCount = FrameCount;
        var sampleRate = _reader.WaveFormat.SampleRate;
        return _provider.TryGetExitPlaybackProgress(frameCount, sampleRate, out progress);
    }

    private long FrameCount =>
        _reader is null
            ? 0
            : _reader.Length / Math.Max(1, _reader.WaveFormat.BlockAlign);

    private LoopPlaybackPlan? FindPlanAtProgress(double progress)
    {
        if (_reader is null || _loopPlans.Length == 0)
        {
            return null;
        }

        var frameCount = FrameCount;
        if (frameCount <= 0)
        {
            return null;
        }

        var sample = (long)Math.Clamp(Math.Floor(Math.Clamp(progress, 0d, 1d) * frameCount), 0, frameCount - 1);
        foreach (var plan in _loopPlans)
        {
            if (sample >= plan.LoopStartSample && sample < plan.LoopEndSample)
            {
                return plan;
            }
        }

        return null;
    }

    private void PushActivePlanToProvider()
    {
        if (_provider is null)
        {
            return;
        }

        if (_activePlan is { } plan)
        {
            _provider.SetActivePlan(plan);
        }
        else
        {
            _provider.SetActivePlan(null);
            _provider.StopExitLayer();
        }
    }

    public void Load(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        StopAndRelease();
        _path = path;
        // AudioFileReader は多チャンネル Extensible の float 変換で
        // ACM（acmFormatSuggest）に頼り NoDriver で失敗するため、変換は自前で行う
        var info = WavFileInfo.Read(path);
        _reader = new WaveFileReader(path);
        _exitReader = new WaveFileReader(path);
        _provider = new StereoFloatWaveProvider(_reader, _exitReader, info);
        PushActivePlanToProvider();
        _output = new WaveOutEvent();
        _output.Init(_provider);
        _output.PlaybackStopped += OnPlaybackStopped;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        StopAndRelease();
        _path = null;
        _loopPlans = [];
        _activePlan = null;
    }

    public void Play()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_output is null || _reader is null)
        {
            return;
        }

        if (_reader.Position >= _reader.Length)
        {
            _reader.Position = 0;
        }

        _output.Play();
        _isPlaying = true;
    }

    public void Pause()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_output is null || !_isPlaying)
        {
            return;
        }

        _output.Pause();
        _isPlaying = false;
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_output is null || _reader is null)
        {
            _isPlaying = false;
            return;
        }

        _output.Stop();
        _reader.Position = 0;
        _provider?.StopExitLayer();
        _isPlaying = false;
    }

    /// <summary>再生中なら一時停止、停止中なら再生。</summary>
    public void Toggle()
    {
        if (_isPlaying)
        {
            Pause();
        }
        else
        {
            Play();
        }
    }

    /// <summary>位置を 0〜1 でシークする。</summary>
    public void Seek(double progress)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_reader is null)
        {
            return;
        }

        // ジャンプ時は Exit 二重再生を直ちに止める（Arm 前でも確実に）
        _provider?.StopExitLayer();

        var duration = _reader.TotalTime;
        if (duration <= TimeSpan.Zero)
        {
            return;
        }

        var clamped = Math.Clamp(progress, 0d, 1d);
        // 終端ぴったりだと即 MediaEnded 扱いになることがあるためわずかに手前へ
        var ticks = (long)(duration.Ticks * clamped);
        if (clamped >= 1d && duration.Ticks > 0)
        {
            ticks = Math.Max(0, duration.Ticks - 1);
        }

        _reader.CurrentTime = TimeSpan.FromTicks(ticks);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAndRelease();
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (_reader is null)
        {
            return;
        }

        // 末尾到達時のみ終了扱い（Stop 呼び出しでも発火するため位置で判定）
        // ループ中はプロバイダが折り返すので、ここに来るのは真の EOF／Stop
        if (_reader.Position >= _reader.Length || _reader.CurrentTime >= _reader.TotalTime)
        {
            _reader.Position = 0;
            _provider?.StopExitLayer();
            _isPlaying = false;
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StopAndRelease()
    {
        _isPlaying = false;
        _provider?.StopExitLayer();
        _provider = null;

        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        if (_reader is not null)
        {
            _reader.Dispose();
            _reader = null;
        }

        if (_exitReader is not null)
        {
            _exitReader.Dispose();
            _exitReader = null;
        }
    }

    /// <summary>
    /// PCM / IEEE float の WAV を ACM を使わずステレオ float に変換する再生用プロバイダ。
    /// メインはループ折り返し、Exit は別リーダでワンショット二重再生してミックスする。
    /// </summary>
    private sealed class StereoFloatWaveProvider : IWaveProvider
    {
        private const float FoldGain = 0.7071f;

        private readonly WaveFileReader _source;
        private readonly WaveFileReader _exitSource;
        private readonly Func<byte[], int, float> _sampleReader;
        private readonly int _sourceBlockAlign;
        private readonly int _channels;
        private readonly int _bytesPerSample;
        private readonly float _normalize;
        private byte[] _pcmScratch = [];
        private byte[] _mainFloat = [];
        private byte[] _exitFloat = [];
        private readonly object _gate = new();
        private LoopPlaybackPlan? _activePlan;
        private bool _exitPlaying;
        private long _exitStartSample;
        private long _exitEndSample;
        private long _exitStartTickMs;

        public StereoFloatWaveProvider(WaveFileReader source, WaveFileReader exitSource, WavFileInfo info)
        {
            if (info.Channels == 0 || info.BlockAlign == 0 || info.SampleRate == 0)
            {
                throw new InvalidDataException("波形フォーマットが不正です。");
            }

            _source = source;
            _exitSource = exitSource;
            _sampleReader = WavPeakReader.CreateSampleReader(info.AudioFormat, info.BitsPerSample);
            _channels = info.Channels;
            _sourceBlockAlign = info.BlockAlign;
            _bytesPerSample = info.BitsPerSample / 8;
            var extraChannels = Math.Max(0, _channels - 2);
            _normalize = 1f / (1f + extraChannels * FoldGain);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat((int)info.SampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public void SetActivePlan(LoopPlaybackPlan? plan)
        {
            lock (_gate)
            {
                _activePlan = plan;
                // アーム／解除だけでは Exit を始めない（ループ折り返しで開始）
                _exitPlaying = false;
            }
        }

        public void StopExitLayer()
        {
            lock (_gate)
            {
                _exitPlaying = false;
            }
        }

        /// <summary>
        /// ループ末端→頭の折り返しと同時に Exit 二重再生を開始／頭から再開する。
        /// </summary>
        private void BeginExitOnLoopWrap(LoopPlaybackPlan loop)
        {
            if (!loop.HasExit)
            {
                return;
            }

            lock (_gate)
            {
                _exitStartSample = loop.LoopEndSample;
                _exitEndSample = loop.ExitEndSample!.Value;
                _exitPlaying = true;
                _exitStartTickMs = Environment.TickCount64;
                SeekExitToSample(_exitStartSample);
            }
        }

        private void WrapMainToLoopStart(LoopPlaybackPlan loop)
        {
            SeekToSample(_source, loop.LoopStartSample);
            BeginExitOnLoopWrap(loop);
        }

        /// <summary>
        /// Exit 二重再生の現在位置（ファイル全体の 0〜1）。再生中でなければ false。
        /// 壁時計ベース（メイン再生ヘッドと同様にバッファ位置の揺れを避ける）。
        /// </summary>
        public bool TryGetExitPlaybackProgress(long frameCount, int sampleRate, out double progress)
        {
            progress = 0;
            if (frameCount <= 0 || sampleRate <= 0)
            {
                return false;
            }

            long exitStart;
            long exitEnd;
            long startTick;
            lock (_gate)
            {
                if (!_exitPlaying)
                {
                    return false;
                }

                exitStart = _exitStartSample;
                exitEnd = _exitEndSample;
                startTick = _exitStartTickMs;
            }

            if (exitEnd <= exitStart)
            {
                return false;
            }

            var elapsedSec = Math.Max(0, (Environment.TickCount64 - startTick) / 1000d);
            var sample = exitStart + (long)(elapsedSec * sampleRate);
            if (sample >= exitEnd)
            {
                return false;
            }

            progress = sample / (double)frameCount;
            return true;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            var framesNeeded = count / 8;
            if (framesNeeded <= 0)
            {
                return 0;
            }

            var outIndex = offset;
            var totalFrames = 0;
            while (totalFrames < framesNeeded)
            {
                LoopPlaybackPlan? plan;
                var exitPlaying = false;
                long exitStart = 0;
                long exitEnd = 0;
                lock (_gate)
                {
                    plan = _activePlan;
                    exitPlaying = _exitPlaying;
                    exitStart = _exitStartSample;
                    exitEnd = _exitEndSample;
                }

                var samplePos = CurrentSample(_source);
                var framesThis = framesNeeded - totalFrames;

                if (plan is { } loop)
                {
                    if (samplePos >= loop.LoopEndSample)
                    {
                        WrapMainToLoopStart(loop);
                        continue;
                    }

                    var untilEnd = loop.LoopEndSample - samplePos;
                    if (untilEnd <= 0)
                    {
                        WrapMainToLoopStart(loop);
                        continue;
                    }

                    framesThis = (int)Math.Min(framesThis, untilEnd);
                }

                // メインを float バッファへ
                EnsureBuffer(ref _mainFloat, framesThis * 8);
                var gotFrames = ReadDecodedFrames(_source, _mainFloat, 0, framesThis);
                if (gotFrames <= 0)
                {
                    break;
                }

                // Exit レイヤ（同時長）。停止／終了後は 0。
                // このチャンク読了後に折り返す場合は、折り返し後の続きで Exit が乗る。
                EnsureBuffer(ref _exitFloat, gotFrames * 8);
                Array.Clear(_exitFloat, 0, gotFrames * 8);
                if (exitPlaying)
                {
                    MixExitLayer(_exitFloat, 0, gotFrames, exitStart, exitEnd);
                }

                // 加算ミックス（簡易クリップ）
                for (var i = 0; i < gotFrames; i++)
                {
                    var mainL = BitConverter.ToSingle(_mainFloat, i * 8);
                    var mainR = BitConverter.ToSingle(_mainFloat, i * 8 + 4);
                    var exitL = BitConverter.ToSingle(_exitFloat, i * 8);
                    var exitR = BitConverter.ToSingle(_exitFloat, i * 8 + 4);
                    BitConverter.TryWriteBytes(buffer.AsSpan(outIndex + i * 8, 4), ClampSample(mainL + exitL));
                    BitConverter.TryWriteBytes(buffer.AsSpan(outIndex + i * 8 + 4, 4), ClampSample(mainR + exitR));
                }

                if (plan is { } armed
                    && CurrentSample(_source) >= armed.LoopEndSample)
                {
                    WrapMainToLoopStart(armed);
                }

                outIndex += gotFrames * 8;
                totalFrames += gotFrames;
            }

            return totalFrames * 8;
        }

        private void MixExitLayer(byte[] dest, int destOffset, int frames, long exitStart, long exitEnd)
        {
            var written = 0;
            while (written < frames)
            {
                bool playing;
                lock (_gate)
                {
                    playing = _exitPlaying;
                    if (!playing)
                    {
                        return;
                    }
                }

                var pos = CurrentSample(_exitSource);
                if (pos < exitStart)
                {
                    SeekExitToSample(exitStart);
                    pos = exitStart;
                }

                if (pos >= exitEnd)
                {
                    lock (_gate)
                    {
                        _exitPlaying = false;
                    }

                    return;
                }

                var chunk = (int)Math.Min(frames - written, exitEnd - pos);
                var got = ReadDecodedFrames(_exitSource, dest, destOffset + written * 8, chunk);
                if (got <= 0)
                {
                    lock (_gate)
                    {
                        _exitPlaying = false;
                    }

                    return;
                }

                written += got;
                if (CurrentSample(_exitSource) >= exitEnd)
                {
                    lock (_gate)
                    {
                        _exitPlaying = false;
                    }

                    return;
                }
            }
        }

        private int ReadDecodedFrames(WaveFileReader reader, byte[] dest, int destOffset, int frames)
        {
            if (frames <= 0)
            {
                return 0;
            }

            var sourceBytes = frames * _sourceBlockAlign;
            EnsureBuffer(ref _pcmScratch, sourceBytes);

            var got = reader.Read(_pcmScratch, 0, sourceBytes);
            var gotFrames = got / _sourceBlockAlign;
            var writeAt = destOffset;
            for (var i = 0; i < gotFrames; i++)
            {
                var frameOffset = i * _sourceBlockAlign;
                float left;
                float right;
                if (_channels == 1)
                {
                    left = right = _sampleReader(_pcmScratch, frameOffset);
                }
                else
                {
                    left = _sampleReader(_pcmScratch, frameOffset);
                    right = _sampleReader(_pcmScratch, frameOffset + _bytesPerSample);
                    for (var ch = 2; ch < _channels; ch++)
                    {
                        var v = _sampleReader(_pcmScratch, frameOffset + ch * _bytesPerSample) * FoldGain;
                        left += v;
                        right += v;
                    }

                    left *= _normalize;
                    right *= _normalize;
                }

                BitConverter.TryWriteBytes(dest.AsSpan(writeAt, 4), left);
                BitConverter.TryWriteBytes(dest.AsSpan(writeAt + 4, 4), right);
                writeAt += 8;
            }

            return gotFrames;
        }

        private static void EnsureBuffer(ref byte[] buffer, int bytes)
        {
            if (buffer.Length < bytes)
            {
                buffer = new byte[bytes];
            }
        }

        private static float ClampSample(float value) =>
            value < -1f ? -1f : value > 1f ? 1f : value;

        private long CurrentSample(WaveFileReader reader) =>
            _sourceBlockAlign <= 0 ? 0 : reader.Position / _sourceBlockAlign;

        private void SeekToSample(WaveFileReader reader, long sample)
        {
            var safe = Math.Max(0, sample);
            reader.Position = safe * (long)_sourceBlockAlign;
        }

        private void SeekExitToSample(long sample) => SeekToSample(_exitSource, sample);
    }
}

/// <summary>ループ本体（終端排他）と、直後 Exit の終端（無ければ null）。</summary>
internal readonly record struct LoopPlaybackPlan(
    long LoopStartSample,
    long LoopEndSample,
    long? ExitEndSample)
{
    public bool HasExit => ExitEndSample is long end && end > LoopEndSample;
}

/// <summary>ループ区間（終端サンプルは排他）。</summary>
internal readonly record struct LoopSampleRange(long StartSample, long EndSample);
