using System.Runtime.InteropServices;

namespace MgaWwiseImporter.UI;

public partial class Form1 : Form
{
    private static readonly Color LogDefault = Color.FromArgb(220, 220, 220);
    private static readonly Color LogHeader = Color.FromArgb(110, 180, 255);
    private static readonly Color LogSuccess = Color.FromArgb(120, 210, 140);
    private static readonly Color LogWarning = Color.FromArgb(255, 180, 70);
    private static readonly Color LogError = Color.FromArgb(255, 110, 110);
    private static readonly Color LogMuted = Color.FromArgb(150, 150, 150);

    // Exact line height in twips (1 pt = 20 twips). Keeps JP + Latin rows uniform.
    private const int LogLineSpacingTwips = 280;

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int EmSetParaFormat = 0x0447;
    private const uint PfmLineSpacing = 0x00000100;
    private const byte LineSpacingExact = 4;

    private DeveloperSettings _developerSettings = new();

    public Form1()
    {
        InitializeComponent();
        _developerSettings = DeveloperSettings.Load();
        TopMost = _developerSettings.TopMost;
        RestoreWindowBounds();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDarkTitleBar();
        ApplyFixedLogLineSpacing();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        BeginInvoke(LoadAutoWaveAsDropped);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        WindowSettings.FromForm(this).Save();
        base.OnFormClosing(e);
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

    private void ProcessDroppedFiles(IEnumerable<string> files)
    {
        var outputs = DroppedFilesProcessor.ProcessAndGetOutputs(files, out var report);
        AppendReport(report);

        if (!_developerSettings.OpenInExternalEditor || outputs.Count == 0)
        {
            return;
        }

        BeginInvoke(() => OpenInExternalEditor(outputs));
    }

    private void OpenInExternalEditor(IReadOnlyList<string> outputPaths)
    {
        foreach (var outputPath in outputPaths)
        {
            var log = ExternalEditorLauncher.Open(outputPath, _developerSettings.ExternalEditorPath);
            if (!string.IsNullOrEmpty(log))
            {
                AppendReport(log + Environment.NewLine);
            }
        }
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
            return LogDefault;
        }

        if (t.StartsWith("[警告]", StringComparison.Ordinal)
            || t.StartsWith("=== 警告", StringComparison.Ordinal)
            || t.StartsWith("[Warning]", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("=== Warnings", StringComparison.OrdinalIgnoreCase))
        {
            return LogWarning;
        }

        if (t.StartsWith("=== エラー", StringComparison.Ordinal)
            || t.StartsWith("=== Error", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("Message :", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("外部エディタ起動スキップ", StringComparison.Ordinal)
            || t.StartsWith("外部エディタ起動失敗", StringComparison.Ordinal)
            || t.StartsWith("自動読み込み対象が見つかりません", StringComparison.Ordinal)
            || t.Contains("(なし)", StringComparison.Ordinal))
        {
            return LogError;
        }

        if (t.StartsWith("=== Write complete", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("外部エディタ起動:", StringComparison.Ordinal))
        {
            return LogSuccess;
        }

        if (t.StartsWith("===", StringComparison.Ordinal))
        {
            return LogHeader;
        }

        if (t.StartsWith("- ", StringComparison.Ordinal)
            || t.StartsWith("Dropped files:", StringComparison.OrdinalIgnoreCase))
        {
            return LogMuted;
        }

        return LogDefault;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

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
