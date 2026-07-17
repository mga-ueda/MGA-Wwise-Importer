namespace MgaWwiseIMImporter.UI;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    private WaveformView waveformView;
    private Panel logAreaPanel;
    private Panel logEditorPanel;
    private ShortcutForwardingRichTextBox editorTextBox;
    private FlowLayoutPanel logButtonPanel;
    private Button logClearButton;
    private Button logCopyButton;
    private Button logDownloadButton;
    private Panel transitionTimePanel;
    private FlowLayoutPanel transitionSettingsPanel;
    private Panel transitionTimeSeparator;
    private Panel fadeInSectionPanel;
    private Label fadeInHeaderLabel;
    private FlowLayoutPanel fadeInChoicesPanel;
    private RadioButton fadeInNoneRadio;
    private RadioButton fadeInOneSecondRadio;
    private RadioButton fadeInThreeSecondsRadio;
    private RadioButton fadeInSixSecondsRadio;
    private RadioButton fadeInNineSecondsRadio;
    private Panel fadeOutSectionPanel;
    private Label transitionTimeHeaderLabel;
    private FlowLayoutPanel transitionTimeChoicesPanel;
    private RadioButton transitionTimeHalfSecondRadio;
    private RadioButton transitionTimeOneSecondRadio;
    private RadioButton transitionTimeThreeSecondsRadio;
    private RadioButton transitionTimeSixSecondsRadio;
    private RadioButton transitionTimeNineSecondsRadio;
    private Panel exitSourceAtSectionPanel;
    private Label exitSourceAtHeaderLabel;
    private FlowLayoutPanel exitSourceAtChoicesPanel;
    private RadioButton exitSourceImmediateRadio;
    private RadioButton exitSourceNextBarRadio;
    private RadioButton exitSourceNextBeatRadio;
    private RadioButton exitSourceNextCueRadio;
    private RadioButton exitSourceExitCueRadio;
    private Panel destinationSyncSectionPanel;
    private Label destinationSyncHeaderLabel;
    private FlowLayoutPanel destinationSyncChoicesPanel;
    private RadioButton destinationSyncEntryCueRadio;
    private RadioButton destinationSyncSameTimeRadio;
    private Panel playlistSelectorPanel;
    private Panel playlistSeparator;
    private Label playlistHeaderLabel;
    private Panel playlistScrollPanel;
    private TableLayoutPanel playlistListLayout;
    private ToolTip playlistToolTip;
    private Panel actionBar;
    private CheckBox detailedLogCheckBox;
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
        components = new System.ComponentModel.Container();
        waveformView = new WaveformView();
        logAreaPanel = new Panel();
        logEditorPanel = new Panel();
        editorTextBox = new ShortcutForwardingRichTextBox();
        logButtonPanel = new FlowLayoutPanel();
        logClearButton = new Button();
        logCopyButton = new Button();
        logDownloadButton = new Button();
        transitionTimePanel = new Panel();
        transitionSettingsPanel = new FlowLayoutPanel();
        transitionTimeSeparator = new Panel();
        fadeInSectionPanel = new Panel();
        fadeInHeaderLabel = new Label();
        fadeInChoicesPanel = new FlowLayoutPanel();
        fadeInNoneRadio = new RadioButton();
        fadeInOneSecondRadio = new RadioButton();
        fadeInThreeSecondsRadio = new RadioButton();
        fadeInSixSecondsRadio = new RadioButton();
        fadeInNineSecondsRadio = new RadioButton();
        fadeOutSectionPanel = new Panel();
        transitionTimeHeaderLabel = new Label();
        transitionTimeChoicesPanel = new FlowLayoutPanel();
        transitionTimeHalfSecondRadio = new RadioButton();
        transitionTimeOneSecondRadio = new RadioButton();
        transitionTimeThreeSecondsRadio = new RadioButton();
        transitionTimeSixSecondsRadio = new RadioButton();
        transitionTimeNineSecondsRadio = new RadioButton();
        exitSourceAtSectionPanel = new Panel();
        exitSourceAtHeaderLabel = new Label();
        exitSourceAtChoicesPanel = new FlowLayoutPanel();
        exitSourceImmediateRadio = new RadioButton();
        exitSourceNextBarRadio = new RadioButton();
        exitSourceNextBeatRadio = new RadioButton();
        exitSourceNextCueRadio = new RadioButton();
        exitSourceExitCueRadio = new RadioButton();
        destinationSyncSectionPanel = new Panel();
        destinationSyncHeaderLabel = new Label();
        destinationSyncChoicesPanel = new FlowLayoutPanel();
        destinationSyncEntryCueRadio = new RadioButton();
        destinationSyncSameTimeRadio = new RadioButton();
        playlistSelectorPanel = new Panel();
        playlistSeparator = new Panel();
        playlistHeaderLabel = new Label();
        playlistScrollPanel = new Panel();
        playlistListLayout = new TableLayoutPanel();
        playlistToolTip = new ToolTip(components);
        actionBar = new Panel();
        detailedLogCheckBox = new CheckBox();
        topMostCheckBox = new CheckBox();
        clearButton = new RoundedButton();
        exportButton = new RoundedButton();
        waapiStatusBar = new WaapiStatusBar();
        SuspendLayout();
        logAreaPanel.SuspendLayout();
        logEditorPanel.SuspendLayout();
        logButtonPanel.SuspendLayout();
        transitionTimePanel.SuspendLayout();
        transitionSettingsPanel.SuspendLayout();
        fadeInSectionPanel.SuspendLayout();
        fadeInChoicesPanel.SuspendLayout();
        fadeOutSectionPanel.SuspendLayout();
        transitionTimeChoicesPanel.SuspendLayout();
        exitSourceAtSectionPanel.SuspendLayout();
        exitSourceAtChoicesPanel.SuspendLayout();
        destinationSyncSectionPanel.SuspendLayout();
        destinationSyncChoicesPanel.SuspendLayout();
        playlistSelectorPanel.SuspendLayout();
        playlistScrollPanel.SuspendLayout();
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
        // logButtonPanel
        //
        logButtonPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        logButtonPanel.BackColor = UiColors.LogBack;
        logButtonPanel.FlowDirection = FlowDirection.RightToLeft;
        logButtonPanel.Name = "logButtonPanel";
        logButtonPanel.Padding = new Padding(2, 0, 2, 2);
        logButtonPanel.Size = new Size(148, 20);
        logButtonPanel.TabIndex = 1;
        logButtonPanel.WrapContents = false;
        logButtonPanel.Controls.Add(logDownloadButton);
        logButtonPanel.Controls.Add(logCopyButton);
        logButtonPanel.Controls.Add(logClearButton);
        //
        // logClearButton
        //
        logClearButton.FlatStyle = FlatStyle.Flat;
        logClearButton.Font = new Font("Yu Gothic UI", 6F);
        logClearButton.Margin = new Padding(2, 0, 0, 1);
        logClearButton.Name = "logClearButton";
        logClearButton.Size = new Size(42, 18);
        logClearButton.TabIndex = 0;
        logClearButton.Text = "クリア";
        logClearButton.Click += LogClearButton_Click;
        //
        // logCopyButton
        //
        logCopyButton.FlatStyle = FlatStyle.Flat;
        logCopyButton.Font = new Font("Yu Gothic UI", 6F);
        logCopyButton.Margin = new Padding(2, 0, 0, 1);
        logCopyButton.Name = "logCopyButton";
        logCopyButton.Size = new Size(42, 18);
        logCopyButton.TabIndex = 1;
        logCopyButton.Text = "コピー";
        logCopyButton.Click += LogCopyButton_Click;
        //
        // logDownloadButton
        //
        logDownloadButton.FlatStyle = FlatStyle.Flat;
        logDownloadButton.Font = new Font("Yu Gothic UI", 6F);
        logDownloadButton.Margin = new Padding(2, 0, 0, 1);
        logDownloadButton.Name = "logDownloadButton";
        logDownloadButton.Size = new Size(54, 18);
        logDownloadButton.TabIndex = 2;
        logDownloadButton.Text = "ダウンロード";
        logDownloadButton.Click += LogDownloadButton_Click;
        //
        // logEditorPanel
        //
        logEditorPanel.Dock = DockStyle.Fill;
        logEditorPanel.Name = "logEditorPanel";
        logEditorPanel.TabIndex = 0;
        logEditorPanel.Controls.Add(editorTextBox);
        logEditorPanel.Controls.Add(logButtonPanel);
        //
        // logAreaPanel
        //
        logAreaPanel.Dock = DockStyle.Fill;
        logAreaPanel.Name = "logAreaPanel";
        logAreaPanel.TabIndex = 0;
        logAreaPanel.Controls.Add(logEditorPanel);
        logAreaPanel.Controls.Add(transitionTimePanel);
        logAreaPanel.Controls.Add(playlistSelectorPanel);
        //
        // transitionTimePanel
        //
        transitionTimePanel.AllowDrop = true;
        transitionTimePanel.Dock = DockStyle.Right;
        transitionTimePanel.Name = "transitionTimePanel";
        transitionTimePanel.Size = new Size(217, 100);
        transitionTimePanel.TabIndex = 1;
        transitionTimePanel.Controls.Add(transitionSettingsPanel);
        transitionTimePanel.Controls.Add(transitionTimeSeparator);
        transitionTimePanel.DragEnter += EditorTextBox_DragEnter;
        transitionTimePanel.DragDrop += EditorTextBox_DragDrop;
        //
        // transitionSettingsPanel
        //
        transitionSettingsPanel.AutoScroll = true;
        transitionSettingsPanel.Dock = DockStyle.Fill;
        transitionSettingsPanel.FlowDirection = FlowDirection.LeftToRight;
        transitionSettingsPanel.Name = "transitionSettingsPanel";
        transitionSettingsPanel.Padding = new Padding(1, 0, 0, 0);
        transitionSettingsPanel.TabIndex = 0;
        transitionSettingsPanel.WrapContents = true;
        transitionSettingsPanel.Controls.Add(fadeInSectionPanel);
        transitionSettingsPanel.Controls.Add(fadeOutSectionPanel);
        transitionSettingsPanel.Controls.Add(exitSourceAtSectionPanel);
        transitionSettingsPanel.Controls.Add(destinationSyncSectionPanel);
        transitionSettingsPanel.SetFlowBreak(fadeOutSectionPanel, true);
        //
        // transitionTimeSeparator
        //
        transitionTimeSeparator.Dock = DockStyle.Left;
        transitionTimeSeparator.Name = "transitionTimeSeparator";
        transitionTimeSeparator.Size = new Size(1, 100);
        transitionTimeSeparator.TabStop = false;
        //
        // fadeInSectionPanel
        //
        fadeInSectionPanel.Margin = new Padding(0);
        fadeInSectionPanel.Name = "fadeInSectionPanel";
        fadeInSectionPanel.Size = new Size(108, 153);
        fadeInSectionPanel.TabIndex = 0;
        fadeInSectionPanel.Controls.Add(fadeInChoicesPanel);
        fadeInSectionPanel.Controls.Add(fadeInHeaderLabel);
        //
        // fadeInHeaderLabel
        //
        fadeInHeaderLabel.Font = new Font("Yu Gothic UI", 8.5F, FontStyle.Bold);
        fadeInHeaderLabel.Dock = DockStyle.Top;
        fadeInHeaderLabel.Margin = new Padding(0);
        fadeInHeaderLabel.Name = "fadeInHeaderLabel";
        fadeInHeaderLabel.Padding = new Padding(10, 0, 4, 0);
        fadeInHeaderLabel.Size = new Size(108, 26);
        fadeInHeaderLabel.TabIndex = 0;
        fadeInHeaderLabel.Text = "Fade In";
        fadeInHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // fadeInChoicesPanel
        //
        fadeInChoicesPanel.Dock = DockStyle.Fill;
        fadeInChoicesPanel.FlowDirection = FlowDirection.TopDown;
        fadeInChoicesPanel.Margin = new Padding(0);
        fadeInChoicesPanel.Name = "fadeInChoicesPanel";
        fadeInChoicesPanel.Padding = new Padding(9, 0, 4, 4);
        fadeInChoicesPanel.Size = new Size(108, 127);
        fadeInChoicesPanel.TabIndex = 1;
        fadeInChoicesPanel.WrapContents = false;
        fadeInChoicesPanel.Controls.Add(fadeInNoneRadio);
        fadeInChoicesPanel.Controls.Add(fadeInOneSecondRadio);
        fadeInChoicesPanel.Controls.Add(fadeInThreeSecondsRadio);
        fadeInChoicesPanel.Controls.Add(fadeInSixSecondsRadio);
        fadeInChoicesPanel.Controls.Add(fadeInNineSecondsRadio);
        //
        // fadeInNoneRadio
        //
        fadeInNoneRadio.AutoSize = true;
        fadeInNoneRadio.Checked = true;
        fadeInNoneRadio.Font = new Font("Yu Gothic UI", 8.5F);
        fadeInNoneRadio.Margin = new Padding(3, 3, 3, 2);
        fadeInNoneRadio.Name = "fadeInNoneRadio";
        fadeInNoneRadio.TabIndex = 0;
        fadeInNoneRadio.Tag = 0D;
        fadeInNoneRadio.Text = "None";
        fadeInNoneRadio.CheckedChanged += FadeInTimeRadio_CheckedChanged;
        //
        // fadeInOneSecondRadio
        //
        fadeInOneSecondRadio.AutoSize = true;
        fadeInOneSecondRadio.Font = new Font("Yu Gothic UI", 8.5F);
        fadeInOneSecondRadio.Margin = new Padding(3, 3, 3, 2);
        fadeInOneSecondRadio.Name = "fadeInOneSecondRadio";
        fadeInOneSecondRadio.TabIndex = 1;
        fadeInOneSecondRadio.Tag = 1D;
        fadeInOneSecondRadio.Text = "1.0 Sec.";
        fadeInOneSecondRadio.CheckedChanged += FadeInTimeRadio_CheckedChanged;
        //
        // fadeInThreeSecondsRadio
        //
        fadeInThreeSecondsRadio.AutoSize = true;
        fadeInThreeSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        fadeInThreeSecondsRadio.Margin = new Padding(3, 3, 3, 2);
        fadeInThreeSecondsRadio.Name = "fadeInThreeSecondsRadio";
        fadeInThreeSecondsRadio.TabIndex = 2;
        fadeInThreeSecondsRadio.Tag = 3D;
        fadeInThreeSecondsRadio.Text = "3.0 Sec.";
        fadeInThreeSecondsRadio.CheckedChanged += FadeInTimeRadio_CheckedChanged;
        //
        // fadeInSixSecondsRadio
        //
        fadeInSixSecondsRadio.AutoSize = true;
        fadeInSixSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        fadeInSixSecondsRadio.Margin = new Padding(3, 3, 3, 2);
        fadeInSixSecondsRadio.Name = "fadeInSixSecondsRadio";
        fadeInSixSecondsRadio.TabIndex = 3;
        fadeInSixSecondsRadio.Tag = 6D;
        fadeInSixSecondsRadio.Text = "6.0 Sec.";
        fadeInSixSecondsRadio.CheckedChanged += FadeInTimeRadio_CheckedChanged;
        //
        // fadeInNineSecondsRadio
        //
        fadeInNineSecondsRadio.AutoSize = true;
        fadeInNineSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        fadeInNineSecondsRadio.Margin = new Padding(3, 3, 3, 2);
        fadeInNineSecondsRadio.Name = "fadeInNineSecondsRadio";
        fadeInNineSecondsRadio.TabIndex = 4;
        fadeInNineSecondsRadio.Tag = 9D;
        fadeInNineSecondsRadio.Text = "9.0 Sec.";
        fadeInNineSecondsRadio.CheckedChanged += FadeInTimeRadio_CheckedChanged;
        //
        // fadeOutSectionPanel
        //
        fadeOutSectionPanel.Margin = new Padding(0);
        fadeOutSectionPanel.Name = "fadeOutSectionPanel";
        fadeOutSectionPanel.Size = new Size(108, 153);
        fadeOutSectionPanel.TabIndex = 1;
        fadeOutSectionPanel.Controls.Add(transitionTimeChoicesPanel);
        fadeOutSectionPanel.Controls.Add(transitionTimeHeaderLabel);
        //
        // transitionTimeHeaderLabel
        //
        transitionTimeHeaderLabel.Font = new Font("Yu Gothic UI", 8.5F, FontStyle.Bold);
        transitionTimeHeaderLabel.Dock = DockStyle.Top;
        transitionTimeHeaderLabel.Margin = new Padding(0);
        transitionTimeHeaderLabel.Name = "transitionTimeHeaderLabel";
        transitionTimeHeaderLabel.Padding = new Padding(10, 0, 4, 0);
        transitionTimeHeaderLabel.Size = new Size(108, 26);
        transitionTimeHeaderLabel.TabIndex = 0;
        transitionTimeHeaderLabel.Text = "Fade Out";
        transitionTimeHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // transitionTimeChoicesPanel
        //
        transitionTimeChoicesPanel.Dock = DockStyle.Fill;
        transitionTimeChoicesPanel.FlowDirection = FlowDirection.TopDown;
        transitionTimeChoicesPanel.Margin = new Padding(0);
        transitionTimeChoicesPanel.Name = "transitionTimeChoicesPanel";
        transitionTimeChoicesPanel.Padding = new Padding(9, 0, 4, 4);
        transitionTimeChoicesPanel.Size = new Size(108, 127);
        transitionTimeChoicesPanel.TabIndex = 1;
        transitionTimeChoicesPanel.WrapContents = false;
        transitionTimeChoicesPanel.Controls.Add(transitionTimeHalfSecondRadio);
        transitionTimeChoicesPanel.Controls.Add(transitionTimeOneSecondRadio);
        transitionTimeChoicesPanel.Controls.Add(transitionTimeThreeSecondsRadio);
        transitionTimeChoicesPanel.Controls.Add(transitionTimeSixSecondsRadio);
        transitionTimeChoicesPanel.Controls.Add(transitionTimeNineSecondsRadio);
        //
        // transitionTimeHalfSecondRadio
        //
        transitionTimeHalfSecondRadio.AutoSize = true;
        transitionTimeHalfSecondRadio.Checked = true;
        transitionTimeHalfSecondRadio.Font = new Font("Yu Gothic UI", 8.5F);
        transitionTimeHalfSecondRadio.Margin = new Padding(3, 3, 3, 2);
        transitionTimeHalfSecondRadio.Name = "transitionTimeHalfSecondRadio";
        transitionTimeHalfSecondRadio.TabIndex = 0;
        transitionTimeHalfSecondRadio.Tag = 0.5D;
        transitionTimeHalfSecondRadio.Text = "0.5 Sec.";
        transitionTimeHalfSecondRadio.CheckedChanged += TransitionTimeRadio_CheckedChanged;
        //
        // transitionTimeOneSecondRadio
        //
        transitionTimeOneSecondRadio.AutoSize = true;
        transitionTimeOneSecondRadio.Font = new Font("Yu Gothic UI", 8.5F);
        transitionTimeOneSecondRadio.Margin = new Padding(3, 3, 3, 2);
        transitionTimeOneSecondRadio.Name = "transitionTimeOneSecondRadio";
        transitionTimeOneSecondRadio.TabIndex = 1;
        transitionTimeOneSecondRadio.Tag = 1D;
        transitionTimeOneSecondRadio.Text = "1.0 Sec.";
        transitionTimeOneSecondRadio.CheckedChanged += TransitionTimeRadio_CheckedChanged;
        //
        // transitionTimeThreeSecondsRadio
        //
        transitionTimeThreeSecondsRadio.AutoSize = true;
        transitionTimeThreeSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        transitionTimeThreeSecondsRadio.Margin = new Padding(3, 3, 3, 2);
        transitionTimeThreeSecondsRadio.Name = "transitionTimeThreeSecondsRadio";
        transitionTimeThreeSecondsRadio.TabIndex = 2;
        transitionTimeThreeSecondsRadio.Tag = 3D;
        transitionTimeThreeSecondsRadio.Text = "3.0 Sec.";
        transitionTimeThreeSecondsRadio.CheckedChanged += TransitionTimeRadio_CheckedChanged;
        //
        // transitionTimeSixSecondsRadio
        //
        transitionTimeSixSecondsRadio.AutoSize = true;
        transitionTimeSixSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        transitionTimeSixSecondsRadio.Margin = new Padding(3, 3, 3, 2);
        transitionTimeSixSecondsRadio.Name = "transitionTimeSixSecondsRadio";
        transitionTimeSixSecondsRadio.TabIndex = 3;
        transitionTimeSixSecondsRadio.Tag = 6D;
        transitionTimeSixSecondsRadio.Text = "6.0 Sec.";
        transitionTimeSixSecondsRadio.CheckedChanged += TransitionTimeRadio_CheckedChanged;
        //
        // transitionTimeNineSecondsRadio
        //
        transitionTimeNineSecondsRadio.AutoSize = true;
        transitionTimeNineSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        transitionTimeNineSecondsRadio.Margin = new Padding(3, 3, 3, 2);
        transitionTimeNineSecondsRadio.Name = "transitionTimeNineSecondsRadio";
        transitionTimeNineSecondsRadio.TabIndex = 4;
        transitionTimeNineSecondsRadio.Tag = 9D;
        transitionTimeNineSecondsRadio.Text = "9.0 Sec.";
        transitionTimeNineSecondsRadio.CheckedChanged += TransitionTimeRadio_CheckedChanged;
        //
        // exitSourceAtSectionPanel
        //
        exitSourceAtSectionPanel.Margin = new Padding(0);
        exitSourceAtSectionPanel.Name = "exitSourceAtSectionPanel";
        exitSourceAtSectionPanel.Size = new Size(108, 155);
        exitSourceAtSectionPanel.TabIndex = 2;
        exitSourceAtSectionPanel.Controls.Add(exitSourceAtChoicesPanel);
        exitSourceAtSectionPanel.Controls.Add(exitSourceAtHeaderLabel);
        //
        // exitSourceAtHeaderLabel
        //
        exitSourceAtHeaderLabel.Font = new Font("Yu Gothic UI", 8.5F, FontStyle.Bold);
        exitSourceAtHeaderLabel.Dock = DockStyle.Top;
        exitSourceAtHeaderLabel.Margin = new Padding(0);
        exitSourceAtHeaderLabel.Name = "exitSourceAtHeaderLabel";
        exitSourceAtHeaderLabel.Padding = new Padding(10, 0, 4, 0);
        exitSourceAtHeaderLabel.Size = new Size(108, 26);
        exitSourceAtHeaderLabel.TabIndex = 2;
        exitSourceAtHeaderLabel.Text = "Exit Source At";
        exitSourceAtHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // exitSourceAtChoicesPanel
        //
        exitSourceAtChoicesPanel.Dock = DockStyle.Fill;
        exitSourceAtChoicesPanel.FlowDirection = FlowDirection.TopDown;
        exitSourceAtChoicesPanel.Margin = new Padding(0);
        exitSourceAtChoicesPanel.Name = "exitSourceAtChoicesPanel";
        exitSourceAtChoicesPanel.Padding = new Padding(9, 0, 4, 4);
        exitSourceAtChoicesPanel.Size = new Size(108, 129);
        exitSourceAtChoicesPanel.TabIndex = 3;
        exitSourceAtChoicesPanel.WrapContents = false;
        exitSourceAtChoicesPanel.Controls.Add(exitSourceImmediateRadio);
        exitSourceAtChoicesPanel.Controls.Add(exitSourceNextBarRadio);
        exitSourceAtChoicesPanel.Controls.Add(exitSourceNextBeatRadio);
        exitSourceAtChoicesPanel.Controls.Add(exitSourceNextCueRadio);
        exitSourceAtChoicesPanel.Controls.Add(exitSourceExitCueRadio);
        //
        // exitSourceImmediateRadio
        //
        exitSourceImmediateRadio.AutoSize = true;
        exitSourceImmediateRadio.Font = new Font("Yu Gothic UI", 8.5F);
        exitSourceImmediateRadio.Margin = new Padding(3, 3, 3, 2);
        exitSourceImmediateRadio.Name = "exitSourceImmediateRadio";
        exitSourceImmediateRadio.TabIndex = 0;
        exitSourceImmediateRadio.Tag = PlaylistExitSourceMode.Immediate;
        exitSourceImmediateRadio.Text = "Immediate";
        exitSourceImmediateRadio.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
        //
        // exitSourceNextBarRadio
        //
        exitSourceNextBarRadio.AutoSize = true;
        exitSourceNextBarRadio.Checked = true;
        exitSourceNextBarRadio.Font = new Font("Yu Gothic UI", 8.5F);
        exitSourceNextBarRadio.Margin = new Padding(3, 3, 3, 2);
        exitSourceNextBarRadio.Name = "exitSourceNextBarRadio";
        exitSourceNextBarRadio.TabIndex = 1;
        exitSourceNextBarRadio.Tag = PlaylistExitSourceMode.NextBar;
        exitSourceNextBarRadio.Text = "Next Bar";
        exitSourceNextBarRadio.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
        //
        // exitSourceNextBeatRadio
        //
        exitSourceNextBeatRadio.AutoSize = true;
        exitSourceNextBeatRadio.Font = new Font("Yu Gothic UI", 8.5F);
        exitSourceNextBeatRadio.Margin = new Padding(3, 3, 3, 2);
        exitSourceNextBeatRadio.Name = "exitSourceNextBeatRadio";
        exitSourceNextBeatRadio.TabIndex = 2;
        exitSourceNextBeatRadio.Tag = PlaylistExitSourceMode.NextBeat;
        exitSourceNextBeatRadio.Text = "Next Beat";
        exitSourceNextBeatRadio.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
        //
        // exitSourceNextCueRadio
        //
        exitSourceNextCueRadio.AutoSize = true;
        exitSourceNextCueRadio.Font = new Font("Yu Gothic UI", 8.5F);
        exitSourceNextCueRadio.Margin = new Padding(3, 3, 3, 2);
        exitSourceNextCueRadio.Name = "exitSourceNextCueRadio";
        exitSourceNextCueRadio.TabIndex = 3;
        exitSourceNextCueRadio.Tag = PlaylistExitSourceMode.NextCue;
        exitSourceNextCueRadio.Text = "Next Cue";
        exitSourceNextCueRadio.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
        //
        // exitSourceExitCueRadio
        //
        exitSourceExitCueRadio.AutoSize = true;
        exitSourceExitCueRadio.Font = new Font("Yu Gothic UI", 8.5F);
        exitSourceExitCueRadio.Margin = new Padding(3, 3, 3, 2);
        exitSourceExitCueRadio.Name = "exitSourceExitCueRadio";
        exitSourceExitCueRadio.TabIndex = 4;
        exitSourceExitCueRadio.Tag = PlaylistExitSourceMode.ExitCue;
        exitSourceExitCueRadio.Text = "Exit Cue";
        exitSourceExitCueRadio.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
        //
        // destinationSyncSectionPanel
        //
        destinationSyncSectionPanel.Margin = new Padding(0);
        destinationSyncSectionPanel.Name = "destinationSyncSectionPanel";
        destinationSyncSectionPanel.Size = new Size(108, 80);
        destinationSyncSectionPanel.TabIndex = 3;
        destinationSyncSectionPanel.Controls.Add(destinationSyncChoicesPanel);
        destinationSyncSectionPanel.Controls.Add(destinationSyncHeaderLabel);
        //
        // destinationSyncHeaderLabel
        //
        destinationSyncHeaderLabel.Font = new Font("Yu Gothic UI", 8.5F, FontStyle.Bold);
        destinationSyncHeaderLabel.Dock = DockStyle.Top;
        destinationSyncHeaderLabel.Margin = new Padding(0);
        destinationSyncHeaderLabel.Name = "destinationSyncHeaderLabel";
        destinationSyncHeaderLabel.Padding = new Padding(10, 0, 4, 0);
        destinationSyncHeaderLabel.Size = new Size(108, 26);
        destinationSyncHeaderLabel.TabIndex = 4;
        destinationSyncHeaderLabel.Text = "Dest. Sync To";
        destinationSyncHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        //
        // destinationSyncChoicesPanel
        //
        destinationSyncChoicesPanel.Dock = DockStyle.Fill;
        destinationSyncChoicesPanel.FlowDirection = FlowDirection.TopDown;
        destinationSyncChoicesPanel.Margin = new Padding(0);
        destinationSyncChoicesPanel.Name = "destinationSyncChoicesPanel";
        destinationSyncChoicesPanel.Padding = new Padding(9, 0, 4, 4);
        destinationSyncChoicesPanel.Size = new Size(108, 54);
        destinationSyncChoicesPanel.TabIndex = 5;
        destinationSyncChoicesPanel.WrapContents = false;
        destinationSyncChoicesPanel.Controls.Add(destinationSyncEntryCueRadio);
        destinationSyncChoicesPanel.Controls.Add(destinationSyncSameTimeRadio);
        //
        // destinationSyncEntryCueRadio
        //
        destinationSyncEntryCueRadio.AutoSize = true;
        destinationSyncEntryCueRadio.Checked = true;
        destinationSyncEntryCueRadio.Font = new Font("Yu Gothic UI", 8.5F);
        destinationSyncEntryCueRadio.Margin = new Padding(3, 3, 3, 2);
        destinationSyncEntryCueRadio.Name = "destinationSyncEntryCueRadio";
        destinationSyncEntryCueRadio.TabIndex = 0;
        destinationSyncEntryCueRadio.Tag = PlaylistDestinationSyncMode.EntryCue;
        destinationSyncEntryCueRadio.Text = "Entry Cue";
        destinationSyncEntryCueRadio.CheckedChanged += DestinationSyncRadio_CheckedChanged;
        //
        // destinationSyncSameTimeRadio
        //
        destinationSyncSameTimeRadio.AutoSize = true;
        destinationSyncSameTimeRadio.Font = new Font("Yu Gothic UI", 8.5F);
        destinationSyncSameTimeRadio.Margin = new Padding(3, 3, 3, 2);
        destinationSyncSameTimeRadio.Name = "destinationSyncSameTimeRadio";
        destinationSyncSameTimeRadio.TabIndex = 1;
        destinationSyncSameTimeRadio.Tag = PlaylistDestinationSyncMode.SameTime;
        destinationSyncSameTimeRadio.Text = "Same Time";
        destinationSyncSameTimeRadio.CheckedChanged += DestinationSyncRadio_CheckedChanged;
        //
        // playlistSelectorPanel
        //
        playlistSelectorPanel.Dock = DockStyle.Right;
        playlistSelectorPanel.AllowDrop = true;
        playlistSelectorPanel.Name = "playlistSelectorPanel";
        playlistSelectorPanel.Size = new Size(240, 100);
        playlistSelectorPanel.TabIndex = 1;
        playlistSelectorPanel.Controls.Add(playlistScrollPanel);
        playlistSelectorPanel.Controls.Add(playlistHeaderLabel);
        playlistSelectorPanel.Controls.Add(playlistSeparator);
        playlistSelectorPanel.DragEnter += EditorTextBox_DragEnter;
        playlistSelectorPanel.DragDrop += EditorTextBox_DragDrop;
        //
        // playlistSeparator
        //
        playlistSeparator.Dock = DockStyle.Left;
        playlistSeparator.Name = "playlistSeparator";
        playlistSeparator.Size = new Size(0, 100);
        playlistSeparator.TabStop = false;
        //
        // playlistHeaderLabel
        //
        playlistHeaderLabel.Dock = DockStyle.Top;
        playlistHeaderLabel.AllowDrop = true;
        playlistHeaderLabel.Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold);
        playlistHeaderLabel.Name = "playlistHeaderLabel";
        playlistHeaderLabel.Padding = new Padding(12, 0, 8, 0);
        playlistHeaderLabel.Size = new Size(239, 26);
        playlistHeaderLabel.TabIndex = 0;
        playlistHeaderLabel.Text = "Music Playlist";
        playlistHeaderLabel.TextAlign = ContentAlignment.MiddleLeft;
        playlistHeaderLabel.DragEnter += EditorTextBox_DragEnter;
        playlistHeaderLabel.DragDrop += EditorTextBox_DragDrop;
        //
        // playlistScrollPanel
        //
        playlistScrollPanel.AutoScroll = true;
        playlistScrollPanel.AllowDrop = true;
        playlistScrollPanel.Dock = DockStyle.Fill;
        playlistScrollPanel.Name = "playlistScrollPanel";
        playlistScrollPanel.Padding = new Padding(9, 0, 8, 8);
        playlistScrollPanel.TabIndex = 1;
        playlistScrollPanel.Controls.Add(playlistListLayout);
        playlistScrollPanel.DragEnter += EditorTextBox_DragEnter;
        playlistScrollPanel.DragDrop += EditorTextBox_DragDrop;
        //
        // playlistListLayout
        //
        playlistListLayout.AutoSize = true;
        playlistListLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        playlistListLayout.AllowDrop = true;
        playlistListLayout.ColumnCount = 1;
        playlistListLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        playlistListLayout.Dock = DockStyle.Top;
        playlistListLayout.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
        playlistListLayout.Name = "playlistListLayout";
        playlistListLayout.RowCount = 0;
        playlistListLayout.TabIndex = 0;
        playlistListLayout.DragEnter += EditorTextBox_DragEnter;
        playlistListLayout.DragDrop += EditorTextBox_DragDrop;
        //
        // actionBar（ステータスバー直上のボタン領域）
        //
        actionBar.Dock = DockStyle.Bottom;
        actionBar.Height = 44;
        actionBar.Name = "actionBar";
        actionBar.Padding = new Padding(10, 6, 10, 6);
        actionBar.TabIndex = 2;
        actionBar.Controls.Add(detailedLogCheckBox);
        actionBar.Controls.Add(topMostCheckBox);
        actionBar.Controls.Add(clearButton);
        actionBar.Controls.Add(exportButton);
        actionBar.Resize += (_, _) => LayoutActionBarControls();
        //
        // detailedLogCheckBox
        //
        detailedLogCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        detailedLogCheckBox.AutoSize = true;
        detailedLogCheckBox.Checked = true;
        detailedLogCheckBox.CheckState = CheckState.Checked;
        detailedLogCheckBox.Font = new Font("Yu Gothic UI", 9F);
        detailedLogCheckBox.Name = "detailedLogCheckBox";
        detailedLogCheckBox.TabIndex = 0;
        detailedLogCheckBox.Text = "詳細ログ";
        detailedLogCheckBox.UseVisualStyleBackColor = true;
        detailedLogCheckBox.CheckedChanged += DetailedLogCheckBox_CheckedChanged;
        //
        // topMostCheckBox
        //
        topMostCheckBox.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        topMostCheckBox.AutoSize = true;
        topMostCheckBox.Font = new Font("Yu Gothic UI", 9F);
        topMostCheckBox.Name = "topMostCheckBox";
        topMostCheckBox.TabIndex = 1;
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
        clearButton.TabIndex = 2;
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
        exportButton.TabIndex = 3;
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
        Controls.Add(logAreaPanel);
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
        logButtonPanel.ResumeLayout(false);
        destinationSyncChoicesPanel.ResumeLayout(false);
        destinationSyncChoicesPanel.PerformLayout();
        destinationSyncSectionPanel.ResumeLayout(false);
        exitSourceAtChoicesPanel.ResumeLayout(false);
        exitSourceAtChoicesPanel.PerformLayout();
        exitSourceAtSectionPanel.ResumeLayout(false);
        transitionTimeChoicesPanel.ResumeLayout(false);
        transitionTimeChoicesPanel.PerformLayout();
        fadeOutSectionPanel.ResumeLayout(false);
        fadeInChoicesPanel.ResumeLayout(false);
        fadeInChoicesPanel.PerformLayout();
        fadeInSectionPanel.ResumeLayout(false);
        transitionSettingsPanel.ResumeLayout(false);
        transitionSettingsPanel.PerformLayout();
        transitionTimePanel.ResumeLayout(false);
        logEditorPanel.ResumeLayout(false);
        playlistScrollPanel.ResumeLayout(false);
        playlistScrollPanel.PerformLayout();
        playlistSelectorPanel.ResumeLayout(false);
        logAreaPanel.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion
}
