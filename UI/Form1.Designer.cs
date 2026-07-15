namespace MgaWwiseImporter.UI;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    private WaveformView waveformView;
    private ShortcutForwardingRichTextBox editorTextBox;

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
        SuspendLayout();
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
        // Form1
        //
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        BackColor = UiColors.WindowBack;
        ClientSize = new Size(960, 640);
        Controls.Add(editorTextBox);
        Controls.Add(waveformView);
        ForeColor = UiColors.WindowFore;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(480, 320);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "MGA Wwise Importer";
        ResumeLayout(false);
    }

    #endregion
}
