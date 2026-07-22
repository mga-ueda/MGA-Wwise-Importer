using NAudio.Wave;
using MgaWwiseIMImporter.UI;

namespace MgaWwiseIMImporter.Wave;

internal enum PlaylistDestinationSyncMode
{
    EntryCue,
    SameTime,
}

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
    private WaveFileReader? _playlistFadeReader;
    private WaveFileReader? _playlistExitFadeReader;
    private WaveFileReader? _playlistPreRollReader;
    private IWavePlayer? _output;
    private StereoFloatWaveProvider? _provider;
    private string? _path;
    /// <summary>再生専用の一時コピー。元ファイルをロックしない。</summary>
    private string? _playbackCopyPath;
    private bool _isPlaying;
    private bool _disposed;
    private bool _suppressPlaybackEnded;
    /// <summary>一時停止中にシークしたか。次の再生前にデバイスの先読みバッファを破棄する。</summary>
    private bool _seekPendingWhilePaused;
    private LoopPlaybackPlan[] _loopPlans = [];
    private LoopPlaybackPlan? _activePlan;
    private AudioOutputSettings _outputSettings = AudioOutputSettings.Default;

    public event EventHandler? PlaybackEnded;
    public event EventHandler<string>? Diagnostic;

    public bool IsPlaying => _isPlaying;

    public bool HasSource => !string.IsNullOrEmpty(_path);

    public TimeSpan Position => _reader?.CurrentTime ?? TimeSpan.Zero;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    /// <summary>直近に生成した出力バッファのピーク値（0〜1）。</summary>
    public float OutputPeak => _provider?.OutputPeak ?? 0f;

    /// <summary>出力フォーマットのサンプルレート。未ロード時は 0。</summary>
    public int OutputSampleRate => _provider?.WaveFormat.SampleRate ?? 0;

    /// <summary>
    /// 直近の出力サンプル（モノラルミックス）を destination の末尾詰めでコピーする。
    /// スペアナ表示用。戻り値は書き込んだサンプル数。
    /// </summary>
    public int ReadRecentOutputSamples(float[] destination) =>
        _provider?.CopyRecentOutputSamples(destination) ?? 0;

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

    public void SetLoopPlans(IReadOnlyList<LoopPlaybackPlan> plans)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _loopPlans = plans.Count == 0 ? [] : plans.ToArray();
        _activePlan = null;
        PushActivePlanToProvider();
    }

    /// <summary>
    /// リージョン端フェード（プレビュー用）。Playlist 遷移フェードと乗算で重ねがけする。
    /// </summary>
    public void SetRegionEdgeFades(IReadOnlyList<RegionEdgeFade>? fades)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider?.SetRegionEdgeFades(fades);
    }

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

    /// <summary>有効中のループ区間（進捗 0〜1）。未アームなら false。</summary>
    public bool TryGetActiveLoopProgress(out double start, out double end)
    {
        start = 0;
        end = 0;
        // Provider が存在する場合、その null は「現在の Playlist に有効なループなし」を意味する。
        // ?? で _activePlan へ戻すと、Playlist 遷移前の古いループを UI が再利用してしまう。
        var plan = _provider is not null
            ? _provider.GetActivePlan()
            : _activePlan;
        if (_reader is null || plan is not { } activePlan)
        {
            return false;
        }

        var frameCount = FrameCount;
        if (frameCount <= 0 || activePlan.LoopEndSample <= activePlan.LoopStartSample)
        {
            return false;
        }

        start = activePlan.LoopStartSample / (double)frameCount;
        end = activePlan.LoopEndSample / (double)frameCount;
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

    public bool TryGetPlaylistFadePlaybackProgress(
        out double progress,
        out bool isExit)
    {
        progress = 0d;
        isExit = false;
        if (_provider is null || _reader is null)
        {
            return false;
        }

        return _provider.TryGetPlaylistFadePlaybackProgress(
            FrameCount,
            _reader.WaveFormat.SampleRate,
            out progress,
            out isExit);
    }

    private long FrameCount =>
        _reader is null
            ? 0
            : _reader.Length / Math.Max(1, _reader.WaveFormat.BlockAlign);

    public long CurrentMainSample => _provider?.CurrentMainSample ?? 0L;

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

    private LoopPlaybackPlan? FindPlanAtSample(long sample)
    {
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

    /// <summary>
    /// 停止／一時停止中に Playlist 範囲の先頭へ移動して再生を開始する。
    /// 範囲は開始を含み、終了を含まないソース WAV のサンプル位置。
    /// </summary>
    public bool StartPlaylistRange(long startSample, long endSample)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_isPlaying
            || _provider is null
            || _output is null
            || !IsValidPlaylistRange(startSample, endSample))
        {
            Trace($"playlist.start rejected playing={_isPlaying} provider={_provider is not null} output={_output is not null} start={startSample} end={endSample} frames={FrameCount}");
            return false;
        }

        // Pause はデバイス側の先読みバッファを保持するため、停止中選択では一度破棄し、
        // 対象 Playlist の先頭が最初に鳴ることを保証する。
        _provider.ClearPlaylistPlayback();
        _suppressPlaybackEnded = true;
        try
        {
            _output.Stop();
        }
        finally
        {
            _suppressPlaybackEnded = false;
        }
        var plan = FindPlanAtSample(startSample);
        _provider.StartPlaylistRange(startSample, endSample, plan);
        _activePlan = plan;
        _seekPendingWhilePaused = false;
        _output.Play();
        _isPlaying = true;
        Trace($"playlist.start accepted start={startSample} end={endSample} loopPlan={plan?.ToString() ?? "none"}");
        return true;
    }

    /// <summary>
    /// 退出境界の手前へアウフタクトを重ね、境界でメインを切り替えて
    /// 旧 Playlist のフェードを始める。退出境界が null なら即時遷移。
    /// </summary>
    public bool TrySchedulePlaylistTransition(
        long startSample,
        long endSample,
        long? sourceExitSample,
        long sourcePartStartSample,
        PlaylistDestinationSyncMode destinationSyncMode,
        long preRollFrameCount,
        bool allowShortPreRoll,
        long fadeSourceEndSample,
        double fadeInSeconds,
        double fadeSeconds,
        out PlaylistTransitionSchedule schedule)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        schedule = default;
        if (!_isPlaying
            || _provider is null
            || sourceExitSample is < 0
            || sourceExitSample > FrameCount
            || sourcePartStartSample < 0
            || sourcePartStartSample > FrameCount
            || preRollFrameCount < 0
            || fadeSourceEndSample > FrameCount
            || !double.IsFinite(fadeInSeconds)
            || fadeInSeconds < 0d
            || !double.IsFinite(fadeSeconds)
            || fadeSeconds < 0d
            || !IsValidPlaylistRange(startSample, endSample))
        {
            Trace($"playlist.schedule rejected playing={_isPlaying} start={startSample} end={endSample} sourceExit={sourceExitSample?.ToString() ?? "immediate"} sourcePartStart={sourcePartStartSample} destinationSync={destinationSyncMode} preRoll={preRollFrameCount} allowShortPreRoll={allowShortPreRoll} fadeEnd={fadeSourceEndSample} fadeInSeconds={fadeInSeconds:R} fadeOutSeconds={fadeSeconds:R} frames={FrameCount}");
            return false;
        }

        var fadeInFrameCount = fadeInSeconds <= 0d
            ? 0
            : Math.Max(
                1,
                (int)Math.Min(
                    int.MaxValue,
                    Math.Round(_provider.WaveFormat.SampleRate * fadeInSeconds)));
        var fadeFrameCount = fadeSeconds <= 0d
            ? 0
            : Math.Max(
                1,
                (int)Math.Min(
                    int.MaxValue,
                    Math.Round(_provider.WaveFormat.SampleRate * fadeSeconds)));
        var accepted = _provider.TrySchedulePlaylistTransition(
            startSample,
            endSample,
            sourceExitSample,
            sourcePartStartSample,
            destinationSyncMode,
            preRollFrameCount,
            allowShortPreRoll,
            fadeSourceEndSample,
            fadeInFrameCount,
            fadeFrameCount,
            FindPlanAtSample,
            out schedule);
        Trace($"playlist.schedule accepted={accepted} generation={schedule.Generation} start={startSample} end={endSample} destinationSync={destinationSyncMode} sourceRelative={schedule.SourceRelativeSample} trigger={schedule.TriggerSample} sync={schedule.SyncBoundarySample} targetSwitch={schedule.TargetSwitchSample} rejection={schedule.RejectionReason ?? "none"} startedImmediately={schedule.StartedImmediately} fadeEnd={fadeSourceEndSample} fadeInSeconds={fadeInSeconds:R} fadeInFrames={fadeInFrameCount} fadeOutSeconds={fadeSeconds:R} fadeOutFrames={fadeFrameCount}");
        return accepted;
    }

    /// <summary>未開始の予約と進行中の旧 Playlist フェードを解除する。</summary>
    public void CancelPlaylistTransition()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _provider?.CancelPlaylistTransition();
        Trace("playlist.cancel-transition");
    }

    /// <summary>Form1 のポーリング用 Playlist 遷移状態。</summary>
    public bool TryGetPlaylistTransitionState(out PlaylistTransitionState state)
    {
        state = default;
        return _provider?.TryGetPlaylistTransitionState(out state) == true;
    }

    private bool IsValidPlaylistRange(long startSample, long endSample) =>
        startSample >= 0 && endSample > startSample && endSample <= FrameCount;

    public void Load(string path)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        StopAndRelease();
        _path = path;
        // 元 WAV を掴み続けないよう、再生用に一時コピーを開く。
        // （外部アプリが同じファイルへ上書き保存できるようにする）
        _playbackCopyPath = CreatePlaybackCopy(path);
        OpenReadersFromPlaybackCopy(path);
    }

    /// <summary>
    /// 複数波形の仮想タイムライン再生用。ソースを一時連結 WAV にして開く（Export 元には使わない）。
    /// </summary>
    public void LoadVirtualConcat(IReadOnlyList<WaveformSourceSpan> spans)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (spans.Count == 0)
        {
            throw new ArgumentException(UiStrings.ErrMultiWaveOnlyNoSpans);
        }

        StopAndRelease();
        _path = spans[0].Path;
        _playbackCopyPath = WavConcatWriter.WriteTempConcat(spans);
        OpenReadersFromPlaybackCopy(_playbackCopyPath);
    }

    private void OpenReadersFromPlaybackCopy(string formatSourcePath)
    {
        // AudioFileReader は多チャンネル Extensible の float 変換で
        // ACM（acmFormatSuggest）に頼り NoDriver で失敗するため、変換は自前で行う
        var info = WavFileInfo.Read(formatSourcePath);
        _reader = new WaveFileReader(_playbackCopyPath!);
        _exitReader = new WaveFileReader(_playbackCopyPath!);
        _playlistFadeReader = new WaveFileReader(_playbackCopyPath!);
        _playlistExitFadeReader = new WaveFileReader(_playbackCopyPath!);
        _playlistPreRollReader = new WaveFileReader(_playbackCopyPath!);
        _provider = new StereoFloatWaveProvider(
            _reader,
            _exitReader,
            _playlistFadeReader,
            _playlistExitFadeReader,
            _playlistPreRollReader,
            info,
            message => Trace(message));
        PushActivePlanToProvider();
        InitOutputDevice();
    }

    /// <summary>
    /// 出力 API／デバイスを差し替える。ソース未ロード時は次回 <see cref="Load"/> で反映。
    /// ロード済みなら再生位置を保って出力だけ再初期化する。
    /// </summary>
    public void ApplyOutputSettings(AudioOutputSettings settings)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _outputSettings = settings;
        if (_provider is null)
        {
            Trace(
                $"audio.output-settings api={AudioOutputSettings.ToIniValue(settings.Api)}"
                + $" device='{settings.DeviceId}' (deferred)");
            return;
        }

        var progress = Progress;
        var wasPlaying = _isPlaying;
        DisposeOutputOnly();
        InitOutputDevice();
        Seek(progress);
        if (wasPlaying)
        {
            Play();
        }
    }

    private void InitOutputDevice()
    {
        if (_provider is null)
        {
            return;
        }

        // AsioOut はコンストラクタで SynchronizationContext.Current を掴む。
        // 背景スレッドで Init すると再生不能／フォールバックになるため UI スレッドへ延期する。
        if (_outputSettings.Api == AudioOutputApi.Asio
            && SynchronizationContext.Current is null)
        {
            Trace(
                $"audio.output-defer api=Asio device='{_outputSettings.DeviceId}'"
                + " (requires UI SynchronizationContext)");
            return;
        }

        try
        {
            _output = AudioOutputFactory.Create(_outputSettings, out var fallbackMessage);
            if (!string.IsNullOrEmpty(fallbackMessage))
            {
                Trace($"audio.output-fallback {fallbackMessage}");
                Diagnostic?.Invoke(this, fallbackMessage);
                // 要求設定は保持する（次回 UI スレッドでの再試行・ダイアログ表示のため）
            }

            _output.Init(_provider);
        }
        catch (Exception ex)
        {
            DisposeOutputOnly();
            var message =
                $"Output init failed ({AudioOutputSettings.ToIniValue(_outputSettings.Api)}"
                + $" '{_outputSettings.DeviceId}'): {ex.Message}; falling back to WaveOut default.";
            Trace($"audio.output-fallback {message}");
            Diagnostic?.Invoke(this, message);
            _output = AudioOutputFactory.Create(AudioOutputSettings.Default, out _);
            _output.Init(_provider);
        }

        _output.PlaybackStopped += OnPlaybackStopped;
        Trace(
            $"audio.output-ready api={AudioOutputSettings.ToIniValue(_outputSettings.Api)}"
            + $" device='{_outputSettings.DeviceId}'"
            + $" type={_output.GetType().Name}");
    }

    /// <summary>
    /// 出力デバイスが未初期化なら現在の設定で初期化する（UI スレッドから呼ぶこと）。
    /// </summary>
    public void EnsureOutputDevice()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_provider is null || _output is not null)
        {
            return;
        }

        InitOutputDevice();
    }

    private void DisposeOutputOnly()
    {
        _isPlaying = false;
        if (_output is null)
        {
            return;
        }

        _suppressPlaybackEnded = true;
        try
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
        }
        finally
        {
            _suppressPlaybackEnded = false;
            _output = null;
        }
    }

    /// <summary>
    /// 元ファイルを一時領域へコピーし、そのパスを返す。
    /// 失敗時は呼び元が元ファイルを掴む状態にならないよう、コピーを破棄する。
    /// </summary>
    private static string CreatePlaybackCopy(string sourcePath)
    {
        var extension = Path.GetExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".wav";
        }

        var copyPath = Path.Combine(
            Path.GetTempPath(),
            $"mga-wwise-playback-{Guid.NewGuid():N}{extension}");
        try
        {
            File.Copy(sourcePath, copyPath, overwrite: true);
            return copyPath;
        }
        catch
        {
            TryDeleteFile(copyPath);
            throw;
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 一時ファイル削除失敗は致命的ではない。
        }
        catch (UnauthorizedAccessException)
        {
            // 一時ファイル削除失敗は致命的ではない。
        }
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

        EnsureOutputDevice();
        if (_output is null || _reader is null)
        {
            return;
        }

        if (_reader.Position >= _reader.Length)
        {
            _reader.Position = 0;
        }

        // 一時停止中にシークしていた場合、デバイスの先読みバッファには旧位置の音が
        // 残っている。そのまま Play すると新しい位置の前に旧位置の音が一瞬鳴るため、
        // 一度 Stop してバッファを破棄してから再生する（リーダー位置は保持される）。
        if (_seekPendingWhilePaused)
        {
            _suppressPlaybackEnded = true;
            try
            {
                _output.Stop();
            }
            finally
            {
                _suppressPlaybackEnded = false;
            }
        }

        _seekPendingWhilePaused = false;
        _output.Play();
        _isPlaying = true;
        Trace($"transport.play sample={_provider?.CurrentMainSample ?? 0}");
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
        _provider?.ResetOutputPeak();
        Trace($"transport.pause sample={_provider?.CurrentMainSample ?? 0}");
    }

    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_output is null || _reader is null)
        {
            _isPlaying = false;
            return;
        }

        _provider?.ClearPlaylistPlayback();
        _output.Stop();
        _reader.Position = 0;
        _provider?.StopExitLayer();
        _isPlaying = false;
        _seekPendingWhilePaused = false;
        _provider?.ResetOutputPeak();
        Trace("transport.stop");
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

        // ジャンプ時は Exit／Playlist 遷移を直ちに止める（Arm 前でも確実に）
        _provider?.ClearPlaylistPlayback();
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
        // 再生中は連続読み出しで自然に切り替わるが、一時停止中は先読みバッファに旧位置が
        // 残るため、次の再生でバッファを破棄する必要があることを記録する。
        if (!_isPlaying)
        {
            _seekPendingWhilePaused = true;
        }

        Trace($"transport.seek progress={clamped:R} sample={_provider?.CurrentMainSample ?? 0}");
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
        if (_suppressPlaybackEnded || _reader is null)
        {
            return;
        }

        // 末尾到達時のみ終了扱い（Stop 呼び出しでも発火するため位置で判定）
        // ループ中はプロバイダが折り返すので、ここに来るのは真の EOF／Stop
        var playlistEnded = _provider?.TryResetPlaylistAfterEnd() == true;
        if (playlistEnded
            || _reader.Position >= _reader.Length
            || _reader.CurrentTime >= _reader.TotalTime)
        {
            CompletePlaybackEnded(playlistEnded);
        }
    }

    /// <summary>
    /// ASIO（AutoStop=false）の終端を UI スレッドから回収する。
    /// 終了処理を行ったら true。
    /// </summary>
    public bool TryCompletePlaybackIfEnded()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isPlaying || _reader is null || _output is null)
        {
            return false;
        }

        // ASIO: コールバック内 Stop を避け、HasReachedEnd を UI から回収する
        if (_output is AsioOut { HasReachedEnd: true })
        {
            var playlistEnded = _provider?.TryResetPlaylistAfterEnd() == true;
            _suppressPlaybackEnded = true;
            try
            {
                _output.Stop();
            }
            finally
            {
                _suppressPlaybackEnded = false;
            }

            CompletePlaybackEnded(playlistEnded);
            return true;
        }

        return false;
    }

    private void CompletePlaybackEnded(bool playlistEnded)
    {
        _provider?.StopExitLayer();
        _isPlaying = false;
        _provider?.ResetOutputPeak();
        Trace($"playback.ended playlistEnded={playlistEnded} sample={_provider?.CurrentMainSample ?? 0}");
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void StopAndRelease()
    {
        _isPlaying = false;
        _seekPendingWhilePaused = false;
        _provider?.ClearPlaylistPlayback();
        _provider?.StopExitLayer();
        _provider?.ResetOutputPeak();
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

        if (_playlistFadeReader is not null)
        {
            _playlistFadeReader.Dispose();
            _playlistFadeReader = null;
        }

        if (_playlistExitFadeReader is not null)
        {
            _playlistExitFadeReader.Dispose();
            _playlistExitFadeReader = null;
        }

        if (_playlistPreRollReader is not null)
        {
            _playlistPreRollReader.Dispose();
            _playlistPreRollReader = null;
        }

        TryDeleteFile(_playbackCopyPath);
        _playbackCopyPath = null;
    }

    private void Trace(string message) => Diagnostic?.Invoke(this, message);

    /// <summary>
    /// PCM / IEEE float の WAV を ACM を使わずステレオ float に変換する再生用プロバイダ。
    /// メインはループ折り返し、Exit は別リーダでワンショット二重再生してミックスする。
    /// </summary>
    private sealed class StereoFloatWaveProvider : IWaveProvider
    {
        private const float FoldGain = 0.7071f;
        private readonly WaveFileReader _source;
        private readonly WaveFileReader _exitSource;
        private readonly WaveFileReader _playlistFadeSource;
        private readonly WaveFileReader _playlistExitFadeSource;
        private readonly WaveFileReader _playlistPreRollSource;
        private readonly Action<string> _diagnostic;
        private readonly Func<byte[], int, float> _sampleReader;
        private readonly int _sourceBlockAlign;
        private readonly int _channels;
        private readonly int _bytesPerSample;
        private readonly float _normalize;
        private byte[] _pcmScratch = [];
        private byte[] _mainFloat = [];
        private byte[] _exitFloat = [];
        private byte[] _playlistFadeFloat = [];
        private byte[] _playlistExitFadeFloat = [];
        private byte[] _playlistPreRollFloat = [];
        private readonly object _gate = new();
        private readonly object _readGate = new();
        private LoopPlaybackPlan? _activePlan;
        private bool _exitPlaying;
        private long _exitStartSample;
        private long _exitEndSample;
        private long _exitStartTickMs;
        private PlaylistTransitionRequest? _pendingPlaylistTransition;
        private long? _playlistStartSample;
        private long? _playlistEndSample;
        private bool _playlistFadePlaying;
        private long _playlistFadeStartSample;
        private long _playlistFadeEndSample;
        private long _playlistFadeStartTickMs;
        private long _playlistFadeExitStartSample;
        private long _playlistFadeExitEndSample;
        private bool _playlistExitFadePlaying;
        private long _playlistExitFadeEndSample;
        private bool _playlistPreRollPlaying;
        private bool _playlistMainFadeInPlaying;
        private long _playlistMainFadeInFramesRead;
        private int _playlistMainFadeInFrameCount;
        private bool _playlistPreRollFadeInPlaying;
        private long _playlistPreRollFadeInFramesRead;
        private int _playlistPreRollFadeInFrameCount;
        private long _playlistFadeIncomingFramesRead;
        private int _playlistFadeIncomingFrameCount;
        private long _playlistFadeFramesRead;
        private int _playlistFadeFrameCount;
        private long _playlistRequestGeneration;
        private long _playlistStartedGeneration;
        private long _playlistStartedTargetSample;
        private IReadOnlyList<RegionEdgeFade> _regionEdgeFades = [];
        private float _outputPeak;
        /// <summary>スペアナ用の直近出力モノラルサンプル（リングバッファ）。</summary>
        private readonly float[] _monitorRing = new float[8192];
        private long _monitorWriteCount;
        private readonly object _monitorGate = new();

        public StereoFloatWaveProvider(
            WaveFileReader source,
            WaveFileReader exitSource,
            WaveFileReader playlistFadeSource,
            WaveFileReader playlistExitFadeSource,
            WaveFileReader playlistPreRollSource,
            WavFileInfo info,
            Action<string> diagnostic)
        {
            if (info.Channels == 0 || info.BlockAlign == 0 || info.SampleRate == 0)
            {
                throw new InvalidDataException(UiStrings.ErrWaveFormatInvalid);
            }

            _source = source;
            _exitSource = exitSource;
            _playlistFadeSource = playlistFadeSource;
            _playlistExitFadeSource = playlistExitFadeSource;
            _playlistPreRollSource = playlistPreRollSource;
            _diagnostic = diagnostic;
            _sampleReader = WavPeakReader.CreateSampleReader(info.AudioFormat, info.BitsPerSample);
            _channels = info.Channels;
            _sourceBlockAlign = info.BlockAlign;
            _bytesPerSample = info.BitsPerSample / 8;
            var extraChannels = Math.Max(0, _channels - 2);
            _normalize = 1f / (1f + extraChannels * FoldGain);
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat((int)info.SampleRate, 2);
            _playlistFadeFrameCount = Math.Max(1, (int)Math.Round(info.SampleRate * 0.5d));
        }

        public WaveFormat WaveFormat { get; }

        public float OutputPeak => Volatile.Read(ref _outputPeak);

        public void ResetOutputPeak() => Volatile.Write(ref _outputPeak, 0f);

        public long CurrentMainSample
        {
            get
            {
                lock (_readGate)
                {
                    return CurrentSample(_source);
                }
            }
        }

        public void SetActivePlan(LoopPlaybackPlan? plan)
        {
            lock (_gate)
            {
                _activePlan = plan;
                // アーム／解除だけでは Exit を始めない（ループ折り返しで開始）
                _exitPlaying = false;
            }
        }

        public void SetRegionEdgeFades(IReadOnlyList<RegionEdgeFade>? fades)
        {
            lock (_gate)
            {
                _regionEdgeFades = fades is null || fades.Count == 0
                    ? []
                    : fades.ToArray();
            }
        }

        public LoopPlaybackPlan? GetActivePlan()
        {
            lock (_gate)
            {
                return _activePlan;
            }
        }

        public void StartPlaylistRange(long startSample, long endSample, LoopPlaybackPlan? plan)
        {
            lock (_readGate)
            {
                var generation = NextPlaylistGeneration();
                SeekToSample(_source, startSample);
                lock (_gate)
                {
                    _pendingPlaylistTransition = null;
                    _playlistFadePlaying = false;
                    _playlistExitFadePlaying = false;
                    _playlistPreRollPlaying = false;
                    ResetMainFadeInNoLock();
                    ResetPreRollFadeInNoLock();
                    _playlistStartSample = startSample;
                    _playlistEndSample = endSample;
                    _activePlan = plan;
                    _exitPlaying = false;
                    _playlistStartedGeneration = generation;
                    _playlistStartedTargetSample = startSample;
                }
                _diagnostic($"provider.playlist-range-start generation={generation} start={startSample} end={endSample} loopPlan={plan?.ToString() ?? "none"}");
            }
        }

        public bool TrySchedulePlaylistTransition(
            long startSample,
            long endSample,
            long? sourceExitSample,
            long sourcePartStartSample,
            PlaylistDestinationSyncMode destinationSyncMode,
            long preRollFrameCount,
            bool allowShortPreRoll,
            long fadeSourceEndSample,
            int fadeInFrameCount,
            int fadeFrameCount,
            Func<long, LoopPlaybackPlan?> findPlan,
            out PlaylistTransitionSchedule schedule)
        {
            lock (_readGate)
            {
                var currentSample = CurrentSample(_source);
                var syncBoundarySample = sourceExitSample ?? currentSample;
                var sourceRelativeSample = Math.Max(
                    0L,
                    syncBoundarySample - sourcePartStartSample);
                if (syncBoundarySample < currentSample
                    || fadeSourceEndSample < syncBoundarySample)
                {
                    schedule = default;
                    _diagnostic($"provider.playlist-schedule-rejected current={currentSample} sourceExit={sourceExitSample?.ToString() ?? "immediate"} sync={syncBoundarySample} fadeEnd={fadeSourceEndSample}");
                    return false;
                }

                var effectivePreRollFrameCount =
                    destinationSyncMode == PlaylistDestinationSyncMode.EntryCue
                        ? preRollFrameCount
                        : 0L;
                var desiredTriggerSample =
                    syncBoundarySample - effectivePreRollFrameCount;
                var startsImmediately = desiredTriggerSample <= currentSample;
                if (startsImmediately
                    && sourceExitSample.HasValue
                    && !allowShortPreRoll)
                {
                    schedule = default;
                    _diagnostic($"provider.playlist-schedule-rejected-short-preroll current={currentSample} desiredTrigger={desiredTriggerSample} sync={syncBoundarySample} preRoll={preRollFrameCount}");
                    return false;
                }

                var triggerSample = startsImmediately
                    ? currentSample
                    : desiredTriggerSample;
                var targetEntrySample = destinationSyncMode switch
                {
                    PlaylistDestinationSyncMode.SameTime =>
                        startSample + sourceRelativeSample,
                    _ => startSample + (syncBoundarySample - triggerSample),
                };
                if (destinationSyncMode == PlaylistDestinationSyncMode.SameTime
                    && targetEntrySample >= endSample)
                {
                    schedule = new PlaylistTransitionSchedule(
                        0,
                        triggerSample,
                        syncBoundarySample,
                        targetEntrySample,
                        startsImmediately,
                        sourceRelativeSample,
                        "same-time-out-of-range");
                    _diagnostic($"provider.playlist-schedule-rejected-same-time current={currentSample} sourcePartStart={sourcePartStartSample} sourceRelative={sourceRelativeSample} sync={syncBoundarySample} targetStart={startSample} targetSwitch={targetEntrySample} targetEnd={endSample}");
                    return false;
                }

                if (triggerSample < 0
                    || targetEntrySample < startSample
                    || targetEntrySample > endSample)
                {
                    schedule = default;
                    _diagnostic($"provider.playlist-schedule-rejected-range current={currentSample} trigger={triggerSample} sync={syncBoundarySample} targetStart={startSample} targetEntry={targetEntrySample} targetEnd={endSample}");
                    return false;
                }

                var generation = NextPlaylistGeneration();
                PlaylistTransitionRequest transition;
                lock (_gate)
                {
                    _pendingPlaylistTransition = null;
                    _playlistPreRollPlaying = false;
                    ResetPreRollFadeInNoLock();
                    transition = new PlaylistTransitionRequest(
                        startSample,
                        targetEntrySample,
                        endSample,
                        triggerSample,
                        syncBoundarySample,
                        Math.Max(syncBoundarySample, fadeSourceEndSample),
                        Math.Max(0, fadeInFrameCount),
                        Math.Max(0, fadeFrameCount),
                        findPlan(targetEntrySample),
                        generation);
                    _pendingPlaylistTransition = transition;
                }

                if (startsImmediately)
                {
                    if (triggerSample < syncBoundarySample)
                    {
                        BeginPlaylistPreRoll(transition);
                    }
                    else
                    {
                        BeginPlaylistTransition(transition);
                    }
                }

                schedule = new PlaylistTransitionSchedule(
                    generation,
                    triggerSample,
                    syncBoundarySample,
                    targetEntrySample,
                    startsImmediately,
                    sourceRelativeSample,
                    null);
                _diagnostic($"provider.playlist-schedule generation={generation} current={currentSample} destinationSync={destinationSyncMode} sourcePartStart={sourcePartStartSample} sourceRelative={sourceRelativeSample} trigger={triggerSample} sync={syncBoundarySample} targetStart={startSample} targetEntry={targetEntrySample} targetEnd={endSample} fadeEnd={fadeSourceEndSample} fadeInFrames={fadeInFrameCount} fadeOutFrames={fadeFrameCount} startedImmediately={startsImmediately}");
                return true;
            }
        }

        public void CancelPlaylistTransition()
        {
            lock (_readGate)
            {
                var currentSample = CurrentSample(_source);
                var hadPending = false;
                var hadFade = false;
                lock (_gate)
                {
                    hadPending = _pendingPlaylistTransition is not null;
                    hadFade = _playlistFadePlaying
                        || _playlistExitFadePlaying
                        || _playlistPreRollPlaying;
                    _pendingPlaylistTransition = null;
                    _playlistFadePlaying = false;
                    _playlistExitFadePlaying = false;
                    _playlistPreRollPlaying = false;
                    ResetPreRollFadeInNoLock();
                }
                _diagnostic($"provider.playlist-cancel current={currentSample} hadPending={hadPending} hadFade={hadFade}");
            }
        }

        public void ClearPlaylistPlayback()
        {
            lock (_readGate)
            {
                lock (_gate)
                {
                    _pendingPlaylistTransition = null;
                    _playlistFadePlaying = false;
                    _playlistExitFadePlaying = false;
                    _playlistPreRollPlaying = false;
                    ResetMainFadeInNoLock();
                    ResetPreRollFadeInNoLock();
                    _playlistStartSample = null;
                    _playlistEndSample = null;
                    _playlistRequestGeneration = 0;
                    _playlistStartedGeneration = 0;
                    _playlistStartedTargetSample = 0;
                }
            }
        }

        public bool TryResetPlaylistAfterEnd()
        {
            lock (_readGate)
            {
                long? start;
                long? end;
                lock (_gate)
                {
                    start = _playlistStartSample;
                    end = _playlistEndSample;
                }

                if (start is not long rangeStart
                    || end is not long rangeEnd
                    || CurrentSample(_source) < rangeEnd)
                {
                    return false;
                }

                SeekToSample(_source, rangeStart);
                lock (_gate)
                {
                    _pendingPlaylistTransition = null;
                    _playlistFadePlaying = false;
                    _playlistExitFadePlaying = false;
                    _playlistPreRollPlaying = false;
                    ResetMainFadeInNoLock();
                    ResetPreRollFadeInNoLock();
                    _exitPlaying = false;
                }

                _diagnostic($"provider.playlist-ended resetTo={rangeStart} end={rangeEnd}");
                return true;
            }
        }

        public bool TryGetPlaylistTransitionState(out PlaylistTransitionState state)
        {
            lock (_gate)
            {
                var pending = _pendingPlaylistTransition;
                if (pending is null && _playlistStartedGeneration == 0)
                {
                    state = default;
                    return false;
                }

                state = new PlaylistTransitionState(
                    pending?.TargetStartSample ?? _playlistStartedTargetSample,
                    pending?.TargetEndSample ?? _playlistEndSample ?? 0,
                    pending?.TriggerSample,
                    pending?.Generation ?? _playlistRequestGeneration,
                    _playlistStartedGeneration,
                    _playlistFadePlaying);
                return true;
            }
        }

        private long NextPlaylistGeneration()
        {
            lock (_gate)
            {
                return ++_playlistRequestGeneration;
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
            _diagnostic($"provider.exit-start start={loop.LoopEndSample} end={loop.ExitEndSample}");
        }

        private void WrapMainToLoopStart(LoopPlaybackPlan loop)
        {
            SeekToSample(_source, loop.LoopStartSample);
            BeginExitOnLoopWrap(loop);
            _diagnostic($"provider.loop-wrap start={loop.LoopStartSample} end={loop.LoopEndSample}");
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

        public bool TryGetPlaylistFadePlaybackProgress(
            long frameCount,
            int sampleRate,
            out double progress,
            out bool isExit)
        {
            progress = 0d;
            isExit = false;
            if (frameCount <= 0 || sampleRate <= 0)
            {
                return false;
            }

            long start;
            long end;
            long startTick;
            long exitStart;
            long exitEnd;
            lock (_gate)
            {
                if (!_playlistFadePlaying)
                {
                    return false;
                }

                start = _playlistFadeStartSample;
                end = Math.Min(
                    _playlistFadeEndSample,
                    start + _playlistFadeFrameCount);
                startTick = _playlistFadeStartTickMs;
                exitStart = _playlistFadeExitStartSample;
                exitEnd = _playlistFadeExitEndSample;
            }

            var elapsedSeconds = Math.Max(
                0d,
                (Environment.TickCount64 - startTick) / 1000d);
            var sample = start + (long)Math.Floor(elapsedSeconds * sampleRate);
            if (sample >= end)
            {
                return false;
            }

            progress = Math.Clamp(sample / (double)frameCount, 0d, 1d);
            isExit = exitEnd > exitStart
                && sample >= exitStart
                && sample < exitEnd;
            return true;
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            lock (_readGate)
            {
                return ReadCore(buffer, offset, count);
            }
        }

        private int ReadCore(byte[] buffer, int offset, int count)
        {
            var framesNeeded = count / 8;
            if (framesNeeded <= 0)
            {
                Volatile.Write(ref _outputPeak, 0f);
                return 0;
            }

            var outIndex = offset;
            var totalFrames = 0;
            var outputPeak = 0f;
            while (totalFrames < framesNeeded)
            {
                LoopPlaybackPlan? plan;
                var exitPlaying = false;
                long exitStart = 0;
                long exitEnd = 0;
                PlaylistTransitionRequest? transition;
                long? playlistEnd;
                var playlistFadePlaying = false;
                var playlistPreRollPlaying = false;
                lock (_gate)
                {
                    plan = _activePlan;
                    exitPlaying = _exitPlaying;
                    exitStart = _exitStartSample;
                    exitEnd = _exitEndSample;
                    transition = _pendingPlaylistTransition;
                    playlistEnd = _playlistEndSample;
                    playlistFadePlaying = _playlistFadePlaying;
                    playlistPreRollPlaying = _playlistPreRollPlaying;
                }

                var samplePos = CurrentSample(_source);
                var framesThis = framesNeeded - totalFrames;

                // Playlist 遷移はループ折り返しより優先する。境界まででチャンクを厳密に分割する。
                if (transition is { } pending)
                {
                    if (!playlistPreRollPlaying)
                    {
                        if (samplePos >= pending.TriggerSample)
                        {
                            if (pending.TriggerSample < pending.SyncBoundarySample)
                            {
                                BeginPlaylistPreRoll(pending);
                            }
                            else
                            {
                                BeginPlaylistTransition(pending);
                            }

                            continue;
                        }

                        framesThis = (int)Math.Min(
                            framesThis,
                            pending.TriggerSample - samplePos);
                    }
                    else
                    {
                        if (samplePos >= pending.SyncBoundarySample)
                        {
                            BeginPlaylistTransition(pending);
                            continue;
                        }

                        framesThis = (int)Math.Min(
                            framesThis,
                            pending.SyncBoundarySample - samplePos);
                    }
                }

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

                if (playlistEnd is long rangeEnd)
                {
                    if (samplePos >= rangeEnd)
                    {
                        break;
                    }

                    framesThis = (int)Math.Min(framesThis, rangeEnd - samplePos);
                }

                // メインを float バッファへ
                EnsureBuffer(ref _mainFloat, framesThis * 8);
                var gotFrames = ReadDecodedFrames(_source, _mainFloat, 0, framesThis);
                if (gotFrames <= 0)
                {
                    break;
                }

                ApplyRegionEdgeGain(_mainFloat, gotFrames, samplePos);

                // Exit レイヤ（同時長）。停止／終了後は 0。
                // このチャンク読了後に折り返す場合は、折り返し後の続きで Exit が乗る。
                EnsureBuffer(ref _exitFloat, gotFrames * 8);
                Array.Clear(_exitFloat, 0, gotFrames * 8);
                if (exitPlaying)
                {
                    var exitPos = CurrentSample(_exitSource);
                    MixExitLayer(_exitFloat, 0, gotFrames, exitStart, exitEnd);
                    ApplyRegionEdgeGain(_exitFloat, gotFrames, exitPos);
                }

                EnsureBuffer(ref _playlistFadeFloat, gotFrames * 8);
                Array.Clear(_playlistFadeFloat, 0, gotFrames * 8);
                if (playlistFadePlaying)
                {
                    var fadePos = CurrentSample(_playlistFadeSource);
                    MixPlaylistFade(_playlistFadeFloat, 0, gotFrames);
                    ApplyRegionEdgeGain(_playlistFadeFloat, gotFrames, fadePos);
                }

                EnsureBuffer(ref _playlistPreRollFloat, gotFrames * 8);
                Array.Clear(_playlistPreRollFloat, 0, gotFrames * 8);
                if (playlistPreRollPlaying)
                {
                    var preRollPos = CurrentSample(_playlistPreRollSource);
                    _ = ReadDecodedFrames(
                        _playlistPreRollSource,
                        _playlistPreRollFloat,
                        0,
                        gotFrames);
                    ApplyRegionEdgeGain(_playlistPreRollFloat, gotFrames, preRollPos);
                }

                ApplyFadeIn(
                    _mainFloat,
                    gotFrames,
                    ref _playlistMainFadeInPlaying,
                    ref _playlistMainFadeInFramesRead,
                    _playlistMainFadeInFrameCount,
                    "main");
                ApplyFadeIn(
                    _playlistPreRollFloat,
                    gotFrames,
                    ref _playlistPreRollFadeInPlaying,
                    ref _playlistPreRollFadeInFramesRead,
                    _playlistPreRollFadeInFrameCount,
                    "pre-roll");

                // 加算ミックス（簡易クリップ）
                for (var i = 0; i < gotFrames; i++)
                {
                    var mainL = BitConverter.ToSingle(_mainFloat, i * 8);
                    var mainR = BitConverter.ToSingle(_mainFloat, i * 8 + 4);
                    var exitL = BitConverter.ToSingle(_exitFloat, i * 8);
                    var exitR = BitConverter.ToSingle(_exitFloat, i * 8 + 4);
                    var fadeL = BitConverter.ToSingle(_playlistFadeFloat, i * 8);
                    var fadeR = BitConverter.ToSingle(_playlistFadeFloat, i * 8 + 4);
                    var preRollL = BitConverter.ToSingle(_playlistPreRollFloat, i * 8);
                    var preRollR = BitConverter.ToSingle(_playlistPreRollFloat, i * 8 + 4);
                    var outputL = ClampSample(mainL + exitL + fadeL + preRollL);
                    var outputR = ClampSample(mainR + exitR + fadeR + preRollR);
                    outputPeak = Math.Max(
                        outputPeak,
                        Math.Max(Math.Abs(outputL), Math.Abs(outputR)));
                    BitConverter.TryWriteBytes(
                        buffer.AsSpan(outIndex + i * 8, 4),
                        outputL);
                    BitConverter.TryWriteBytes(
                        buffer.AsSpan(outIndex + i * 8 + 4, 4),
                        outputR);
                }

                PushMonitorSamples(buffer, outIndex, gotFrames);
                outIndex += gotFrames * 8;
                totalFrames += gotFrames;
            }

            Volatile.Write(ref _outputPeak, outputPeak);
            return totalFrames * 8;
        }

        private void PushMonitorSamples(byte[] buffer, int offset, int frames)
        {
            lock (_monitorGate)
            {
                for (var i = 0; i < frames; i++)
                {
                    var left = BitConverter.ToSingle(buffer, offset + i * 8);
                    var right = BitConverter.ToSingle(buffer, offset + i * 8 + 4);
                    _monitorRing[(int)(_monitorWriteCount % _monitorRing.Length)] =
                        (left + right) * 0.5f;
                    _monitorWriteCount++;
                }
            }
        }

        /// <summary>直近サンプルを destination の末尾詰めでコピー（不足分は先頭を 0 埋め）。</summary>
        public int CopyRecentOutputSamples(float[] destination)
        {
            lock (_monitorGate)
            {
                var available = (int)Math.Min(
                    _monitorWriteCount,
                    Math.Min(destination.Length, _monitorRing.Length));
                var start = _monitorWriteCount - available;
                for (var i = 0; i < available; i++)
                {
                    destination[destination.Length - available + i] =
                        _monitorRing[(int)((start + i) % _monitorRing.Length)];
                }

                if (available < destination.Length)
                {
                    Array.Clear(destination, 0, destination.Length - available);
                }

                return available;
            }
        }

        private void BeginPlaylistPreRoll(PlaylistTransitionRequest transition)
        {
            SeekToSample(_playlistPreRollSource, transition.TargetStartSample);
            lock (_gate)
            {
                _playlistPreRollPlaying = true;
                _playlistPreRollFadeInFrameCount = transition.FadeInFrameCount;
                _playlistPreRollFadeInFramesRead = 0;
                _playlistPreRollFadeInPlaying =
                    transition.FadeInFrameCount > 0;
                _playlistStartedGeneration = transition.Generation;
                _playlistStartedTargetSample = transition.TargetStartSample;
            }

            _diagnostic(
                $"provider.playlist-preroll-start generation={transition.Generation}"
                + $" trigger={transition.TriggerSample}"
                + $" sync={transition.SyncBoundarySample}"
                + $" targetStart={transition.TargetStartSample}"
                + $" targetEntry={transition.TargetEntrySample}"
                + $" fadeInFrames={transition.FadeInFrameCount}");
        }

        private void BeginPlaylistTransition(PlaylistTransitionRequest transition)
        {
            // 同期境界までは旧 Playlist をメインで維持し、ここから専用リーダーでフェードする。
            SeekToSample(_playlistFadeSource, transition.SyncBoundarySample);
            long exitFadeStart = 0;
            long exitFadeEnd = 0;
            lock (_gate)
            {
                if (_exitPlaying)
                {
                    exitFadeStart = CurrentSample(_exitSource);
                    exitFadeEnd = _exitEndSample;
                }
            }

            var carryExitFade = exitFadeEnd > exitFadeStart;
            if (carryExitFade)
            {
                SeekToSample(_playlistExitFadeSource, exitFadeStart);
            }

            SeekToSample(_source, transition.TargetEntrySample);
            var continuedFromPreRoll = false;
            var sourceExitWillBeMaintained = false;
            lock (_gate)
            {
                var oldPlan = _activePlan;
                continuedFromPreRoll = _playlistPreRollPlaying;
                sourceExitWillBeMaintained = carryExitFade
                    || oldPlan is { HasExit: true } sourcePlan
                    && transition.FadeFrameCount
                        > Math.Max(
                            0L,
                            sourcePlan.LoopEndSample
                            - transition.SyncBoundarySample);
                // 同時に保持できる旧フェードはこの1本だけ。再遷移時は上書きして先頭から開始。
                // Fade Out=None（0フレーム）のときは旧ソースを重ねず即切り替え。
                _playlistFadePlaying = transition.FadeFrameCount > 0;
                _playlistFadeStartSample = transition.SyncBoundarySample;
                _playlistFadeEndSample = transition.FadeSourceEndSample;
                _playlistFadeStartTickMs = Environment.TickCount64;
                _playlistFadeExitStartSample = oldPlan is { HasExit: true }
                    ? oldPlan.Value.LoopEndSample
                    : 0;
                _playlistFadeExitEndSample = oldPlan is { HasExit: true }
                    ? oldPlan.Value.ExitEndSample!.Value
                    : 0;
                _playlistFadeIncomingFramesRead =
                    _playlistMainFadeInPlaying
                        ? _playlistMainFadeInFramesRead
                        : 0;
                _playlistFadeIncomingFrameCount =
                    _playlistMainFadeInPlaying
                        ? _playlistMainFadeInFrameCount
                        : 0;
                _playlistExitFadePlaying = carryExitFade;
                _playlistExitFadeEndSample = exitFadeEnd;
                _playlistFadeFramesRead = 0;
                _playlistFadeFrameCount = transition.FadeFrameCount;
                _playlistPreRollPlaying = false;
                if (continuedFromPreRoll)
                {
                    _playlistMainFadeInPlaying =
                        _playlistPreRollFadeInPlaying;
                    _playlistMainFadeInFramesRead =
                        _playlistPreRollFadeInFramesRead;
                    _playlistMainFadeInFrameCount =
                        _playlistPreRollFadeInFrameCount;
                }
                else
                {
                    _playlistMainFadeInFrameCount =
                        transition.FadeInFrameCount;
                    _playlistMainFadeInFramesRead = 0;
                    _playlistMainFadeInPlaying =
                        transition.FadeInFrameCount > 0;
                }
                ResetPreRollFadeInNoLock();
                _playlistStartSample = transition.TargetStartSample;
                _playlistEndSample = transition.TargetEndSample;
                _activePlan = transition.TargetPlan;
                _exitPlaying = false;
                _pendingPlaylistTransition = null;
                _playlistStartedGeneration = transition.Generation;
                _playlistStartedTargetSample = transition.TargetStartSample;
            }
            _diagnostic(
                $"provider.playlist-transition-start generation={transition.Generation}"
                + $" trigger={transition.TriggerSample}"
                + $" sync={transition.SyncBoundarySample}"
                + $" targetStart={transition.TargetStartSample}"
                + $" targetEntry={transition.TargetEntrySample}"
                + $" targetEnd={transition.TargetEndSample}"
                + $" fadeInFrames={transition.FadeInFrameCount}"
                + $" fadeInContinuedFromPreRoll={continuedFromPreRoll}"
                + $" sourceExitWillBeMaintained={sourceExitWillBeMaintained}"
                + $" oldExitCarried={carryExitFade}"
                + $" oldExitStart={exitFadeStart}"
                + $" oldExitEnd={exitFadeEnd}");
        }

        private void MixPlaylistFade(byte[] dest, int destOffset, int frames)
        {
            var framesRemaining = _playlistFadeFrameCount - _playlistFadeFramesRead;
            if (framesRemaining <= 0)
            {
                StopPlaylistFade();
                return;
            }

            var chunk = (int)Math.Min(frames, framesRemaining);
            var sourceRemaining = Math.Max(
                0L,
                _playlistFadeEndSample - CurrentSample(_playlistFadeSource));
            var mainFrames = (int)Math.Min(chunk, sourceRemaining);
            var mainGot = mainFrames > 0
                ? ReadDecodedFrames(_playlistFadeSource, dest, destOffset, mainFrames)
                : 0;

            var exitGot = 0;
            if (_playlistExitFadePlaying)
            {
                var exitRemaining = Math.Max(
                    0L,
                    _playlistExitFadeEndSample - CurrentSample(_playlistExitFadeSource));
                var exitFrames = (int)Math.Min(chunk, exitRemaining);
                if (exitFrames > 0)
                {
                    EnsureBuffer(ref _playlistExitFadeFloat, exitFrames * 8);
                    exitGot = ReadDecodedFrames(
                        _playlistExitFadeSource,
                        _playlistExitFadeFloat,
                        0,
                        exitFrames);
                    for (var i = 0; i < exitGot; i++)
                    {
                        var at = destOffset + i * 8;
                        BitConverter.TryWriteBytes(
                            dest.AsSpan(at, 4),
                            ClampSample(
                                BitConverter.ToSingle(dest, at)
                                + BitConverter.ToSingle(_playlistExitFadeFloat, i * 8)));
                        BitConverter.TryWriteBytes(
                            dest.AsSpan(at + 4, 4),
                            ClampSample(
                                BitConverter.ToSingle(dest, at + 4)
                                + BitConverter.ToSingle(_playlistExitFadeFloat, i * 8 + 4)));
                    }
                }
            }

            var got = Math.Max(mainGot, exitGot);
            for (var i = 0; i < got; i++)
            {
                var fadeIndex = _playlistFadeFramesRead + i;
                var fadeOutGain = _playlistFadeFrameCount <= 1
                    ? 0f
                    : 1f - fadeIndex / (float)(_playlistFadeFrameCount - 1);
                var fadeInGain = _playlistFadeIncomingFrameCount <= 0
                    ? 1f
                    : _playlistFadeIncomingFrameCount <= 1
                        ? 1f
                        : Math.Min(
                            1f,
                            (_playlistFadeIncomingFramesRead + fadeIndex)
                            / (float)(_playlistFadeIncomingFrameCount - 1));
                var gain = fadeOutGain * fadeInGain;
                var at = destOffset + i * 8;
                BitConverter.TryWriteBytes(
                    dest.AsSpan(at, 4),
                    BitConverter.ToSingle(dest, at) * gain);
                BitConverter.TryWriteBytes(
                    dest.AsSpan(at + 4, 4),
                    BitConverter.ToSingle(dest, at + 4) * gain);
            }

            _playlistFadeFramesRead += got;
            if (got <= 0 || _playlistFadeFramesRead >= _playlistFadeFrameCount)
            {
                StopPlaylistFade();
            }
        }

        private void StopPlaylistFade()
        {
            lock (_gate)
            {
                _playlistFadePlaying = false;
                _playlistFadeStartSample = 0;
                _playlistFadeStartTickMs = 0;
                _playlistFadeExitStartSample = 0;
                _playlistFadeExitEndSample = 0;
                _playlistFadeIncomingFramesRead = 0;
                _playlistFadeIncomingFrameCount = 0;
                _playlistExitFadePlaying = false;
            }
        }

        private void ApplyRegionEdgeGain(byte[] buffer, int frames, long startSample)
        {
            IReadOnlyList<RegionEdgeFade> fades;
            lock (_gate)
            {
                fades = _regionEdgeFades;
            }

            if (fades.Count == 0 || frames <= 0)
            {
                return;
            }

            for (var i = 0; i < frames; i++)
            {
                var gain = RegionEdgeFade.GainAt(startSample + i, fades);
                if (Math.Abs(gain - 1f) < 1e-6f)
                {
                    continue;
                }

                var at = i * 8;
                BitConverter.TryWriteBytes(
                    buffer.AsSpan(at, 4),
                    BitConverter.ToSingle(buffer, at) * gain);
                BitConverter.TryWriteBytes(
                    buffer.AsSpan(at + 4, 4),
                    BitConverter.ToSingle(buffer, at + 4) * gain);
            }
        }

        private void ApplyFadeIn(
            byte[] buffer,
            int frames,
            ref bool playing,
            ref long framesRead,
            int frameCount,
            string layer)
        {
            if (!playing || frameCount <= 0 || frames <= 0)
            {
                return;
            }

            for (var i = 0; i < frames; i++)
            {
                var fadeIndex = framesRead + i;
                var gain = frameCount <= 1
                    ? 1f
                    : Math.Min(
                        1f,
                        fadeIndex / (float)(frameCount - 1));
                var at = i * 8;
                BitConverter.TryWriteBytes(
                    buffer.AsSpan(at, 4),
                    BitConverter.ToSingle(buffer, at) * gain);
                BitConverter.TryWriteBytes(
                    buffer.AsSpan(at + 4, 4),
                    BitConverter.ToSingle(buffer, at + 4) * gain);
            }

            framesRead += frames;
            if (framesRead >= frameCount)
            {
                playing = false;
                _diagnostic(
                    $"provider.playlist-fade-in-complete layer={layer}"
                    + $" frames={frameCount}");
            }
        }

        private void ResetMainFadeInNoLock()
        {
            _playlistMainFadeInPlaying = false;
            _playlistMainFadeInFramesRead = 0;
            _playlistMainFadeInFrameCount = 0;
        }

        private void ResetPreRollFadeInNoLock()
        {
            _playlistPreRollFadeInPlaying = false;
            _playlistPreRollFadeInFramesRead = 0;
            _playlistPreRollFadeInFrameCount = 0;
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

        private sealed record PlaylistTransitionRequest(
            long TargetStartSample,
            long TargetEntrySample,
            long TargetEndSample,
            long TriggerSample,
            long SyncBoundarySample,
            long FadeSourceEndSample,
            int FadeInFrameCount,
            int FadeFrameCount,
            LoopPlaybackPlan? TargetPlan,
            long Generation);
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

/// <summary>音声スレッドが確定した Playlist 遷移タイミング。</summary>
internal readonly record struct PlaylistTransitionSchedule(
    long Generation,
    long TriggerSample,
    long SyncBoundarySample,
    long TargetSwitchSample,
    bool StartedImmediately,
    long SourceRelativeSample,
    string? RejectionReason);

/// <summary>Playlist 遷移のポーリング用スナップショット。</summary>
internal readonly record struct PlaylistTransitionState(
    long TargetStartSample,
    long TargetEndSample,
    long? PendingBoundarySample,
    long RequestGeneration,
    long StartedGeneration,
    bool IsOldPlaylistFading);
