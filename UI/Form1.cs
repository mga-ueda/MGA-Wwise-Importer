using System.Runtime.InteropServices;

namespace MgaWwiseImporter.UI;

public partial class Form1 : Form
{
    // Exact line height in twips (1 pt = 20 twips). Keeps JP + Latin rows uniform.
    private const int LogLineSpacingTwips = 280;

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int EmSetParaFormat = 0x0447;
    private const uint PfmLineSpacing = 0x00000100;
    private const byte LineSpacingExact = 4;

    private DeveloperSettings _developerSettings = new();
    private readonly WaveAudioPlayer _audioPlayer = new();
    private readonly System.Windows.Forms.Timer _playheadTimer = new() { Interval = 16 };
    private double _smoothProgress;
    private double _anchorProgress;
    private long _anchorTickMs;
    private ColorDevPanelForm? _colorDevPanel;
    private int _exportGeneration;

    public Form1()
    {
        UiColors.LoadFromIni();
        InitializeComponent();
        KeyPreview = true;
        _developerSettings = DeveloperSettings.Load();
        TopMost = _developerSettings.TopMost;
        RestoreWindowBounds();

        _playheadTimer.Tick += (_, _) => UpdatePlayhead();
        _audioPlayer.PlaybackEnded += (_, _) =>
        {
            if (IsDisposed)
            {
                return;
            }

            BeginInvoke(() =>
            {
                _playheadTimer.Stop();
                AnchorPlayhead(0);
                waveformView.SetPlayhead(0, recordTrail: false);
            });
        };
        waveformView.SeekRequested += (_, progress) => SeekPlayback(progress);
        editorTextBox.HandleCreated += (_, _) => ApplyDarkEditorChrome();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
        ApplyFixedLogLineSpacing();
        ApplyDarkEditorChrome();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BeginInvoke(LoadAutoWaveAsDropped);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _playheadTimer.Stop();
        _audioPlayer.Dispose();
        _playheadTimer.Dispose();
        WindowSettings.FromForm(this).Save();
        base.OnFormClosing(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Space)
        {
            TogglePlayback();
            return true;
        }

        if (keyData == (Keys.Control | Keys.Shift | Keys.C))
        {
            ShowColorDevPanel();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
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
        BackColor = UiColors.WindowBack;
        ForeColor = UiColors.WindowFore;
        editorTextBox.BackColor = UiColors.LogBack;
        editorTextBox.ForeColor = UiColors.LogDefault;
        waveformView.RefreshAppearance();
    }

    private void RestoreWindowBounds()
    {
        var settings = WindowSettings.Load();
        if (settings is null || !settings.TryApply(this))
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
        waveformView.ClearExportHighlight();
        _playheadTimer.Stop();
        _audioPlayer.Stop();

        // 解析中に OS が白消ししないよう、先に暗いフレームを確定する
        waveformView.CommitDarkFrame();

        var report = DroppedFilesProcessor.Process(files, out var preview);
        AppendReport(report);

        if (preview is null)
        {
            _audioPlayer.Clear();
            waveformView.ClearPreview();
            return;
        }

        waveformView.SetPreview(
            preview.Peaks,
            preview.SourcePath,
            preview.Bars,
            preview.Markers,
            preview.Cycles,
            preview.Regions,
            preview.OutputParts);
        try
        {
            _audioPlayer.Load(preview.SourcePath);
            waveformView.SetPlayhead(0, recordTrail: false);
        }
        catch (Exception ex)
        {
            AppendReport(
                $"=== エラー ==={Environment.NewLine}"
                + $"Message : 再生の準備に失敗: {ex.Message}{Environment.NewLine}{Environment.NewLine}");
        }

        if (preview.OutputParts.Count == 0 || exportGeneration != _exportGeneration)
        {
            return;
        }

        // リージョン付きプレビューが画面に載ってから問い合わせる
        await waveformView.WaitForRevealAsync();
        if (IsDisposed || exportGeneration != _exportGeneration)
        {
            return;
        }

        var directory = Path.GetDirectoryName(preview.SourcePath) ?? string.Empty;
        var confirm = MessageBox.Show(
            this,
            $"リージョン情報付きの分割 WAV を {preview.OutputParts.Count} ファイル書き出します。\n"
            + $"保存先: {directory}\n\n"
            + "エクスポートしますか？",
            "WAV エクスポート",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1);

        if (confirm != DialogResult.Yes)
        {
            AppendReport(
                $"=== Export ==={Environment.NewLine}"
                + "Message : エクスポートをスキップしました。"+ Environment.NewLine
                + Environment.NewLine);
            return;
        }

        if (exportGeneration != _exportGeneration)
        {
            return;
        }

        await RunExportAsync(preview, exportGeneration);
    }

    private async Task RunExportAsync(WaveformPreviewData preview, int exportGeneration)
    {
        string exportReport;
        try
        {
            exportReport = await Task.Run(() => WaveformExporter.Export(
                preview.SourcePath,
                preview.WavInfo,
                preview.OutputParts,
                preview.Regions,
                preview.Bars,
                preview.Markers,
                onPartBegin: part =>
                {
                    if (exportGeneration != _exportGeneration || IsDisposed)
                    {
                        return;
                    }

                    try
                    {
                        // 書き出し開始前に UI へ反映し、発光が見えてから I/O に入る
                        Invoke(() =>
                        {
                            if (exportGeneration != _exportGeneration || IsDisposed)
                            {
                                return;
                            }

                            waveformView.SetExportHighlight(part.Number);
                            waveformView.Update();
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
                }));
        }
        catch (Exception ex)
        {
            exportReport =
                $"=== エラー ==={Environment.NewLine}"
                + $"Message : 書き出しに失敗: {ex.Message}{Environment.NewLine}{Environment.NewLine}";
        }

        if (IsDisposed || exportGeneration != _exportGeneration)
        {
            return;
        }

        waveformView.ClearExportHighlight();
        AppendReport(exportReport);
    }

    private void TogglePlayback()
    {
        if (!_audioPlayer.HasSource)
        {
            return;
        }

        _audioPlayer.Toggle();
        if (_audioPlayer.IsPlaying)
        {
            // 再生開始時だけ位置を取り込み、以降は壁時計で表示（エンジンには触れない）
            AnchorPlayhead(_audioPlayer.Progress);
            _playheadTimer.Start();
        }
        else
        {
            _playheadTimer.Stop();
            // 停止時のみエンジン位置に合わせる
            AnchorPlayhead(_audioPlayer.Progress);
        }

        UpdatePlayhead();
    }

    private void SeekPlayback(double progress)
    {
        if (!_audioPlayer.HasSource)
        {
            return;
        }

        _audioPlayer.Seek(progress);
        AnchorPlayhead(_audioPlayer.Progress);
        // シーク直後は残光を付けない（ジャンプの残像を避ける）
        waveformView.SetPlayhead(_smoothProgress, recordTrail: false);
    }

    private void UpdatePlayhead()
    {
        if (!_audioPlayer.HasSource)
        {
            waveformView.SetPlayhead(null);
            return;
        }

        if (_audioPlayer.IsPlaying)
        {
            // 再生中は Progress を読まない（バッファ位置の跳ね返り／往復を避ける）
            var durationSec = _audioPlayer.Duration.TotalSeconds;
            if (durationSec > 0)
            {
                var elapsedSec = (Environment.TickCount64 - _anchorTickMs) / 1000d;
                _smoothProgress = Math.Clamp(_anchorProgress + elapsedSec / durationSec, 0d, 1d);
            }

            waveformView.SetPlayhead(_smoothProgress, recordTrail: true);
        }
        else
        {
            waveformView.SetPlayhead(_smoothProgress, recordTrail: false);
        }
    }

    private void AnchorPlayhead(double progress)
    {
        _anchorProgress = Math.Clamp(progress, 0d, 1d);
        _anchorTickMs = Environment.TickCount64;
        _smoothProgress = _anchorProgress;
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
            || t.StartsWith("自動読み込み対象が見つかりません", StringComparison.Ordinal)
            || t.Contains("(なし)", StringComparison.Ordinal))
        {
            return UiColors.LogError;
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
        const int wmEraseBkgnd = 0x0014;
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
