namespace MgaWwiseIMImporter.UI;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    private WaveformView waveformView;
    private ShortcutForwardingRichTextBox editorTextBox;
    private Panel actionBar;
    private CheckBox topMostCheckBox;
    private RoundedButton clearButton;
    private RoundedButton exportButton;
    private WaapiStatusBar waapiStatusBar;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    #region Windows Form Designer generated code

    private void InitializeComponent()
    {
        waveformView = new WaveformView();
        editorTextBox = new ShortcutForwardingRichTextBox();
        actionBar = new Panel();
        topMostCheckBox = new CheckBox();
        clearButton = new RoundedButton();
        exportButton = new RoundedButton();
        waapiStatusBar = new WaapiStatusBar();
        SuspendLayout();
        actionBar.SuspendLayout();
        //
        // waveformView
        //
        waveformView.AllowDrop = true;
        waveformView.BackColor = UiColors.WaveformBack;
        waveformView.Dock = DockStyle.Top;
        waveformView.Name = "waveformView";
        waveformView.TabIndex = 1;
        waveformView.DragEnter += EditorTextBox_DragEnter;
        waveformView.DragDrop += EditorTextBox_DragDrop;
        //
        // editorTextBox
        //
        editorTextBox.AllowDrop = true;
        editorTextBox.BackColor = UiColors.LogBack;
        editorTextBox.BorderStyle = BorderStyle.None;
        editorTextBox.DetectUrls = false;
        editorTextBox.Dock = DockStyle.Fill;
        editorTextBox.Font = new Font("MS Gothic", 10F);
        editorTextBox.ForeColor = UiColors.LogDefault;
        editorTextBox.HideSelection = false;
        editorTextBox.Name = "editorTextBox";
        editorTextBox.ReadOnly = true;
        editorTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        editorTextBox.TabIndex = 0;
        editorTextBox.WordWrap = true;
        editorTextBox.DragEnter += EditorTextBox_DragEnter;
        editorTextBox.DragDrop += EditorTextBox_DragDrop;
        //
        // actionBar（ステータスバー直上のボタン領域）
        //
        actionBar.Dock = DockStyle.Bottom;
        actionBar.Height = 44;
        actionBar.Name = "actionBar";
        actionBar.Padding = new Padding(10, 6, 10, 6);
        actionBar.TabIndex = 2;
        actionBar.Controls.Add(topMostCheckBox);
        actionBar.Controls.Add(clearButton);
        actionBar.Controls.Add(exportButton);
        actionBar.Resize += (_, _) => LayoutActionBarControls();
        //
        // topMostCheckBox
        //
        topMostCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        topMostCheckBox.AutoSize = true;
        topMostCheckBox.Font = new Font("Yu Gothic UI", 9F);
        topMostCheckBox.Name = "topMostCheckBox";
        topMostCheckBox.TabIndex = 0;
        topMostCheckBox.Text = "最前面";
        topMostCheckBox.UseVisualStyleBackColor = true;
        topMostCheckBox.CheckedChanged += TopMostCheckBox_CheckedChanged;
        //
        // clearButton
        //
        clearButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        clearButton.Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold);
        clearButton.Name = "clearButton";
        clearButton.Size = new Size(88, 32);
        clearButton.TabIndex = 1;
        clearButton.Text = "クリア";
        clearButton.Click += ClearButton_Click;
        //
        // exportButton
        //
        exportButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        exportButton.Enabled = false;
        exportButton.Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold);
        exportButton.Name = "exportButton";
        exportButton.Size = new Size(120, 32);
        exportButton.TabIndex = 2;
        exportButton.Text = "エクスポート";
        exportButton.Click += ExportButton_Click;
        //
        // waapiStatusBar
        //
        waapiStatusBar.Name = "waapiStatusBar";
        waapiStatusBar.TabIndex = 3;
        //
        // Form1
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = UiColors.WindowBack;
        ClientSize = new Size(960, 640);
        // Dock 順: Fill → Bottom（内側→外側）→ Top（後から追加したものが外側）
        Controls.Add(editorTextBox);
        Controls.Add(actionBar);
        Controls.Add(waapiStatusBar);
        Controls.Add(waveformView);
        ForeColor = UiColors.WindowFore;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(480, 320);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "MGA Wwise IMImporter";
        actionBar.ResumeLayout(false);
        actionBar.PerformLayout();
        ResumeLayout(false);
    }

    #endregion
}
