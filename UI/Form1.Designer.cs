namespace MgaWwiseImporter.UI;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    private RichTextBox editorTextBox;

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
        editorTextBox = new RichTextBox();
        SuspendLayout();
        //
        // editorTextBox
        //
        editorTextBox.AllowDrop = true;
        editorTextBox.BackColor = Color.FromArgb(30, 30, 30);
        editorTextBox.BorderStyle = BorderStyle.None;
        editorTextBox.DetectUrls = false;
        editorTextBox.Dock = DockStyle.Fill;
        editorTextBox.Font = new Font("MS Gothic", 10F);
        editorTextBox.ForeColor = Color.FromArgb(220, 220, 220);
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
        BackColor = Color.FromArgb(30, 30, 30);
        ClientSize = new Size(960, 640);
        Controls.Add(editorTextBox);
        ForeColor = Color.White;
        MaximizeBox = false;
        MinimizeBox = true;
        MinimumSize = new Size(480, 320);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "MGA Wwise Importer";
        ResumeLayout(false);
    }

    #endregion
}
