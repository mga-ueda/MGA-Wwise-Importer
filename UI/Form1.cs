using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace MgaWwiseIMImporter.UI;

internal enum PlaylistExitSourceMode
{
    Immediate,
    NextBar,
    NextBeat,
    NextCue,
    ExitCue,
}

public partial class Form1 : Form
{
    // Exact line height in twips (1 pt = 20 twips). Keeps JP + Latin rows uniform.
    private const int LogLineSpacingTwips = 280;

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int EmSetParaFormat = 0x0447;
    private const uint PfmLineSpacing = 0x00000100;
    private const byte LineSpacingExact = 4;

    private DeveloperSettings _developerSettings = new();
    private WaapiSettings _waapiSettings = new();
    private WaapiProbeResult? _waapiLastResult;
    private string _waapiLoggedSelectionPath = string.Empty;
    private int _waapiPollFailCount;
    private bool _waapiPollBusy;
    private const int WaapiPollFailThreshold = 3;
    private const int WaapiConnectedPollMs = 1500;
    private const int WaapiDisconnectedPollMs = 3000;
    private readonly WaveAudioPlayer _audioPlayer = new();
    private readonly System.Windows.Forms.Timer _playheadTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer _playlistBlinkTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer _playlistTransitionGlowTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer _waveformScrollTimer = new() { Interval = 16 };
    private readonly System.Windows.Forms.Timer _waapiSelectionTimer = new() { Interval = WaapiConnectedPollMs };
    private double _smoothProgress;
    private double _anchorProgress;
    private long _anchorTickMs;
    private ColorDevPanelForm? _colorDevPanel;
    private int _exportGeneration;
    private WaveformPreviewData? _loadedPreview;
    private WaveformPreviewSession? _previewSession;
    private bool _exportBusy;
    private bool _populatingPlaylistChoices;
    private bool _automaticPlaylistPlayback;
    private double _playlistFadeInSeconds;
    private double _playlistFadeSeconds = 0.5d;
    private PlaylistExitSourceMode _playlistExitSourceMode = PlaylistExitSourceMode.NextBar;
    private PlaylistDestinationSyncMode _playlistDestinationSyncMode =
        PlaylistDestinationSyncMode.EntryCue;
    private int? _activeAutomaticPlaylistPartNumber;
    private int? _requestedPlaylistPartNumber;
    private int? _manualPlaylistPartNumber;
    private int? _hoveredPlaylistPartNumber;
    private int? _hoveredPlaylistListPartNumber;
    private int? _lastAutoScrolledPlaylistPartNumber;
    private long _pendingPlaylistTransitionGeneration;
    private long _pendingPlaylistBoundarySample;
    private long _pendingPlaylistSyncBoundarySample;
    private long _pendingPlaylistTargetSample;
    private long _pendingPlaylistTargetEntrySample;
    private bool _pendingPlaylistAudioStarted;
    private double _pendingPlaylistBlinkLevel;
    private int? _playlistTransitionGlowPartNumber;
    private long _playlistTransitionGlowStartTickMs;
    private double _playlistTransitionGlowDurationMs;
    private double _playlistTransitionGlowLevel;
    private double? _pendingWaveformScrollStart;
    /// <summary>
    /// 戻る方向ジャンプ中に再生を一時停止したとき true。キーアップで再開する。
    /// </summary>
    private bool _resumePlaybackAfterBackwardSeek;
    private TransportCommand? _activeTransportShortcutCommand;
    private Keys _activeTransportShortcutKeyCode = Keys.None;
    private long _diagnosticSequence;

