using NAudio.Wave;

namespace MgaWwiseImporter.Wave;

/// <summary>
/// Wave ファイルの再生。位置は Position で取得する。
/// 変換は自前で行い ACM ドライバに依存しない（マルチチャンネル／Extensible 対応）。
/// </summary>
internal sealed class WaveAudioPlayer : IDisposable
{
    private WaveFileReader? _reader;
    private WaveOutEvent? _output;
    private string? _path;
    private bool _isPlaying;
    private bool _disposed;

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

    public void Load(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        StopAndRelease();
        _path = path;
        // AudioFileReader は多チャンネル Extensible の float 変換で
        // ACM（acmFormatSuggest）に頼り NoDriver で失敗するため、変換は自前で行う
        var info = WavFileInfo.Read(path);
        _reader = new WaveFileReader(path);
        _output = new WaveOutEvent();
        _output.Init(new StereoFloatWaveProvider(_reader, info));
        _output.PlaybackStopped += OnPlaybackStopped;
    }

    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        StopAndRelease();
        _path = null;
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
        if (_reader.Position >= _reader.Length || _reader.CurrentTime >= _reader.TotalTime)
        {
            _reader.Position = 0;
            _isPlaying = false;
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StopAndRelease()
    {
        _isPlaying = false;

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
    }

    /// <summary>
    /// PCM / IEEE float の WAV を ACM を使わずステレオ float に変換する再生用プロバイダ。
    /// 3ch 以上は前 2ch を左右に据え、残りのチャンネルを両側へ -3dB で折り込む。
    /// </summary>
    private sealed class StereoFloatWaveProvider : IWaveProvider
    {
        private const float FoldGain = 0.7071f;

        private readonly WaveFileReader _source;
        private readonly Func<byte[], int, float> _sampleReader;
        private readonly int _sourceBlockAlign;
        private readonly int _channels;
        private readonly int _bytesPerSample;
        private readonly float _normalize;
        private byte[] _sourceBuffer = [];

        public StereoFloatWaveProvider(WaveFileReader source, WavFileInfo info)
        {
            if (info.Channels == 0 || info.BlockAlign == 0 || info.SampleRate == 0)
            {
                throw new InvalidDataException("波形フォーマットが不正です。");
            }

            _source = source;
            _sampleReader = WavPeakReader.CreateSampleReader(info.AudioFormat, info.BitsPerSample);
            _channels = info.Channels;
            _sourceBlockAlign = info.BlockAlign;
            _bytesPerSample = info.BitsPerSample / 8;
            var extraChannels = Math.Max(0, _channels - 2);
            _normalize = 1f / (1f + extraChannels * FoldGain);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat((int)info.SampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(byte[] buffer, int offset, int count)
        {
            // 出力 1 フレーム = float L + float R = 8 バイト
            var frames = count / 8;
            if (frames <= 0)
            {
                return 0;
            }

            var sourceBytes = frames * _sourceBlockAlign;
            if (_sourceBuffer.Length < sourceBytes)
            {
                _sourceBuffer = new byte[sourceBytes];
            }

            var got = _source.Read(_sourceBuffer, 0, sourceBytes);
            var gotFrames = got / _sourceBlockAlign;
            var outIndex = offset;
            for (var i = 0; i < gotFrames; i++)
            {
                var frameOffset = i * _sourceBlockAlign;
                float left;
                float right;
                if (_channels == 1)
                {
                    left = right = _sampleReader(_sourceBuffer, frameOffset);
                }
                else
                {
                    left = _sampleReader(_sourceBuffer, frameOffset);
                    right = _sampleReader(_sourceBuffer, frameOffset + _bytesPerSample);
                    for (var ch = 2; ch < _channels; ch++)
                    {
                        var v = _sampleReader(_sourceBuffer, frameOffset + ch * _bytesPerSample) * FoldGain;
                        left += v;
                        right += v;
                    }

                    left *= _normalize;
                    right *= _normalize;
                }

                BitConverter.TryWriteBytes(buffer.AsSpan(outIndex, 4), left);
                BitConverter.TryWriteBytes(buffer.AsSpan(outIndex + 4, 4), right);
                outIndex += 8;
            }

            return gotFrames * 8;
        }
    }
}
