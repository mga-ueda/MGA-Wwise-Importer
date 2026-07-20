using System.Globalization;
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

internal static class PlaylistUiNames
{
    /// <summary>Exit Source At ラジオの表示名。</summary>
    public static string ToUiName(this PlaylistExitSourceMode mode) => mode switch
    {
        PlaylistExitSourceMode.Immediate => "Immediate",
        PlaylistExitSourceMode.NextBar => "Next Bar",
        PlaylistExitSourceMode.NextBeat => "Next Beat",
        PlaylistExitSourceMode.NextCue => "Next Cue",
        PlaylistExitSourceMode.ExitCue => "Exit Cue",
        _ => mode.ToString(),
    };

    /// <summary>遷移先同期モードの表示名（ログ・診断用）。</summary>
    public static string ToUiName(this PlaylistDestinationSyncMode mode) => mode switch
    {
        PlaylistDestinationSyncMode.EntryCue => "Entry Cue",
        PlaylistDestinationSyncMode.SameTime => "Same Time",
        _ => mode.ToString(),
    };

    /// <summary>Marker Grid ラジオの表示名。</summary>
    public static string ToUiName(this MarkerGridOverrideMode mode) => mode switch
    {
        MarkerGridOverrideMode.Default => "Timeline",
        MarkerGridOverrideMode.Bar => "Bar",
        MarkerGridOverrideMode.Beat => "Beat",
        _ => mode.ToString(),
    };

    /// <summary>Fade In / Fade Out の秒数に対応する表示名。</summary>
    public static string ToFadeUiName(double seconds, bool isFadeIn)
    {
        if (seconds <= 0d)
        {
            return "None";
        }

        return $"{seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)} Sec.";
    }
}

[Flags]
internal enum UiInteractionLock
{
    None = 0,
    SourceNameEdit = 1,
    Export = 2,
    Load = 4,
    MarkerOptionsEdit = 8,
}

public partial class Form1 : Form
{
    // Exact line height in twips (1 pt = 20 twips). Keeps JP + Latin rows uniform.
    private const int LogLineSpacingTwips = 280;

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int EmSetParaFormat = 0x0447;
    private const uint PfmLineSpacing = 0x00000100;
    private const byte LineSpacingExact = 4;

    /// <summary>プレイリスト行（グループ枠・ステータスラベル）の左インデント（非スケール px）。</summary>
    private const int PlaylistItemIndent = 15;

    [DllImport("user32.dll")]
    private static extern bool HideCaret(IntPtr hWnd);

    private const int EmSetRect = 0x00B3;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref NativeRect lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private DeveloperSettings _developerSettings = new();
    private WaapiSettings _waapiSettings = new();
    private ProjectSettingsStore _projectStore = ProjectSettingsStore.Load();
    private string _loadedProjectName = ProjectSettingsStore.DefaultName;
    private bool _creatingNewProject;
    private bool _suppressProjectUiEvents;
    private string _projectOutputDirectory = string.Empty;
    private string _lastWavePath = string.Empty;
    private WaapiProbeResult? _waapiLastResult;
    private string _waapiLoggedSelectionPath = string.Empty;
    private string _lastLoggedPreflightKey = string.Empty;
    private bool _keepTarget;
    private string _keptTargetPath = string.Empty;
    private string _keptTargetProjectFilePath = string.Empty;
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
#if DEBUG
    private ColorDevPanelForm? _colorDevPanel;
#endif
    private int _exportGeneration;
    private WaveformPreviewData? _loadedPreview;
    private WaveformPreviewSession? _previewSession;
    private IReadOnlyList<string> _lastInputFiles = [];
    private string? _sourceBaseNameOverride;
    private bool _exportBusy;
    private UiInteractionLock _uiInteractionLocks;
    private ExportGlassOverlay? _exportOverlay;
    private string _busyOverlayMessage = "Exporting";
    private bool _populatingPlaylistChoices;
    private bool _automaticPlaylistPlayback;
    private double _playlistFadeInSeconds;
    private double _playlistFadeSeconds;
    /// <summary>プロジェクト既定の Exit Source At（パート未設定時のフォールバック。編集では書き換えない）。</summary>
    private PlaylistExitSourceMode _playlistExitSourceMode = PlaylistExitSourceMode.NextBar;

    /// <summary>パート番号 → Exit Source At（Last Wave セッションへ永続化）。</summary>
    private readonly Dictionary<int, PlaylistExitSourceMode> _playlistExitSourceModes = new();

    /// <summary>パート番号 → Fade In 秒数。</summary>
    private readonly Dictionary<int, double> _playlistFadeInSecondsByPart = new();

    /// <summary>パート番号 → Fade Out 秒数。</summary>
    private readonly Dictionary<int, double> _playlistFadeOutSecondsByPart = new();

    /// <summary>パート番号 → 同一グループ内遷移用 Fade In 秒数。</summary>
    private readonly Dictionary<int, double> _playlistGroupFadeInSecondsByPart = new();

    /// <summary>パート番号 → 同一グループ内遷移用 Fade Out 秒数。</summary>
    private readonly Dictionary<int, double> _playlistGroupFadeOutSecondsByPart = new();

    /// <summary>プロジェクト既定のグループ内 Fade In（パート未設定時）。</summary>
    private double _playlistGroupFadeInSeconds;

    /// <summary>プロジェクト既定のグループ内 Fade Out（パート未設定時）。</summary>
    private double _playlistGroupFadeOutSeconds;
    /// <summary>トランジション設定ラジオの編集対象パート（直近クリックした Playlist）。</summary>
    private int? _transitionSettingsEditPartNumber;
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
    private double? _pendingSourceLoopStart;
    private double? _pendingSourceLoopEnd;
    private double _pendingPlaylistBlinkLevel;
    private int? _playlistTransitionGlowPartNumber;
    private long _playlistTransitionGlowStartTickMs;
    private double _playlistTransitionGlowDurationMs;
    private double _playlistTransitionGlowLevel;
    private double? _pendingWaveformScrollStart;

    /// <summary>パート番号 → グループ ID（Last Wave セッションへ永続化）。</summary>
    private readonly Dictionary<int, int> _playlistPartGroupIds = new();

    /// <summary>グループ ID → 色パレット index（作成順）。</summary>
    private readonly Dictionary<int, int> _playlistGroupColorIndexes = new();

    /// <summary>書き出し対象外のパート番号（Last Wave セッションへ永続化）。</summary>
    private readonly HashSet<int> _disabledPlaylistPartNumbers = [];

    private int _nextPlaylistGroupId = 1;
    private int _nextPlaylistGroupColorIndex;
    private bool _playlistGroupPaintActive;
    private bool _playlistGroupPaintErase;
    private int? _playlistGroupPaintGroupId;
    private int? _playlistGroupPaintLastPartNumber;
    /// <summary>Shift 押し続け中に再利用するグループ ID。キーを離すまで維持する。</summary>
    private int? _playlistGroupPaintStickyGroupId;
    private bool _playlistDisablePaintActive;
    private bool _playlistDisablePaintSetDisabled;
    private int? _playlistDisablePaintLastPartNumber;
    private bool _suppressNextPlaylistClick;
    /// <summary>
    /// 戻る方向ジャンプ中に再生を一時停止したとき true。キーアップで再開する。
    /// </summary>
    private bool _resumePlaybackAfterBackwardSeek;
    private TransportCommand? _activeTransportShortcutCommand;
    private Keys _activeTransportShortcutKeyCode = Keys.None;
    /// <summary>G ジャンプで最後に確定した小節番号（リロードで破棄。INI には書かない）。</summary>
    private int? _lastJumpedBarNumber;
    private readonly MarkerSettings _markerSettings = new();
#if DEBUG
    private long _diagnosticSequence;
#endif

    /// <summary>
    /// Fade／Playlist と More Options をまとめる固定高ホスト。
    /// 親（rightSidePanel）がログ領域いっぱいに伸びても、Playlist 高さが Fade In と揃うようにする。
    /// </summary>
    private readonly Panel _rightSideContentHost = new()
    {
        Dock = DockStyle.Top,
        Name = "rightSideContentHost",
        TabStop = false,
    };

    public Form1()
    {
        UiColors.LoadFromIni();
        AppFonts.EnsureRegistered();
        InitializeComponent();
        WireRightSideContentHost();
        transportBar.CommandHoldEnded += TransportBar_CommandHoldEnded;
        ApplyActionBarToolTips();
        DpiChanged += (_, _) =>
        {
            AdjustTransitionSectionHeights();
            ApplyMarkerOptionsPanelFixedHeight();
            SyncRightSideContentHostHeight();
            AlignCompactFileNumbersCheckBox();
            AlignProjectBarInputs();
            UpdateMinimumWindowSize();
            LayoutActionBarCopyright();
        };
        actionBar.SizeChanged += (_, _) => LayoutActionBarCopyright();
        ApplyWindowIcon();
        brandLogoPictureBox.Image = LoadBrandLogo();
        // 初回レイアウト途中のフレームを見せず、描画完了後に一度で表示する。
        Opacity = 0d;
        actionBar.BackColor = UiColors.ForControlBack(UiColors.ActionBarBack);
        ApplyActionBarButtonColors();
        ApplyActionBarTextColors();
        LayoutActionBarCopyright();
        ApplyLogButtonColors();
        ApplyProjectBarColors();
        transportBar.ApplyColors();
        UpdateTransportPlaybackState();
        ClearPlaylistChoices("Playlist はありません");
        AdjustTransitionSectionHeights();
        UpdateGroupFadeRadioEnabled();
        ApplyMarkerOptionsPanelFixedHeight();
        SyncRightSideContentHostHeight();
        AlignCompactFileNumbersCheckBox();
        UpdateMinimumWindowSize();
        markerOptionsPanel.Bind(_markerSettings);
        markerOptionsPanel.SettingsChanged += (_, _) =>
        {
            ApplyMarkerSettings();
            PersistMarkersToProject();
            PersistStreamingToProject();
            PersistLoudnessToProject();
            ReleaseFocusToWaveform();
        };
        markerOptionsPanel.TextEditingChanged += (_, editing) =>
            SetUiInteractionLocked(UiInteractionLock.MarkerOptionsEdit, editing);
        markerOptionsPanel.RequiredHeightChanged += (_, _) =>
        {
            // More Options 開閉分はウィンドウ高さへ転嫁し、Music Playlist の高さを保つ。
            var previousPanelHeight = markerOptionsPanel.Height;
            var desiredPanelHeight = markerOptionsPanel.RequiredHeight;
            var delta = desiredPanelHeight - previousPanelHeight;
            markerOptionsPanel.Height = desiredPanelHeight;
            SyncRightSideContentHostHeight();

            if (delta != 0 && WindowState == FormWindowState.Normal)
            {
                if (delta < 0)
                {
                    UpdateMinimumWindowSize();
                }

                var targetFormHeight = Height + delta;
                if (delta > 0)
                {
                    Height = targetFormHeight;
                    UpdateMinimumWindowSize();
                }
                else
                {
                    Height = Math.Max(MinimumSize.Height, targetFormHeight);
                }
            }
            else
            {
                UpdateMinimumWindowSize();
            }

            UpdatePlaylistSelectorWidth();
            PersistMoreOptionsToProject();
        };
        waveformView.MarkerGridOverride = _markerSettings.GridOverride;
        ApplyPlaylistSelectorColors();
        waapiStatusBar.ApplyColors();
        waapiStatusBar.KeepTargetChanged += WaapiStatusBar_KeepTargetChanged;
        KeyPreview = true;
        _developerSettings = DeveloperSettings.Load();
        MigrateLegacyWaapiKeepTargetIntoActiveProject();
        _waapiSettings = WaapiSettings.Load();
        WireProjectBarEvents();
        ApplyProjectProfile(_projectStore.GetActive(), selectInCombo: true);
#if DEBUG
        detailedLogCheckBox.Checked = _developerSettings.DetailedPlaybackLog;
#else
        // リリース版では開発用ログ UI をレイアウトから除去し、イベントも無効化する。
        detailedLogCheckBox.CheckedChanged -= DetailedLogCheckBox_CheckedChanged;
        detailedLogCheckBox.Checked = false;
        detailedLogCheckBox.Enabled = false;
        detailedLogCheckBox.Visible = false;
        actionControlsPanel.Controls.Remove(detailedLogCheckBox);
#endif
        RestoreWindowBounds();
        LayoutActionBarCopyright();

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
                // タイマー停止後は UpdatePlayhead が回らないため、メーターを明示的に落とす
                UpdateSourceLevelMeter();
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
        waveformView.TransportFeedbackRequested += (_, command) =>
            transportBar.PulseCommandFeedback(command);
        waveformHorizontalScrollBar.ScrollRequested += QueueWaveformHorizontalScroll;
        waveformHorizontalScrollBar.ScrollCompleted += (_, _) => FlushWaveformHorizontalScroll();
        _waveformScrollTimer.Tick += (_, _) => FlushWaveformHorizontalScroll();
        UpdateWaveformHorizontalScrollBar();
        waveformView.MarkerEditRequested += WaveformView_MarkerEditRequested;
        waveformView.SourceNameEditCommitted += (_, e) => ApplySourceBaseName(e.Name);
        waveformView.SourceNameEditStateChanged += (_, e) =>
            SetUiInteractionLocked(UiInteractionLock.SourceNameEdit, e.IsEditing);
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
        // 非クライアント枠が確定したあとで縮小限界を再計算する。
        UpdateMinimumWindowSize();
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
        AlignProjectBarInputs();
        LayoutActionBarCopyright();
        Refresh();
        Update();

        // フォームを不透明化する前にすりガラスを載せ、素の UI が一瞬見えるのを防ぐ。
        SetUiInteractionLocked(UiInteractionLock.Load, locked: true, "Starting");
        Opacity = 1d;
        Activate();

        // 起動時にプロジェクト名コンボが先頭フォーカス／全選択になるのを防ぐ。
        projectNameComboBox.ClearTextSelection();
        ReleaseFocusToWaveform();

        BeginInvoke(RunStartupSequenceAsync);
    }