    public Form1()
    {
        UiColors.LoadFromIni();
        AppFonts.EnsureRegistered();
        InitializeComponent();
        brandLogoPictureBox.Image = LoadBrandLogo();
        // 初回レイアウト途中のフレームを見せず、描画完了後に一度で表示する。
        Opacity = 0d;
        actionBar.BackColor = UiColors.ForControlBack(UiColors.ActionBarBack);
        ApplyActionBarButtonColors();
        ApplyActionBarTextColors();
        transportBar.ApplyColors();
        UpdateTransportPlaybackState();
        ClearPlaylistChoices("Playlist はありません");
        ApplyPlaylistSelectorColors();
        KeyPreview = true;
        _developerSettings = DeveloperSettings.Load();
        _waapiSettings = WaapiSettings.Load();
        TopMost = _developerSettings.TopMost;
        topMostCheckBox.Checked = _developerSettings.TopMost;
        detailedLogCheckBox.Checked = _developerSettings.DetailedPlaybackLog;
        RestoreWindowBounds();

        _playheadTimer.Tick += (_, _) => UpdatePlayhead();
        _playlistBlinkTimer.Tick += (_, _) => UpdatePendingPlaylistBlink();
        _playlistTransitionGlowTimer.Tick += (_, _) => UpdatePlaylistTransitionGlow();
        logAreaPanel.Resize += (_, _) => UpdatePlaylistSelectorWidth();
        logEditorPanel.Resize += (_, _) => PositionLogButtons();
        PositionLogButtons();
        _waapiSelectionTimer.Tick += async (_, _) => await PollWaapiAsync();
        _audioPlayer.PlaybackEnded += (_, _) =>
        {
            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(() =>
            {
                WritePlaybackDiagnostic("playback.ended");
                _resumePlaybackAfterBackwardSeek = false;
                ClearPendingPlaylistUiTransition();
                ClearPlaylistPlaybackSelection();
                _playheadTimer.Stop();
                var resetProgress = _audioPlayer.Progress;
                AnchorPlayhead(resetProgress);
                waveformView.SetPlayhead(resetProgress, recordTrail: false, ensureVisible: true);
                waveformView.SetExitPlayhead(null);
                waveformView.SetFadeOutPlayhead(null);
                UpdateTransportPlaybackState();
                UpdateTransportPosition();
            });
        };
        _audioPlayer.Diagnostic += (_, message) =>
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(() => WritePlaybackDiagnostic(
                    "audio.engine",
                    new { message }));
            }
            catch (InvalidOperationException)
            {
                // 終了処理と音声スレッドの診断通知が競合した場合は破棄する。
            }
        };
        waveformView.SeekRequested += (_, progress) => SeekPlayback(progress);
        waveformView.TimeViewChanged += (_, _) => UpdateWaveformHorizontalScrollBar();
        waveformHorizontalScrollBar.ScrollRequested += QueueWaveformHorizontalScroll;
        waveformHorizontalScrollBar.ScrollCompleted += (_, _) => FlushWaveformHorizontalScroll();
        _waveformScrollTimer.Tick += (_, _) => FlushWaveformHorizontalScroll();
        UpdateWaveformHorizontalScrollBar();
        waveformView.MarkerEditRequested += WaveformView_MarkerEditRequested;
        waveformView.PlaylistHoverChanged += (_, partNumber) =>
        {
            _hoveredPlaylistPartNumber = partNumber;
            ApplyPlaylistSelectorColors();
        };
        editorTextBox.ShortcutHandler = keyData => TryProcessWaveformShortcut(keyData);
        editorTextBox.HandleCreated += (_, _) => ApplyDarkEditorChrome();
        playlistScrollPanel.HandleCreated += (_, _) => ApplyDarkScrollChrome(playlistScrollPanel);
        transportBar.HandleCreated += (_, _) => ApplyDarkScrollChrome(transportBar);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
        ApplyFixedLogLineSpacing();
        ApplyDarkEditorChrome();
        ApplyDarkScrollableChrome();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BeginInvoke(CompleteInitialRender);
    }

    private void CompleteInitialRender()
    {
        if (IsDisposed)
        {
            return;
        }

        // Visible かつ透明な状態で全コントロールのレイアウトと初回描画を同期完了させる。
        PerformLayout();
        Refresh();
        Update();
        Opacity = 1d;
        Activate();

        BeginInvoke(RunStartupSequenceAsync);
    }

    /// <summary>起動直後: WAAPI 接続確認 → 自動波形読み込み。</summary>
    private async void RunStartupSequenceAsync()
    {
        if (_waapiSettings.ProbeOnStartup)
        {
            waapiStatusBar.SetPending();
            try
            {
                var result = await WaapiStartupProbe.RunAsync(_waapiSettings);
                if (!IsDisposed)
                {
                    ApplyWaapiProbeResult(result, logReport: true);
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed)
                {
                    ApplyWaapiProbeResult(
                        new WaapiProbeResult
                        {
                            Ok = false,
                            Url = _waapiSettings.Url,
                            Message = ex.Message,
                        },
                        logReport: true);
                }
            }
        }
        else
        {
            waapiStatusBar.SetSkipped();
            _waapiSelectionTimer.Stop();
        }

        if (!IsDisposed)
        {
            LoadAutoWaveAsDropped();
        }
    }

    private void ApplyWaapiProbeResult(WaapiProbeResult result, bool logReport)
    {
        _waapiLastResult = result;
        waapiStatusBar.SetResult(result);
        if (logReport)
        {
            AppendReport(result.FormatLogReport());
            _waapiLoggedSelectionPath = result.SelectedPath;
        }

        if (result.Ok)
        {
            _waapiPollFailCount = 0;
        }

        // 切断後もポーリング継続（再接続待ち）。間隔だけ広げる。
        _waapiSelectionTimer.Interval = result.Ok ? WaapiConnectedPollMs : WaapiDisconnectedPollMs;
        if (!_waapiSelectionTimer.Enabled)
        {
            _waapiSelectionTimer.Start();
        }
    }

    /// <summary>
    /// 接続中は選択更新。連続失敗で切断表示。切断中は再接続を試行。
    /// </summary>
    private async Task PollWaapiAsync()
    {
        if (IsDisposed || _waapiPollBusy || _waapiLastResult is null)
        {
            return;
        }

        _waapiPollBusy = true;
        try
        {
            if (_waapiLastResult.Ok)
            {
                await PollWaapiWhileConnectedAsync().ConfigureAwait(true);
            }
            else
            {
                await PollWaapiWhileDisconnectedAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            _waapiPollBusy = false;
        }
    }

    private async Task PollWaapiWhileConnectedAsync()
    {
        try
        {
            var (path, name, type) = await WaapiStartupProbe.RefreshSelectionAsync(_waapiSettings)
                .ConfigureAwait(true);
            if (IsDisposed || _waapiLastResult is not { Ok: true })
            {
                return;
            }

            _waapiPollFailCount = 0;

            if (string.Equals(path, _waapiLastResult.SelectedPath, StringComparison.Ordinal)
                && string.Equals(type, _waapiLastResult.SelectedType, StringComparison.Ordinal))
            {
                return;
            }

            _waapiLastResult = new WaapiProbeResult
            {
                Ok = true,
                Url = _waapiLastResult.Url,
                WwiseVersion = _waapiLastResult.WwiseVersion,
                ProcessPath = _waapiLastResult.ProcessPath,
                Project = _waapiLastResult.Project,
                ProjectName = _waapiLastResult.ProjectName,
                SelectedPath = path,
                SelectedName = name,
                SelectedType = type,
            };

            waapiStatusBar.UpdateSelection(
                _waapiLastResult.WwiseVersion,
                _waapiLastResult.ProjectName,
                path);

            if (!string.Equals(path, _waapiLoggedSelectionPath, StringComparison.Ordinal))
            {
                _waapiLoggedSelectionPath = path;
                AppendReport(
                    path.Length > 0
                        ? $"Target  : {path}{Environment.NewLine}"
                        : $"Target  : （未選択）{Environment.NewLine}");
            }
        }
        catch
        {
            // 一時失敗は許容し、連続 N 回で切断扱いにする。
            _waapiPollFailCount++;
            if (_waapiPollFailCount < WaapiPollFailThreshold || IsDisposed)
            {
                return;
            }

            ApplyWaapiProbeResult(
                new WaapiProbeResult
                {
                    Ok = false,
                    Url = _waapiSettings.Url,
                    Message = "接続できません。Wwise 起動と WAAPI 有効化を確認してください。",
                },
                logReport: true);
        }
    }

    private async Task PollWaapiWhileDisconnectedAsync()
    {
        try
        {
            var result = await WaapiStartupProbe.RunAsync(_waapiSettings).ConfigureAwait(true);
            if (IsDisposed || _waapiLastResult is not { Ok: false })
            {
                return;
            }

            if (result.Ok)
            {
                ApplyWaapiProbeResult(result, logReport: true);
            }
        }
        catch
        {
            // 切断中の再接続失敗はログを出さず、次ティックで再試行。
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _waapiSelectionTimer.Stop();
        _playheadTimer.Stop();
        _playlistBlinkTimer.Stop();
        _playlistTransitionGlowTimer.Stop();
        _waveformScrollTimer.Stop();
        _audioPlayer.Dispose();
        _waapiSelectionTimer.Dispose();
        _playheadTimer.Dispose();
        _playlistBlinkTimer.Dispose();
        _playlistTransitionGlowTimer.Dispose();
        _waveformScrollTimer.Dispose();
        WindowSettings.FromForm(this).Save();
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Playlist一覧でも Space は全体の再生／一時停止を優先する。
        // 遷移設定・Transport 上では矢印キーをフォーカス移動へ渡す。
        // Playlist 項目自体はフォーカスを取らない（FlatPlaylistButton.Selectable=false）。
        if ((transitionTimePanel.ContainsFocus || transportBar.ContainsFocus)
            && keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        if (keyData == Keys.Escape)
        {
            ConfirmAndExit();
            return true;
        }

        if (TryProcessWaveformShortcut(keyData))
        {
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>ESC からの終了。確認ダイアログで Yes のときだけ閉じる。</summary>
    private void ConfirmAndExit()
    {
        var confirm = MessageBox.Show(
            this,
            "アプリケーションを終了しますか？",
            "終了確認",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (confirm == DialogResult.Yes)
        {
            Close();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (_activeTransportShortcutCommand is not null
            && e.KeyCode == _activeTransportShortcutKeyCode)
        {
            EndActiveTransportShortcutFeedback();
        }

        if (_resumePlaybackAfterBackwardSeek && IsBackwardSeekKey(e.KeyCode))
        {
            ResumePlaybackAfterBackwardSeek();
            e.Handled = true;
        }

        base.OnKeyUp(e);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        EndActiveTransportShortcutFeedback();
        base.OnDeactivate(e);
    }

    private static bool IsBackwardSeekKey(Keys keyCode) =>
        keyCode is Keys.Home or Keys.Left or Keys.PageUp;

    /// <summary>
    /// 波形ビュー操作用ショートカット。ログ欄フォーカス時も <see cref="ShortcutForwardingRichTextBox"/> 経由で呼ばれる。
    /// </summary>
    private bool TryProcessWaveformShortcut(Keys keyData, bool showUiFeedback = true)
    {
        if (showUiFeedback
            && TryGetTransportCommandForShortcut(keyData, out var feedbackCommand))
        {
            if (_activeTransportShortcutCommand is { } activeCommand
                && activeCommand != feedbackCommand)
            {
                transportBar.EndShortcutFeedback(activeCommand);
            }

            _activeTransportShortcutCommand = feedbackCommand;
            _activeTransportShortcutKeyCode = keyData & Keys.KeyCode;
            transportBar.BeginShortcutFeedback(feedbackCommand);
        }

        if (keyData == Keys.G)
        {
            // モーダル表示中にもフェードが進むよう、ダイアログ表示前に解放する。
            EndActiveTransportShortcutFeedback();
            ShowBarJumpDialog();
            return true;
        }

        if (keyData == Keys.Space)
        {
            // ホールド中の自動再開はキャンセル（Space のトグルに委ねる）
            _resumePlaybackAfterBackwardSeek = false;
            TogglePlayback();
            return true;
        }

        // 修飾キー付きを先に判定
        if (keyData == (Keys.Control | Keys.Shift | Keys.Up))
        {
            waveformView.ZoomAmpToMax();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.Down))
        {
            waveformView.ResetAmpZoom();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Up))
        {
            waveformView.ZoomTimeToMax();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Down))
        {
            waveformView.ResetTimeZoom();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Home))
        {
            PauseForBackwardSeekHold();
            waveformView.PanTimeToStart();
            SeekPlayback(0);
            return true;
        }

        if (keyData == (Keys.Control | Keys.End))
        {
            waveformView.PanTimeToEnd();
            SeekPlayback(1);
            return true;
        }

        if (keyData == (Keys.Control | Keys.Left))
        {
            PauseForBackwardSeekHold();
            waveformView.SeekToPreviousRegionSplit();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Right))
        {
            waveformView.SeekToNextRegionSplit();
            return true;
        }

        if (keyData == Keys.Home)
        {
            PauseForBackwardSeekHold();
            waveformView.SeekToPreviousBar();
            return true;
        }

        if (keyData == Keys.End)
        {
            waveformView.SeekToNextBar();
            return true;
        }

        if (keyData == Keys.PageUp)
        {
            PauseForBackwardSeekHold();
            waveformView.SeekToPreviousPage();
            return true;
        }

        if (keyData == Keys.PageDown)
        {
            waveformView.SeekToNextPage();
            return true;
        }

        if (keyData == (Keys.Shift | Keys.Up))
        {
            waveformView.ZoomAmpIn();
            return true;
        }

        if (keyData == (Keys.Shift | Keys.Down))
        {
            waveformView.ZoomAmpOut();
            return true;
        }

        if (keyData == Keys.Up)
        {
            waveformView.ZoomTimeIn();
            return true;
        }

        if (keyData == Keys.Down)
        {
            waveformView.ZoomTimeOut();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.C))
        {
            ShowColorDevPanel();
            return true;
        }

        return false;
    }

    private static bool TryGetTransportCommandForShortcut(
        Keys keyData,
        out TransportCommand command)
    {
        var mapped = keyData switch
        {
            Keys.Space => TransportCommand.TogglePlayback,
            Keys.G => TransportCommand.JumpToBar,
            Keys.Control | Keys.Home => TransportCommand.GoToStart,
            Keys.Control | Keys.Left => TransportCommand.PreviousRegion,
            Keys.Home => TransportCommand.PreviousBar,
            Keys.PageUp => TransportCommand.PreviousPage,
            Keys.PageDown => TransportCommand.NextPage,
            Keys.End => TransportCommand.NextBar,
            Keys.Control | Keys.Right => TransportCommand.NextRegion,
            Keys.Control | Keys.End => TransportCommand.GoToEnd,
            Keys.Up => TransportCommand.TimeZoomIn,
            Keys.Down => TransportCommand.TimeZoomOut,
            Keys.Control | Keys.Up => TransportCommand.TimeZoomMax,
            Keys.Control | Keys.Down => TransportCommand.TimeZoomReset,
            Keys.Shift | Keys.Up => TransportCommand.AmpZoomIn,
            Keys.Shift | Keys.Down => TransportCommand.AmpZoomOut,
            Keys.Control | Keys.Shift | Keys.Up => TransportCommand.AmpZoomMax,
            Keys.Control | Keys.Shift | Keys.Down => TransportCommand.AmpZoomReset,
            _ => (TransportCommand?)null,
        };

        command = mapped.GetValueOrDefault();
        return mapped.HasValue;
    }

    private void EndActiveTransportShortcutFeedback()
    {
        if (_activeTransportShortcutCommand is { } command)
        {
            transportBar.EndShortcutFeedback(command);
        }

        _activeTransportShortcutCommand = null;
        _activeTransportShortcutKeyCode = Keys.None;
    }

    private void TransportBar_CommandInvoked(object? sender, TransportCommand command)
    {
        var keyData = command switch
        {
            TransportCommand.TogglePlayback => Keys.Space,
            TransportCommand.JumpToBar => Keys.G,
            TransportCommand.GoToStart => Keys.Control | Keys.Home,
            TransportCommand.PreviousRegion => Keys.Control | Keys.Left,
            TransportCommand.PreviousBar => Keys.Home,
            TransportCommand.PreviousPage => Keys.PageUp,
            TransportCommand.NextPage => Keys.PageDown,
            TransportCommand.NextBar => Keys.End,
            TransportCommand.NextRegion => Keys.Control | Keys.Right,
            TransportCommand.GoToEnd => Keys.Control | Keys.End,
            TransportCommand.TimeZoomIn => Keys.Up,
            TransportCommand.TimeZoomOut => Keys.Down,
            TransportCommand.TimeZoomMax => Keys.Control | Keys.Up,
            TransportCommand.TimeZoomReset => Keys.Control | Keys.Down,
            TransportCommand.AmpZoomIn => Keys.Shift | Keys.Up,
            TransportCommand.AmpZoomOut => Keys.Shift | Keys.Down,
            TransportCommand.AmpZoomMax => Keys.Control | Keys.Shift | Keys.Up,
            TransportCommand.AmpZoomReset => Keys.Control | Keys.Shift | Keys.Down,
            _ => Keys.None,
        };

        if (keyData == Keys.None)
        {
            return;
        }

        TryProcessWaveformShortcut(keyData, showUiFeedback: false);

        // キーボードではキーを離すまで一時停止する「戻る」操作も、
        // ボタンではクリック完了時点をキーアップ相当として直ちに再開する。
        if (_resumePlaybackAfterBackwardSeek)
        {
            ResumePlaybackAfterBackwardSeek();
        }

        UpdateTransportPlaybackState();
    }

    /// <summary>
    /// 戻る方向ジャンプ中は再生ヘッドが進まないよう一時停止する（キーアップで再開）。
    /// </summary>
    private void PauseForBackwardSeekHold()
    {
        if (!_audioPlayer.HasSource || !_audioPlayer.IsPlaying || _resumePlaybackAfterBackwardSeek)
        {
            return;
        }

        // タイマー基準の現在位置を確定してから止める
        var durationSec = _audioPlayer.Duration.TotalSeconds;
        if (durationSec > 0)
        {
            var elapsedSec = (Environment.TickCount64 - _anchorTickMs) / 1000d;
            _smoothProgress = Math.Clamp(_anchorProgress + elapsedSec / durationSec, 0d, 1d);
        }

        _playheadTimer.Stop();
        _audioPlayer.Pause();
        UpdateTransportPlaybackState();
        SeekPlayback(_smoothProgress);
        _resumePlaybackAfterBackwardSeek = true;
        UpdatePlayhead();
    }

    private void ResumePlaybackAfterBackwardSeek()
    {
        if (!_resumePlaybackAfterBackwardSeek)
        {
            return;
        }

        _resumePlaybackAfterBackwardSeek = false;
        if (!_audioPlayer.HasSource)
        {
            return;
        }

        SeekPlayback(_smoothProgress);
        _audioPlayer.Play();
        AnchorPlayhead(_smoothProgress);
        _playheadTimer.Start();
        UpdatePlayhead();
        UpdateTransportPlaybackState();
    }

    private void ShowColorDevPanel()
    {
        if (_colorDevPanel is null || _colorDevPanel.IsDisposed)
        {
            _colorDevPanel = new ColorDevPanelForm();
            _colorDevPanel.ColorsChanged += (_, _) => ApplyUiColors();
            PositionColorDevPanel(_colorDevPanel);
        }

        _colorDevPanel.RefreshRows();
        _colorDevPanel.Show(this);
        _colorDevPanel.BringToFront();
    }

    private void PositionColorDevPanel(ColorDevPanelForm panel)
    {
        var screen = Screen.FromControl(this).WorkingArea;
        var x = Math.Min(Right + 8, screen.Right - panel.Width);
        var y = Math.Max(screen.Top, Top);
        panel.Location = new Point(Math.Max(screen.Left, x), y);
    }

    private void ApplyUiColors()
    {
        // WinForms の BackColor はアルファ非対応（A≠255 だと起動／適用時に落ちる）
        BackColor = UiColors.ForControlBack(UiColors.WindowBack);
        ForeColor = UiColors.WindowFore;
        editorTextBox.BackColor = UiColors.ForControlBack(UiColors.LogBack);
        editorTextBox.ForeColor = UiColors.LogDefault;
        logEditorPanel.BackColor = editorTextBox.BackColor;
        logButtonPanel.BackColor = editorTextBox.BackColor;
        ApplyLogButtonColors();
        ApplyPlaylistSelectorColors();
        actionBar.BackColor = UiColors.ForControlBack(UiColors.ActionBarBack);
        ApplyActionBarButtonColors();
        ApplyActionBarTextColors();
        topMostCheckBox.ForeColor = UiColors.ActionOptionFore;
        detailedLogCheckBox.ForeColor = UiColors.ActionOptionFore;
        waapiStatusBar.ApplyColors();
        transportBar.ApplyColors();
        waveformView.RefreshAppearance();
        waveformHostPanel.BackColor = UiColors.ForControlBack(UiColors.WaveformScrollTrack);
        waveformHorizontalScrollBar.ApplyColors();
    }

    private void UpdateWaveformHorizontalScrollBar()
    {
        waveformHorizontalScrollBar.SetViewport(
            waveformView.TimeViewStart,
            waveformView.TimeViewSpan);
    }

    private void QueueWaveformHorizontalScroll(object? sender, double viewStart)
    {
        _pendingWaveformScrollStart = viewStart;
        if (!_waveformScrollTimer.Enabled)
        {
            _waveformScrollTimer.Start();
        }
    }

    private void FlushWaveformHorizontalScroll()
    {
        if (_pendingWaveformScrollStart is not double viewStart)
        {
            _waveformScrollTimer.Stop();
            return;
        }

        _pendingWaveformScrollStart = null;
        waveformView.SetTimeViewStart(viewStart);
        if (_pendingWaveformScrollStart is null)
        {
            _waveformScrollTimer.Stop();
        }
    }

    private void ApplyActionBarTextColors()
    {
        copyrightLinkLabel.ForeColor = UiColors.ActionCopyrightFore;
        copyrightLinkLabel.LinkColor = UiColors.ActionLinkFore;
        copyrightLinkLabel.ActiveLinkColor = UiColors.ActionLinkHoverFore;
        copyrightLinkLabel.VisitedLinkColor = UiColors.ActionLinkFore;
    }

    private void ApplyActionBarButtonColors()
    {
        var innerBack = UiColors.ForControlBack(UiColors.ActionButtonInnerBack);

        exportButton.BackColor = UiColors.ForControlBack(UiColors.ExportButtonFill);
        exportButton.ForeColor = UiColors.ExportButtonFore;
        exportButton.HoverBackColor = UiColors.ForControlBack(UiColors.ExportButtonHoverFill);
        exportButton.PressedBackColor = UiColors.ForControlBack(UiColors.ExportButtonHoverFill);
        exportButton.DisabledBackColor = innerBack;
        exportButton.DisabledForeColor = UiColors.ActionButtonDisabledFore;
        exportButton.BorderColor = UiColors.ForControlBack(UiColors.ExportButtonBack);
        exportButton.HoverBorderColor = UiColors.ForControlBack(UiColors.ExportButtonHoverBack);
        exportButton.PressedBorderColor = UiColors.ForControlBack(UiColors.ExportButtonPressedBack);
        exportButton.DisabledBorderColor = UiColors.ForControlBack(UiColors.ActionButtonDisabledBorder);
        exportButton.BorderSize = 2;

        clearButton.BackColor = UiColors.ForControlBack(UiColors.ClearButtonFill);
        clearButton.ForeColor = UiColors.ClearButtonFore;
        clearButton.HoverBackColor = UiColors.ForControlBack(UiColors.ClearButtonHoverFill);
        clearButton.PressedBackColor = UiColors.ForControlBack(UiColors.ClearButtonHoverFill);
        clearButton.DisabledBackColor = innerBack;
        clearButton.DisabledForeColor = UiColors.ActionButtonDisabledFore;
        clearButton.BorderColor = UiColors.ForControlBack(UiColors.ClearButtonBack);
        clearButton.HoverBorderColor = UiColors.ForControlBack(UiColors.ClearButtonHoverBack);
        clearButton.PressedBorderColor = UiColors.ForControlBack(UiColors.ClearButtonPressedBack);
        clearButton.DisabledBorderColor = UiColors.ForControlBack(UiColors.ActionButtonDisabledBorder);
        clearButton.BorderSize = 2;

        topMostCheckBox.ForeColor = UiColors.ActionOptionFore;
        topMostCheckBox.BackColor = actionBar.BackColor;
        RefreshFlatOptionControl(topMostCheckBox);
        detailedLogCheckBox.ForeColor = UiColors.ActionOptionFore;
        detailedLogCheckBox.BackColor = actionBar.BackColor;
        RefreshFlatOptionControl(detailedLogCheckBox);
    }

    private void ApplyLogButtonColors()
    {
        foreach (var button in new[] { logClearButton, logCopyButton, logDownloadButton })
        {
            button.BackColor = UiColors.ForControlBack(UiColors.LogButtonBack);
            button.ForeColor = UiColors.LogButtonFore;
            button.HoverBackColor = UiColors.ForControlBack(UiColors.LogButtonHoverBack);
            button.PressedBackColor = UiColors.ForControlBack(UiColors.LogButtonHoverBack);
            button.AccentColor = UiColors.ForControlBack(UiColors.LogButtonBorder);
            button.ActiveForeColor = UiColors.LogButtonFore;
        }
    }

    private void PositionLogButtons()
    {
        const int scrollbarGap = 0;
        const int bottomGap = 10;
        var scrollbarWidth = SystemInformation.VerticalScrollBarWidth;
        logButtonPanel.Left = Math.Max(
            0,
            logEditorPanel.ClientSize.Width
            - logButtonPanel.Width
            - scrollbarWidth
            - scrollbarGap);
        logButtonPanel.Top = Math.Max(
            0,
            logEditorPanel.ClientSize.Height
            - logButtonPanel.Height
            - bottomGap);
        logButtonPanel.BringToFront();
    }

    private void ApplyPlaylistSelectorColors()
    {
        var back = UiColors.ForControlBack(UiColors.PlaylistBack);
        playlistSelectorPanel.BackColor = back;
        playlistScrollPanel.BackColor = back;
        playlistListLayout.BackColor = back;
        playlistHeaderLabel.BackColor = back;
        playlistHeaderLabel.ForeColor = UiColors.PlaylistDefaultFore;
        playlistSeparator.BackColor = UiColors.ForControlBack(UiColors.PlaylistButtonBorder);
        transitionTimePanel.BackColor = back;
        transitionSettingsPanel.BackColor = back;
        fadeInSectionPanel.BackColor = back;
        fadeInChoicesPanel.BackColor = back;
        fadeInHeaderLabel.BackColor = back;
        fadeInHeaderLabel.ForeColor = UiColors.PlaylistDefaultFore;
        fadeOutSectionPanel.BackColor = back;
        transitionTimeChoicesPanel.BackColor = back;
        transitionTimeHeaderLabel.BackColor = back;
        transitionTimeHeaderLabel.ForeColor = UiColors.PlaylistDefaultFore;
        exitSourceAtSectionPanel.BackColor = back;
        exitSourceAtChoicesPanel.BackColor = back;
        exitSourceAtHeaderLabel.BackColor = back;
        exitSourceAtHeaderLabel.ForeColor = UiColors.PlaylistDefaultFore;
        destinationSyncSectionPanel.BackColor = back;
        destinationSyncChoicesPanel.BackColor = back;
        destinationSyncHeaderLabel.BackColor = back;
        destinationSyncHeaderLabel.ForeColor = UiColors.PlaylistDefaultFore;
        transitionTimeSeparator.BackColor =
            UiColors.ForControlBack(UiColors.PlaylistButtonBorder);
        foreach (var radio in new[]
        {
            fadeInNoneRadio,
            fadeInOneSecondRadio,
            fadeInThreeSecondsRadio,
            fadeInSixSecondsRadio,
            fadeInNineSecondsRadio,
            transitionTimeHalfSecondRadio,
            transitionTimeOneSecondRadio,
            transitionTimeThreeSecondsRadio,
            transitionTimeSixSecondsRadio,
            transitionTimeNineSecondsRadio,
            exitSourceImmediateRadio,
            exitSourceNextBarRadio,
            exitSourceNextBeatRadio,
            exitSourceNextCueRadio,
            exitSourceExitCueRadio,
            destinationSyncEntryCueRadio,
            destinationSyncSameTimeRadio,
        })
        {
            radio.BackColor = back;
            radio.ForeColor = UiColors.PlaylistOptionFore;
            RefreshFlatOptionControl(radio);
        }

        foreach (Control control in playlistListLayout.Controls)
        {
            control.BackColor = back;
            control.ForeColor = UiColors.PlaylistDefaultFore;
            if (control is not FlatPlaylistButton { Tag: WaveformOutputPart part } button)
            {
                continue;
            }

            button.Enabled = true;
            button.FlatAppearance.BorderSize = 0;
            button.IndicatorColor = null;
            button.IndicatorGlowLevel = 0d;
            var isAutomatic = _automaticPlaylistPlayback
                && _activeAutomaticPlaylistPartNumber == part.Number;
            var isManual = !_automaticPlaylistPlayback
                && _manualPlaylistPartNumber == part.Number;
            var isPending = _pendingPlaylistTransitionGeneration != 0
                && _requestedPlaylistPartNumber == part.Number;

            if (_audioPlayer.IsPlaying && isPending)
            {
                button.IndicatorColor = BlendColor(
                    UiColors.ForControlBack(UiColors.PlaylistButtonBorder),
                    UiColors.ForControlBack(UiColors.PlaylistTransitionBorder),
                    _pendingPlaylistBlinkLevel);
                button.ForeColor = UiColors.PlaylistActiveFore;
            }
            else if (_audioPlayer.IsPlaying && (isAutomatic || isManual))
            {
                button.IndicatorColor = UiColors.ForControlBack(
                    isManual
                        ? UiColors.PlaylistManualBorder
                        : UiColors.PlaylistTransitionBorder);
                button.ForeColor = UiColors.PlaylistActiveFore;
            }
            else if (_hoveredPlaylistPartNumber == part.Number
                || _hoveredPlaylistListPartNumber == part.Number)
            {
                button.ForeColor = UiColors.PlaylistHoverFore;
            }

            if (_playlistTransitionGlowPartNumber == part.Number
                && _playlistTransitionGlowLevel > 0d)
            {
                button.IndicatorColor ??= UiColors.ForControlBack(
                    isManual
                        ? UiColors.PlaylistManualBorder
                        : UiColors.PlaylistTransitionBorder);
                button.IndicatorGlowLevel = _playlistTransitionGlowLevel;
            }
        }

        EnsureHighlightedPlaylistVisible();
    }

    private static void RefreshFlatOptionControl(Control control)
    {
        switch (control)
        {
            case FlatOptionRadioButton radio:
                radio.ApplyColors();
                break;
            case FlatOptionCheckBox checkBox:
                checkBox.ApplyColors();
                break;
        }
    }

    private void EnsureHighlightedPlaylistVisible()
    {
        int? targetPartNumber = null;
        if (_pendingPlaylistTransitionGeneration != 0)
        {
            targetPartNumber = _requestedPlaylistPartNumber;
        }

        targetPartNumber ??= _hoveredPlaylistPartNumber;
        if (targetPartNumber is null && _audioPlayer.IsPlaying)
        {
            targetPartNumber = _automaticPlaylistPlayback
                ? _activeAutomaticPlaylistPartNumber
                : _manualPlaylistPartNumber;
        }

        targetPartNumber ??= _playlistTransitionGlowLevel > 0d
            ? _playlistTransitionGlowPartNumber
            : null;

        if (_lastAutoScrolledPlaylistPartNumber == targetPartNumber)
        {
            return;
        }

        _lastAutoScrolledPlaylistPartNumber = targetPartNumber;
        if (targetPartNumber is not int partNumber)
        {
            return;
        }

        var targetButton = playlistListLayout.Controls
            .OfType<Button>()
            .FirstOrDefault(button =>
                button.Tag is WaveformOutputPart part
                && part.Number == partNumber);
        if (targetButton is not null)
        {
            playlistScrollPanel.ScrollControlIntoView(targetButton);
        }
    }

    private void TransitionTimeRadio_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton { Checked: true, Tag: double fadeSeconds })
        {
            return;
        }

        _playlistFadeSeconds = fadeSeconds;
        ApplyPlaylistSelectorColors();
        WritePlaybackDiagnostic(
            "playlist.fade-out-preset-changed",
            new
            {
                fadeOutSeconds = fadeSeconds,
                appliesFromNextRequest = _pendingPlaylistTransitionGeneration != 0,
            });
    }

    private void FadeInTimeRadio_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton { Checked: true, Tag: double fadeInSeconds })
        {
            return;
        }

        _playlistFadeInSeconds = fadeInSeconds;
        ApplyPlaylistSelectorColors();
        WritePlaybackDiagnostic(
            "playlist.fade-in-preset-changed",
            new
            {
                fadeInSeconds,
                appliesFromNextRequest = _pendingPlaylistTransitionGeneration != 0,
            });
    }

    private void ExitSourceAtRadio_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton
            {
                Checked: true,
                Tag: PlaylistExitSourceMode mode,
            })
        {
            return;
        }

        _playlistExitSourceMode = mode;
        ApplyPlaylistSelectorColors();
        WritePlaybackDiagnostic(
            "playlist.exit-source-mode-changed",
            new
            {
                mode = mode.ToString(),
                appliesFromNextRequest = _pendingPlaylistTransitionGeneration != 0,
            });
    }

    private void DestinationSyncRadio_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton
            {
                Checked: true,
                Tag: PlaylistDestinationSyncMode mode,
            })
        {
            return;
        }

        _playlistDestinationSyncMode = mode;
        ApplyPlaylistSelectorColors();
        WritePlaybackDiagnostic(
            "playlist.destination-sync-mode-changed",
            new
            {
                mode = mode.ToString(),
                appliesFromNextRequest = _pendingPlaylistTransitionGeneration != 0,
            });
    }

    private static Color BlendColor(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            255,
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private void UpdatePlaylistSelectorWidth()
    {
        if (logAreaPanel.ClientSize.Width <= 0)
        {
            return;
        }

        var textWidth = TextRenderer.MeasureText(
            playlistHeaderLabel.Text,
            playlistHeaderLabel.Font,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
        foreach (Control control in playlistListLayout.Controls)
        {
            textWidth = Math.Max(
                textWidth,
                TextRenderer.MeasureText(
                    control.Text,
                    control.Font,
                    Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width);
        }

        var chromeWidth = playlistScrollPanel.Padding.Horizontal
            + SystemInformation.VerticalScrollBarWidth
            + 20;
        const int minimumWidth = 132;
        var maximumWidth = Math.Max(
            minimumWidth,
            (int)Math.Round(logAreaPanel.ClientSize.Width * 0.45d));
        var desiredWidth = Math.Clamp(
            textWidth + chromeWidth,
            minimumWidth,
            maximumWidth);
        if (playlistSelectorPanel.Width != desiredWidth)
        {
            playlistSelectorPanel.Width = desiredWidth;
        }
    }

    private void PopulatePlaylistChoices(IReadOnlyList<WaveformOutputPart> parts)
    {
        _populatingPlaylistChoices = true;
        try
        {
            DisposePlaylistChoiceControls();

            if (parts.Count == 0)
            {
                AddPlaylistStatusLabel("Playlist はありません");
                return;
            }

            foreach (var part in parts)
            {
                var name = Path.GetFileNameWithoutExtension(part.FileName);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = part.FileName;
                }

                var button = new FlatPlaylistButton
                {
                    AccessibleName = name,
                    AllowDrop = true,
                    AutoEllipsis = true,
                    BackColor = UiColors.ForControlBack(UiColors.PlaylistBack),
                    Cursor = Cursors.Hand,
                    Dock = DockStyle.Fill,
                    Enabled = true,
                    Font = new Font("Yu Gothic UI", 8.5F),
                    ForeColor = UiColors.PlaylistDefaultFore,
                    Height = 30,
                    Margin = new Padding(3, 1, 3, 1),
                    Padding = new Padding(2, 0, 2, 0),
                    TabStop = false,
                    Tag = part,
                    Text = name,
                    TextAlign = ContentAlignment.MiddleLeft,
                    UseMnemonic = false,
                    UseVisualStyleBackColor = false,
                };
                button.FlatAppearance.BorderSize = 0;
                button.Click += PlaylistButton_Click;
                button.MouseEnter += PlaylistButton_MouseEnter;
                button.MouseLeave += PlaylistButton_MouseLeave;
                button.DragEnter += EditorTextBox_DragEnter;
                button.DragDrop += EditorTextBox_DragDrop;
                playlistToolTip.SetToolTip(button, name);
                playlistListLayout.Controls.Add(button);
            }
        }
        finally
        {
            _populatingPlaylistChoices = false;
            UpdatePlaylistSelectorWidth();
            ApplyPlaylistSelectorColors();
        }
    }

    private void ClearPlaylistChoices(string message)
    {
        _populatingPlaylistChoices = true;
        try
        {
            DisposePlaylistChoiceControls();
            AddPlaylistStatusLabel(message);
        }
        finally
        {
            _populatingPlaylistChoices = false;
            UpdatePlaylistSelectorWidth();
            ApplyPlaylistSelectorColors();
        }
    }

    private void DisposePlaylistChoiceControls()
    {
        _automaticPlaylistPlayback = false;
        _activeAutomaticPlaylistPartNumber = null;
        _requestedPlaylistPartNumber = null;
        _manualPlaylistPartNumber = null;
        _hoveredPlaylistPartNumber = null;
        _hoveredPlaylistListPartNumber = null;
        _lastAutoScrolledPlaylistPartNumber = null;
        ClearPlaylistTransitionGlow();
        waveformView.SetPlaylistHoverHighlight(null);
        var controls = playlistListLayout.Controls.Cast<Control>().ToArray();
        playlistListLayout.Controls.Clear();
        playlistListLayout.RowStyles.Clear();
        playlistListLayout.RowCount = 0;
        foreach (var control in controls)
        {
            control.Dispose();
        }
    }

    private void ClearPlaylistPlaybackSelection()
    {
        _automaticPlaylistPlayback = false;
        _activeAutomaticPlaylistPartNumber = null;
        _requestedPlaylistPartNumber = null;
        _manualPlaylistPartNumber = null;
        ApplyPlaylistSelectorColors();
    }

    private void AddPlaylistStatusLabel(string message)
    {
        var label = new Label
        {
            AllowDrop = true,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            Font = new Font("Yu Gothic UI", 9F),
            Height = 30,
            Margin = new Padding(3, 1, 3, 1),
            Padding = new Padding(2, 0, 2, 0),
            Text = message,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        label.DragEnter += EditorTextBox_DragEnter;
        label.DragDrop += EditorTextBox_DragDrop;
        playlistListLayout.Controls.Add(label);
    }

    private void PlaylistButton_Click(object? sender, EventArgs e)
    {
        if (_populatingPlaylistChoices
            || sender is not Button { Tag: WaveformOutputPart part })
        {
            return;
        }

        WritePlaybackDiagnostic(
            "playlist.button-clicked",
            new
            {
                part = part.Number,
                part.FileName,
                part.StartSampleOffset,
                part.EndSampleOffset,
            });
        RequestPlaylistPlayback(part);
    }

    private void PlaylistButton_MouseEnter(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: WaveformOutputPart part })
        {
            return;
        }

        _hoveredPlaylistListPartNumber = part.Number;
        waveformView.SetPlaylistHoverHighlight(part.Number);
        ApplyPlaylistSelectorColors();
    }

    private void PlaylistButton_MouseLeave(object? sender, EventArgs e)
    {
        if (sender is not Button { Tag: WaveformOutputPart part }
            || _hoveredPlaylistListPartNumber != part.Number)
        {
            return;
        }

        _hoveredPlaylistListPartNumber = null;
        waveformView.SetPlaylistHoverHighlight(null);
        ApplyPlaylistSelectorColors();
    }

    private void SetManualPlaylistHighlight(double progress)
    {
        if (_loadedPreview is not { } preview || preview.WavInfo.FrameCount <= 0)
        {
            return;
        }

        var frameCount = preview.WavInfo.FrameCount;
        var sample = (long)Math.Clamp(
            Math.Floor(Math.Clamp(progress, 0d, 1d) * frameCount),
            0d,
            Math.Max(0L, frameCount - 1));
        var partNumber = preview.OutputParts
            .Where(p => sample >= p.StartSampleOffset && sample < p.EndSampleOffset)
            .Select(p => (int?)p.Number)
            .FirstOrDefault();

        if (!_automaticPlaylistPlayback && _manualPlaylistPartNumber == partNumber)
        {
            return;
        }

        _automaticPlaylistPlayback = false;
        _activeAutomaticPlaylistPartNumber = null;
        _requestedPlaylistPartNumber = null;
        _manualPlaylistPartNumber = partNumber;
        WritePlaybackDiagnostic(
            "timeline.manual-part-changed",
            new { progress, partNumber });
        ApplyPlaylistSelectorColors();
    }

    private void RequestPlaylistPlayback(WaveformOutputPart target)
    {
        if (_loadedPreview is not { } preview
            || !_audioPlayer.HasSource
            || preview.WavInfo.FrameCount <= 0)
        {
            return;
        }

        var frameCount = preview.WavInfo.FrameCount;
        WritePlaybackDiagnostic(
            "playlist.request",
            new
            {
                target = target.Number,
                target.FileName,
                target.StartSampleOffset,
                target.EndSampleOffset,
                wasPlaying = _audioPlayer.IsPlaying,
            });
        if (!_audioPlayer.IsPlaying)
        {
            ClearPendingPlaylistUiTransition();
            _audioPlayer.CancelPlaylistTransition();
            if (!_audioPlayer.StartPlaylistRange(target.StartSampleOffset, target.EndSampleOffset))
            {
                WritePlaybackDiagnostic(
                    "playlist.immediate-start-rejected",
                    new { target = target.Number });
                ClearPlaylistPlaybackSelection();
                return;
            }

            _automaticPlaylistPlayback = true;
            _activeAutomaticPlaylistPartNumber = target.Number;
            _requestedPlaylistPartNumber = null;
            _manualPlaylistPartNumber = null;
            var progress = target.StartSampleOffset / (double)frameCount;
            AnchorPlayhead(progress);
            waveformView.SetPlayhead(progress, recordTrail: false, ensureVisible: true);
            waveformView.SetExitPlayhead(null);
            waveformView.SetFadeOutPlayhead(null);
            _playheadTimer.Start();
            UpdateTransportPlaybackState();
            StartPlaylistTransitionGlow();
            WritePlaybackDiagnostic(
                "playlist.immediate-started",
                new { target = target.Number, progress });
            return;
        }

        // 予約先と現在再生中の項目は別管理する。遷移完了までは現在色を維持する。
        _requestedPlaylistPartNumber = target.Number;
        ApplyPlaylistSelectorColors();
        var currentSample = Math.Clamp(
            (long)Math.Floor(_smoothProgress * frameCount),
            0L,
            Math.Max(0L, frameCount - 1));
        var currentPart = preview.OutputParts
            .Where(p => currentSample >= p.StartSampleOffset && currentSample < p.EndSampleOffset)
            .Select(p => (WaveformOutputPart?)p)
            .FirstOrDefault();
        var currentPartStart = currentPart?.StartSampleOffset ?? 0L;
        var currentPartEnd = currentPart?.EndSampleOffset ?? frameCount;

        var transitionLimit = currentPartEnd;
        var hasActiveLoop = _audioPlayer.TryGetActiveLoopProgress(
            out _,
            out var loopEndProgress);
        if (hasActiveLoop)
        {
            var loopEnd = (long)Math.Round(loopEndProgress * frameCount);
            if (loopEnd > currentSample)
            {
                transitionLimit = Math.Min(transitionLimit, loopEnd);
            }
        }

        var destinationSyncMode = _playlistDestinationSyncMode;
        var anacrusisFrames =
            destinationSyncMode == PlaylistDestinationSyncMode.EntryCue
                ? GetLeadingAnacrusisFrameCount(preview, target)
                : 0L;
        var exitSourceMode = _playlistExitSourceMode;
        var boundaries = GetPlaylistExitBoundaries(
            preview,
            _previewSession?.EffectiveMarkers ?? preview.Markers,
            exitSourceMode,
            currentSample,
            currentPartStart,
            currentPartEnd,
            transitionLimit,
            hasActiveLoop);
        WritePlaybackDiagnostic(
            "playlist.transition-candidates",
            new
            {
                target = target.Number,
                exitSourceMode = exitSourceMode.ToString(),
                destinationSyncMode = destinationSyncMode.ToString(),
                currentSample,
                currentPartStart,
                currentPartEnd,
                transitionLimit,
                anacrusisFrames,
                boundaries,
            });

        if (exitSourceMode == PlaylistExitSourceMode.Immediate)
        {
            if (TrySchedulePlaylistTransition(
                    target,
                    currentPartStart,
                    currentPartEnd,
                    anacrusisFrames,
                    sourceExitSample: null,
                    allowShortPreRoll: true,
                    exitSourceMode,
                    destinationSyncMode,
                    out var terminalFailure))
            {
                return;
            }

            if (terminalFailure)
            {
                _requestedPlaylistPartNumber = null;
                ApplyPlaylistSelectorColors();
                return;
            }
        }
        else
        {
            var allowShortPreRoll =
                exitSourceMode is PlaylistExitSourceMode.NextCue
                    or PlaylistExitSourceMode.ExitCue;
            foreach (var boundary in boundaries)
            {
                if (TrySchedulePlaylistTransition(
                        target,
                        currentPartStart,
                        currentPartEnd,
                        anacrusisFrames,
                        boundary,
                        allowShortPreRoll,
                        exitSourceMode,
                        destinationSyncMode,
                        out var candidateTerminalFailure))
                {
                    return;
                }

                if (candidateTerminalFailure)
                {
                    _requestedPlaylistPartNumber = null;
                    ApplyPlaylistSelectorColors();
                    return;
                }
            }

            // Exit Cue を既に音声バッファへ渡していた場合、過去へ戻さず即時退出する。
            var fallbackTerminalFailure = false;
            if (exitSourceMode == PlaylistExitSourceMode.ExitCue
                && boundaries.Count > 0
                && boundaries[0] <= _audioPlayer.CurrentMainSample
                && TrySchedulePlaylistTransition(
                    target,
                    currentPartStart,
                    currentPartEnd,
                    anacrusisFrames,
                    sourceExitSample: null,
                    allowShortPreRoll: true,
                    exitSourceMode,
                    destinationSyncMode,
                    out fallbackTerminalFailure))
            {
                return;
            }
            else if (fallbackTerminalFailure)
            {
                _requestedPlaylistPartNumber = null;
                ApplyPlaylistSelectorColors();
                return;
            }
        }

        AppendReport(
            $"Playlist 遷移を予約できませんでした: {target.FileName}"
            + Environment.NewLine);
        WritePlaybackDiagnostic(
            "playlist.transition-schedule-failed",
            new { target = target.Number, currentSample, currentPartEnd });
        _requestedPlaylistPartNumber = null;
        ApplyPlaylistSelectorColors();
    }

    private bool TrySchedulePlaylistTransition(
        WaveformOutputPart target,
        long currentPartStart,
        long currentPartEnd,
        long anacrusisFrames,
        long? sourceExitSample,
        bool allowShortPreRoll,
        PlaylistExitSourceMode exitSourceMode,
        PlaylistDestinationSyncMode destinationSyncMode,
        out bool terminalFailure)
    {
        terminalFailure = false;
        if (!_audioPlayer.TrySchedulePlaylistTransition(
                target.StartSampleOffset,
                target.EndSampleOffset,
                sourceExitSample,
                currentPartStart,
                destinationSyncMode,
                anacrusisFrames,
                allowShortPreRoll,
                currentPartEnd,
                _playlistFadeInSeconds,
                _playlistFadeSeconds,
                out var schedule))
        {
            if (schedule.RejectionReason == "same-time-out-of-range")
            {
                terminalFailure = true;
                var targetDuration =
                    target.EndSampleOffset - target.StartSampleOffset;
                AppendReport(
                    $"Same Time の遷移位置が遷移先の範囲外です: {target.FileName}"
                    + $" (相対={schedule.SourceRelativeSample}, 長さ={targetDuration})"
                    + Environment.NewLine);
                WritePlaybackDiagnostic(
                    "playlist.transition-rejected-same-time-range",
                    new
                    {
                        target = target.Number,
                        target.FileName,
                        exitSourceMode = exitSourceMode.ToString(),
                        destinationSyncMode = destinationSyncMode.ToString(),
                        currentPartStart,
                        currentPartEnd,
                        schedule.SyncBoundarySample,
                        schedule.SourceRelativeSample,
                        schedule.TargetSwitchSample,
                        target.StartSampleOffset,
                        target.EndSampleOffset,
                        targetDuration,
                    });
            }

            return false;
        }

        SetPendingPlaylistUiTransition(
            schedule.Generation,
            schedule.TriggerSample,
            schedule.SyncBoundarySample,
            target.StartSampleOffset,
            schedule.TargetSwitchSample);
        WritePlaybackDiagnostic(
            "playlist.transition-scheduled",
            new
            {
                target = target.Number,
                exitSourceMode = exitSourceMode.ToString(),
                destinationSyncMode = destinationSyncMode.ToString(),
                schedule.Generation,
                schedule.TriggerSample,
                schedule.SyncBoundarySample,
                schedule.TargetSwitchSample,
                schedule.SourceRelativeSample,
                schedule.StartedImmediately,
                requestedSourceExitSample = sourceExitSample,
                anacrusisFrames,
                allowShortPreRoll,
                fadeInSeconds = _playlistFadeInSeconds,
                fadeSeconds = _playlistFadeSeconds,
                currentPartEnd,
            });

        if (schedule.StartedImmediately
            && schedule.TriggerSample == schedule.SyncBoundarySample)
        {
            CommitPendingPlaylistUiTransition(
                schedule.SyncBoundarySample,
                schedule.TargetSwitchSample,
                "immediate");
        }

        return true;
    }

    private static IReadOnlyList<long> GetPlaylistExitBoundaries(
        WaveformPreviewData preview,
        IReadOnlyList<WaveformMarkerMark> markers,
        PlaylistExitSourceMode mode,
        long currentSample,
        long currentPartStart,
        long currentPartEnd,
        long transitionLimit,
        bool hasActiveLoop)
    {
        IEnumerable<long> candidates = mode switch
        {
            PlaylistExitSourceMode.Immediate => [],
            PlaylistExitSourceMode.NextBar => preview.Bars
                .Where(mark => !mark.IsTempoChangeOnly)
                .Select(mark => mark.SampleOffset)
                .Append(transitionLimit),
            PlaylistExitSourceMode.NextBeat => EnumerateBeatBoundaries(
                    preview.Bars,
                    transitionLimit)
                .Append(transitionLimit),
            PlaylistExitSourceMode.NextCue => markers
                .Where(marker =>
                    marker.SampleOffset >= currentPartStart
                    && marker.SampleOffset < currentPartEnd)
                .Select(marker => marker.SampleOffset),
            PlaylistExitSourceMode.ExitCue =>
            [
                GetPlaylistExitCueSample(
                    preview,
                    currentPartStart,
                    currentPartEnd,
                    transitionLimit,
                    hasActiveLoop),
            ],
            _ => [],
        };

        return candidates
            .Where(sample =>
                (mode == PlaylistExitSourceMode.ExitCue || sample > currentSample)
                && sample <= (mode == PlaylistExitSourceMode.ExitCue
                    ? currentPartEnd
                    : transitionLimit))
            .Distinct()
            .Order()
            .ToArray();
    }

    private static IEnumerable<long> EnumerateBeatBoundaries(
        IReadOnlyList<WaveformBarMark> bars,
        long limit)
    {
        var barLines = bars
            .Where(mark => !mark.IsTempoChangeOnly)
            .OrderBy(mark => mark.SampleOffset)
            .ToArray();
        for (var i = 0; i + 1 < barLines.Length; i++)
        {
            var bar = barLines[i];
            var next = barLines[i + 1];
            if (bar.SampleOffset >= limit)
            {
                yield break;
            }

            var end = Math.Min(next.SampleOffset, limit);
            var span = next.SampleOffset - bar.SampleOffset;
            var beatCount = Math.Max(1, bar.Numerator);
            if (span <= 0)
            {
                continue;
            }

            for (var beat = 0; beat < beatCount; beat++)
            {
                var sample = bar.SampleOffset
                    + (long)Math.Round(
                        span * beat / (double)beatCount,
                        MidpointRounding.AwayFromZero);
                if (sample < end)
                {
                    yield return sample;
                }
            }
        }
    }

    private static long GetPlaylistExitCueSample(
        WaveformPreviewData preview,
        long currentPartStart,
        long currentPartEnd,
        long transitionLimit,
        bool hasActiveLoop)
    {
        var lastRegion = preview.Regions
            .Where(region =>
                !region.IsExcluded
                && region.StartSampleOffset < currentPartEnd
                && region.EndSampleOffset > currentPartStart)
            .OrderBy(region => region.StartSampleOffset)
            .Select(region => (WaveformRegionMark?)region)
            .LastOrDefault();
        var exitCue = lastRegion is { } last
            && last.NameSuffix.Equals(
                WaveformRegionBuilder.LoopEndSuffix,
                StringComparison.OrdinalIgnoreCase)
            ? Math.Max(currentPartStart, last.StartSampleOffset)
            : currentPartEnd;
        return hasActiveLoop
            ? Math.Min(exitCue, transitionLimit)
            : exitCue;
    }

    private static long GetLeadingAnacrusisFrameCount(
        WaveformPreviewData preview,
        WaveformOutputPart target)
    {
        var expectedStart = target.StartSampleOffset;
        foreach (var region in preview.Regions.OrderBy(region => region.StartSampleOffset))
        {
            if (region.EndSampleOffset <= expectedStart)
            {
                continue;
            }

            if (region.StartSampleOffset != expectedStart
                || region.IsExcluded
                || !region.NameSuffix.Equals(
                    WaveformRegionBuilder.AnacrusisSuffix,
                    StringComparison.OrdinalIgnoreCase)
                || region.EndSampleOffset > target.EndSampleOffset)
            {
                break;
            }

            expectedStart = region.EndSampleOffset;
        }

        return Math.Max(0L, expectedStart - target.StartSampleOffset);
    }

    private void SetPendingPlaylistUiTransition(
        long generation,
        long triggerSample,
        long syncBoundarySample,
        long targetSample,
        long targetEntrySample)
    {
        _pendingPlaylistTransitionGeneration = generation;
        _pendingPlaylistBoundarySample = triggerSample;
        _pendingPlaylistSyncBoundarySample = syncBoundarySample;
        _pendingPlaylistTargetSample = targetSample;
        _pendingPlaylistTargetEntrySample = targetEntrySample;
        _pendingPlaylistAudioStarted = false;
        waveformView.SetAnacrusisPlayhead(null);
        _pendingPlaylistBlinkLevel = GetPlaylistBeatBlinkLevel();
        ApplyPlaylistSelectorColors();
        _playlistBlinkTimer.Start();
        WritePlaybackDiagnostic(
            "playlist.pending-set",
            new { generation, triggerSample, syncBoundarySample, targetSample, targetEntrySample });
    }

    private double CommitPendingPlaylistUiTransition(
        long oldTimelineSample,
        long targetTimelineSample,
        string reason)
    {
        if (_loadedPreview is not { } preview
            || preview.WavInfo.FrameCount <= 0
            || _pendingPlaylistTransitionGeneration == 0)
        {
            return _smoothProgress;
        }

        var frameCount = preview.WavInfo.FrameCount;
        var progress = Math.Clamp(
            targetTimelineSample / (double)frameCount,
            0d,
            1d);
        AnchorPlayhead(progress);
        waveformView.ClearPlayheadTrail();
        waveformView.SetPlayhead(
            progress,
            recordTrail: false,
            ensureVisible: true);
        _activeAutomaticPlaylistPartNumber =
            _requestedPlaylistPartNumber
            ?? preview.OutputParts
                .Where(part =>
                    _pendingPlaylistTargetSample >= part.StartSampleOffset
                    && _pendingPlaylistTargetSample < part.EndSampleOffset)
                .Select(part => (int?)part.Number)
                .FirstOrDefault();
        _automaticPlaylistPlayback = true;
        _manualPlaylistPartNumber = null;
        ApplyPlaylistSelectorColors();
        WritePlaybackDiagnostic(
            "playlist.transition-ui-committed",
            new
            {
                requestedGeneration = _pendingPlaylistTransitionGeneration,
                trigger = _pendingPlaylistBoundarySample,
                sync = _pendingPlaylistSyncBoundarySample,
                target = _pendingPlaylistTargetSample,
                targetEntry = _pendingPlaylistTargetEntrySample,
                oldTimelineSample,
                targetTimelineSample,
                progress,
                reason,
            });
        StartPlaylistTransitionGlow();
        ClearPendingPlaylistUiTransition();
        return progress;
    }

    private void ClearPendingPlaylistUiTransition()
    {
        var wasPending = _pendingPlaylistTransitionGeneration != 0;
        if (wasPending)
        {
            WritePlaybackDiagnostic(
                "playlist.pending-cleared",
                new
                {
                    generation = _pendingPlaylistTransitionGeneration,
                    trigger = _pendingPlaylistBoundarySample,
                    sync = _pendingPlaylistSyncBoundarySample,
                    target = _pendingPlaylistTargetSample,
                    targetEntry = _pendingPlaylistTargetEntrySample,
                    audioStarted = _pendingPlaylistAudioStarted,
                });
        }

        _pendingPlaylistTransitionGeneration = 0;
        _pendingPlaylistBoundarySample = 0;
        _pendingPlaylistSyncBoundarySample = 0;
        _pendingPlaylistTargetSample = 0;
        _pendingPlaylistTargetEntrySample = 0;
        _pendingPlaylistAudioStarted = false;
        _requestedPlaylistPartNumber = null;
        _pendingPlaylistBlinkLevel = 0d;
        _playlistBlinkTimer.Stop();
        waveformView.SetAnacrusisPlayhead(null);
        if (wasPending)
        {
            ApplyPlaylistSelectorColors();
        }
    }

    private void UpdatePendingPlaylistBlink()
    {
        if (_pendingPlaylistTransitionGeneration == 0)
        {
            return;
        }

        var level = GetPlaylistBeatBlinkLevel();
        if (Math.Abs(level - _pendingPlaylistBlinkLevel) < 0.005d)
        {
            return;
        }

        _pendingPlaylistBlinkLevel = level;
        ApplyPlaylistSelectorColors();
    }

    private double GetPlaylistBeatBlinkLevel()
    {
        if (!TryGetPlaylistBeatTiming(out var beatPhase, out _))
        {
            return 0.5d;
        }

        // 拍頭=100%、裏拍=50%。両方向をコサインで滑らかにつなぐ。
        return 0.75d + 0.25d * Math.Cos(beatPhase * Math.PI * 2d);
    }

    private bool TryGetPlaylistBeatTiming(
        out double beatPhase,
        out double beatDurationMs)
    {
        beatPhase = 0d;
        beatDurationMs = 0d;
        if (_loadedPreview is not { } preview
            || preview.WavInfo.FrameCount <= 0
            || preview.WavInfo.SampleRate == 0)
        {
            return false;
        }

        var frameCount = preview.WavInfo.FrameCount;
        var sample = (long)Math.Clamp(
            Math.Floor(Math.Clamp(_smoothProgress, 0d, 1d) * frameCount),
            0d,
            Math.Max(0L, frameCount - 1));
        var bar = preview.Bars
            .Where(mark => !mark.IsTempoChangeOnly && mark.SampleOffset <= sample)
            .OrderBy(mark => mark.SampleOffset)
            .LastOrDefault();
        var tempo = preview.Bars
            .Where(mark => mark.SampleOffset <= sample)
            .OrderBy(mark => mark.SampleOffset)
            .LastOrDefault();

        var bpm = tempo.Bpm > 0d ? tempo.Bpm : bar.Bpm;
        var denominator = tempo.Denominator > 0 ? tempo.Denominator : bar.Denominator;
        if (bpm <= 0d || denominator <= 0)
        {
            return false;
        }

        var beatSamples = preview.WavInfo.SampleRate
            * 60d
            / bpm
            * 4d
            / denominator;
        if (beatSamples <= 1d)
        {
            return false;
        }

        var relativeBeats = Math.Max(0d, sample - bar.SampleOffset) / beatSamples;
        beatPhase = relativeBeats - Math.Floor(relativeBeats);
        beatDurationMs = beatSamples / preview.WavInfo.SampleRate * 1000d;
        return true;
    }

    private void StartPlaylistTransitionGlow()
    {
        var activePartNumber = _automaticPlaylistPlayback
            ? _activeAutomaticPlaylistPartNumber
            : _manualPlaylistPartNumber;
        if (activePartNumber is not int partNumber)
        {
            ApplyPlaylistSelectorColors();
            return;
        }

        _playlistTransitionGlowPartNumber = partNumber;
        _playlistTransitionGlowStartTickMs = Environment.TickCount64;
        if (TryGetPlaylistBeatTiming(out var beatPhase, out var beatDurationMs))
        {
            var remainingBeat = beatPhase <= 1e-3 ? 1d : 1d - beatPhase;
            _playlistTransitionGlowDurationMs = Math.Clamp(
                beatDurationMs * remainingBeat,
                50d,
                5000d);
        }
        else
        {
            _playlistTransitionGlowDurationMs = 500d;
        }

        _playlistTransitionGlowLevel = 1d;
        _playlistTransitionGlowTimer.Start();
        ApplyPlaylistSelectorColors();
    }

    private void UpdatePlaylistTransitionGlow()
    {
        var elapsed = Math.Max(
            0L,
            Environment.TickCount64 - _playlistTransitionGlowStartTickMs);
        var t = Math.Clamp(
            elapsed / Math.Max(1d, _playlistTransitionGlowDurationMs),
            0d,
            1d);
        if (t >= 1d)
        {
            ClearPlaylistTransitionGlow();
            return;
        }

        // 小節頭の全点灯から次の拍頭の消灯まで、片方向のコサインで滑らかに落とす。
        _playlistTransitionGlowLevel = (1d + Math.Cos(t * Math.PI)) * 0.5d;
        ApplyPlaylistSelectorColors();
    }

    private void ClearPlaylistTransitionGlow()
    {
        _playlistTransitionGlowTimer.Stop();
        _playlistTransitionGlowPartNumber = null;
        _playlistTransitionGlowStartTickMs = 0;
        _playlistTransitionGlowDurationMs = 0d;
        _playlistTransitionGlowLevel = 0d;
        ApplyPlaylistSelectorColors();
    }

    private static Image? LoadBrandLogo()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Branding", "MiyabiGameAudio.png");
        if (!File.Exists(path))
        {
            return null;
        }

        using var source = Image.FromFile(path);
        return new Bitmap(source);
    }

    private void UpdateExportButtonState()
    {
        exportButton.Enabled =
            !_exportBusy
            && _loadedPreview is { OutputParts.Count: > 0 };
    }

    private void TopMostCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        TopMost = topMostCheckBox.Checked;
        DeveloperSettings.SaveTopMost(topMostCheckBox.Checked);
    }

    private void CopyrightLinkLabel_LinkClicked(object? sender, LinkLabelLinkClickedEventArgs e)
    {
        const string repositoryUrl = "https://github.com/mga-ueda/MGA-Wwise-IMImporter";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(repositoryUrl)
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Unable to open GitHub",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void DetailedLogCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        DeveloperSettings.SaveDetailedPlaybackLog(detailedLogCheckBox.Checked);
        if (detailedLogCheckBox.Checked)
        {
            WritePlaybackDiagnostic("diagnostic.enabled");
        }
    }

    private void LogClearButton_Click(object? sender, EventArgs e)
    {
        WritePlaybackDiagnostic("log.cleared");
        editorTextBox.Clear();
    }

    private void LogCopyButton_Click(object? sender, EventArgs e)
    {
        if (editorTextBox.TextLength == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(editorTextBox.Text);
            WritePlaybackDiagnostic("log.copied", new { characters = editorTextBox.TextLength });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ログのコピーに失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LogDownloadButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = "log",
            FileName = $"MgaWwiseIMImporter-{DateTime.Now:yyyyMMdd-HHmmss}.log",
            Filter = "Log file (*.log)|*.log|Text file (*.txt)|*.txt|All files (*.*)|*.*",
            OverwritePrompt = true,
            Title = "ログを保存",
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, editorTextBox.Text, new UTF8Encoding(false));
            WritePlaybackDiagnostic(
                "log.downloaded",
                new { path = dialog.FileName, characters = editorTextBox.TextLength });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ログの保存に失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ClearButton_Click(object? sender, EventArgs e)
    {
        _exportGeneration++;
        _exportBusy = false;
        _loadedPreview = null;
        _previewSession = null;
        _resumePlaybackAfterBackwardSeek = false;
        ClearPendingPlaylistUiTransition();
        _playheadTimer.Stop();
        _audioPlayer.Stop();
        _audioPlayer.Clear();
        UpdateTransportPlaybackState();
        waveformView.ClearPreview();
        UpdateTransportPosition();
        editorTextBox.Clear();
        ClearPlaylistChoices("Playlist はありません");
        UpdateExportButtonState();
    }

    private void RestoreWindowBounds()
    {
        var settings = WindowSettings.Load();
        if (settings is null)
        {
            StartPosition = FormStartPosition.CenterScreen;
            return;
        }

        if (settings.Width > 0 && settings.Height > 0)
        {
            // 起動時に INI から読んだ外形サイズを、このセッションの縮小限界にする。
            MinimumSize = new Size(settings.Width, settings.Height);
        }

        if (!settings.TryApply(this))
        {
            StartPosition = FormStartPosition.CenterScreen;
        }
    }

    private void ApplyDarkTitleBar()
    {
        if (!IsHandleCreated)
        {
            return;
        }

        var useDarkMode = 1;
        _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
    }

    /// <summary>
    /// エディタのスクロールバー等をダークテーマ寄りの見た目にする（対応 OS のみ）。
    /// </summary>
    private void ApplyDarkEditorChrome()
    {
        if (!editorTextBox.IsHandleCreated)
        {
            return;
        }

        var useDarkMode = 1;
        _ = DwmSetWindowAttribute(
            editorTextBox.Handle,
            DwmwaUseImmersiveDarkMode,
            ref useDarkMode,
            sizeof(int));

        // Win10 1809+ / Win11: Explorer ダーク・スクロールバー
        _ = SetWindowTheme(editorTextBox.Handle, "DarkMode_Explorer", null);
    }

    private void ApplyDarkScrollableChrome()
    {
        ApplyDarkScrollChrome(playlistScrollPanel);
        ApplyDarkScrollChrome(transportBar);
    }

    private static void ApplyDarkScrollChrome(Control control)
    {
        if (!control.IsHandleCreated)
        {
            return;
        }

        _ = SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
        control.Invalidate(invalidateChildren: true);
    }

    private void ApplyFixedLogLineSpacing(bool entireDocument = false)
    {
        if (!editorTextBox.IsHandleCreated)
        {
            return;
        }

        var format = new ParaFormat2
        {
            cbSize = Marshal.SizeOf<ParaFormat2>(),
            dwMask = PfmLineSpacing,
            bLineSpacingRule = LineSpacingExact,
            dyLineSpacing = LogLineSpacingTwips,
            rgxTabs = new int[32],
        };

        if (entireDocument)
        {
            var previousStart = editorTextBox.SelectionStart;
            var previousLength = editorTextBox.SelectionLength;
            editorTextBox.SelectAll();
            _ = SendMessage(editorTextBox.Handle, EmSetParaFormat, IntPtr.Zero, ref format);
            editorTextBox.Select(previousStart, previousLength);
            return;
        }

        _ = SendMessage(editorTextBox.Handle, EmSetParaFormat, IntPtr.Zero, ref format);
    }

    private void EditorTextBox_DragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            return;
        }

        e.Effect = DragDropEffects.None;
    }

    private void EditorTextBox_DragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }

        ProcessDroppedFiles(files);
    }

    private void LoadAutoWaveAsDropped()
    {
        if (!_developerSettings.AutoLoadOnStartup)
        {
            return;
        }

        var wavPath = _developerSettings.ResolveAutoLoadWavePath();
        if (wavPath is null)
        {
            return;
        }

        if (!File.Exists(wavPath))
        {
            AppendReport($"自動読み込み対象が見つかりません: {wavPath}" + Environment.NewLine);
            return;
        }

        ProcessDroppedFiles([wavPath]);
    }

    private async void ProcessDroppedFiles(IEnumerable<string> files)
    {
        var exportGeneration = ++_exportGeneration;
        _loadedPreview = null;
        _previewSession = null;
        _exportBusy = false;
        UpdateTransportPosition();
        ClearPendingPlaylistUiTransition();
        ClearPlaylistChoices("読み込み中…");
        UpdateExportButtonState();
        waveformView.ClearExportHighlight();
        _playheadTimer.Stop();
        _audioPlayer.Stop();
        UpdateTransportPlaybackState();

        // 解析中に OS が白消ししないよう、先に暗いフレームを確定する
        waveformView.CommitDarkFrame();

        // 巨大 WAV のピーク走査で UI を止めないよう、解析は背景スレッドで行う
        var fileList = files.ToList();
        var (report, preview) = await Task.Run(() =>
        {
            var text = DroppedFilesProcessor.Process(fileList, out var previewData);
            return (text, previewData);
        });

        if (IsDisposed || exportGeneration != _exportGeneration)
        {
            return;
        }

        AppendReport(report);

        if (preview is null)
        {
            _audioPlayer.Clear();
            waveformView.ClearPreview();
            _loadedPreview = null;
            _previewSession = null;
            UpdateTransportPosition();
            ClearPlaylistChoices("Playlist を取得できませんでした");
            UpdateExportButtonState();
            return;
        }

        _previewSession = new WaveformPreviewSession(preview);
        waveformView.SetPreview(
            preview.Peaks,
            preview.SourcePath,
            preview.WavInfo,
            preview.Bars,
            _previewSession.EffectiveMarkers,
            preview.Cycles,
            preview.Regions,
            preview.OutputParts);
        try
        {
            _audioPlayer.Load(preview.SourcePath);
            _audioPlayer.SetLoopPlans(WaveAudioPlayer.BuildLoopPlans(preview.Regions));
            waveformView.SetPlayhead(0, recordTrail: false);
        }
        catch (Exception ex)
        {
            AppendReport(
                $"=== エラー ==={Environment.NewLine}"
                + $"Message : 再生の準備に失敗: {ex.Message}{Environment.NewLine}{Environment.NewLine}");
        }

        _loadedPreview = preview;
        UpdateTransportPosition();
        PopulatePlaylistChoices(preview.OutputParts);
        WritePlaybackDiagnostic(
            "source.loaded",
            new
            {
                preview.SourcePath,
                preview.WavInfo.FrameCount,
                preview.WavInfo.SampleRate,
                bars = preview.Bars.Count,
                regions = preview.Regions.Count,
                playlists = preview.OutputParts.Select(part => new
                {
                    part.Number,
                    part.FileName,
                    part.StartSampleOffset,
                    part.EndSampleOffset,
                }),
            });
        UpdateExportButtonState();

        if (preview.OutputParts.Count == 0)
        {
            return;
        }

        var directory = Path.GetDirectoryName(preview.SourcePath) ?? string.Empty;
        AppendReport(
            $"=== Export ==={Environment.NewLine}"
            + $"Message : 出力パート {preview.OutputParts.Count} 件。［エクスポート］で分割 WAV を書き出せます。{Environment.NewLine}"
            + $"保存先  : {directory}{Environment.NewLine}{Environment.NewLine}");
    }

    private async void ExportButton_Click(object? sender, EventArgs e)
    {
        if (_exportBusy || _loadedPreview is not { OutputParts.Count: > 0 } preview)
        {
            return;
        }

        var exportGeneration = _exportGeneration;
        var exportMarkers = (_previewSession?.EffectiveMarkers ?? preview.Markers).ToArray();
        var wwiseMarkers = (_previewSession?.WwiseMarkers ?? preview.Markers).ToArray();
        _exportBusy = true;
        UpdateExportButtonState();

        try
        {
            await RunExportAsync(preview, exportMarkers, exportGeneration);

            if (IsDisposed || exportGeneration != _exportGeneration)
            {
                return;
            }

            await RunWwiseImportAsync(preview, wwiseMarkers, exportGeneration);
        }
        finally
        {
            if (!IsDisposed)
            {
                _exportBusy = false;
                UpdateExportButtonState();
            }
        }
    }

    /// <summary>
    /// エクスポート済み WAV を Wwise の選択位置へ Music 構造として流し込む。
    /// 接続不可・未選択・キャンセル時はログを残してスキップする。
    /// </summary>
    private async Task RunWwiseImportAsync(
        WaveformPreviewData preview,
        IReadOnlyList<WaveformMarkerMark> markers,
        int exportGeneration)
    {
        // 書き出しに失敗したパートがあるときは中断（全ファイルの存在を確認）
        var directory = Path.GetDirectoryName(preview.SourcePath) ?? string.Empty;
        var missing = preview.OutputParts
            .Select(p => Path.Combine(directory, p.FileName))
            .Where(p => !File.Exists(p))
            .ToList();
        if (missing.Count > 0)
        {
            AppendReport(
                $"=== Wwise Import ==={Environment.NewLine}"
                + $"Message : 書き出しファイルが見つからないためスキップしました: {Path.GetFileName(missing[0])}{Environment.NewLine}{Environment.NewLine}");
            return;
        }

        // 作成先 = Wwise 上の現在選択（インポート直前に再取得）
        string targetPath;
        try
        {
            var (path, _, _) = await WaapiStartupProbe.RefreshSelectionAsync(_waapiSettings);
            targetPath = path;
        }
        catch (Exception ex)
        {
            AppendReport(
                $"=== Wwise Import ==={Environment.NewLine}"
                + $"Status  : NG{Environment.NewLine}"
                + $"Message : Wwise へ接続できないためスキップしました。{ex.Message}{Environment.NewLine}{Environment.NewLine}");
            return;
        }

        if (IsDisposed || exportGeneration != _exportGeneration)
        {
            return;
        }

        if (targetPath.Length == 0)
        {
            AppendReport(
                $"=== Wwise Import ==={Environment.NewLine}"
                + $"Message : Wwise 上で作成先オブジェクトが選択されていないためスキップしました。{Environment.NewLine}{Environment.NewLine}");
            return;
        }

        var importSettings = WwiseImportSettings.Load();

        WwiseMusicPlan plan;
        try
        {
            plan = WwiseMusicPlanBuilder.Build(
                preview.SourcePath,
                preview.WavInfo.SampleRate,
                preview.OutputParts,
                preview.Regions,
                preview.Bars,
                markers);
        }
        catch (Exception ex)
        {
            AppendReport(
                $"=== Wwise Import ==={Environment.NewLine}"
                + $"Message : インポート計画の作成に失敗: {ex.Message}{Environment.NewLine}{Environment.NewLine}");
            return;
        }

        var mode = plan.IsMultiPart
            ? $"Music Switch Container「{plan.ContainerName}」+ Playlist × {plan.Playlists.Count}"
            : $"Music Playlist Container「{plan.ContainerName}」";

        var overwriteStateGroup = false;
        if (plan.IsMultiPart)
        {
            var stateGroupPath = importSettings.ResolveStateGroupPath(plan.ContainerName);
            bool exists;
            try
            {
                exists = await WaapiObjectUtil.ExistsAsync(_waapiSettings, stateGroupPath);
            }
            catch (Exception ex)
            {
                AppendReport(
                    $"=== Wwise Import ==={Environment.NewLine}"
                    + $"Status  : NG{Environment.NewLine}"
                    + $"Message : State Group の存在確認に失敗: {ex.Message}{Environment.NewLine}{Environment.NewLine}");
                return;
            }

            if (IsDisposed || exportGeneration != _exportGeneration)
            {
                return;
            }

            if (exists)
            {
                var conflict = MessageBox.Show(
                    this,
                    $"State Group が既に存在します。\n\n"
                    + $"{stateGroupPath}\n\n"
                    + "上書き（削除して新規作成）してインポートを続けますか？\n"
                    + "「いいえ」でインポート全体を中断します。",
                    "State Group の衝突",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (conflict != DialogResult.Yes || exportGeneration != _exportGeneration)
                {
                    AppendReport(
                        $"=== Wwise Import ==={Environment.NewLine}"
                        + $"Message : 既存 State Group のためインポートを中断しました。{Environment.NewLine}"
                        + $"StateGrp : {stateGroupPath}{Environment.NewLine}{Environment.NewLine}");
                    return;
                }

                overwriteStateGroup = true;
            }
        }

        var confirmLines =
            $"Wwise へインポートします。\n\n"
            + $"作成先: {targetPath}\n"
            + $"構成: {mode}\n"
            + $"Music Segment: {plan.TotalSegmentCount} 個\n";
        if (plan.IsMultiPart)
        {
            confirmLines +=
                $"State Group: {importSettings.ResolveStateGroupPath(plan.ContainerName)}\n"
                + $"  States: {string.Join(", ", plan.Playlists.Select(p => p.Name))}\n";
            if (overwriteStateGroup)
            {
                confirmLines += "  （既存 State Group は上書き）\n";
            }
            else
            {
                confirmLines += "  （State Group を新規作成）\n";
            }
        }

        confirmLines += "\n実行しますか？";

        var confirm = MessageBox.Show(
            this,
            confirmLines,
            "Wwise インポート",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (confirm != DialogResult.Yes || exportGeneration != _exportGeneration)
        {
            AppendReport(
                $"=== Wwise Import ==={Environment.NewLine}"
                + $"Message : インポートをスキップしました。{Environment.NewLine}{Environment.NewLine}");
            return;
        }

        try
        {
            var log = await Task.Run(() => WaapiMusicImporter.ImportAsync(
                _waapiSettings,
                importSettings,
                plan,
                targetPath,
                preview.WavInfo.SampleRate,
                preview.WavInfo.BlockAlign,
                overwriteStateGroup));
            if (!IsDisposed)
            {
                AppendReport(log);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                AppendReport(
                    $"=== Wwise Import ==={Environment.NewLine}"
                    + $"Status  : NG{Environment.NewLine}"
                    + $"Message : {ex.Message}{Environment.NewLine}{Environment.NewLine}");
            }
        }
    }

    private async Task RunExportAsync(
        WaveformPreviewData preview,
        IReadOnlyList<WaveformMarkerMark> markers,
        int exportGeneration)
    {
        try
        {
            await Task.Run(() => WaveformExporter.Export(
                preview.SourcePath,
                preview.WavInfo,
                preview.OutputParts,
                preview.Regions,
                preview.Bars,
                markers,
                onPartBegin: part =>
                {
                    // 書き出し開始の瞬間に発光を点ける（見えてから I/O へ）
                    NotifyExportPartGlow(part.Number, exportGeneration, holdMs: 120);
                },
                onPartEnd: part =>
                {
                    // 書き出し完了の瞬間にもう一度発光を見せてから消す
                    NotifyExportPartGlow(part.Number, exportGeneration, holdMs: 160);
                    NotifyExportPartGlow(null, exportGeneration, holdMs: 40);
                },
                onLog: text => NotifyExportLog(text, exportGeneration)));
        }
        catch (Exception ex)
        {
            if (IsDisposed || exportGeneration != _exportGeneration)
            {
                return;
            }

            AppendReport(
                $"=== エラー ==={Environment.NewLine}"
                + $"Message : 書き出しに失敗: {ex.Message}{Environment.NewLine}{Environment.NewLine}");
        }

        if (IsDisposed || exportGeneration != _exportGeneration)
        {
            return;
        }

        waveformView.ClearExportHighlight();
    }

    /// <summary>
    /// 書き出しパートの枠発光を UI スレッドへ同期反映する。
    /// holdMs &gt; 0 のときは描画が見えるよう短く待ってから返す（バックグラウンド側から呼ぶ想定）。
    /// </summary>
    private void NotifyExportPartGlow(int? partNumber, int exportGeneration, int holdMs = 0)
    {
        if (exportGeneration != _exportGeneration || IsDisposed)
        {
            return;
        }

        try
        {
            Invoke(() =>
            {
                if (exportGeneration != _exportGeneration || IsDisposed)
                {
                    return;
                }

                waveformView.SetExportHighlight(partNumber);
                waveformView.Update();
            });
        }
        catch (ObjectDisposedException)
        {
            return;
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (holdMs > 0)
        {
            Thread.Sleep(holdMs);
        }
    }

    /// <summary>書き出しログをパート単位で UI へ逐次反映する。</summary>
    private void NotifyExportLog(string text, int exportGeneration)
    {
        if (string.IsNullOrEmpty(text) || exportGeneration != _exportGeneration || IsDisposed)
        {
            return;
        }

        try
        {
            Invoke(() =>
            {
                if (exportGeneration != _exportGeneration || IsDisposed)
                {
                    return;
                }

                AppendReport(text);
            });
        }
        catch (ObjectDisposedException)
        {
            // クローズ直後は無視
        }
        catch (InvalidOperationException)
        {
            // ハンドル破棄直後など
        }
    }

    private void ShowBarJumpDialog()
    {
        using var dialog = new BarJumpDialogForm(waveformView.GetNearestBarNumber())
        {
            // メインが最前面でもダイアログが背面に回らないようにする
            TopMost = TopMost,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.BarNumber is not int barNumber)
        {
            return;
        }

        if (!waveformView.TrySeekToBarNumber(barNumber))
        {
            AppendReport(
                $"=== Go To Measure ==={Environment.NewLine}"
                + $"Message : 小節 {barNumber} が見つかりません。{Environment.NewLine}{Environment.NewLine}");
        }
    }

    private void TogglePlayback()
    {
        if (!_audioPlayer.HasSource)
        {
            return;
        }

        var wasPlaying = _audioPlayer.IsPlaying;
        var hadPendingPlaylistTransition = _pendingPlaylistTransitionGeneration != 0;
        WritePlaybackDiagnostic(
            "transport.toggle-requested",
            new { wasPlaying, hadPendingPlaylistTransition });
        if (!_automaticPlaylistPlayback)
        {
            SetManualPlaylistHighlight(_smoothProgress);
        }

        if (_audioPlayer.IsPlaying && hadPendingPlaylistTransition)
        {
            UpdatePlayhead();
        }

        _audioPlayer.Toggle();
        if (_audioPlayer.IsPlaying)
        {
            // 再生開始時だけ位置を取り込み、以降は壁時計で表示（エンジンには触れない）
            AnchorPlayhead(hadPendingPlaylistTransition ? _smoothProgress : _audioPlayer.Progress);
            // 開始位置が -L 内ならその区間だけループ。外ならループなし
            _audioPlayer.ArmLoopAtProgress(_smoothProgress);
            _playheadTimer.Start();
        }
        else
        {
            _playheadTimer.Stop();
            // 停止時のみエンジン位置に合わせる
            AnchorPlayhead(hadPendingPlaylistTransition ? _smoothProgress : _audioPlayer.Progress);
        }

        UpdatePlayhead();
        if (!wasPlaying && _audioPlayer.IsPlaying)
        {
            StartPlaylistTransitionGlow();
        }
        else
        {
            ApplyPlaylistSelectorColors();
        }
        UpdateTransportPlaybackState();
        WritePlaybackDiagnostic(
            "transport.toggle-completed",
            new { isPlaying = _audioPlayer.IsPlaying });
    }

    private void UpdateTransportPlaybackState()
    {
        transportBar.IsPlaying = _audioPlayer.IsPlaying;
    }

    private void UpdateTransportPosition()
    {
        if (_loadedPreview is not { } preview
            || preview.WavInfo.FrameCount <= 0
            || preview.WavInfo.SampleRate == 0
            || preview.Bars.Count == 0)
        {
            transportBar.SetPosition(null);
            return;
        }

        var frameCount = preview.WavInfo.FrameCount;
        var timeSample = (long)Math.Round(Math.Clamp(_smoothProgress, 0d, 1d) * frameCount);
        timeSample = Math.Clamp(timeSample, 0L, frameCount);
        var positionSample = Math.Min(timeSample, frameCount - 1);

        WaveformBarMark? activeBar = null;
        WaveformBarMark? activeState = null;
        WaveformBarMark? nextBar = null;
        foreach (var mark in preview.Bars)
        {
            if (mark.SampleOffset <= positionSample)
            {
                activeState = mark;
                if (!mark.IsTempoChangeOnly)
                {
                    activeBar = mark;
                }
                continue;
            }

            if (!mark.IsTempoChangeOnly)
            {
                nextBar = mark;
                break;
            }
        }

        activeBar ??= preview.Bars.FirstOrDefault(mark => !mark.IsTempoChangeOnly);
        activeState ??= activeBar;
        if (activeBar is not { } bar || activeState is not { } state)
        {
            transportBar.SetPosition(null);
            return;
        }

        var estimatedBarSamples = state.Bpm > 0d && state.Denominator > 0
            ? (long)Math.Round(
                60d / state.Bpm
                * state.Numerator
                * 4d / state.Denominator
                * preview.WavInfo.SampleRate)
            : frameCount - bar.SampleOffset;
        var barEndSample = nextBar?.SampleOffset
            ?? Math.Min(frameCount, bar.SampleOffset + Math.Max(1L, estimatedBarSamples));
        var barLengthSamples = Math.Max(1L, barEndSample - bar.SampleOffset);
        var offsetInBar = Math.Clamp(positionSample - bar.SampleOffset, 0L, barLengthSamples - 1);
        var beatPosition = offsetInBar / (double)barLengthSamples * Math.Max(1, state.Numerator);
        var beatZeroBased = Math.Min(
            Math.Max(0, state.Numerator - 1),
            Math.Max(0, (int)Math.Floor(beatPosition)));
        var subdivision = Math.Clamp(
            (int)Math.Floor((beatPosition - beatZeroBased) * 4d) + 1,
            1,
            4);

        transportBar.SetPosition(new TransportPositionInfo(
            state.Bpm,
            state.Numerator,
            state.Denominator,
            Math.Max(0, bar.BarNumber),
            beatZeroBased + 1,
            subdivision,
            TimeSpan.FromSeconds(timeSample / (double)preview.WavInfo.SampleRate)));
    }

    private void WaveformView_MarkerEditRequested(
        object? sender,
        MarkerEditRequestedEventArgs e)
    {
        if (_previewSession is not { } session)
        {
            return;
        }

        var changed = e.Mode switch
        {
            MarkerEditMode.Add => session.AddMarkers(e.SampleOffsets),
            MarkerEditMode.Remove => session.RemoveMarkers(e.SampleOffsets),
            _ => false,
        };
        if (!changed)
        {
            return;
        }

        waveformView.SetMarkers(session.EffectiveMarkers);
        WritePlaybackDiagnostic(
            e.Mode == MarkerEditMode.Add ? "marker.added" : "marker.removed",
            new
            {
                samples = e.SampleOffsets,
                effectiveCount = session.EffectiveMarkers.Count,
            });
    }

    private void SeekPlayback(double progress)
    {
        if (!_audioPlayer.HasSource)
        {
            return;
        }

        WritePlaybackDiagnostic(
            "transport.seek-requested",
            new { requestedProgress = progress });
        _audioPlayer.CancelPlaylistTransition();
        ClearPendingPlaylistUiTransition();
        var clamped = Math.Clamp(progress, 0d, 1d);
        SetManualPlaylistHighlight(clamped);
        _audioPlayer.Seek(clamped);
        // ジャンプ先が -L 内ならその区間に付け替え、外ならループ解除
        _audioPlayer.ArmLoopAtProgress(clamped);
        // エンジンは時間丸めで僅かにずれるので、表示は要求位置を優先する
        AnchorPlayhead(clamped);
        waveformView.SetPlayhead(clamped, recordTrail: false);
        waveformView.SetExitPlayhead(null);
        waveformView.SetFadeOutPlayhead(null);
        UpdateTransportPosition();
        WritePlaybackDiagnostic(
            "transport.seek-completed",
            new { clampedProgress = clamped });
    }

    private void UpdatePlayhead()
    {
        if (!_audioPlayer.HasSource)
        {
            waveformView.SetPlayhead(null);
            transportBar.SetPosition(null);
            UpdateSourceLevelMeter();
            return;
        }

        if (_audioPlayer.IsPlaying)
        {
            // 再生中は Progress を読まない（バッファ位置の跳ね返り／往復を避ける）
            var durationSec = _audioPlayer.Duration.TotalSeconds;
            if (durationSec > 0)
            {
                var elapsedSec = (Environment.TickCount64 - _anchorTickMs) / 1000d;
                var progress = _anchorProgress + elapsedSec / durationSec;

                if (_pendingPlaylistTransitionGeneration != 0
                    && _loadedPreview is { } preview
                    && preview.WavInfo.FrameCount > 0)
                {
                    var frameCount = preview.WavInfo.FrameCount;
                    var oldTimelineSample = (long)Math.Floor(progress * frameCount);
                    if (!_pendingPlaylistAudioStarted
                        && _audioPlayer.TryGetPlaylistTransitionState(out var transition)
                        && transition.StartedGeneration >= _pendingPlaylistTransitionGeneration)
                    {
                        if (oldTimelineSample >= _pendingPlaylistBoundarySample)
                        {
                            _pendingPlaylistAudioStarted = true;
                            WritePlaybackDiagnostic(
                                "playlist.transition-audio-started",
                                new
                                {
                                    transition.StartedGeneration,
                                    requestedGeneration = _pendingPlaylistTransitionGeneration,
                                    trigger = _pendingPlaylistBoundarySample,
                                    sync = _pendingPlaylistSyncBoundarySample,
                                    target = _pendingPlaylistTargetSample,
                                    targetEntry = _pendingPlaylistTargetEntrySample,
                                    oldTimelineSample,
                                    progress,
                                });
                        }
                    }

                    if (_pendingPlaylistAudioStarted
                        && oldTimelineSample < _pendingPlaylistSyncBoundarySample)
                    {
                        var preRollElapsed = Math.Max(
                            0L,
                            oldTimelineSample - _pendingPlaylistBoundarySample);
                        var anacrusisSample = Math.Min(
                            _pendingPlaylistTargetEntrySample,
                            _pendingPlaylistTargetSample + preRollElapsed);
                        waveformView.SetAnacrusisPlayhead(
                            anacrusisSample / (double)frameCount,
                            recordTrail: true);
                    }

                    if (_pendingPlaylistAudioStarted
                        && oldTimelineSample >= _pendingPlaylistSyncBoundarySample)
                    {
                        var overshoot = Math.Max(
                            0L,
                            oldTimelineSample - _pendingPlaylistSyncBoundarySample);
                        var targetTimelineSample =
                            _pendingPlaylistTargetEntrySample + overshoot;
                        progress = CommitPendingPlaylistUiTransition(
                            oldTimelineSample,
                            targetTimelineSample,
                            "scheduled");
                    }
                }

                // Provider は先読みで先に遷移し得るため、UIが境界へ到達するまでは
                // 遷移先のループ状態を旧タイムライン表示へ適用しない。
                if (_pendingPlaylistTransitionGeneration == 0)
                {
                    // 未アームのまま -L に入ったら、そこで初めてループを有効化
                    if (!_audioPlayer.TryGetActiveLoopProgress(out _, out _)
                        && _audioPlayer.TryGetLoopProgress(progress, out _, out _))
                    {
                        _audioPlayer.ArmLoopAtProgress(progress);
                    }

                    progress = WrapProgressForLoop(progress);
                    if (progress + 1e-12 < _smoothProgress)
                    {
                        waveformView.ClearPlayheadTrail();
                    }
                }

                _smoothProgress = Math.Clamp(progress, 0d, 1d);
            }

            if (!_automaticPlaylistPlayback)
            {
                SetManualPlaylistHighlight(_smoothProgress);
            }

            waveformView.SetPlayhead(_smoothProgress, recordTrail: true);
            double? targetExitProgress = null;
            if (_audioPlayer.TryGetExitPlaybackProgress(out var exitProgress))
            {
                targetExitProgress = exitProgress;
            }

            if (_audioPlayer.TryGetPlaylistFadePlaybackProgress(
                    out var fadeProgress,
                    out var fadeReachedExit))
            {
                waveformView.SetFadeOutPlayhead(
                    fadeProgress,
                    recordTrail: true,
                    isExit: fadeReachedExit);
            }
            else
            {
                waveformView.SetFadeOutPlayhead(null);
            }

            waveformView.SetExitPlayhead(
                targetExitProgress,
                recordTrail: targetExitProgress is not null);
        }
        else
        {
            waveformView.SetPlayhead(_smoothProgress, recordTrail: false);
            waveformView.SetExitPlayhead(null);
            waveformView.SetFadeOutPlayhead(null);
        }

        UpdateTransportPosition();
        UpdateSourceLevelMeter();
    }

    private void UpdateSourceLevelMeter()
    {
        var peak = _audioPlayer.IsPlaying ? _audioPlayer.OutputPeak : 0f;
        var targetLevel = peak <= 0.001f
            ? 0f
            : (float)Math.Clamp((20d * Math.Log10(peak) + 60d) / 60d, 0d, 1d);
        waveformView.SetOutputLevel(targetLevel, decay: _audioPlayer.IsPlaying);
    }

    /// <summary>
    /// アーム中の -L 区間だけ進捗を折り返す（シークで外へ出たらアーム解除済み）。
    /// </summary>
    private double WrapProgressForLoop(double progress)
    {
        if (!_audioPlayer.TryGetActiveLoopProgress(out var start, out var end))
        {
            return progress;
        }

        var span = end - start;
        if (span <= 1e-12)
        {
            return progress;
        }

        // ループ開始より前なら、入るまでは直線再生
        if (progress < start)
        {
            return progress;
        }

        var relative = progress - start;
        var wrapped = start + (relative - Math.Floor(relative / span) * span);
        if (wrapped >= end)
        {
            wrapped = start;
        }

        // 長時間再生での累積誤差を抑えるため、1 周以上回ったらアンカーを更新
        if (progress - _anchorProgress >= span)
        {
            _anchorProgress = wrapped;
            _anchorTickMs = Environment.TickCount64;
        }

        return wrapped;
    }

    private void AnchorPlayhead(double progress)
    {
        _anchorProgress = Math.Clamp(progress, 0d, 1d);
        _anchorTickMs = Environment.TickCount64;
        _smoothProgress = _anchorProgress;
    }

    private void WritePlaybackDiagnostic(string eventName, object? data = null)
    {
        if (!detailedLogCheckBox.Checked || IsDisposed)
        {
            return;
        }

        var activePartNumber = _automaticPlaylistPlayback
            ? _activeAutomaticPlaylistPartNumber
            : _manualPlaylistPartNumber;
        var activePart = _loadedPreview?.OutputParts
            .Where(part => part.Number == activePartNumber)
            .Select(part => (WaveformOutputPart?)part)
            .FirstOrDefault();
        var frameCount = _loadedPreview?.WavInfo.FrameCount ?? 0L;
        var smoothSample = frameCount > 0
            ? (long)Math.Clamp(
                Math.Floor(Math.Clamp(_smoothProgress, 0d, 1d) * frameCount),
                0d,
                Math.Max(0L, frameCount - 1))
            : 0L;
        var hasLoop = _audioPlayer.TryGetActiveLoopProgress(out var loopStart, out var loopEnd);
        var envelope = new
        {
            schema = "mga.playback-debug.v1",
            sequence = Interlocked.Increment(ref _diagnosticSequence),
            utc = DateTimeOffset.UtcNow.ToString("O"),
            tickMs = Environment.TickCount64,
            @event = eventName,
            state = new
            {
                source = _loadedPreview?.SourcePath,
                audioPlaying = _audioPlayer.IsPlaying,
                audioHasSource = _audioPlayer.HasSource,
                smoothProgress = Math.Round(_smoothProgress, 9),
                smoothSample,
                frameCount,
                activePart = activePart?.Number,
                activeFile = activePart?.FileName,
                requestedPart = _requestedPlaylistPartNumber,
                automaticPlaylist = _automaticPlaylistPlayback,
                transitionFadeInSeconds = _playlistFadeInSeconds,
                transitionFadeSeconds = _playlistFadeSeconds,
                exitSourceMode = _playlistExitSourceMode.ToString(),
                destinationSyncMode = _playlistDestinationSyncMode.ToString(),
                manualPart = _manualPlaylistPartNumber,
                pendingGeneration = _pendingPlaylistTransitionGeneration,
                pendingTriggerSample = _pendingPlaylistBoundarySample,
                pendingSyncBoundarySample = _pendingPlaylistSyncBoundarySample,
                pendingTargetSample = _pendingPlaylistTargetSample,
                pendingTargetEntrySample = _pendingPlaylistTargetEntrySample,
                pendingAudioStarted = _pendingPlaylistAudioStarted,
                activeLoop = hasLoop
                    ? new
                    {
                        startProgress = Math.Round(loopStart, 9),
                        endProgress = Math.Round(loopEnd, 9),
                    }
                    : null,
            },
            data,
        };
        AppendReport(
            "[PlaybackDebug] "
            + JsonSerializer.Serialize(envelope)
            + Environment.NewLine);
    }

    private void AppendReport(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var endsWithNewline = normalized.EndsWith('\n');
        var lines = normalized.Split('\n');
        var count = endsWithNewline ? lines.Length - 1 : lines.Length;

        editorTextBox.SuspendLayout();
        try
        {
            for (var i = 0; i < count; i++)
            {
                AppendColoredLine(lines[i]);
            }
        }
        finally
        {
            editorTextBox.ResumeLayout();
        }

        editorTextBox.SelectionStart = editorTextBox.TextLength;
        editorTextBox.ScrollToCaret();
    }

    private void AppendColoredLine(string line)
    {
        editorTextBox.SelectionStart = editorTextBox.TextLength;
        editorTextBox.SelectionLength = 0;
        ApplyFixedLogLineSpacing();
        editorTextBox.SelectionColor = ColorForLogLine(line);
        editorTextBox.AppendText(line + "\n");
    }

    private static Color ColorForLogLine(string line)
    {
        var t = line.TrimStart();
        if (t.Length == 0)
        {
            return UiColors.LogDefault;
        }

        if (t.StartsWith("Status  : OK", StringComparison.Ordinal))
        {
            return UiColors.SeekCyan;
        }

        if (t.StartsWith("[警告]", StringComparison.Ordinal)
            || t.StartsWith("=== 警告", StringComparison.Ordinal)
            || t.StartsWith("[Warning]", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("=== Warnings", StringComparison.OrdinalIgnoreCase))
        {
            return UiColors.LogWarning;
        }

        if (t.StartsWith("=== エラー", StringComparison.Ordinal)
            || t.StartsWith("=== Error", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Message :", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Status  : 接続失敗", StringComparison.Ordinal)
            || t.StartsWith("Status  : NG", StringComparison.Ordinal)
            || t.StartsWith("自動読み込み対象が見つかりません", StringComparison.Ordinal)
            || t.StartsWith("Target  : （未選択）", StringComparison.Ordinal)
            || t.Contains("(なし)", StringComparison.Ordinal))
        {
            return UiColors.LogError;
        }

        if (t.StartsWith("Target  :", StringComparison.Ordinal))
        {
            return UiColors.SeekCyan;
        }

        if (t.StartsWith("===", StringComparison.Ordinal))
        {
            return UiColors.LogHeader;
        }

        if (t.StartsWith("- ", StringComparison.Ordinal)
            || t.StartsWith("Dropped files:", StringComparison.OrdinalIgnoreCase))
        {
            return UiColors.LogMuted;
        }

        return UiColors.LogDefault;
    }

    protected override void WndProc(ref Message m)
    {
        const int wmMouseWheel = 0x020A;
        const int wmEraseBkgnd = 0x0014;

        if (m.Msg == wmMouseWheel && !IsDisposed && waveformView is { IsDisposed: false })
        {
            var screenPoint = Control.MousePosition;
            var waveScreen = waveformView.RectangleToScreen(waveformView.ClientRectangle);
            if (waveScreen.Contains(screenPoint))
            {
                // high word of wParam is signed wheel delta
                var wheelDelta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                if ((ModifierKeys & Keys.Control) == Keys.Control)
                {
                    waveformView.ZoomAmpByWheel(wheelDelta);
                }
                else if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                {
                    waveformView.PanTimeByWheel(wheelDelta);
                }
                else
                {
                    var client = waveformView.PointToClient(screenPoint);
                    waveformView.ZoomTimeByWheel(wheelDelta, client.X);
                }

                m.Result = IntPtr.Zero;
                return;
            }
        }

        if (m.Msg == wmEraseBkgnd)
        {
            if (m.WParam != IntPtr.Zero)
            {
                using var g = Graphics.FromHdc(m.WParam);
                g.Clear(UiColors.WindowBack);
            }

            m.Result = 1;
            return;
        }

        base.WndProc(ref m);
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(IntPtr hWnd, string? pszSubAppName, string? pszSubIdList);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref ParaFormat2 lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct ParaFormat2
    {
        public int cbSize;
        public uint dwMask;
        public short wNumbering;
        public short wEffects;
        public int dxStartIndent;
        public int dxRightIndent;
        public int dxOffset;
        public short wAlignment;
        public short cTabCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public int[] rgxTabs;
        public int dySpaceBefore;
        public int dySpaceAfter;
        public int dyLineSpacing;
        public short sStyle;
        public byte bLineSpacingRule;
        public byte bOutlineLevel;
        public short wShadingWeight;
        public short wShadingStyle;
        public short wNumberingStart;
        public short wNumberingStyle;
        public short wNumberingTab;
        public short wBorderSpace;
        public short wBorderWidth;
        public short wBorders;
    }
}