    /// <summary>起動直後: WAAPI 接続確認 → 自動波形読み込み。</summary>
    private async void RunStartupSequenceAsync()
    {
        var lastSessionLoadStarted = false;
        try
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
                        await TryRestoreKeptTargetAsync(logReport: true).ConfigureAwait(true);
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
                lastSessionLoadStarted = LoadLastWaveAsDropped();
            }
        }
        finally
        {
            // Last Session 読み込みへ続く場合は、そちらがすりガラス解除を引き継ぐ。
            if (!IsDisposed && !lastSessionLoadStarted)
            {
                SetUiInteractionLocked(UiInteractionLock.Load, locked: false);
            }
        }
    }

    private void ApplyWaapiProbeResult(WaapiProbeResult result, bool logReport)
    {
        _waapiLastResult = result;
        if (result.Ok)
        {
            RefreshWaapiStatusDisplay();
            _waapiPollFailCount = 0;
        }
        else
        {
            waapiStatusBar.SetResult(result);
        }

        if (logReport)
        {
            AppendReport(FormatWaapiLogReport(result));
            _waapiLoggedSelectionPath = GetDisplayTargetPath();
        }

        // 切断後もポーリング継続（再接続待ち）。間隔だけ広げる。
        _waapiSelectionTimer.Interval = result.Ok ? WaapiConnectedPollMs : WaapiDisconnectedPollMs;
        if (!_waapiSelectionTimer.Enabled)
        {
            _waapiSelectionTimer.Start();
        }

        UpdateExportButtonState();
    }

    private void MigrateLegacyWaapiKeepTargetIntoActiveProject()
    {
        if (!WaapiSettings.TryReadLegacyKeepTarget(
                out var keepTarget,
                out var keptPath,
                out var keptProject))
        {
            return;
        }

        var profile = _projectStore.GetActive();
        if (profile.KeepTarget || profile.KeptTargetPath.Length > 0)
        {
            return;
        }

        _projectStore.SaveKeepTarget(
            profile.Name,
            keepTarget,
            keptPath,
            keptProject);
    }

    private void WaapiStatusBar_KeepTargetChanged(object? sender, EventArgs e)
    {
        var enabled = waapiStatusBar.KeepTargetChecked;
        if (enabled)
        {
            var livePath = _waapiLastResult is { Ok: true } live
                ? live.SelectedPath.Trim()
                : string.Empty;
            var pathToKeep = livePath.Length > 0 ? livePath : _keptTargetPath.Trim();
            if (pathToKeep.Length == 0)
            {
                waapiStatusBar.KeepTargetChecked = false;
                AppendReport(
                    "Keep Target : 作成先が表示されていないためオンにできません。"
                    + " Wwise で作成先を選んでから再度オンにしてください。"
                    + Environment.NewLine);
                ReleaseFocusToWaveform();
                return;
            }

            var projectFile = livePath.Length > 0
                ? (_waapiLastResult?.ProjectFilePath ?? string.Empty)
                : _keptTargetProjectFilePath;
            PersistKeepTargetToProject(true, pathToKeep, projectFile);
            AppendReport(
                $"Keep Target : ON（このパスへ書き出します → {pathToKeep}）{Environment.NewLine}");
        }
        else
        {
            PersistKeepTargetToProject(false, _keptTargetPath, _keptTargetProjectFilePath);
            AppendReport($"Keep Target : OFF（Wwise の選択に追従します）{Environment.NewLine}");
        }

        RefreshWaapiStatusDisplay();
        UpdateExportButtonState();
        ReleaseFocusToWaveform();
    }

    private void PersistKeepTargetToProject(
        bool enabled,
        string keptTargetPath,
        string keptTargetProjectFilePath)
    {
        _keepTarget = enabled;
        _keptTargetPath = keptTargetPath.Trim();
        _keptTargetProjectFilePath = keptTargetProjectFilePath.Trim();
        if (_creatingNewProject || !_projectStore.ContainsName(_loadedProjectName))
        {
            return;
        }

        _projectStore.SaveKeepTarget(
            _loadedProjectName,
            enabled,
            _keptTargetPath,
            _keptTargetProjectFilePath);
    }

    private void PersistMarkersToProject()
    {
        if (_suppressProjectUiEvents
            || _creatingNewProject
            || !_projectStore.ContainsName(_loadedProjectName))
        {
            return;
        }

        _projectStore.SaveMarkers(_loadedProjectName, _markerSettings);
    }

    private void PersistStreamingToProject()
    {
        if (_suppressProjectUiEvents
            || _creatingNewProject
            || !_projectStore.ContainsName(_loadedProjectName))
        {
            return;
        }

        _projectStore.SaveStreaming(
            _loadedProjectName,
            markerOptionsPanel.StreamEnabled,
            markerOptionsPanel.LookAheadMs,
            markerOptionsPanel.PrefetchLengthMs);
    }

    private void PersistLoudnessToProject()
    {
        if (_suppressProjectUiEvents
            || _creatingNewProject
            || !_projectStore.ContainsName(_loadedProjectName))
        {
            return;
        }

        _projectStore.SaveLoudness(
            _loadedProjectName,
            markerOptionsPanel.LoudnessNormalizeEnabled,
            markerOptionsPanel.LoudnessTargetLkfs,
            markerOptionsPanel.LoudnessPreserveGroupBalance);
    }

    private void PersistMoreOptionsToProject()
    {
        if (_suppressProjectUiEvents
            || _creatingNewProject
            || !_projectStore.ContainsName(_loadedProjectName))
        {
            return;
        }

        _projectStore.SaveMoreOptionsExpanded(
            _loadedProjectName,
            markerOptionsPanel.MoreOptionsExpanded);
    }

    private string GetDisplayTargetPath()
    {
        if (_keepTarget)
        {
            return _keptTargetPath;
        }

        return _waapiLastResult is { Ok: true } result
            ? result.SelectedPath
            : string.Empty;
    }

    private void RefreshWaapiStatusDisplay()
    {
        if (_waapiLastResult is not { Ok: true } result)
        {
            return;
        }

        var path = _keepTarget ? _keptTargetPath : result.SelectedPath;
        waapiStatusBar.UpdateSelection(
            result.WwiseVersion,
            result.ProjectName,
            path,
            keepTarget: _keepTarget);
    }

    private string FormatWaapiLogReport(WaapiProbeResult result)
    {
        if (!result.Ok)
        {
            return result.FormatLogReport();
        }

        var lines = new List<string>
        {
            "=== WAAPI ===",
            "Status  : OK",
        };
        if (result.WwiseVersion.Length > 0)
        {
            lines.Add($"Wwise   : {result.WwiseVersion}");
        }

        if (result.Project.Length > 0)
        {
            lines.Add($"Project : {result.Project}");
        }

        var displayPath = GetDisplayTargetPath();
        if (_keepTarget)
        {
            lines.Add(displayPath.Length > 0
                ? $"Target  : Keep → {displayPath}（このパスへ書き出します）"
                : "Target  : Keep → （未設定）");
        }
        else
        {
            lines.Add(displayPath.Length > 0
                ? $"Target  : {displayPath}"
                : "Target  : （未選択）");
            if (result.SelectedType.Length > 0)
            {
                lines.Add($"Type    : {result.SelectedType}");
            }
        }

        lines.Add(string.Empty);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>
    /// Keep Target がオンなら記憶パスを Wwise 上で再選択する（表示は Keep パスのまま）。
    /// </summary>
    private async Task TryRestoreKeptTargetAsync(bool logReport)
    {
        if (IsDisposed || !_keepTarget || _waapiLastResult is not { Ok: true })
        {
            return;
        }

        var keptPath = _keptTargetPath.Trim();
        if (keptPath.Length == 0)
        {
            if (logReport)
            {
                AppendReport(
                    "Keep Target : 作成先パスが未設定です。"
                    + Environment.NewLine);
            }

            RefreshWaapiStatusDisplay();
            return;
        }

        var (applied, _, message) = await WaapiSelection.TryRestoreKeptTargetAsync(
                _waapiSettings,
                keptPath,
                _keptTargetProjectFilePath,
                _waapiLastResult.ProjectFilePath)
            .ConfigureAwait(true);
        if (IsDisposed)
        {
            return;
        }

        if (logReport)
        {
            if (applied)
            {
                AppendReport(
                    $"Keep Target : Wwise 上でも作成先を合わせました → {keptPath}"
                    + Environment.NewLine
                    + $"Keep Target : EXPORT はこのパスへ書き出します → {keptPath}"
                    + Environment.NewLine);
            }
            else if (message.Length > 0)
            {
                AppendReport(
                    $"{message}{Environment.NewLine}"
                    + $"Keep Target : Wwise 上の選択に関わらず、EXPORT はこのパスへ書き出します → {keptPath}"
                    + Environment.NewLine);
            }
        }

        try
        {
            var (path, name, type) = await WaapiStartupProbe.RefreshSelectionAsync(_waapiSettings)
                .ConfigureAwait(true);
            if (!IsDisposed && _waapiLastResult is { Ok: true })
            {
                _waapiLastResult = new WaapiProbeResult
                {
                    Ok = true,
                    Url = _waapiLastResult.Url,
                    WwiseVersion = _waapiLastResult.WwiseVersion,
                    ProcessPath = _waapiLastResult.ProcessPath,
                    Project = _waapiLastResult.Project,
                    ProjectName = _waapiLastResult.ProjectName,
                    ProjectFilePath = _waapiLastResult.ProjectFilePath,
                    SelectedPath = path,
                    SelectedName = name,
                    SelectedType = type,
                };
            }
        }
        catch
        {
            // 無視（表示は Keep パス固定）
        }

        RefreshWaapiStatusDisplay();
        _waapiLoggedSelectionPath = GetDisplayTargetPath();
        UpdateExportButtonState();
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

            // ライブ選択は常に内部へ保持（Keep Target OFF 復帰用）。表示は Keep 中は固定。
            var selectionChanged =
                !string.Equals(path, _waapiLastResult.SelectedPath, StringComparison.Ordinal)
                || !string.Equals(type, _waapiLastResult.SelectedType, StringComparison.Ordinal);

            if (selectionChanged)
            {
                _waapiLastResult = new WaapiProbeResult
                {
                    Ok = true,
                    Url = _waapiLastResult.Url,
                    WwiseVersion = _waapiLastResult.WwiseVersion,
                    ProcessPath = _waapiLastResult.ProcessPath,
                    Project = _waapiLastResult.Project,
                    ProjectName = _waapiLastResult.ProjectName,
                    ProjectFilePath = _waapiLastResult.ProjectFilePath,
                    SelectedPath = path,
                    SelectedName = name,
                    SelectedType = type,
                };
            }

            if (_keepTarget)
            {
                // Keep 中は Wwise 側の選択変更で表示・ログ・記憶を動かさない。
                return;
            }

            if (!selectionChanged)
            {
                return;
            }

            RefreshWaapiStatusDisplay();

            if (!string.Equals(path, _waapiLoggedSelectionPath, StringComparison.Ordinal))
            {
                _waapiLoggedSelectionPath = path;
                AppendReport(
                    path.Length > 0
                        ? $"Target  : {path}{Environment.NewLine}"
                        : $"Target  : （未選択）{Environment.NewLine}");
            }

            UpdateExportButtonState();
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
                await TryRestoreKeptTargetAsync(logReport: true).ConfigureAwait(true);
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
        _exportOverlay?.HideOverlay();
        _exportOverlay?.Dispose();
        _exportOverlay = null;
        _audioPlayer.Dispose();
        _waapiSelectionTimer.Dispose();
        _playheadTimer.Dispose();
        _playlistBlinkTimer.Dispose();
        _playlistTransitionGlowTimer.Dispose();
        _waveformScrollTimer.Dispose();
        WindowSettings.FromForm(this).Save();
        // 終了時は Active 名だけ記憶する（作業状態のオートセーブはしない）。
        if (!_creatingNewProject && _projectStore.ContainsName(_loadedProjectName))
        {
            _projectStore.SetActive(_loadedProjectName);
        }
        else
        {
            _projectStore.SaveActiveNameOnly();
        }

        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (_uiInteractionLocks != UiInteractionLock.None)
        {
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // Playlist一覧でも Space は全体の再生／一時停止を優先する。
        // 操作系ボタン／ラジオ／チェックはフォーカスを取らない。
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
        var confirm = OwnerCenteredMessageBox.Show(
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
        if (e.KeyCode is Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey)
        {
            ClearPlaylistGroupPaintStickyId();
        }

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
        ClearPlaylistGroupPaintStickyId();
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
        if (_uiInteractionLocks != UiInteractionLock.None)
        {
            return false;
        }

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
            waveformView.SeekToPreviousPlaylist();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Right))
        {
            waveformView.SeekToNextPlaylist();
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

#if DEBUG
        if (keyData == (Keys.Control | Keys.Shift | Keys.C))
        {
            ShowColorDevPanel();
            return true;
        }
#endif

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
            Keys.Control | Keys.Left => TransportCommand.PreviousPlaylist,
            Keys.Home => TransportCommand.PreviousBar,
            Keys.PageUp => TransportCommand.PreviousPage,
            Keys.PageDown => TransportCommand.NextPage,
            Keys.End => TransportCommand.NextBar,
            Keys.Control | Keys.Right => TransportCommand.NextPlaylist,
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
        if (_uiInteractionLocks != UiInteractionLock.None)
        {
            return;
        }

        var keyData = command switch
        {
            TransportCommand.TogglePlayback => Keys.Space,
            TransportCommand.JumpToBar => Keys.G,
            TransportCommand.GoToStart => Keys.Control | Keys.Home,
            TransportCommand.PreviousPlaylist => Keys.Control | Keys.Left,
            TransportCommand.PreviousBar => Keys.Home,
            TransportCommand.PreviousPage => Keys.PageUp,
            TransportCommand.NextPage => Keys.PageDown,
            TransportCommand.NextBar => Keys.End,
            TransportCommand.NextPlaylist => Keys.Control | Keys.Right,
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
        ReleaseFocusToWaveform();

        // 通常クリックは完了時点をキーアップ相当とする。リピート対象ボタンは
        // MouseUp / MouseLeave まで一時停止を維持し、CommandHoldEnded で再開する。
        if (_resumePlaybackAfterBackwardSeek && !transportBar.IsCommandHeld)
        {
            ResumePlaybackAfterBackwardSeek();
        }

        UpdateTransportPlaybackState();
    }

    private void TransportBar_CommandHoldEnded(object? sender, EventArgs e)
    {
        if (_resumePlaybackAfterBackwardSeek)
        {
            ResumePlaybackAfterBackwardSeek();
        }

        UpdateTransportPlaybackState();
    }

    /// <summary>
    /// 操作系コントロールへフォーカスが残ると ↑↓ がフォーカス移動になるため、波形へ戻す。
    /// </summary>
    private void ReleaseFocusToWaveform()
    {
        if (waveformView is not { IsHandleCreated: true, CanFocus: true })
        {
            return;
        }

        if (waveformView.ContainsFocus)
        {
            return;
        }

        // 名前編集中やマーカーコメント／Stream 入力中はフォーカスを奪わない。
        // プロジェクト書き出し先（ReadOnly）は例外で波形へ戻す。
        // Form.ActiveControl は UserControl 止まりのため、入れ子の ActiveControl を辿る。
        if (GetDeepActiveControl() is TextBox textBox
            && !ReferenceEquals(textBox, projectOutputPathTextBox))
        {
            return;
        }

        waveformView.Focus();
    }

    /// <summary>入れ子の ContainerControl を辿った、実際にフォーカスを持つコントロール。</summary>
    private Control? GetDeepActiveControl()
    {
        Control? current = ActiveControl;
        while (current is ContainerControl { ActiveControl: { } nested })
        {
            current = nested;
        }

        return current;
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

#if DEBUG
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
#endif

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
        ApplyProjectBarColors();
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
        var currentStart = _pendingWaveformScrollStart ?? waveformView.TimeViewStart;
        if (viewStart < currentStart - 1e-12)
        {
            transportBar.PulseCommandFeedback(TransportCommand.PreviousPage);
        }
        else if (viewStart > currentStart + 1e-12)
        {
            transportBar.PulseCommandFeedback(TransportCommand.NextPage);
        }

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

    /// <summary>
    /// 権利表記をロゴ下端に揃え、右側の操作群（Debug Log 等）と重ならない幅に抑える。
    /// </summary>
    private void LayoutActionBarCopyright()
    {
        if (actionBar is null || copyrightLinkLabel is null || brandLogoPictureBox is null)
        {
            return;
        }

        // 操作群を前面に置き、万一の重なりでもクリック・描画を優先する。
        actionControlsPanel.BringToFront();
        brandLogoPictureBox.BringToFront();

        const int gap = 12;
        var left = copyrightLinkLabel.Left;
        var rightLimit = actionControlsPanel.Left;
        if (rightLimit <= left)
        {
            rightLimit = actionBar.ClientSize.Width - actionBar.Padding.Right;
        }

        var maxWidth = Math.Max(80, rightLimit - left - gap);
        copyrightLinkLabel.Width = maxWidth;
        copyrightLinkLabel.Top = brandLogoPictureBox.Bottom - copyrightLinkLabel.Height;
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

        reloadButton.BackColor = UiColors.ForControlBack(UiColors.ReloadButtonFill);
        reloadButton.ForeColor = UiColors.ReloadButtonFore;
        reloadButton.HoverBackColor = UiColors.ForControlBack(UiColors.ReloadButtonHoverFill);
        reloadButton.PressedBackColor = UiColors.ForControlBack(UiColors.ReloadButtonHoverFill);
        reloadButton.DisabledBackColor = innerBack;
        reloadButton.DisabledForeColor = UiColors.ActionButtonDisabledFore;
        reloadButton.BorderColor = UiColors.ForControlBack(UiColors.ReloadButtonBack);
        reloadButton.HoverBorderColor = UiColors.ForControlBack(UiColors.ReloadButtonHoverBack);
        reloadButton.PressedBorderColor = UiColors.ForControlBack(UiColors.ReloadButtonPressedBack);
        reloadButton.DisabledBorderColor = UiColors.ForControlBack(UiColors.ActionButtonDisabledBorder);
        reloadButton.BorderSize = 2;

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
            // トランスポートと同じホバー／押下の塗り（枠なし）
            button.HoverBackColor = UiColors.ForControlBack(UiColors.TransportHoverBack);
            button.PressedBackColor = UiColors.ForControlBack(UiColors.TransportPressedBack);
            button.AccentColor = Color.Empty;
            button.ActiveForeColor = UiColors.LogButtonFore;
            button.Invalidate();
        }
    }

    private void ApplyProjectBarColors()
    {
        var barBack = UiColors.ForControlBack(UiColors.ProjectBarBack);
        var inputBack = UiColors.ForControlBack(UiColors.ProjectBarInputBack);
        var inputFore = UiColors.ProjectBarInputFore;
        projectBar.BackColor = barBack;
        projectActionPanel.BackColor = barBack;
        projectNameSpacer.BackColor = barBack;
        projectNameComboBox.ApplyColors();
        projectOutputPathTextBox.BackColor = inputBack;
        projectOutputPathTextBox.ForeColor = inputFore;
        // コンボ・出力パスと同じ枠色（ChromeBorder 系）に揃える。
        projectOutputPathTextBox.BorderColor = UiColors.ProjectBarBorder;
        var iconFore = UiColors.LogButtonFore;
        ApplyProjectIconButtonColors(projectFolderButton, iconFore, barBack);
        ApplyProjectIconButtonColors(projectSaveButton, iconFore, barBack);
        ApplyProjectIconButtonColors(projectDeleteButton, iconFore, barBack);
        loadLastWaveCheckBox.ForeColor = UiColors.ActionOptionFore;
        loadLastWaveCheckBox.BackColor = barBack;
        RefreshFlatOptionControl(loadLastWaveCheckBox);
        topMostCheckBox.ForeColor = UiColors.ActionOptionFore;
        topMostCheckBox.BackColor = barBack;
        RefreshFlatOptionControl(topMostCheckBox);
        projectSpectrumView.BackColor = barBack;
        projectSpectrumView.Invalidate();
        projectBar.Invalidate();
    }

    private static void ApplyProjectIconButtonColors(
        TransportIconButton button,
        Color iconFore,
        Color barBack)
    {
        button.BackColor = barBack;
        button.ForeColor = iconFore;
        button.HoverBackColor = UiColors.ForControlBack(UiColors.TransportHoverBack);
        button.PressedBackColor = UiColors.ForControlBack(UiColors.TransportPressedBack);
        button.AccentColor = Color.Empty;
        button.ActiveForeColor = iconFore;
        button.Invalidate();
    }

    private void WireProjectBarEvents()
    {
        projectSpectrumView.Player = _audioPlayer;
        projectBar.Paint += ProjectBar_Paint;
        projectNameComboBox.SelectedIndexChanged += ProjectNameComboBox_SelectedIndexChanged;
        projectNameComboBox.SelectionChangeCommitted += ProjectNameComboBox_SelectionChangeCommitted;
        projectNameComboBox.DropDownClosed += ProjectNameComboBox_DropDownClosed;
        projectFolderButton.Click += ProjectFolderButton_Click;
        projectSaveButton.Click += ProjectSaveButton_Click;
        projectDeleteButton.Click += ProjectDeleteButton_Click;
        projectOutputPathTextBox.GotFocus += ProjectOutputPathTextBox_GotFocus;
        projectOutputPathTextBox.Enter += (_, _) => HideProjectPathCaret();
        projectOutputPathTextBox.Click += (_, _) => HideProjectPathCaret();
        projectBar.Resize += (_, _) => AlignProjectBarInputs();
        waveformView.Resize += (_, _) => SyncProjectNameComboWidthToInfoLane();
        waveformView.InfoLaneWidthChanged += (_, _) =>
        {
            SyncProjectNameComboWidthToInfoLane();
            AlignProjectPathTextRect();
        };
        // EM_SETRECT の整形矩形はリサイズで既定へ戻るため再適用する。
        projectOutputPathTextBox.Resize += (_, _) => AlignProjectPathTextRect();
        projectOutputPathTextBox.HandleCreated += (_, _) => AlignProjectPathTextRect();
    }

    /// <summary>
    /// プロジェクト名コンボと出力先テキストボックスの高さをバーの内側高さに揃え、
    /// 双方のテキスト縦位置も一致させる。コンボ幅は情報レーン右端に合わせる。
    /// </summary>
    private void AlignProjectBarInputs()
    {
        var targetHeight = projectBar.DisplayRectangle.Height;
        if (targetHeight <= 0)
        {
            return;
        }

        projectNameComboBox.SetControlHeight(targetHeight);
        SyncProjectNameComboWidthToInfoLane();
        AlignProjectPathTextRect();
    }

    /// <summary>
    /// プロジェクト名コンボの右端を、波形左の情報レーン（Measure 列）右端に揃える。
    /// </summary>
    private void SyncProjectNameComboWidthToInfoLane()
    {
        if (!IsHandleCreated
            || !projectNameComboBox.IsHandleCreated
            || !waveformView.IsHandleCreated
            || projectNameComboBox.IsDisposed
            || waveformView.IsDisposed)
        {
            return;
        }

        var infoRightScreen = waveformView
            .PointToScreen(new Point(waveformView.InfoLaneRightX, 0))
            .X;
        var comboLeftScreen = projectNameComboBox.PointToScreen(Point.Empty).X;
        // ドロップダウン矢印分は最低限確保する。
        var minWidth = Math.Max(48, SystemInformation.VerticalScrollBarWidth + 24);
        var width = Math.Max(minWidth, infoRightScreen - comboLeftScreen);
        if (projectNameComboBox.Width == width)
        {
            return;
        }

        projectNameComboBox.Width = width;
    }

    /// <summary>
    /// 出力先テキストボックス（Multiline）の整形矩形を EM_SETRECT で調整し、
    /// テキスト上端をコンボの編集領域上端（スクリーン座標）に合わせる。
    /// </summary>
    private void AlignProjectPathTextRect()
    {
        if (!projectOutputPathTextBox.IsHandleCreated
            || !projectNameComboBox.IsHandleCreated
            || projectNameComboBox.GetEditItemBounds() is not Rectangle editBounds)
        {
            return;
        }

        var comboTextTop = projectNameComboBox.PointToScreen(editBounds.Location).Y;
        var boxClientTop = projectOutputPathTextBox.PointToScreen(Point.Empty).Y;
        var topInset = Math.Max(0, comboTextTop - boxClientTop);
        var client = projectOutputPathTextBox.ClientSize;
        if (client.Width <= 0 || client.Height <= 0)
        {
            return;
        }

        var rect = new NativeRect
        {
            Left = 4,
            Top = topInset,
            Right = Math.Max(4, client.Width - 4),
            Bottom = client.Height,
        };
        _ = SendMessage(projectOutputPathTextBox.Handle, EmSetRect, IntPtr.Zero, ref rect);
    }

    private void ProjectBar_Paint(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(UiColors.ProjectBarBorder);
        e.Graphics.DrawLine(pen, 0, projectBar.Height - 1, projectBar.Width, projectBar.Height - 1);
    }

    private void ProjectOutputPathTextBox_GotFocus(object? sender, EventArgs e)
    {
        HideProjectPathCaret();
        BeginInvoke(() =>
        {
            if (!IsDisposed)
            {
                ReleaseFocusToWaveform();
            }
        });
    }

    private void HideProjectPathCaret()
    {
        if (projectOutputPathTextBox.IsHandleCreated)
        {
            _ = HideCaret(projectOutputPathTextBox.Handle);
        }

        projectOutputPathTextBox.SelectionLength = 0;
    }

    private void RefreshProjectComboItems(string? selectName)
    {
        _suppressProjectUiEvents = true;
        try
        {
            projectNameComboBox.BeginUpdate();
            projectNameComboBox.Items.Clear();
            foreach (var name in _projectStore.Names)
            {
                projectNameComboBox.Items.Add(name);
            }

            projectNameComboBox.Items.Add(ProjectSettingsStore.NewProjectMenuItem);
            if (!string.IsNullOrWhiteSpace(selectName))
            {
                var index = projectNameComboBox.Items.IndexOf(selectName);
                projectNameComboBox.SelectedIndex = index >= 0 ? index : 0;
                projectNameComboBox.Text = selectName;
            }

            // SelectedIndex 設定で全選択になるため、直後に解除する。
            projectNameComboBox.ClearTextSelection();
        }
        finally
        {
            projectNameComboBox.EndUpdate();
            _suppressProjectUiEvents = false;
        }
    }

    private void ApplyProjectProfile(
        ProjectProfile profile,
        bool selectInCombo,
        bool asNewDraft = false)
    {
        _suppressProjectUiEvents = true;
        try
        {
            _creatingNewProject = asNewDraft;
            _loadedProjectName = profile.Name;
            _projectOutputDirectory = profile.OutputDirectory?.Trim() ?? string.Empty;
            projectOutputPathTextBox.Text = _projectOutputDirectory;

            profile.CopyMarkerInto(_markerSettings);
            markerOptionsPanel.Bind(_markerSettings);
            markerOptionsPanel.BindStreaming(
                profile.StreamEnabled,
                profile.LookAheadMs,
                profile.PrefetchLengthMs);
            markerOptionsPanel.BindLoudness(
                profile.LoudnessNormalizeEnabled,
                profile.LoudnessTargetLkfs,
                profile.LoudnessPreserveGroupBalance);
            markerOptionsPanel.BindMoreOptions(profile.MoreOptionsExpanded);
            waveformView.MarkerGridOverride = _markerSettings.GridOverride;
            if (_previewSession is { } session)
            {
                session.SetCommentRule(_markerSettings.ToCommentRule());
                waveformView.SetMarkers(session.EffectiveMarkers);
            }

            SelectFadeRadio(fadeInChoicesPanel, profile.FadeInSeconds, FadeInTimeRadio_CheckedChanged);
            SelectFadeRadio(transitionTimeChoicesPanel, profile.FadeOutSeconds, TransitionTimeRadio_CheckedChanged);
            _playlistFadeInSeconds = profile.FadeInSeconds;
            _playlistFadeSeconds = profile.FadeOutSeconds;
            _playlistExitSourceMode = profile.ExitSourceAt;
            if (_transitionSettingsEditPartNumber is int editPart)
            {
                ShowTransitionSettingsForPart(editPart);
            }
            else
            {
                SelectExitSourceRadio(profile.ExitSourceAt);
            }

            compactFileNumbersCheckBox.CheckedChanged -= CompactFileNumbersCheckBox_CheckedChanged;
            compactFileNumbersCheckBox.Checked = profile.CompactFileNumbers;
            compactFileNumbersCheckBox.CheckedChanged += CompactFileNumbersCheckBox_CheckedChanged;
            loadLastWaveCheckBox.CheckedChanged -= LoadLastWaveCheckBox_CheckedChanged;
            loadLastWaveCheckBox.Checked = profile.LoadLastWaveOnStartup;
            loadLastWaveCheckBox.CheckedChanged += LoadLastWaveCheckBox_CheckedChanged;
            _lastWavePath = profile.LastWavePath?.Trim() ?? string.Empty;
            topMostCheckBox.CheckedChanged -= TopMostCheckBox_CheckedChanged;
            topMostCheckBox.Checked = profile.AlwaysOnTop;
            topMostCheckBox.CheckedChanged += TopMostCheckBox_CheckedChanged;
            TopMost = profile.AlwaysOnTop;

            _keepTarget = profile.KeepTarget;
            _keptTargetPath = profile.KeptTargetPath?.Trim() ?? string.Empty;
            _keptTargetProjectFilePath = profile.KeptTargetProjectFilePath?.Trim() ?? string.Empty;
            waapiStatusBar.KeepTargetChecked = _keepTarget;

            if (selectInCombo)
            {
                RefreshProjectComboItems(profile.Name);
            }
            else
            {
                projectNameComboBox.Text = profile.Name;
            }
        }
        finally
        {
            _suppressProjectUiEvents = false;
        }

        if (_loadedPreview is { } preview)
        {
            UpdatePlaylistDisplayNames(preview.OutputParts);
        }

        if (_waapiLastResult is { Ok: true })
        {
            RefreshWaapiStatusDisplay();
            _waapiLoggedSelectionPath = GetDisplayTargetPath();
            if (_keepTarget)
            {
                _ = TryRestoreKeptTargetAsync(logReport: true);
            }
        }

        UpdateExportButtonState();
    }

    private void SelectFadeRadio(
        FlowLayoutPanel panel,
        double seconds,
        EventHandler handler)
    {
        FlatOptionRadioButton? match = null;
        foreach (Control control in panel.Controls)
        {
            if (control is FlatOptionRadioButton { Tag: double tag }
                && Math.Abs(tag - seconds) < 0.0001d)
            {
                match = control as FlatOptionRadioButton;
                break;
            }
        }

        match ??= panel.Controls.OfType<FlatOptionRadioButton>().FirstOrDefault();
        if (match is null)
        {
            return;
        }

        match.CheckedChanged -= handler;
        match.Checked = true;
        match.CheckedChanged += handler;
    }

    private void SelectExitSourceRadio(PlaylistExitSourceMode mode)
    {
        FlatOptionRadioButton? match = null;
        foreach (var radio in EnumerateExitSourceRadios())
        {
            if (radio.Tag is PlaylistExitSourceMode tag && tag == mode)
            {
                match = radio;
                break;
            }
        }

        match ??= exitSourceNextBarRadio;
        match.CheckedChanged -= ExitSourceAtRadio_CheckedChanged;
        match.Checked = true;
        match.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
    }

    private IEnumerable<FlatOptionRadioButton> EnumerateExitSourceRadios()
    {
        yield return exitSourceImmediateRadio;
        yield return exitSourceNextBarRadio;
        yield return exitSourceNextBeatRadio;
        yield return exitSourceNextCueRadio;
        yield return exitSourceExitCueRadio;
    }

    private PlaylistExitSourceMode ResolveExitSourceMode(int partNumber)
    {
        if (_playlistExitSourceModes.TryGetValue(partNumber, out var mode))
        {
            return mode;
        }

        return _playlistExitSourceMode;
    }

    private double ResolveFadeInSeconds(int partNumber)
    {
        if (_playlistFadeInSecondsByPart.TryGetValue(partNumber, out var seconds))
        {
            return seconds;
        }

        return _playlistFadeInSeconds;
    }

    private double ResolveFadeOutSeconds(int partNumber)
    {
        if (_playlistFadeOutSecondsByPart.TryGetValue(partNumber, out var seconds))
        {
            return seconds;
        }

        return _playlistFadeSeconds;
    }

    private double ResolveGroupFadeInSeconds(int partNumber)
    {
        if (_playlistGroupFadeInSecondsByPart.TryGetValue(partNumber, out var seconds))
        {
            return seconds;
        }

        return _playlistGroupFadeInSeconds;
    }

    private double ResolveGroupFadeOutSeconds(int partNumber)
    {
        if (_playlistGroupFadeOutSecondsByPart.TryGetValue(partNumber, out var seconds))
        {
            return seconds;
        }

        return _playlistGroupFadeOutSeconds;
    }

    /// <summary>
    /// 遷移に使う Fade。
    /// 同一グループ内（Same Time）は Group 用のみ。通常の Fade In/Out は使わない。
    /// グループ外からの遷移（Entry Cue）は通常の Fade In/Out を使う。
    /// </summary>
    private (double FadeInSeconds, double FadeOutSeconds) ResolveTransitionFadeSeconds(
        int targetPartNumber,
        PlaylistDestinationSyncMode destinationSyncMode)
    {
        if (destinationSyncMode == PlaylistDestinationSyncMode.SameTime)
        {
            return (
                ResolveGroupFadeInSeconds(targetPartNumber),
                ResolveGroupFadeOutSeconds(targetPartNumber));
        }

        return (
            ResolveFadeInSeconds(targetPartNumber),
            ResolveFadeOutSeconds(targetPartNumber));
    }

    /// <summary>
    /// Exit Source / Fade In・Out / Group Fade の適用範囲。
    /// 同一グループ ID のメンバーだけ共通。未グループ／別 ID は各パート独立。
    /// </summary>
    private IEnumerable<int> EnumerateTransitionSettingsScope(int partNumber)
    {
        if (_playlistPartGroupIds.TryGetValue(partNumber, out var groupId))
        {
            foreach (var pair in _playlistPartGroupIds)
            {
                if (pair.Value == groupId)
                {
                    yield return pair.Key;
                }
            }

            yield break;
        }

        yield return partNumber;
    }

    private void StoreExitSourceMode(int partNumber, PlaylistExitSourceMode mode)
    {
        foreach (var scoped in EnumerateTransitionSettingsScope(partNumber))
        {
            _playlistExitSourceModes[scoped] = mode;
        }
    }

    private void StoreFadeInSeconds(int partNumber, double seconds)
    {
        foreach (var scoped in EnumerateTransitionSettingsScope(partNumber))
        {
            _playlistFadeInSecondsByPart[scoped] = seconds;
        }
    }

    private void StoreFadeOutSeconds(int partNumber, double seconds)
    {
        foreach (var scoped in EnumerateTransitionSettingsScope(partNumber))
        {
            _playlistFadeOutSecondsByPart[scoped] = seconds;
        }
    }

    private void StoreGroupFadeInSeconds(int partNumber, double seconds)
    {
        foreach (var scoped in EnumerateTransitionSettingsScope(partNumber))
        {
            _playlistGroupFadeInSecondsByPart[scoped] = seconds;
        }
    }

    private void StoreGroupFadeOutSeconds(int partNumber, double seconds)
    {
        foreach (var scoped in EnumerateTransitionSettingsScope(partNumber))
        {
            _playlistGroupFadeOutSecondsByPart[scoped] = seconds;
        }
    }

    /// <summary>
    /// 既存グループへ参加したパートへ、同じグループ ID の既存メンバー設定をコピーする。
    /// </summary>
    private void CopyTransitionSettingsFromGroupPeers(int partNumber, int groupId)
    {
        var peers = _playlistPartGroupIds
            .Where(pair => pair.Value == groupId && pair.Key != partNumber)
            .Select(pair => pair.Key)
            .OrderBy(number => number)
            .ToArray();
        if (peers.Length == 0)
        {
            return;
        }

        var source = peers[0];
        _playlistExitSourceModes[partNumber] = ResolveExitSourceMode(source);
        _playlistFadeInSecondsByPart[partNumber] = ResolveFadeInSeconds(source);
        _playlistFadeOutSecondsByPart[partNumber] = ResolveFadeOutSeconds(source);
        _playlistGroupFadeInSecondsByPart[partNumber] = ResolveGroupFadeInSeconds(source);
        _playlistGroupFadeOutSecondsByPart[partNumber] = ResolveGroupFadeOutSeconds(source);
    }

    private void ShowTransitionSettingsForPart(int partNumber)
    {
        _transitionSettingsEditPartNumber = partNumber;
        SelectFadeRadio(
            fadeInChoicesPanel,
            ResolveFadeInSeconds(partNumber),
            FadeInTimeRadio_CheckedChanged);
        SelectFadeRadio(
            transitionTimeChoicesPanel,
            ResolveFadeOutSeconds(partNumber),
            TransitionTimeRadio_CheckedChanged);
        SelectFadeRadio(
            fadeInGroupChoicesPanel,
            ResolveGroupFadeInSeconds(partNumber),
            FadeInGroupTimeRadio_CheckedChanged);
        SelectFadeRadio(
            fadeOutGroupChoicesPanel,
            ResolveGroupFadeOutSeconds(partNumber),
            FadeOutGroupTimeRadio_CheckedChanged);
        SelectExitSourceRadio(ResolveExitSourceMode(partNumber));
    }

    private void ClearPlaylistTransitionSettingsState()
    {
        _playlistExitSourceModes.Clear();
        _playlistFadeInSecondsByPart.Clear();
        _playlistFadeOutSecondsByPart.Clear();
        _playlistGroupFadeInSecondsByPart.Clear();
        _playlistGroupFadeOutSecondsByPart.Clear();
        _transitionSettingsEditPartNumber = null;
        SelectFadeRadio(fadeInChoicesPanel, _playlistFadeInSeconds, FadeInTimeRadio_CheckedChanged);
        SelectFadeRadio(transitionTimeChoicesPanel, _playlistFadeSeconds, TransitionTimeRadio_CheckedChanged);
        SelectFadeRadio(
            fadeInGroupChoicesPanel,
            _playlistGroupFadeInSeconds,
            FadeInGroupTimeRadio_CheckedChanged);
        SelectFadeRadio(
            fadeOutGroupChoicesPanel,
            _playlistGroupFadeOutSeconds,
            FadeOutGroupTimeRadio_CheckedChanged);
        SelectExitSourceRadio(_playlistExitSourceMode);
        UpdateGroupFadeRadioEnabled();
    }

    /// <summary>
    /// Group Fade はグループ Playlist の再生中のみ有効。それ以外は操作できない。
    /// </summary>
    private void UpdateGroupFadeRadioEnabled()
    {
        var playingPart = _automaticPlaylistPlayback
            ? _activeAutomaticPlaylistPartNumber
            : _manualPlaylistPartNumber;
        var enabled = playingPart is int part
            && _playlistPartGroupIds.ContainsKey(part)
            && !_disabledPlaylistPartNumbers.Contains(part);

        foreach (var radio in EnumerateGroupFadeRadios())
        {
            if (radio.Enabled == enabled)
            {
                continue;
            }

            radio.Enabled = enabled;
            RefreshFlatOptionControl(radio);
        }
    }

    private IEnumerable<FlatOptionRadioButton> EnumerateGroupFadeRadios()
    {
        yield return fadeInGroupNoneRadio;
        yield return fadeInGroupOneSecondRadio;
        yield return fadeInGroupThreeSecondsRadio;
        yield return fadeInGroupSixSecondsRadio;
        yield return fadeInGroupNineSecondsRadio;
        yield return fadeOutGroupNoneRadio;
        yield return fadeOutGroupOneSecondRadio;
        yield return fadeOutGroupThreeSecondsRadio;
        yield return fadeOutGroupSixSecondsRadio;
        yield return fadeOutGroupNineSecondsRadio;
    }

    private ProjectProfile CaptureCurrentProfile(string name)
    {
        var profile = ProjectSettingsStore.CreateAppDefaults(name);
        profile.FadeInSeconds = _playlistFadeInSeconds;
        profile.FadeOutSeconds = _playlistFadeSeconds;
        profile.ExitSourceAt = _playlistExitSourceMode;
        profile.CopyMarkerFrom(_markerSettings);
        profile.CompactFileNumbers = compactFileNumbersCheckBox.Checked;
        profile.AlwaysOnTop = topMostCheckBox.Checked;
        profile.LoadLastWaveOnStartup = loadLastWaveCheckBox.Checked;
        profile.LastWavePath = _lastWavePath;
        profile.OutputDirectory = _projectOutputDirectory;
        profile.KeepTarget = _keepTarget;
        profile.KeptTargetPath = _keptTargetPath;
        profile.KeptTargetProjectFilePath = _keptTargetProjectFilePath;
        profile.LookAheadMs = markerOptionsPanel.LookAheadMs;
        profile.PrefetchLengthMs = markerOptionsPanel.PrefetchLengthMs;
        profile.StreamEnabled = markerOptionsPanel.StreamEnabled;
        profile.LoudnessNormalizeEnabled = markerOptionsPanel.LoudnessNormalizeEnabled;
        profile.LoudnessTargetLkfs = markerOptionsPanel.LoudnessTargetLkfs;
        profile.LoudnessPreserveGroupBalance = markerOptionsPanel.LoudnessPreserveGroupBalance;
        profile.MoreOptionsExpanded = markerOptionsPanel.MoreOptionsExpanded;
        return profile;
    }

    private void ProjectNameComboBox_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressProjectUiEvents)
        {
            return;
        }

        var selected = projectNameComboBox.SelectedItem as string;
        if (selected is null)
        {
            return;
        }

        if (string.Equals(selected, ProjectSettingsStore.NewProjectMenuItem, StringComparison.Ordinal))
        {
            BeginNewProjectDraft();
            return;
        }

        if (string.Equals(selected, _loadedProjectName, StringComparison.OrdinalIgnoreCase)
            && !_creatingNewProject)
        {
            ReleaseProjectComboFocus();
            return;
        }

        ApplyProjectProfile(_projectStore.GetRequired(selected), selectInCombo: false);
        _projectStore.SetActive(selected);
        ReleaseProjectComboFocus();
    }

    private void ProjectNameComboBox_SelectionChangeCommitted(object? sender, EventArgs e)
    {
        if (_suppressProjectUiEvents || _creatingNewProject)
        {
            return;
        }

        ReleaseProjectComboFocus();
    }

    private void ProjectNameComboBox_DropDownClosed(object? sender, EventArgs e)
    {
        if (_suppressProjectUiEvents || _creatingNewProject)
        {
            return;
        }

        // 新規ドラフト作成中以外は、閉じたあとに選択ハイライトとフォーカスを外す。
        ReleaseProjectComboFocus();
    }

    /// <summary>
    /// プロジェクト名コンボの全選択ハイライトを消し、波形へフォーカスを戻す。
    /// ComboBox が選択直後にフォーカスを取り戻すため BeginInvoke で遅延する。
    /// </summary>
    private void ReleaseProjectComboFocus()
    {
        BeginInvoke(() =>
        {
            if (IsDisposed || _creatingNewProject)
            {
                return;
            }

            projectNameComboBox.ClearTextSelection();
            ReleaseFocusToWaveform();
        });
    }

    private void BeginNewProjectDraft()
    {
        var draftName = _projectStore.SuggestNewProjectName();
        var profile = ProjectSettingsStore.CreateAppDefaults(draftName);
        ApplyProjectProfile(profile, selectInCombo: false, asNewDraft: true);
        _suppressProjectUiEvents = true;
        try
        {
            projectNameComboBox.SelectedIndex = -1;
            projectNameComboBox.Text = draftName;
            projectNameComboBox.SelectAll();
            projectNameComboBox.Focus();
        }
        finally
        {
            _suppressProjectUiEvents = false;
        }
    }

    private void ProjectFolderButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "波形の書き出し先フォルダを選択",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_projectOutputDirectory)
                ? _projectOutputDirectory
                : string.Empty,
        };
        if (OwnerCenteredMessageBox.ShowDialog(this, dialog) == DialogResult.OK)
        {
            _projectOutputDirectory = dialog.SelectedPath.Trim();
            projectOutputPathTextBox.Text = _projectOutputDirectory;
            UpdateExportButtonState();
        }

        ReleaseFocusToWaveform();
    }

    private void ProjectSaveButton_Click(object? sender, EventArgs e)
    {
        try
        {
            var newName = projectNameComboBox.Text;
            var profile = CaptureCurrentProfile(newName);
            var savedName = _projectStore.SaveProfile(
                _loadedProjectName,
                newName,
                profile,
                _creatingNewProject);
            _creatingNewProject = false;
            _loadedProjectName = savedName;
            RefreshProjectComboItems(savedName);
            AppendReport(
                $"=== Project ==={Environment.NewLine}"
                + $"Message : プロジェクト「{savedName}」を保存しました。{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(
                this,
                ex.Message,
                "プロジェクトの保存に失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        ReleaseFocusToWaveform();
    }

    private void ProjectDeleteButton_Click(object? sender, EventArgs e)
    {
        if (_creatingNewProject)
        {
            ApplyProjectProfile(_projectStore.GetActive(), selectInCombo: true);
            ReleaseFocusToWaveform();
            return;
        }

        var name = _loadedProjectName;
        var confirm = OwnerCenteredMessageBox.Show(
            this,
            $"プロジェクト「{name}」を削除しますか？",
            "プロジェクト削除",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (confirm != DialogResult.Yes)
        {
            ReleaseFocusToWaveform();
            return;
        }

        try
        {
            var next = _projectStore.Delete(name);
            ApplyProjectProfile(next, selectInCombo: true);
            AppendReport(
                $"=== Project ==={Environment.NewLine}"
                + $"Message : プロジェクト「{name}」を削除しました。{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(
                this,
                ex.Message,
                "プロジェクトの削除に失敗",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        ReleaseFocusToWaveform();
    }

    private bool HasEnabledExportParts()
    {
        return _loadedPreview is { OutputParts.Count: > 0 } preview
            && preview.OutputParts.Any(part =>
                !_disabledPlaylistPartNumbers.Contains(part.Number));
    }

    private ExportPreflightResult EvaluateExportPreflight() =>
        ExportPreflight.Evaluate(
            _projectOutputDirectory,
            _waapiLastResult,
            HasEnabledExportParts(),
            keepTarget: _keepTarget,
            keptTargetPath: _keptTargetPath);

    /// <summary>
    /// 事前検証の結果が変わったときだけログへ出す（ポーリングで連打しない）。
    /// </summary>
    private void LogExportPreflightIfChanged(ExportPreflightResult preflight)
    {
        var key = $"{preflight.CanExport}|{preflight.Reason}|{preflight.OutputDirectory}"
            + $"|{preflight.TargetPath}|{preflight.ProjectFilePath}";
        if (string.Equals(key, _lastLoggedPreflightKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastLoggedPreflightKey = key;
        if (!preflight.CanExport)
        {
            AppendReport(preflight.FormatLogMessage());
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

    /// <summary>
    /// ラジオ行は AutoScale 後も 30px 固定のため、セクションパネルの高さも
    /// 実際の中身（ヘッダー＋行数）にフィットさせて余白を除去する。
    /// </summary>
    private void AdjustTransitionSectionHeights()
    {
        AdjustFadeSectionHeight(
            fadeInSectionPanel,
            fadeInHeaderLabel,
            fadeInChoicesPanel,
            fadeInGroupDividerLabel,
            fadeInGroupChoicesPanel);
        AdjustFadeSectionHeight(
            fadeOutSectionPanel,
            transitionTimeHeaderLabel,
            transitionTimeChoicesPanel,
            fadeOutGroupDividerLabel,
            fadeOutGroupChoicesPanel);
        AdjustTransitionSectionHeight(exitSourceAtSectionPanel, exitSourceAtHeaderLabel, exitSourceAtChoicesPanel);
    }

    private static void AdjustFadeSectionHeight(
        Panel section,
        Label header,
        FlowLayoutPanel normalChoices,
        Label groupDivider,
        FlowLayoutPanel groupChoices)
    {
        var normalHeight = MeasureChoicesHeight(normalChoices);
        var groupHeight = MeasureChoicesHeight(groupChoices);
        normalChoices.Height = normalHeight;
        groupChoices.Height = groupHeight;
        section.Height = header.Height + normalHeight + groupDivider.Height + groupHeight;
    }

    private static int MeasureChoicesHeight(FlowLayoutPanel choices)
    {
        var contentHeight = choices.Padding.Vertical;
        foreach (Control control in choices.Controls)
        {
            contentHeight += control.Height + control.Margin.Vertical;
        }

        return contentHeight;
    }

    private static void AdjustTransitionSectionHeight(
        Panel section,
        Label header,
        FlowLayoutPanel choices)
    {
        choices.Height = MeasureChoicesHeight(choices);
        section.Height = header.Height + choices.Height;
    }

    private void ApplyPlaylistSelectorColors()
    {
        var back = UiColors.ForControlBack(UiColors.PlaylistBack);
        var headerBack = UiColors.ForControlBack(UiColors.SectionHeaderBack);
        playlistSelectorPanel.BackColor = back;
        playlistScrollPanel.BackColor = back;
        playlistListLayout.BackColor = back;
        playlistHeaderLabel.BackColor = back;
        playlistHeaderLabel.BarColor = headerBack;
        playlistHeaderLabel.ForeColor = UiColors.PlaylistDefaultFore;
        compactFileNumbersCheckBox.ForeColor = UiColors.PlaylistOptionFore;
        compactFileNumbersCheckBox.BackColor = back;
        RefreshFlatOptionControl(compactFileNumbersCheckBox);
        playlistSeparator.BackColor = UiColors.ForControlBack(UiColors.PlaylistButtonBorder);
        rightSidePanel.BackColor = back;
        _rightSideContentHost.BackColor = back;
        markerOptionsPanel.ApplyColors();
        transitionTimePanel.BackColor = back;
        transitionSettingsPanel.BackColor = back;
        fadeInSectionPanel.BackColor = back;
        fadeInChoicesPanel.BackColor = back;
        fadeInHeaderLabel.BackColor = back;
        fadeInHeaderLabel.BarColor = headerBack;
        fadeInHeaderLabel.ForeColor = UiColors.PlaylistDefaultFore;
        fadeOutSectionPanel.BackColor = back;
        transitionTimeChoicesPanel.BackColor = back;
        transitionTimeHeaderLabel.BackColor = back;
        transitionTimeHeaderLabel.BarColor = headerBack;
        transitionTimeHeaderLabel.ForeColor = UiColors.PlaylistDefaultFore;
        fadeInGroupChoicesPanel.BackColor = back;
        fadeInGroupDividerLabel.BackColor = back;
        fadeInGroupDividerLabel.ForeColor = UiColors.PlaylistDefaultFore;
        fadeOutGroupChoicesPanel.BackColor = back;
        fadeOutGroupDividerLabel.BackColor = back;
        fadeOutGroupDividerLabel.ForeColor = UiColors.PlaylistDefaultFore;
        exitSourceAtSectionPanel.BackColor = back;
        exitSourceAtChoicesPanel.BackColor = back;
        exitSourceAtHeaderLabel.BackColor = back;
        exitSourceAtHeaderLabel.BarColor = headerBack;
        exitSourceAtHeaderLabel.ForeColor = UiColors.PlaylistDefaultFore;
        transitionTimeSeparator.BackColor =
            UiColors.ForControlBack(UiColors.PlaylistButtonBorder);
        foreach (var radio in new[]
        {
            fadeInNoneRadio,
            fadeInOneSecondRadio,
            fadeInThreeSecondsRadio,
            fadeInSixSecondsRadio,
            fadeInNineSecondsRadio,
            fadeInGroupNoneRadio,
            fadeInGroupOneSecondRadio,
            fadeInGroupThreeSecondsRadio,
            fadeInGroupSixSecondsRadio,
            fadeInGroupNineSecondsRadio,
            transitionTimeHalfSecondRadio,
            transitionTimeOneSecondRadio,
            transitionTimeThreeSecondsRadio,
            transitionTimeSixSecondsRadio,
            transitionTimeNineSecondsRadio,
            fadeOutGroupNoneRadio,
            fadeOutGroupOneSecondRadio,
            fadeOutGroupThreeSecondsRadio,
            fadeOutGroupSixSecondsRadio,
            fadeOutGroupNineSecondsRadio,
            exitSourceImmediateRadio,
            exitSourceNextBarRadio,
            exitSourceNextBeatRadio,
            exitSourceNextCueRadio,
            exitSourceExitCueRadio,
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
            if (control is PlaylistGroupSwatch { Tag: WaveformOutputPart swatchPart } swatch)
            {
                swatch.GroupColor = TryGetPlaylistGroupColor(swatchPart.Number);
                continue;
            }

            if (control is not FlatPlaylistButton { Tag: WaveformOutputPart part } button)
            {
                continue;
            }

            button.Enabled = true;
            // 通常時は枠なし。遷移待ちの明滅と遷移完了時の発光だけ枠へ描く。
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.BorderColor =
                UiColors.ForControlBack(UiColors.PlaylistButtonBorder);
            var isAutomatic = _automaticPlaylistPlayback
                && _activeAutomaticPlaylistPartNumber == part.Number;
            var isManual = !_automaticPlaylistPlayback
                && _manualPlaylistPartNumber == part.Number;
            var isPending = _pendingPlaylistTransitionGeneration != 0
                && _requestedPlaylistPartNumber == part.Number;

            if (_audioPlayer.IsPlaying && isPending)
            {
                button.FlatAppearance.BorderSize = 2;
                button.FlatAppearance.BorderColor = BlendColor(
                    UiColors.ForControlBack(UiColors.PlaylistButtonBorder),
                    UiColors.ForControlBack(UiColors.PlaylistTransitionBorder),
                    _pendingPlaylistBlinkLevel);
                button.ForeColor = UiColors.PlaylistActiveFore;
            }
            else if (_audioPlayer.IsPlaying && (isAutomatic || isManual))
            {
                // 再生中は枠ではなく塗り色で示す。
                button.BackColor = UiColors.ForControlBack(
                    isManual
                        ? UiColors.PlaylistManualBack
                        : UiColors.PlaylistAutoBack);
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
                // 従来の完了フェード値をそのまま使い、塗りではなく枠を発光させる。
                button.FlatAppearance.BorderSize = 2;
                button.FlatAppearance.BorderColor = BlendColor(
                    UiColors.ForControlBack(UiColors.PlaylistButtonBorder),
                    UiColors.ForControlBack(
                        isManual
                            ? UiColors.PlaylistManualBorder
                            : UiColors.PlaylistTransitionBorder),
                    _playlistTransitionGlowLevel);
            }

            // 無効パートは再生・ホバー色より優先して赤文字にする。
            if (_disabledPlaylistPartNumbers.Contains(part.Number))
            {
                button.ForeColor = UiColors.LogError;
            }
        }

        EnsureHighlightedPlaylistVisible();
    }

    private Color? TryGetPlaylistGroupColor(int partNumber)
    {
        if (!_playlistPartGroupIds.TryGetValue(partNumber, out var groupId)
            || !_playlistGroupColorIndexes.TryGetValue(groupId, out var colorIndex))
        {
            return null;
        }

        return UiColors.PlaylistGroupColorAt(colorIndex);
    }

    /// <summary>グループ枠色だけを更新する（一覧の自動スクロール副作用を避ける）。</summary>
    private void ApplyPlaylistGroupColorsOnly()
    {
        foreach (Control control in playlistListLayout.Controls)
        {
            if (control is not PlaylistGroupSwatch { Tag: WaveformOutputPart part } swatch)
            {
                continue;
            }

            swatch.GroupColor = TryGetPlaylistGroupColor(part.Number);
        }

        waveformView.SetPlaylistGroupColors(BuildPlaylistGroupColorMap());
        if (_loadedPreview is { } preview)
        {
            UpdatePlaylistDisplayNames(preview.OutputParts, updateWaveform: false);
        }
    }

    private void UpdatePlaylistDisplayNames(
        IReadOnlyList<WaveformOutputPart> parts,
        bool updateWaveform = true)
    {
        var sourcePath = _loadedPreview?.SourcePath;
        if (string.IsNullOrEmpty(sourcePath))
        {
            return;
        }

        var namingSourcePath = BuildNamingSourcePath(sourcePath);
        var enabledParts = BuildProjectedEnabledParts(parts, namingSourcePath);
        var enabledGroups = BuildEnabledPartGroupIds();
        var nameOverrides = BuildPlaylistNameOverrides(enabledParts);
        var names = enabledParts.Length == 0
            ? new Dictionary<int, string>()
            : WwiseMusicPlanBuilder.BuildPlaylistDisplayNames(
                namingSourcePath,
                enabledParts,
                enabledGroups,
                nameOverrides).ToDictionary(pair => pair.Key, pair => pair.Value);

        var excludedIndex = 0;
        foreach (var part in parts.OrderBy(part => part.StartSampleOffset))
        {
            if (_disabledPlaylistPartNumbers.Contains(part.Number))
            {
                names[part.Number] = $"Excluded Region {++excludedIndex}";
            }
        }

        foreach (Control control in playlistListLayout.Controls)
        {
            if (control.Tag is not WaveformOutputPart part)
            {
                continue;
            }

            if (!names.TryGetValue(part.Number, out var name))
            {
                name = Path.GetFileNameWithoutExtension(part.FileName);
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = part.FileName;
                }
            }

            if (control is FlatPlaylistButton button)
            {
                button.Text = name;
                button.AccessibleName = name;
            }

            playlistToolTip.SetToolTip(control, BuildPlaylistGroupToolTip(name));
        }

        if (updateWaveform)
        {
            waveformView.SetPlaylistDisplayNames(
                names,
                enabledGroups,
                BuildPlaylistGroupColorMap());
            waveformView.SetDisabledPlaylistParts(_disabledPlaylistPartNumbers);
        }

        QueuePlaylistSelectorWidthUpdate();
    }

    /// <summary>グループ所属パートの番号 → グループ色。未グループのパートは含めない。</summary>
    private Dictionary<int, Color> BuildPlaylistGroupColorMap()
    {
        var map = new Dictionary<int, Color>();
        foreach (var partNumber in _playlistPartGroupIds.Keys)
        {
            if (TryGetPlaylistGroupColor(partNumber) is Color color)
            {
                map[partNumber] = color;
            }
        }

        return map;
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

        if (_transitionSettingsEditPartNumber is int partNumber)
        {
            StoreFadeOutSeconds(partNumber, fadeSeconds);
            PersistLastWaveSessionIfPossible();
        }
        else
        {
            _playlistFadeSeconds = fadeSeconds;
        }

        ApplyPlaylistSelectorColors();
        ReleaseFocusToWaveform();
        WritePlaybackDiagnostic(
            "playlist.fade-out-preset-changed",
            new
            {
                fadeOut = PlaylistUiNames.ToFadeUiName(fadeSeconds, isFadeIn: false),
                fadeOutSeconds = fadeSeconds,
                part = _transitionSettingsEditPartNumber,
                stored = _transitionSettingsEditPartNumber is not null,
                appliesFromNextRequest = _pendingPlaylistTransitionGeneration != 0,
            });
    }

    private void FadeInTimeRadio_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton { Checked: true, Tag: double fadeInSeconds })
        {
            return;
        }

        if (_transitionSettingsEditPartNumber is int partNumber)
        {
            StoreFadeInSeconds(partNumber, fadeInSeconds);
            PersistLastWaveSessionIfPossible();
        }
        else
        {
            _playlistFadeInSeconds = fadeInSeconds;
        }

        ApplyPlaylistSelectorColors();
        ReleaseFocusToWaveform();
        WritePlaybackDiagnostic(
            "playlist.fade-in-preset-changed",
            new
            {
                fadeIn = PlaylistUiNames.ToFadeUiName(fadeInSeconds, isFadeIn: true),
                fadeInSeconds,
                part = _transitionSettingsEditPartNumber,
                stored = _transitionSettingsEditPartNumber is not null,
                appliesFromNextRequest = _pendingPlaylistTransitionGeneration != 0,
            });
    }

    private void FadeInGroupTimeRadio_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton { Checked: true, Tag: double fadeInSeconds })
        {
            return;
        }

        if (_transitionSettingsEditPartNumber is int partNumber)
        {
            StoreGroupFadeInSeconds(partNumber, fadeInSeconds);
            PersistLastWaveSessionIfPossible();
        }
        else
        {
            _playlistGroupFadeInSeconds = fadeInSeconds;
        }

        ApplyPlaylistSelectorColors();
        ReleaseFocusToWaveform();
        WritePlaybackDiagnostic(
            "playlist.fade-in-group-preset-changed",
            new
            {
                fadeIn = PlaylistUiNames.ToFadeUiName(fadeInSeconds, isFadeIn: true),
                fadeInSeconds,
                part = _transitionSettingsEditPartNumber,
                stored = _transitionSettingsEditPartNumber is not null,
                appliesFromNextRequest = _pendingPlaylistTransitionGeneration != 0,
            });
    }

    private void FadeOutGroupTimeRadio_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not RadioButton { Checked: true, Tag: double fadeSeconds })
        {
            return;
        }

        if (_transitionSettingsEditPartNumber is int partNumber)
        {
            StoreGroupFadeOutSeconds(partNumber, fadeSeconds);
            PersistLastWaveSessionIfPossible();
        }
        else
        {
            _playlistGroupFadeOutSeconds = fadeSeconds;
        }

        ApplyPlaylistSelectorColors();
        ReleaseFocusToWaveform();
        WritePlaybackDiagnostic(
            "playlist.fade-out-group-preset-changed",
            new
            {
                fadeOut = PlaylistUiNames.ToFadeUiName(fadeSeconds, isFadeIn: false),
                fadeOutSeconds = fadeSeconds,
                part = _transitionSettingsEditPartNumber,
                stored = _transitionSettingsEditPartNumber is not null,
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

        if (_transitionSettingsEditPartNumber is int partNumber)
        {
            StoreExitSourceMode(partNumber, mode);
            PersistLastWaveSessionIfPossible();
        }
        else
        {
            _playlistExitSourceMode = mode;
        }

        ApplyPlaylistSelectorColors();
        ReleaseFocusToWaveform();
        WritePlaybackDiagnostic(
            "playlist.exit-source-mode-changed",
            new
            {
                mode = mode.ToUiName(),
                part = _transitionSettingsEditPartNumber,
                stored = _transitionSettingsEditPartNumber is not null,
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

        // 描画と同じ条件で測る（NoPadding 無し）。読み込み完了後の実テキストを使う。
        var textWidth = FlatPlaylistButton.MeasureDisplayTextWidth(
            playlistHeaderLabel.Text,
            playlistHeaderLabel.Font);
        foreach (Control control in playlistListLayout.Controls)
        {
            textWidth = Math.Max(
                textWidth,
                FlatPlaylistButton.MeasureDisplayTextWidth(control.Text, control.Font));
        }

        // FlatPlaylistButton の描画余白に合わせる。
        // （スクロール Padding + スクロールバー + グループ枠カラム
        //  + ボタン Margin + テキスト左右 + わずかな安全幅）
        var sampleButton = playlistListLayout.Controls
            .OfType<FlatPlaylistButton>()
            .FirstOrDefault();
        var sampleMargin = sampleButton?.Margin.Horizontal ?? 6;
        var samplePadding = sampleButton?.Padding.Horizontal ?? 4;
        var sampleSwatch = playlistListLayout.Controls
            .OfType<PlaylistGroupSwatch>()
            .FirstOrDefault();
        var swatchColumnWidth = sampleSwatch is null
            ? 0
            : sampleSwatch.Width + sampleSwatch.Margin.Horizontal;
        var chromeWidth = playlistScrollPanel.Padding.Horizontal
            + SystemInformation.VerticalScrollBarWidth
            + swatchColumnWidth
            + sampleMargin
            + samplePadding
            + 4;
        const int minimumWidth = 132;
        var desiredWidth = Math.Max(minimumWidth, textWidth + chromeWidth);

        // 下部のマーカーオプション（全カラム）が収まる幅を保証する。
        var transitionWidth = GetTransitionColumnWidth();
        desiredWidth = Math.Max(
            desiredWidth,
            markerOptionsPanel.RequiredWidth - transitionWidth);
        if (playlistSelectorPanel.Width != desiredWidth)
        {
            playlistSelectorPanel.Width = desiredWidth;
        }

        // 遷移設定カラム＋ Playlist カラムで右側全体の幅を決める。
        var desiredRightWidth = transitionWidth + desiredWidth;
        if (rightSidePanel.Width != desiredRightWidth)
        {
            rightSidePanel.Width = desiredRightWidth;
        }
    }

    /// <summary>
    /// プレイリスト項目の追加直後はレイアウト未確定のため、
    /// 次のメッセージ処理で幅を確定する。
    /// </summary>
    private void QueuePlaylistSelectorWidthUpdate()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            UpdatePlaylistSelectorWidth();
            return;
        }

        BeginInvoke(UpdatePlaylistSelectorWidth);
    }

    /// <summary>
    /// 遷移設定（Fade In / Fade Out / Exit Source At）カラムの必要幅。
    /// セクションパネルは AutoScale で拡縮されるため実測から算出する。
    /// </summary>
    private int GetTransitionColumnWidth()
    {
        return transitionTimeSeparator.Width
            + transitionSettingsPanel.Padding.Horizontal
            + fadeInSectionPanel.Width + fadeInSectionPanel.Margin.Horizontal
            + fadeOutSectionPanel.Width + fadeOutSectionPanel.Margin.Horizontal
            + exitSourceAtSectionPanel.Width + exitSourceAtSectionPanel.Margin.Horizontal;
    }

    /// <summary>
    /// rightSidePanel 直下の子を固定高ホストへ移し、ログ領域が縦に伸びても
    /// Music Playlist が Fade In 下端より下へ広がらないようにする。
    /// </summary>
    private void WireRightSideContentHost()
    {
        var children = rightSidePanel.Controls.Cast<Control>().ToArray();
        rightSidePanel.Controls.Clear();
        foreach (var child in children)
        {
            _rightSideContentHost.Controls.Add(child);
        }

        rightSidePanel.Controls.Add(_rightSideContentHost);
    }

    /// <summary>
    /// ホスト高さを「Fade 行高＋ More Options 高」に固定する。
    /// Playlist（Compact Num. 含む）の下端が Fade In セクション下端と一致する。
    /// </summary>
    private void SyncRightSideContentHostHeight()
    {
        var transitionRowsHeight = Math.Max(
            Math.Max(fadeInSectionPanel.Height, fadeOutSectionPanel.Height),
            exitSourceAtSectionPanel.Height);
        var desired = transitionRowsHeight + markerOptionsPanel.Height;
        if (_rightSideContentHost.Height != desired)
        {
            _rightSideContentHost.Height = desired;
        }
    }

    /// <summary>
    /// More Options は内容が収まる高さへ固定する。
    /// 開閉時の高さ差分はウィンドウ側で吸収し、Music Playlist の高さを保つ。
    /// </summary>
    private void ApplyMarkerOptionsPanelFixedHeight()
    {
        var desiredHeight = markerOptionsPanel.RequiredHeight;
        if (markerOptionsPanel.Height != desiredHeight)
        {
            markerOptionsPanel.Height = desiredHeight;
        }
    }

    /// <summary>
    /// Compact Num. のチェック枠の左端を、プレイリスト行のグループ枠
    /// （左マージン <see cref="PlaylistItemIndent"/>、非スケール）と揃える。
    /// チェック枠は Padding.Left から約 3px（DPI スケール）内側に描画されるため差し引く。
    /// </summary>
    private void AlignCompactFileNumbersCheckBox()
    {
        var glyphInset = (int)Math.Round(3f * DeviceDpi / 96f);
        compactFileNumbersCheckBox.Padding = new Padding(
            Math.Max(0, PlaylistItemIndent - glyphInset),
            0,
            0,
            0);
    }

    /// <summary>マーカーオプションの変更をメモリへ反映する（永続化はプロジェクトへ自動保存）。</summary>
    private void ApplyMarkerSettings()
    {
        waveformView.MarkerGridOverride = _markerSettings.GridOverride;
        if (_previewSession is { } session)
        {
            session.SetCommentRule(_markerSettings.ToCommentRule());
            waveformView.SetMarkers(session.EffectiveMarkers);
        }

        WritePlaybackDiagnostic(
            "marker.settings-changed",
            new
            {
                markerGrid = _markerSettings.GridOverride.ToUiName(),
                digits = _markerSettings.CommentDigits,
                zeroPad = _markerSettings.CommentZeroPad,
                prefixEnabled = _markerSettings.CommentPrefixEnabled,
                prefix = _markerSettings.CommentPrefix,
                suffixEnabled = _markerSettings.CommentSuffixEnabled,
                suffix = _markerSettings.CommentSuffix,
                separatorEnabled = _markerSettings.CommentJoinerEnabled,
                separator = _markerSettings.CommentJoiner,
                resetPerPart = _markerSettings.CommentResetPerPart,
            });
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
                button.MouseDown += PlaylistGroupTarget_MouseDown;
                button.MouseMove += PlaylistGroupTarget_MouseMove;
                button.MouseUp += PlaylistGroupTarget_MouseUp;
                button.MouseEnter += PlaylistButton_MouseEnter;
                button.MouseLeave += PlaylistButton_MouseLeave;
                button.DragEnter += EditorTextBox_DragEnter;
                button.DragDrop += EditorTextBox_DragDrop;
                playlistToolTip.SetToolTip(button, BuildPlaylistGroupToolTip(name));

                var swatch = new PlaylistGroupSwatch
                {
                    AllowDrop = true,
                    BackColor = UiColors.ForControlBack(UiColors.PlaylistBack),
                    Dock = DockStyle.Fill,
                    Height = 30,
                    // ヘッダー「Music Playlist」より少し内側へインデントする。
                    Margin = new Padding(PlaylistItemIndent, 1, 0, 1),
                    Tag = part,
                    Width = PlaylistGroupSwatch.ControlWidth,
                };
                swatch.MouseDown += PlaylistGroupTarget_MouseDown;
                swatch.MouseMove += PlaylistGroupTarget_MouseMove;
                swatch.MouseUp += PlaylistGroupTarget_MouseUp;
                swatch.MouseEnter += PlaylistButton_MouseEnter;
                swatch.MouseLeave += PlaylistButton_MouseLeave;
                swatch.DragEnter += EditorTextBox_DragEnter;
                swatch.DragDrop += EditorTextBox_DragDrop;
                swatch.GroupColor = TryGetPlaylistGroupColor(part.Number);
                playlistToolTip.SetToolTip(swatch, BuildPlaylistGroupToolTip(name));

                playlistListLayout.Controls.Add(swatch);
                playlistListLayout.Controls.Add(button);
            }

            UpdatePlaylistDisplayNames(parts);
        }
        finally
        {
            _populatingPlaylistChoices = false;
            QueuePlaylistSelectorWidthUpdate();
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
            QueuePlaylistSelectorWidthUpdate();
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
        ClearPlaylistDisableState();
        ClearPlaylistGroupState();
        ClearPlaylistTransitionSettingsState();
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

    private void ClearPlaylistGroupState()
    {
        _playlistPartGroupIds.Clear();
        _playlistGroupColorIndexes.Clear();
        _nextPlaylistGroupId = 1;
        _nextPlaylistGroupColorIndex = 0;
        _playlistGroupPaintStickyGroupId = null;
        ApplyPlaylistGroupMarkerSharing();
        EndPlaylistGroupPaint();
    }

    private void ClearPlaylistDisableState()
    {
        _disabledPlaylistPartNumbers.Clear();
        EndPlaylistDisablePaint();
        waveformView.SetDisabledPlaylistParts([]);
        if (_previewSession is { } session)
        {
            session.SetDisabledPartNumbers(null);
        }
    }

    private void EndPlaylistGroupPaint()
    {
        _playlistGroupPaintActive = false;
        _playlistGroupPaintErase = false;
        _playlistGroupPaintGroupId = null;
        _playlistGroupPaintLastPartNumber = null;
        // Sticky ID は Shift 押し続け中に残し、隙間を跨いだ再ドラッグでも同 ID を使う。
        if (_loadedPreview is { } preview)
        {
            UpdatePlaylistDisplayNames(preview.OutputParts);
        }
    }

    private void ClearPlaylistGroupPaintStickyId()
    {
        _playlistGroupPaintStickyGroupId = null;
    }

    private void EndPlaylistDisablePaint()
    {
        _playlistDisablePaintActive = false;
        _playlistDisablePaintLastPartNumber = null;
    }

    private void ClearPlaylistPlaybackSelection()
    {
        _automaticPlaylistPlayback = false;
        _activeAutomaticPlaylistPartNumber = null;
        _requestedPlaylistPartNumber = null;
        _manualPlaylistPartNumber = null;
        ApplyPlaylistSelectorColors();
        UpdateGroupFadeRadioEnabled();
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
            // プレイリスト項目（スウォッチ）のインデントと揃える。
            Margin = new Padding(PlaylistItemIndent, 1, 3, 1),
            Padding = new Padding(2, 0, 2, 0),
            Text = message,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        label.DragEnter += EditorTextBox_DragEnter;
        label.DragDrop += EditorTextBox_DragDrop;
        playlistListLayout.Controls.Add(label);
        playlistListLayout.SetColumnSpan(label, 2);
    }

    private void PlaylistButton_Click(object? sender, EventArgs e)
    {
        if (_populatingPlaylistChoices
            || sender is not Button { Tag: WaveformOutputPart part })
        {
            return;
        }

        if (_suppressNextPlaylistClick)
        {
            _suppressNextPlaylistClick = false;
            return;
        }

        if (_disabledPlaylistPartNumbers.Contains(part.Number))
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
        ShowTransitionSettingsForPart(part.Number);
        RequestPlaylistPlayback(part);
    }

    private void PlaylistGroupTarget_MouseDown(object? sender, MouseEventArgs e)
    {
        if (_populatingPlaylistChoices
            || e.Button != MouseButtons.Left
            || sender is not Control { Tag: WaveformOutputPart part })
        {
            return;
        }

        var modifiers = ModifierKeys;
        var ctrl = (modifiers & Keys.Control) == Keys.Control;
        var shift = (modifiers & Keys.Shift) == Keys.Shift;
        if (ctrl && shift)
        {
            _suppressNextPlaylistClick = sender is Button;
            _playlistDisablePaintActive = true;
            _playlistDisablePaintSetDisabled =
                !_disabledPlaylistPartNumbers.Contains(part.Number);
            _playlistDisablePaintLastPartNumber = null;
            if (sender is Control disableTarget)
            {
                disableTarget.Capture = true;
            }

            ApplyPlaylistDisablePaintAtCursor();
            return;
        }

        var erase = ctrl;
        var paint = shift;
        if (!erase && !paint)
        {
            return;
        }

        // 無効（Excluded Region）はグループ化・解除の対象にしない。
        if (_disabledPlaylistPartNumbers.Contains(part.Number))
        {
            return;
        }

        _suppressNextPlaylistClick = sender is Button;
        _playlistGroupPaintActive = true;
        _playlistGroupPaintErase = erase;
        _playlistGroupPaintLastPartNumber = null;
        if (sender is Control paintTarget)
        {
            paintTarget.Capture = true;
        }

        if (erase)
        {
            _playlistGroupPaintGroupId = null;
        }
        else if (_playlistGroupPaintStickyGroupId is int stickyGroupId)
        {
            // Shift 押し続け中は同じ ID で上書き塗りを続ける。
            _playlistGroupPaintGroupId = stickyGroupId;
        }
        else
        {
            // 既存グループ上から始めても新規 ID を発行し、解除せずに上書きできるようにする。
            var groupId = _nextPlaylistGroupId++;
            _playlistGroupColorIndexes[groupId] = _nextPlaylistGroupColorIndex++;
            _playlistGroupPaintGroupId = groupId;
            _playlistGroupPaintStickyGroupId = groupId;
        }

        ApplyPlaylistGroupPaintAtCursor();
    }

    private void PlaylistGroupTarget_MouseMove(object? sender, MouseEventArgs e)
    {
        if ((e.Button & MouseButtons.Left) == 0)
        {
            return;
        }

        if (_playlistDisablePaintActive)
        {
            ApplyPlaylistDisablePaintAtCursor();
            return;
        }

        if (!_playlistGroupPaintActive)
        {
            return;
        }

        ApplyPlaylistGroupPaintAtCursor();
    }

    private void PlaylistGroupTarget_MouseUp(object? sender, MouseEventArgs e)
    {
        if (_playlistDisablePaintActive)
        {
            ApplyPlaylistDisablePaintAtCursor();
            EndPlaylistDisablePaint();
            if (_suppressNextPlaylistClick && IsHandleCreated)
            {
                BeginInvoke(() => _suppressNextPlaylistClick = false);
            }

            return;
        }

        if (!_playlistGroupPaintActive)
        {
            return;
        }

        ApplyPlaylistGroupPaintAtCursor();
        EndPlaylistGroupPaint();

        // Button の Click は MouseUp の直後に発火する。ドラッグで Click が発火しない場合に
        // 抑制状態を次回へ持ち越さないよう、現在の入力処理が終わった後で解除する。
        if (_suppressNextPlaylistClick && IsHandleCreated)
        {
            BeginInvoke(() => _suppressNextPlaylistClick = false);
        }
    }

    private static string BuildPlaylistGroupToolTip(string playlistName) =>
        $"{playlistName}{Environment.NewLine}"
        + "Shift + クリック／ドラッグ: グループ化（既存グループも新しい ID で上書き可）"
        + Environment.NewLine
        + "Ctrl + クリック／ドラッグ: グループ解除"
        + Environment.NewLine
        + "Ctrl + Shift + クリック／ドラッグ: 無効化／再有効化";

    private WaveformOutputPart? HitTestPlaylistPartAtCursor()
    {
        // マウスキャプチャ下でも隣行へ塗れるよう、レイアウト座標でヒットテストする。
        // 枠・ボタンのどちらの上を通っても同じ行として扱う。
        var client = playlistListLayout.PointToClient(Cursor.Position);
        foreach (Control control in playlistListLayout.Controls)
        {
            if (control.Tag is WaveformOutputPart candidate
                && control.Bounds.Contains(client))
            {
                return candidate;
            }
        }

        return null;
    }

    private void ApplyPlaylistGroupPaintAtCursor()
    {
        if (!_playlistGroupPaintActive)
        {
            return;
        }

        if (HitTestPlaylistPartAtCursor() is not { } part)
        {
            return;
        }

        if (_playlistGroupPaintLastPartNumber == part.Number)
        {
            return;
        }

        _playlistGroupPaintLastPartNumber = part.Number;
        if (_disabledPlaylistPartNumbers.Contains(part.Number))
        {
            return;
        }

        if (_playlistGroupPaintErase)
        {
            RemovePlaylistPartFromGroup(part.Number);
        }
        else if (_playlistGroupPaintGroupId is int groupId)
        {
            AssignPlaylistPartToGroup(part.Number, groupId);
        }

        ApplyPlaylistGroupMarkerSharing();
        ApplyPlaylistGroupColorsOnly();
        PersistLastWaveSessionIfPossible();
    }

    private void ApplyPlaylistDisablePaintAtCursor()
    {
        if (!_playlistDisablePaintActive)
        {
            return;
        }

        if (HitTestPlaylistPartAtCursor() is not { } part)
        {
            return;
        }

        if (_playlistDisablePaintLastPartNumber == part.Number)
        {
            return;
        }

        _playlistDisablePaintLastPartNumber = part.Number;
        SetPlaylistPartDisabled(part.Number, _playlistDisablePaintSetDisabled);
    }

    private void SetPlaylistPartDisabled(int partNumber, bool disabled)
    {
        var changed = disabled
            ? _disabledPlaylistPartNumbers.Add(partNumber)
            : _disabledPlaylistPartNumbers.Remove(partNumber);
        if (!changed)
        {
            return;
        }

        if (disabled)
        {
            CancelPlaybackForDisabledPart(partNumber);
            RemovePlaylistPartFromGroup(partNumber);
        }

        ApplyPlaylistDisableUi();
        PersistLastWaveSessionIfPossible();
    }

    private void CancelPlaybackForDisabledPart(int partNumber)
    {
        if (_requestedPlaylistPartNumber == partNumber)
        {
            _audioPlayer.CancelPlaylistTransition();
            ClearPendingPlaylistUiTransition();
            _requestedPlaylistPartNumber = null;
        }

        if (_activeAutomaticPlaylistPartNumber == partNumber
            || _manualPlaylistPartNumber == partNumber)
        {
            _audioPlayer.CancelPlaylistTransition();
            ClearPendingPlaylistUiTransition();
            if (_audioPlayer.IsPlaying
                && (_activeAutomaticPlaylistPartNumber == partNumber
                    || _manualPlaylistPartNumber == partNumber))
            {
                _audioPlayer.Stop();
                UpdateTransportPlaybackState();
                _playheadTimer.Stop();
            }

            ClearPlaylistPlaybackSelection();
        }
    }

    private void ApplyPlaylistDisableUi()
    {
        if (_previewSession is { } session)
        {
            session.SetDisabledPartNumbers(_disabledPlaylistPartNumbers);
            session.SetPlaylistGroups(BuildEnabledPartGroupIds());
            waveformView.SetMarkers(session.EffectiveMarkers);
        }

        waveformView.SetDisabledPlaylistParts(_disabledPlaylistPartNumbers);
        if (_loadedPreview is { } preview)
        {
            UpdatePlaylistDisplayNames(preview.OutputParts);
        }

        ApplyPlaylistSelectorColors();
        UpdateExportButtonState();
    }

    /// <summary>
    /// 現在の Playlist グループをマーカーセッションへ反映する。
    /// グループ内では最小 part 番号のマーカー情報が全メンバーへ共有される。
    /// 無効パートはグループ共有から除外する。
    /// </summary>
    private void ApplyPlaylistGroupMarkerSharing()
    {
        if (_previewSession is not { } session)
        {
            return;
        }

        session.SetDisabledPartNumbers(_disabledPlaylistPartNumbers);
        session.SetPlaylistGroups(BuildEnabledPartGroupIds());
        waveformView.SetMarkers(session.EffectiveMarkers);
    }

    private Dictionary<int, int> BuildEnabledPartGroupIds() =>
        _playlistPartGroupIds
            .Where(pair => !_disabledPlaylistPartNumbers.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    private void AssignPlaylistPartToGroup(int partNumber, int groupId)
    {
        if (_disabledPlaylistPartNumbers.Contains(partNumber))
        {
            return;
        }

        if (_playlistPartGroupIds.TryGetValue(partNumber, out var previous)
            && previous == groupId)
        {
            return;
        }

        if (_playlistPartGroupIds.TryGetValue(partNumber, out var oldGroupId))
        {
            _playlistPartGroupIds.Remove(partNumber);
            DiscardPlaylistGroupIfEmpty(oldGroupId);
        }

        _playlistPartGroupIds[partNumber] = groupId;
        if (!_playlistGroupColorIndexes.ContainsKey(groupId))
        {
            _playlistGroupColorIndexes[groupId] = _nextPlaylistGroupColorIndex++;
        }

        CopyTransitionSettingsFromGroupPeers(partNumber, groupId);
        UpdateGroupFadeRadioEnabled();
    }

    private void RemovePlaylistPartFromGroup(int partNumber)
    {
        if (!_playlistPartGroupIds.Remove(partNumber, out var groupId))
        {
            return;
        }

        DiscardPlaylistGroupIfEmpty(groupId);
        UpdateGroupFadeRadioEnabled();
    }

    private void DiscardPlaylistGroupIfEmpty(int groupId)
    {
        if (_playlistPartGroupIds.Values.Any(id => id == groupId))
        {
            return;
        }

        _playlistGroupColorIndexes.Remove(groupId);
    }

    private void PlaylistButton_MouseEnter(object? sender, EventArgs e)
    {
        if (sender is not Control { Tag: WaveformOutputPart part })
        {
            return;
        }

        _hoveredPlaylistListPartNumber = part.Number;
        waveformView.SetPlaylistHoverHighlight(part.Number);
        ApplyPlaylistSelectorColors();
    }

    private void PlaylistButton_MouseLeave(object? sender, EventArgs e)
    {
        if (sender is not Control { Tag: WaveformOutputPart part }
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
        UpdateGroupFadeRadioEnabled();
    }

    private void RequestPlaylistPlayback(WaveformOutputPart target)
    {
        if (_loadedPreview is not { } preview
            || !_audioPlayer.HasSource
            || preview.WavInfo.FrameCount <= 0
            || _disabledPlaylistPartNumbers.Contains(target.Number))
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
            UpdateGroupFadeRadioEnabled();
            WritePlaybackDiagnostic(
                "playlist.immediate-started",
                new { target = target.Number, progress });
            return;
        }

        // 予約先と現在再生中の項目は別管理する。遷移完了までは現在色を維持する。
        _requestedPlaylistPartNumber = target.Number;
        ApplyPlaylistSelectorColors();
        UpdateGroupFadeRadioEnabled();
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

        var destinationSyncMode = ResolvePlaylistDestinationSyncMode(currentPart, target);
        var anacrusisFrames =
            destinationSyncMode == PlaylistDestinationSyncMode.EntryCue
                ? GetLeadingAnacrusisFrameCount(preview, target)
                : 0L;
        var exitSourceMode = ResolveExitSourceMode(target.Number);
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
                exitSourceMode = exitSourceMode.ToUiName(),
                destinationSyncMode = destinationSyncMode.ToUiName(),
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

    private PlaylistDestinationSyncMode ResolvePlaylistDestinationSyncMode(
        WaveformOutputPart? current,
        WaveformOutputPart target)
    {
        if (current is not { } currentPart
            || !_playlistPartGroupIds.TryGetValue(currentPart.Number, out var currentGroupId)
            || !_playlistPartGroupIds.TryGetValue(target.Number, out var targetGroupId))
        {
            return PlaylistDestinationSyncMode.EntryCue;
        }

        // 同じレイヤーグループ内は再生位置を維持する。
        // 別のグループ間、および未グループとの遷移は Entry Cue に同期する。
        return currentGroupId == targetGroupId
            ? PlaylistDestinationSyncMode.SameTime
            : PlaylistDestinationSyncMode.EntryCue;
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
        var (fadeInSeconds, fadeOutSeconds) = ResolveTransitionFadeSeconds(
            target.Number,
            destinationSyncMode);
        if (!_audioPlayer.TrySchedulePlaylistTransition(
                target.StartSampleOffset,
                target.EndSampleOffset,
                sourceExitSample,
                currentPartStart,
                destinationSyncMode,
                anacrusisFrames,
                allowShortPreRoll,
                currentPartEnd,
                fadeInSeconds,
                fadeOutSeconds,
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
                        exitSourceMode = exitSourceMode.ToUiName(),
                        destinationSyncMode = destinationSyncMode.ToUiName(),
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
                exitSourceMode = exitSourceMode.ToUiName(),
                destinationSyncMode = destinationSyncMode.ToUiName(),
                schedule.Generation,
                schedule.TriggerSample,
                schedule.SyncBoundarySample,
                schedule.TargetSwitchSample,
                schedule.SourceRelativeSample,
                schedule.StartedImmediately,
                requestedSourceExitSample = sourceExitSample,
                anacrusisFrames,
                allowShortPreRoll,
                fadeInSeconds,
                fadeSeconds = fadeOutSeconds,
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
        // 遷移待ち中も旧タイムラインの -L 折り返しを維持するため、待ち開始時点のループを固定する。
        // （Provider が先に遷移先ループへアームしても、UI の旧位置計算が壊れないようにする）
        if (_audioPlayer.TryGetActiveLoopProgress(out var loopStart, out var loopEnd))
        {
            _pendingSourceLoopStart = loopStart;
            _pendingSourceLoopEnd = loopEnd;
        }
        else
        {
            _pendingSourceLoopStart = null;
            _pendingSourceLoopEnd = null;
        }

        // 表示中の折り返し済み位置へ壁時計アンカーを合わせ、待ち開始直後の位置ジャンプを防ぐ。
        AnchorPlayhead(_smoothProgress);
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
        UpdateGroupFadeRadioEnabled();
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
        _pendingSourceLoopStart = null;
        _pendingSourceLoopEnd = null;
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

    /// <summary>
    /// タイトルバー／タスクバー用のウィンドウアイコンを設定する。
    /// exe 埋め込みアイコン（ApplicationIcon）とは別に Form.Icon が必要なため、
    /// 同梱の .ico から読み込む。
    /// </summary>
    private void ApplyWindowIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Branding", "MgaWwiseIMImporter.ico");
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(path);
            Icon = new Icon(stream);
        }
        catch (Exception)
        {
            // アイコン読み込み失敗は致命的でないため既定アイコンのまま続行する。
        }
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

    private void ApplyActionBarToolTips()
    {
        playlistToolTip.SetToolTip(
            detailedLogCheckBox,
            "再生・操作の詳細な診断情報を画面ログへ出力します（開発用）。");
        playlistToolTip.SetToolTip(
            compactFileNumbersCheckBox,
            "ON: 無効化した Playlist があっても、書き出す WAV の番号を 1 から詰めます。"
            + Environment.NewLine
            + "OFF: 元の番号を維持します（欠番が残ります）。");
        playlistToolTip.SetToolTip(
            loadLastWaveCheckBox,
            "起動時に、選択中のプロジェクトで最後に読み込んだ波形を再読み込みします。");
        playlistToolTip.SetToolTip(
            topMostCheckBox,
            "ウィンドウを常に最前面へ表示します。");
        playlistToolTip.SetToolTip(
            reloadButton,
            "最後にドロップまたは自動読み込みした WAV／XML を、元のファイルから再読み込みします。"
            + Environment.NewLine
            + "ログ・Playlist のグループ化・無効化・追加マーカーはリセットされます。");
        playlistToolTip.SetToolTip(
            exportButton,
            "分割 WAV を書き出し、続けて Wwise へインポートします。"
            + Environment.NewLine
            + "無効化した Playlist は書き出し対象外です。");
    }

    /// <summary>
    /// 編集・書き出し・読み込み中の操作ロックを理由別に管理する。
    /// 複数の理由が重なっても、最後のロックが解除されるまでショートカットは再開しない。
    /// </summary>
    private void SetUiInteractionLocked(
        UiInteractionLock reason,
        bool locked,
        string? overlayMessage = null)
    {
        var messageChanged = false;
        if (locked
            && reason is UiInteractionLock.Export or UiInteractionLock.Load
            && !string.IsNullOrWhiteSpace(overlayMessage))
        {
            var trimmed = overlayMessage.Trim();
            if (!string.Equals(_busyOverlayMessage, trimmed, StringComparison.Ordinal))
            {
                _busyOverlayMessage = trimmed;
                messageChanged = true;
            }
        }

        var next = locked
            ? _uiInteractionLocks | reason
            : _uiInteractionLocks & ~reason;
        if (next == _uiInteractionLocks)
        {
            // ロック継続中のメッセージ差し替え（Starting → Loading Last Session など）。
            if (messageChanged && next.HasFlag(reason))
            {
                UpdateBusyGlassOverlay();
            }

            return;
        }

        _uiInteractionLocks = next;
        EndActiveTransportShortcutFeedback();
        _resumePlaybackAfterBackwardSeek = false;

        UpdateBusyGlassOverlay();
        UpdateExportButtonState();
    }

    /// <summary>
    /// 書き出し／読み込み中はコントロールを無効化せず、WAAPI ステータスバーを除くフォーム全体を
    /// すりガラスで覆ってマウス操作を遮断する（ショートカットは <see cref="_uiInteractionLocks"/> 側で抑止）。
    /// 解除は完了ログを短く見せたあと、子ウィンドウの Opacity フェードで行う。
    /// </summary>
    private void UpdateBusyGlassOverlay()
    {
        string? message = null;
        if (_uiInteractionLocks.HasFlag(UiInteractionLock.Export))
        {
            message = "Exporting";
        }
        else if (_uiInteractionLocks.HasFlag(UiInteractionLock.Load))
        {
            message = _busyOverlayMessage;
        }

        if (message is not null)
        {
            _exportOverlay ??= new ExportGlassOverlay();
            if (_exportOverlay.IsShowingBusy)
            {
                _exportOverlay.SetMessage(message);
            }
            else
            {
                var coverBounds = new Rectangle(0, 0, ClientSize.Width, waapiStatusBar.Top);
                _exportOverlay.ShowOverlay(this, coverBounds, message);
            }

            return;
        }

        _exportOverlay?.BeginFadeOut();
    }

    private void UpdateExportButtonState()
    {
        var preflight = EvaluateExportPreflight();
        exportButton.Enabled = !_exportBusy
            && !_uiInteractionLocks.HasFlag(UiInteractionLock.Export)
            && !_uiInteractionLocks.HasFlag(UiInteractionLock.Load)
            && preflight.CanExport;

        // 読み込み済みのときだけ事前検証の変化をログ（起動直後の空状態は黙る）
        if (_loadedPreview is not null)
        {
            LogExportPreflightIfChanged(preflight);
        }
    }

    private void TopMostCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        TopMost = topMostCheckBox.Checked;
        ReleaseFocusToWaveform();
    }

    private void LoadLastWaveCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (!_creatingNewProject && _projectStore.ContainsName(_loadedProjectName))
        {
            _projectStore.SaveLoadLastWaveOnStartup(
                _loadedProjectName,
                loadLastWaveCheckBox.Checked);
        }

        ReleaseFocusToWaveform();
    }

    private void CompactFileNumbersCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
        if (_loadedPreview is { } preview)
        {
            UpdatePlaylistDisplayNames(preview.OutputParts);
        }

        UpdateExportButtonState();
        ReleaseFocusToWaveform();
    }

    private WaveformOutputPart[] BuildProjectedEnabledParts(
        IReadOnlyList<WaveformOutputPart> parts,
        string sourcePath)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = "wave";
        }

        var enabled = parts
            .Where(part => !_disabledPlaylistPartNumbers.Contains(part.Number))
            .OrderBy(part => part.StartSampleOffset)
            .ThenBy(part => part.Number)
            .ToArray();
        var projected = new WaveformOutputPart[enabled.Length];
        for (var i = 0; i < enabled.Length; i++)
        {
            var part = enabled[i];
            var fileNumber = compactFileNumbersCheckBox.Checked ? i + 1 : part.Number;
            projected[i] = part with { FileName = $"{baseName}_{fileNumber}.wav" };
        }

        return projected;
    }

    private string BuildNamingSourcePath(string sourcePath)
    {
        var baseName = _sourceBaseNameOverride;
        if (string.IsNullOrWhiteSpace(baseName))
        {
            return sourcePath;
        }

        var directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        return Path.Combine(directory, baseName + Path.GetExtension(sourcePath));
    }

    private void ApplySourceBaseName(string editedName)
    {
        if (_loadedPreview is not { } preview)
        {
            return;
        }

        var originalName = Path.GetFileNameWithoutExtension(preview.SourcePath);
        var name = editedName.Trim();
        if (name.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4].Trim();
        }

        // 空欄は元のファイル名へ戻す。
        if (name.Length == 0)
        {
            _sourceBaseNameOverride = null;
            waveformView.SetSourceDisplayName(originalName);
            UpdatePlaylistDisplayNames(preview.OutputParts);
            return;
        }

        if (name.EndsWith(' ')
            || name.EndsWith('.')
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            OwnerCenteredMessageBox.Show(
                this,
                "ファイル名として使用できる、拡張子なしの名前を入力してください。",
                "名前を変更できません",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            waveformView.SetSourceDisplayName(
                _sourceBaseNameOverride ?? originalName);
            return;
        }

        _sourceBaseNameOverride = name;
        waveformView.SetSourceDisplayName(name);
        UpdatePlaylistDisplayNames(preview.OutputParts);
    }

    private Dictionary<int, string> BuildPlaylistNameOverrides(
        IReadOnlyList<WaveformOutputPart> enabledParts)
    {
        // 除外が無い通常時、または番号を詰めるときは Playlist 単位の連番に任せる。
        // （パート単位の FileName を流用すると、グループの2つ目が _4 など欠番に見える）
        if (_disabledPlaylistPartNumbers.Count == 0
            || compactFileNumbersCheckBox.Checked)
        {
            return [];
        }

        // Compact OFF: 各 Playlist 単位の代表番号（最小 Number）を残す。
        return enabledParts.ToDictionary(
            part => part.Number,
            part => Path.GetFileNameWithoutExtension(part.FileName));
    }

    /// <summary>
    /// 無効パートを除いた書き出し／Wwise 用スナップショット。
    /// Compact File Numbers が ON なら FileName だけ 1 から詰める（Number は安定 ID）。
    /// </summary>
    private PlaylistExportSnapshot BuildPlaylistExportSnapshot(
        WaveformPreviewData preview,
        IReadOnlyList<WaveformMarkerMark> markers)
    {
        var parts = BuildProjectedEnabledParts(
            preview.OutputParts,
            BuildNamingSourcePath(preview.SourcePath));
        var enabledNumbers = parts.Select(part => part.Number).ToHashSet();
        var groups = _playlistPartGroupIds
            .Where(pair => enabledNumbers.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value);
        var filteredMarkers = markers
            .Where(marker => parts.Any(part =>
                marker.SampleOffset >= part.StartSampleOffset
                && marker.SampleOffset < part.EndSampleOffset))
            .ToArray();

        return new PlaylistExportSnapshot(
            parts,
            groups,
            filteredMarkers,
            BuildPlaylistNameOverrides(parts),
            BuildExportExitSourceModes(enabledNumbers));
    }

    private IReadOnlyDictionary<int, PlaylistExitSourceMode> BuildExportExitSourceModes(
        IReadOnlySet<int> enabledNumbers)
    {
        var result = new Dictionary<int, PlaylistExitSourceMode>();
        foreach (var partNumber in enabledNumbers)
        {
            result[partNumber] = ResolveExitSourceMode(partNumber);
        }

        return result;
    }

    private readonly record struct PlaylistExportSnapshot(
        IReadOnlyList<WaveformOutputPart> Parts,
        IReadOnlyDictionary<int, int> PartGroupIds,
        IReadOnlyList<WaveformMarkerMark> Markers,
        IReadOnlyDictionary<int, string> PlaylistNameOverrides,
        IReadOnlyDictionary<int, PlaylistExitSourceMode> PartExitSourceModes);

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
            OwnerCenteredMessageBox.Show(
                this,
                ex.Message,
                "Unable to open GitHub",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void DetailedLogCheckBox_CheckedChanged(object? sender, EventArgs e)
    {
#if DEBUG
        DeveloperSettings.SaveDetailedPlaybackLog(detailedLogCheckBox.Checked);
        if (detailedLogCheckBox.Checked)
        {
            WritePlaybackDiagnostic("diagnostic.enabled");
        }
#endif
        ReleaseFocusToWaveform();
    }

    private void LogClearButton_Click(object? sender, EventArgs e)
    {
        WritePlaybackDiagnostic("log.cleared");
        editorTextBox.Clear();
        ReleaseFocusToWaveform();
    }

    private void LogCopyButton_Click(object? sender, EventArgs e)
    {
        if (editorTextBox.TextLength == 0)
        {
            ReleaseFocusToWaveform();
            return;
        }

        try
        {
            Clipboard.SetText(editorTextBox.Text);
            WritePlaybackDiagnostic("log.copied", new { characters = editorTextBox.TextLength });
        }
        catch (Exception ex)
        {
            OwnerCenteredMessageBox.Show(this, ex.Message, "ログのコピーに失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        ReleaseFocusToWaveform();
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
        if (OwnerCenteredMessageBox.ShowDialog(this, dialog) != DialogResult.OK)
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
            OwnerCenteredMessageBox.Show(this, ex.Message, "ログの保存に失敗", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        ReleaseFocusToWaveform();
    }

    private void ReloadButton_Click(object? sender, EventArgs e)
    {
        if (_lastInputFiles.Count == 0)
        {
            return;
        }

        editorTextBox.Clear();
        ProcessDroppedFiles(_lastInputFiles, rememberInputFiles: false);
        ReleaseFocusToWaveform();
    }

    private void RestoreWindowBounds()
    {
        var settings = WindowSettings.Load();
        if (settings is null)
        {
            StartPosition = FormStartPosition.CenterScreen;
            return;
        }

        if (!settings.TryApply(this))
        {
            StartPosition = FormStartPosition.CenterScreen;
        }
    }

    /// <summary>
    /// トランスポート全体が収まる幅と、Exit Source At の Exit Cue が見切れない高さを
    /// メインフォームの縮小限界にする。
    /// 保存された前回サイズは縮小限界には使用しない。
    /// </summary>
    private void UpdateMinimumWindowSize()
    {
        var nonClientWidth = Math.Max(0, Width - ClientSize.Width);
        var nonClientHeight = Math.Max(0, Height - ClientSize.Height);
        var safetyMargin = (int)Math.Ceiling(8f * DeviceDpi / 96f);

        // Fade In / Fade Out（各々 Group 区分込み）と Exit Source At を 1 行に並べる。
        var transitionRowsHeight = Math.Max(
            Math.Max(fadeInSectionPanel.Height, fadeOutSectionPanel.Height),
            exitSourceAtSectionPanel.Height);
        var requiredLogAreaHeight =
            transitionRowsHeight + markerOptionsPanel.RequiredHeight;
        var fixedChromeHeight =
            projectBar.Height
            + waveformHostPanel.Height
            + transportBar.Height
            + waapiStatusBar.Height
            + actionBar.Height;

        MinimumSize = new Size(
            transportBar.RequiredWidth + nonClientWidth + safetyMargin,
            fixedChromeHeight + requiredLogAreaHeight + nonClientHeight + safetyMargin);
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
        if (_uiInteractionLocks.HasFlag(UiInteractionLock.Export)
            || _uiInteractionLocks.HasFlag(UiInteractionLock.Load))
        {
            e.Effect = DragDropEffects.None;
            return;
        }

        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
            return;
        }

        e.Effect = DragDropEffects.None;
    }

    private void EditorTextBox_DragDrop(object? sender, DragEventArgs e)
    {
        if (_uiInteractionLocks.HasFlag(UiInteractionLock.Export)
            || _uiInteractionLocks.HasFlag(UiInteractionLock.Load))
        {
            return;
        }

        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
        {
            return;
        }

        ProcessDroppedFiles(files);
    }

    /// <returns>読み込み処理を開始したら true（すりガラス解除は呼び出し側ではなく読み込み側が担当）。</returns>
    private bool LoadLastWaveAsDropped()
    {
        if (!loadLastWaveCheckBox.Checked || string.IsNullOrWhiteSpace(_lastWavePath))
        {
            return false;
        }

        string wavPath;
        try
        {
            wavPath = Path.GetFullPath(_lastWavePath);
        }
        catch (Exception ex)
        {
            AppendReport($"前回読み込んだ波形のパスが不正です: {ex.Message}" + Environment.NewLine);
            return false;
        }

        if (!File.Exists(wavPath))
        {
            AppendReport($"前回読み込んだ波形が見つかりません: {wavPath}" + Environment.NewLine);
            return false;
        }

        ProcessDroppedFiles([wavPath], isLastSessionLoad: true);
        return true;
    }

    private async void ProcessDroppedFiles(
        IEnumerable<string> files,
        bool rememberInputFiles = true,
        bool isLastSessionLoad = false)
    {
        var fileList = files
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        if (fileList.Length == 0)
        {
            return;
        }

        var loadAlreadyActive = _uiInteractionLocks.HasFlag(UiInteractionLock.Load);
        if (_uiInteractionLocks.HasFlag(UiInteractionLock.Export)
            || (loadAlreadyActive && !isLastSessionLoad))
        {
            return;
        }

        if (rememberInputFiles)
        {
            _lastInputFiles = fileList;
            reloadButton.Enabled = true;
        }

        var exportGeneration = ++_exportGeneration;
        var loadMessage = isLastSessionLoad ? "Loading Last Session" : "Loading";
        // 起動中すりガラスが既にあればスナップショットは維持し、メッセージだけ差し替える。
        SetUiInteractionLocked(UiInteractionLock.Load, locked: true, loadMessage);

        WaveformPreviewData? preview = null;
        try
        {
            _sourceBaseNameOverride = null;
            _loadedPreview = null;
            _previewSession = null;
            _exportBusy = false;
            UpdateTransportPosition();
            ClearPendingPlaylistUiTransition();
            ClearPlaylistChoices("読み込み中…");
            UpdateExportButtonState();
            waveformView.ClearExportHighlight();
            _playheadTimer.Stop();
            // 再読込前に再生ハンドル／一時コピーを解放し、元 WAV の上書きや再解析を妨げない。
            _audioPlayer.Clear();
            UpdateTransportPlaybackState();

            // 解析中に OS が白消ししないよう、先に暗いフレームを確定する
            waveformView.CommitDarkFrame();

            // 巨大 WAV のピーク走査で UI を止めないよう、解析は背景スレッドで行う
            var (report, previewData) = await Task.Run(() =>
            {
                var text = DroppedFilesProcessor.Process(fileList, out var processed);
                return (text, processed);
            });
            preview = previewData;

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
            _previewSession.SetCommentRule(_markerSettings.ToCommentRule());

            // 再生用一時コピーは大きな WAV だと数秒かかる。
            try
            {
                await Task.Run(() =>
                {
                    _audioPlayer.Load(preview.SourcePath);
                    _audioPlayer.SetLoopPlans(WaveAudioPlayer.BuildLoopPlans(preview.Regions));
                });
            }
            catch (Exception ex)
            {
                AppendReport(
                    $"=== エラー ==={Environment.NewLine}"
                    + $"Message : 再生の準備に失敗: {ex.Message}{Environment.NewLine}{Environment.NewLine}");
            }

            if (IsDisposed || exportGeneration != _exportGeneration)
            {
                return;
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                SetUiInteractionLocked(UiInteractionLock.Load, locked: false);
            }
        }

        if (IsDisposed || exportGeneration != _exportGeneration || preview is null)
        {
            return;
        }

        waveformView.SetPreview(
            preview.Peaks,
            preview.SourcePath,
            preview.WavInfo,
            preview.Bars,
            _previewSession.EffectiveMarkers,
            preview.Cycles,
            preview.Regions,
            preview.OutputParts);
        waveformView.SetPlayhead(0, recordTrail: false);

        _loadedPreview = preview;
        _lastWavePath = Path.GetFullPath(preview.SourcePath);
        if (!_creatingNewProject && _projectStore.ContainsName(_loadedProjectName))
        {
            _projectStore.SaveLastWavePath(_loadedProjectName, _lastWavePath);
        }

        UpdateTransportPosition();
        PopulatePlaylistChoices(preview.OutputParts);
        TryRestoreLastWaveSession(preview);
        PersistLastWaveSessionIfPossible();
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

        var preflight = EvaluateExportPreflight();
        var directory = preflight.CanExport
            ? preflight.OutputDirectory
            : (_projectOutputDirectory.Trim().Length > 0
                ? _projectOutputDirectory.Trim()
                : "（未選択）");
        AppendReport(
            $"=== Export ==={Environment.NewLine}"
            + (preflight.CanExport
                ? $"Message : 出力パート {preview.OutputParts.Count} 件。［EXPORT］で分割 WAV を書き出し、Wwise へ登録できます。{Environment.NewLine}"
                : $"Message : 出力パート {preview.OutputParts.Count} 件。書き出し条件未達: {preflight.Reason}{Environment.NewLine}")
            + $"保存先  : {directory}{Environment.NewLine}{Environment.NewLine}");
        _lastLoggedPreflightKey = $"{preflight.CanExport}|{preflight.Reason}|{preflight.OutputDirectory}"
            + $"|{preflight.TargetPath}|{preflight.ProjectFilePath}";
    }

    /// <summary>
    /// サイドカーに保存したグループ／無効化／トランジション設定／アプリ追加マーカーを部分復元する。
    /// </summary>
    private void TryRestoreLastWaveSession(WaveformPreviewData preview)
    {
        if (_creatingNewProject || !_projectStore.ContainsName(_loadedProjectName))
        {
            return;
        }

        if (!_projectStore.TryReadLastWaveSession(_loadedProjectName, out var state) || state is null)
        {
            return;
        }

        var wavePath = LastWaveSessionState.NormalizePath(preview.SourcePath);
        if (!string.Equals(state.WavePath, wavePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!state.TryGetPartGroupIds(out var savedGroups)
            || !state.TryGetGroupColorIndexes(out var savedColors)
            || !state.TryGetPartExitSourceModes(out var savedExitSources)
            || !state.TryGetPartFadeSeconds(
                out var savedFadeIns,
                out var savedFadeOuts,
                out var savedGroupFadeIns,
                out var savedGroupFadeOuts))
        {
            AppendReport(
                $"=== Session ==={Environment.NewLine}"
                + $"Message : 前回セッションの読み込みに失敗しました（形式不正）。{Environment.NewLine}{Environment.NewLine}");
            return;
        }

        var hasAny =
            state.Parts.Count > 0
            || savedGroups.Count > 0
            || state.DisabledPartNumbers.Count > 0
            || state.UserMarkerSampleOffsets.Count > 0
            || savedExitSources.Count > 0
            || savedFadeIns.Count > 0
            || savedFadeOuts.Count > 0
            || savedGroupFadeIns.Count > 0
            || savedGroupFadeOuts.Count > 0;
        if (!hasAny)
        {
            return;
        }

        var loadedByNumber = preview.OutputParts.ToDictionary(part => part.Number);
        var matchingNumbers = new HashSet<int>();
        foreach (var signature in state.Parts)
        {
            if (loadedByNumber.TryGetValue(signature.Number, out var part)
                && signature.Matches(part))
            {
                matchingNumbers.Add(signature.Number);
            }
        }

        if (state.Parts.Count > 0 && matchingNumbers.Count == 0)
        {
            AppendReport(
                $"=== Session ==={Environment.NewLine}"
                + $"Message : 前回セッションはパート構成が一致しないため復元しませんでした。{Environment.NewLine}{Environment.NewLine}");
            return;
        }

        var disabledRequested = state.DisabledPartNumbers.Count;
        var disabledApplied = 0;
        foreach (var partNumber in state.DisabledPartNumbers)
        {
            if (!matchingNumbers.Contains(partNumber))
            {
                continue;
            }

            if (_disabledPlaylistPartNumbers.Add(partNumber))
            {
                disabledApplied++;
            }
        }

        var groupRequested = savedGroups.Count;
        var groupApplied = 0;
        foreach (var (partNumber, groupId) in savedGroups)
        {
            if (!matchingNumbers.Contains(partNumber)
                || _disabledPlaylistPartNumbers.Contains(partNumber))
            {
                continue;
            }

            _playlistPartGroupIds[partNumber] = groupId;
            groupApplied++;
        }

        foreach (var groupId in _playlistPartGroupIds.Values.Distinct())
        {
            if (savedColors.TryGetValue(groupId, out var colorIndex))
            {
                _playlistGroupColorIndexes[groupId] = colorIndex;
            }
            else if (!_playlistGroupColorIndexes.ContainsKey(groupId))
            {
                _playlistGroupColorIndexes[groupId] = _nextPlaylistGroupColorIndex++;
            }
        }

        var maxGroupId = _playlistPartGroupIds.Count == 0
            ? 0
            : _playlistPartGroupIds.Values.Max();
        _nextPlaylistGroupId = Math.Max(Math.Max(1, state.NextGroupId), maxGroupId + 1);

        var maxColorIndex = _playlistGroupColorIndexes.Count == 0
            ? -1
            : _playlistGroupColorIndexes.Values.Max();
        _nextPlaylistGroupColorIndex = Math.Max(
            Math.Max(0, state.NextColorIndex),
            maxColorIndex + 1);

        var markerRequested = state.UserMarkerSampleOffsets.Count;
        var markerApplied = 0;
        if (_previewSession is { } session && markerRequested > 0)
        {
            session.AddMarkers(state.UserMarkerSampleOffsets);
            var present = session.GetUserMarkerSampleOffsets().ToHashSet();
            markerApplied = state.UserMarkerSampleOffsets.Count(sample => present.Contains(sample));
        }

        var exitRequested = savedExitSources.Count;
        var exitApplied = 0;
        foreach (var (partNumber, mode) in savedExitSources)
        {
            if (!loadedByNumber.ContainsKey(partNumber))
            {
                continue;
            }

            if (state.Parts.Count > 0 && !matchingNumbers.Contains(partNumber))
            {
                continue;
            }

            _playlistExitSourceModes[partNumber] = mode;
            exitApplied++;
        }

        var fadeInApplied = 0;
        foreach (var (partNumber, seconds) in savedFadeIns)
        {
            if (!loadedByNumber.ContainsKey(partNumber))
            {
                continue;
            }

            if (state.Parts.Count > 0 && !matchingNumbers.Contains(partNumber))
            {
                continue;
            }

            _playlistFadeInSecondsByPart[partNumber] = seconds;
            fadeInApplied++;
        }

        var fadeOutApplied = 0;
        foreach (var (partNumber, seconds) in savedFadeOuts)
        {
            if (!loadedByNumber.ContainsKey(partNumber))
            {
                continue;
            }

            if (state.Parts.Count > 0 && !matchingNumbers.Contains(partNumber))
            {
                continue;
            }

            _playlistFadeOutSecondsByPart[partNumber] = seconds;
            fadeOutApplied++;
        }

        var groupFadeInApplied = 0;
        foreach (var (partNumber, seconds) in savedGroupFadeIns)
        {
            if (!loadedByNumber.ContainsKey(partNumber))
            {
                continue;
            }

            if (state.Parts.Count > 0 && !matchingNumbers.Contains(partNumber))
            {
                continue;
            }

            _playlistGroupFadeInSecondsByPart[partNumber] = seconds;
            groupFadeInApplied++;
        }

        var groupFadeOutApplied = 0;
        foreach (var (partNumber, seconds) in savedGroupFadeOuts)
        {
            if (!loadedByNumber.ContainsKey(partNumber))
            {
                continue;
            }

            if (state.Parts.Count > 0 && !matchingNumbers.Contains(partNumber))
            {
                continue;
            }

            _playlistGroupFadeOutSecondsByPart[partNumber] = seconds;
            groupFadeOutApplied++;
        }

        var settingsPart = _playlistExitSourceModes.Keys
            .Concat(_playlistFadeInSecondsByPart.Keys)
            .Concat(_playlistFadeOutSecondsByPart.Keys)
            .Concat(_playlistGroupFadeInSecondsByPart.Keys)
            .Concat(_playlistGroupFadeOutSecondsByPart.Keys)
            .Where(loadedByNumber.ContainsKey)
            .OrderBy(number => number)
            .Cast<int?>()
            .FirstOrDefault();
        if (settingsPart is int editPart)
        {
            ShowTransitionSettingsForPart(editPart);
        }

        ApplyPlaylistDisableUi();
        ApplyPlaylistGroupColorsOnly();

        AppendReport(
            $"=== Session ==={Environment.NewLine}"
            + $"Message : 前回セッションを部分復元: グループ {groupApplied}/{groupRequested}、"
            + $"無効 {disabledApplied}/{disabledRequested}、"
            + $"マーカー {markerApplied}/{markerRequested}、"
            + $"Exit Source {exitApplied}/{exitRequested}、"
            + $"Fade In {fadeInApplied}/{savedFadeIns.Count}、"
            + $"Fade Out {fadeOutApplied}/{savedFadeOuts.Count}、"
            + $"Fade In Group {groupFadeInApplied}/{savedGroupFadeIns.Count}、"
            + $"Fade Out Group {groupFadeOutApplied}/{savedGroupFadeOuts.Count}"
            + $"{Environment.NewLine}{Environment.NewLine}");
    }

    /// <summary>
    /// 現在のグループ／無効化／トランジション設定／アプリ追加マーカーをサイドカーへオートセーブする。
    /// </summary>
    private void PersistLastWaveSessionIfPossible()
    {
        if (_creatingNewProject || !_projectStore.ContainsName(_loadedProjectName))
        {
            return;
        }

        if (_loadedPreview is not { } preview
            || _previewSession is not { } session
            || string.IsNullOrWhiteSpace(_lastWavePath))
        {
            return;
        }

        var state = LastWaveSessionState.Capture(
            _lastWavePath,
            preview.OutputParts,
            _playlistPartGroupIds,
            _playlistGroupColorIndexes,
            _nextPlaylistGroupId,
            _nextPlaylistGroupColorIndex,
            session.GetUserMarkerSampleOffsets(),
            _disabledPlaylistPartNumbers,
            _playlistExitSourceModes,
            _playlistFadeInSecondsByPart,
            _playlistFadeOutSecondsByPart,
            _playlistGroupFadeInSecondsByPart,
            _playlistGroupFadeOutSecondsByPart);
        _projectStore.SaveLastWaveSession(_loadedProjectName, state);
    }

    /// <summary>EXPORT 開始時に再生・遷移予約を止める（位置は保持）。</summary>
    private void StopPlaybackForExport()
    {
        _audioPlayer.CancelPlaylistTransition();
        ClearPendingPlaylistUiTransition();
        ClearPlaylistTransitionGlow();
        _playheadTimer.Stop();
        if (_audioPlayer.IsPlaying)
        {
            _audioPlayer.Pause();
        }

        ClearPlaylistPlaybackSelection();
        UpdateTransportPlaybackState();
        UpdatePlayhead();
        WritePlaybackDiagnostic("export.playback-stopped");
    }

    private async void ExportButton_Click(object? sender, EventArgs e)
    {
        if (_exportBusy || _loadedPreview is not { OutputParts.Count: > 0 } preview)
        {
            return;
        }

        // クリック時点で接続・プロジェクト・選択・書き出し先を再検証（失敗時は WAV を書き始めない）
        ExportPreflightResult preflight;
        try
        {
            var result = await WaapiStartupProbe.RunAsync(_waapiSettings);
            if (!IsDisposed)
            {
                ApplyWaapiProbeResult(result, logReport: false);
                await TryRestoreKeptTargetAsync(logReport: true).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            AppendReport(
                $"=== Export Preflight ==={Environment.NewLine}"
                + $"Status  : NG{Environment.NewLine}"
                + $"Message : Wwise 状態の取得に失敗: {ex.Message}{Environment.NewLine}{Environment.NewLine}");
            UpdateExportButtonState();
            ReleaseFocusToWaveform();
            return;
        }

        if (IsDisposed)
        {
            return;
        }

        preflight = EvaluateExportPreflight();
        UpdateExportButtonState();
        if (!preflight.CanExport)
        {
            AppendReport(preflight.FormatLogMessage());
            _lastLoggedPreflightKey = $"{preflight.CanExport}|{preflight.Reason}|{preflight.OutputDirectory}"
                + $"|{preflight.TargetPath}|{preflight.ProjectFilePath}";
            OwnerCenteredMessageBox.Show(
                this,
                preflight.Reason,
                "EXPORT",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            ReleaseFocusToWaveform();
            return;
        }

        var outputDirectory = preflight.OutputDirectory;
        var targetPath = preflight.TargetPath;

        var exportGeneration = _exportGeneration;
        var wwiseMarkers = (_previewSession?.WwiseMarkers ?? preview.Markers).ToArray();
        var wwiseSnapshot = BuildPlaylistExportSnapshot(preview, wwiseMarkers);
        if (wwiseSnapshot.Parts.Count == 0)
        {
            return;
        }

        StopPlaybackForExport();

        _exportBusy = true;
        SetUiInteractionLocked(UiInteractionLock.Export, locked: true, "Exporting");
        UpdateExportButtonState();

        try
        {
            await RunWwiseImportAsync(
                preview,
                wwiseSnapshot,
                exportGeneration,
                outputDirectory,
                targetPath);
        }
        finally
        {
            if (!IsDisposed)
            {
                _exportBusy = false;
                SetUiInteractionLocked(UiInteractionLock.Export, locked: false);
                UpdateExportButtonState();
                ReleaseFocusToWaveform();
            }
        }
    }

    /// <summary>
    /// エクスポート済み WAV を Wwise の選択位置へ Music 構造として流し込む。
    /// キャンセル時はログを残してスキップする。作成先は EXPORT 開始時に固定したパスを使う。
    /// </summary>
    private async Task RunWwiseImportAsync(
        WaveformPreviewData preview,
        PlaylistExportSnapshot snapshot,
        int exportGeneration,
        string outputDirectory,
        string targetPath)
    {
        void ReportProgress(string message)
        {
            // すりガラス中もログエディタへ残す（AppendReport がオーバーレイへもミラーする）。
            var text = message.EndsWith('\n') || message.EndsWith("\r\n", StringComparison.Ordinal)
                ? message
                : message + Environment.NewLine;
            AppendReport(text);
        }

        if (targetPath.Length == 0)
        {
            AppendReport(
                $"=== Wwise Import ==={Environment.NewLine}"
                + $"Message : Wwise 上で作成先オブジェクトが選択されていないためスキップしました。{Environment.NewLine}{Environment.NewLine}");
            return;
        }

        var importSettings = WwiseImportSettings.Load()
            .WithStreaming(
                markerOptionsPanel.StreamEnabled,
                markerOptionsPanel.LookAheadMs,
                markerOptionsPanel.PrefetchLengthMs);

        WwiseMusicPlan plan;
        try
        {
            ReportProgress("Building import plan...");
            plan = WwiseMusicPlanBuilder.Build(
                BuildNamingSourcePath(preview.SourcePath),
                preview.WavInfo.SampleRate,
                snapshot.Parts,
                preview.Regions,
                preview.Bars,
                snapshot.Markers,
                snapshot.PartGroupIds,
                snapshot.PlaylistNameOverrides,
                outputDirectory,
                snapshot.PartExitSourceModes,
                _playlistExitSourceMode);
            ReportProgress($"Plan ready: {plan.Playlists.Count} playlist(s).");
        }
        catch (Exception ex)
        {
            AppendReport(
                $"=== Wwise Import ==={Environment.NewLine}"
                + $"Message : インポート計画の作成に失敗: {ex.Message}{Environment.NewLine}{Environment.NewLine}");
            return;
        }

        var updateExistingStateGroup = false;
        if (plan.IsMultiPart)
        {
            var stateGroupPath = importSettings.ResolveStateGroupPath(plan.ContainerName);
            bool exists;
            try
            {
                ReportProgress("Checking State Group...");
                exists = await WaapiObjectUtil.ExistsAsync(_waapiSettings, stateGroupPath);
                ReportProgress(exists
                    ? "Existing State Group found."
                    : "State Group is available.");
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

            // 既存 State Group は削除せず、object.set の merge で同一オブジェクトを更新する。
            updateExistingStateGroup = exists;
        }

        if (exportGeneration != _exportGeneration)
        {
            return;
        }

        try
        {
            // 進行ログは Progress → AppendReport でエディタ／オーバーレイへ逐次出す。
            // 完了後にまとめて再出力すると二重になるため、戻り値の全文は捨てる。
            var progress = new Progress<string>(ReportProgress);
            _ = await Task.Run(() => WaapiMusicImporter.ImportAsync(
                _waapiSettings,
                importSettings,
                plan,
                targetPath,
                preview.SourcePath,
                outputDirectory,
                snapshot.Parts,
                preview.WavInfo,
                snapshot.PartGroupIds,
                markerOptionsPanel.LoudnessNormalizeEnabled,
                markerOptionsPanel.LoudnessTargetLkfs,
                markerOptionsPanel.LoudnessPreserveGroupBalance,
                updateExistingStateGroup,
                progress));
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

    private void ShowBarJumpDialog()
    {
        // 初回は現在位置の最近傍小節。一度ジャンプしたあとはその値を初期表示する。
        using var dialog = new BarJumpDialogForm(
            _lastJumpedBarNumber ?? waveformView.GetNearestBarNumber())
        {
            // メインが最前面でもダイアログが背面に回らないようにする
            TopMost = TopMost,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.BarNumber is not int barNumber)
        {
            return;
        }

        _lastJumpedBarNumber = barNumber;
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
        PersistLastWaveSessionIfPossible();
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

                // Provider は先読みで先に遷移し得るため、待ち中は待ち開始時のソース側ループでだけ折り返す。
                // 遷移先ループを旧タイムライン表示へ適用しない。
                if (_pendingPlaylistTransitionGeneration == 0)
                {
                    // 未アームのまま -L に入ったら、そこで初めてループを有効化
                    if (!_audioPlayer.TryGetActiveLoopProgress(out _, out _)
                        && _audioPlayer.TryGetLoopProgress(progress, out _, out _))
                    {
                        _audioPlayer.ArmLoopAtProgress(progress);
                    }

                    progress = WrapProgressForLoop(progress);
                }
                else
                {
                    progress = WrapProgressForLoopRange(
                        progress,
                        _pendingSourceLoopStart,
                        _pendingSourceLoopEnd);
                }

                if (progress + 1e-12 < _smoothProgress)
                {
                    waveformView.ClearPlayheadTrail();
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

        return WrapProgressForLoopRange(progress, start, end);
    }

    private double WrapProgressForLoopRange(double progress, double? startNullable, double? endNullable)
    {
        if (startNullable is not double start || endNullable is not double end)
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

        // 折り返したら壁時計アンカーも同期する。
        // （シーク直後など残りが短い場合、旧「1周分」条件だとアンカーが古いまま残り、
        //  遷移待ちで折り返しを止めた瞬間に位置が跳ねる）
        if (Math.Abs(wrapped - progress) > 1e-9)
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
#if DEBUG
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
                transitionFadeInSeconds = (_transitionSettingsEditPartNumber is int fadeInPart
                        ? ResolveFadeInSeconds(fadeInPart)
                        : _playlistFadeInSeconds),
                transitionFadeIn = PlaylistUiNames.ToFadeUiName(
                    _transitionSettingsEditPartNumber is int fadeInPart2
                        ? ResolveFadeInSeconds(fadeInPart2)
                        : _playlistFadeInSeconds,
                    isFadeIn: true),
                transitionFadeSeconds = (_transitionSettingsEditPartNumber is int fadeOutPart
                        ? ResolveFadeOutSeconds(fadeOutPart)
                        : _playlistFadeSeconds),
                transitionFadeOut = PlaylistUiNames.ToFadeUiName(
                    _transitionSettingsEditPartNumber is int fadeOutPart2
                        ? ResolveFadeOutSeconds(fadeOutPart2)
                        : _playlistFadeSeconds,
                    isFadeIn: false),
                exitSourceMode = (_transitionSettingsEditPartNumber is int editPart
                        ? ResolveExitSourceMode(editPart)
                        : _playlistExitSourceMode).ToUiName(),
                exitSourceEditPart = _transitionSettingsEditPartNumber,
                destinationSyncMode = "Automatic (same group: Same Time / otherwise: Entry Cue)",
                markerGrid = _markerSettings.GridOverride.ToUiName(),
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
#endif
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

        // すりガラス表示中は同じ内容をオーバーレイ左下にも出す（読み込み／書き出し共通）。
        // 専用ログではなく、エディタへ残す内容のミラー表示。
        if (_uiInteractionLocks.HasFlag(UiInteractionLock.Export)
            || _uiInteractionLocks.HasFlag(UiInteractionLock.Load))
        {
            _exportOverlay?.AppendLog(text);
            // 長い WAAPI 待ちの間も直近行が見えるよう、即座に再描画する。
            _exportOverlay?.Update();
        }
    }

    private void AppendColoredLine(string line)
    {
        editorTextBox.SelectionStart = editorTextBox.TextLength;
        editorTextBox.SelectionLength = 0;
        ApplyFixedLogLineSpacing();
        editorTextBox.SelectionColor = ColorForLogLine(line);
        editorTextBox.AppendText(line + "\n");
    }

    /// <summary>通常ログとエクスポートオーバーレイで共有するログ行の色定義。</summary>
    internal static Color ColorForLogLine(string line)
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
            || t.StartsWith("Status  : 接続失敗", StringComparison.Ordinal)
            || t.StartsWith("Status  : NG", StringComparison.Ordinal)
            || t.StartsWith("自動読み込み対象が見つかりません", StringComparison.Ordinal)
            || t.StartsWith("Target  : （未選択）", StringComparison.Ordinal)
            || t.Contains("(なし)", StringComparison.Ordinal)
            || IsErrorMessageLine(t))
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

    /// <summary>
    /// <c>Message :</c> 行のうち、失敗・未達など実害のある内容だけをエラー色にする。
    /// 保存成功やセッション部分復元などの案内は通常色のままにする。
    /// </summary>
    private static bool IsErrorMessageLine(string trimmedLine)
    {
        if (!trimmedLine.StartsWith("Message :", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return trimmedLine.Contains("失敗", StringComparison.Ordinal)
            || trimmedLine.Contains("エラー", StringComparison.Ordinal)
            || trimmedLine.Contains("見つかりません", StringComparison.Ordinal)
            || trimmedLine.Contains("未達", StringComparison.Ordinal)
            || trimmedLine.Contains("形式不正", StringComparison.Ordinal)
            || trimmedLine.Contains("復元しません", StringComparison.Ordinal)
            || trimmedLine.Contains("スキップしました", StringComparison.Ordinal)
            || trimmedLine.Contains("ドロップしてください", StringComparison.Ordinal)
            || trimmedLine.Contains("必要です", StringComparison.Ordinal);
    }

    protected override void WndProc(ref Message m)
    {
        const int wmMouseWheel = 0x020A;
        const int wmEraseBkgnd = 0x0014;
        const int wmSysCommand = 0x0112;
        const int scKeyMenu = 0xF100;

        // Alt 単独でシステムメニューモードに入るとフォーカスが外れ、
        // 波形ショートカットや操作系の挙動が崩れるため握りつぶす。
        // LParam==0 が Alt 単独。Alt+Space（システムメニュー）は通す。
        if (m.Msg == wmSysCommand
            && ((int)(m.WParam.ToInt64() & 0xFFF0)) == scKeyMenu
            && m.LParam == IntPtr.Zero)
        {
            return;
        }

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
                    transportBar.PulseCommandFeedback(
                        wheelDelta > 0
                            ? TransportCommand.AmpZoomIn
                            : TransportCommand.AmpZoomOut);
                }
                else if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                {
                    waveformView.PanTimeByWheel(wheelDelta);
                    transportBar.PulseCommandFeedback(
                        wheelDelta > 0
                            ? TransportCommand.PreviousPage
                            : TransportCommand.NextPage);
                }
                else
                {
                    var client = waveformView.PointToClient(screenPoint);
                    waveformView.ZoomTimeByWheel(wheelDelta, client.X);
                    transportBar.PulseCommandFeedback(
                        wheelDelta > 0
                            ? TransportCommand.TimeZoomIn
                            : TransportCommand.TimeZoomOut);
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
