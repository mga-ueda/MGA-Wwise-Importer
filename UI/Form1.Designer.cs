namespace MgaWwiseIMImporter.UI;

partial class Form1
{
    private System.ComponentModel.IContainer components = null;
    private Panel waveformHostPanel;
    private WaveformView waveformView;
    private ThinHorizontalScrollBar waveformHorizontalScrollBar;
    private TransportBar transportBar;
    private Panel logAreaPanel;
    private Panel logEditorPanel;
    private ShortcutForwardingRichTextBox editorTextBox;
    private FlowLayoutPanel logButtonPanel;
    private TransportIconButton logClearButton;
    private TransportIconButton logCopyButton;
    private TransportIconButton logDownloadButton;
    private Panel transitionTimePanel;
    private FlowLayoutPanel transitionSettingsPanel;
    private Panel transitionTimeSeparator;
    private Panel fadeInSectionPanel;
    private SectionHeaderLabel fadeInHeaderLabel;
    private FlowLayoutPanel fadeInChoicesPanel;
    private FlatOptionRadioButton fadeInNoneRadio;
    private FlatOptionRadioButton fadeInOneSecondRadio;
    private FlatOptionRadioButton fadeInThreeSecondsRadio;
    private FlatOptionRadioButton fadeInSixSecondsRadio;
    private FlatOptionRadioButton fadeInNineSecondsRadio;
    private Panel fadeOutSectionPanel;
    private SectionHeaderLabel transitionTimeHeaderLabel;
    private FlowLayoutPanel transitionTimeChoicesPanel;
    private FlatOptionRadioButton transitionTimeHalfSecondRadio;
    private FlatOptionRadioButton transitionTimeOneSecondRadio;
    private FlatOptionRadioButton transitionTimeThreeSecondsRadio;
    private FlatOptionRadioButton transitionTimeSixSecondsRadio;
    private FlatOptionRadioButton transitionTimeNineSecondsRadio;
    private Panel exitSourceAtSectionPanel;
    private SectionHeaderLabel exitSourceAtHeaderLabel;
    private FlowLayoutPanel exitSourceAtChoicesPanel;
    private FlatOptionRadioButton exitSourceImmediateRadio;
    private FlatOptionRadioButton exitSourceNextBarRadio;
    private FlatOptionRadioButton exitSourceNextBeatRadio;
    private FlatOptionRadioButton exitSourceNextCueRadio;
    private FlatOptionRadioButton exitSourceExitCueRadio;
    private Panel rightSidePanel;
    private MarkerOptionsPanel markerOptionsPanel;
    private Panel playlistSelectorPanel;
    private Panel playlistSeparator;
    private SectionHeaderLabel playlistHeaderLabel;
    private Panel playlistScrollPanel;
    private TableLayoutPanel playlistListLayout;
    private ToolTip playlistToolTip;
    private Panel actionBar;
    private PictureBox brandLogoPictureBox;
    private LinkLabel copyrightLinkLabel;
    private FlowLayoutPanel actionControlsPanel;
    private FlatOptionCheckBox detailedLogCheckBox;
    private FlatOptionCheckBox compactFileNumbersCheckBox;
    private FlatOptionCheckBox topMostCheckBox;
    private RoundedButton reloadButton;
    private RoundedButton exportButton;
    private WaapiStatusBar waapiStatusBar;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            brandLogoPictureBox?.Image?.Dispose();
        }

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
        waveformHostPanel = new Panel();
        waveformView = new WaveformView();
        waveformHorizontalScrollBar = new ThinHorizontalScrollBar();
        transportBar = new TransportBar();
        logAreaPanel = new Panel();
        logEditorPanel = new Panel();
        editorTextBox = new ShortcutForwardingRichTextBox();
        logButtonPanel = new FlowLayoutPanel();
        logClearButton = new TransportIconButton(TransportIcon.Clear);
        logCopyButton = new TransportIconButton(TransportIcon.Copy);
        logDownloadButton = new TransportIconButton(TransportIcon.Download);
        transitionTimePanel = new Panel();
        transitionSettingsPanel = new FlowLayoutPanel();
        transitionTimeSeparator = new Panel();
        fadeInSectionPanel = new Panel();
        fadeInHeaderLabel = new SectionHeaderLabel();
        fadeInChoicesPanel = new FlowLayoutPanel();
        fadeInNoneRadio = new FlatOptionRadioButton();
        fadeInOneSecondRadio = new FlatOptionRadioButton();
        fadeInThreeSecondsRadio = new FlatOptionRadioButton();
        fadeInSixSecondsRadio = new FlatOptionRadioButton();
        fadeInNineSecondsRadio = new FlatOptionRadioButton();
        fadeOutSectionPanel = new Panel();
        transitionTimeHeaderLabel = new SectionHeaderLabel();
        transitionTimeChoicesPanel = new FlowLayoutPanel();
        transitionTimeHalfSecondRadio = new FlatOptionRadioButton();
        transitionTimeOneSecondRadio = new FlatOptionRadioButton();
        transitionTimeThreeSecondsRadio = new FlatOptionRadioButton();
        transitionTimeSixSecondsRadio = new FlatOptionRadioButton();
        transitionTimeNineSecondsRadio = new FlatOptionRadioButton();
        exitSourceAtSectionPanel = new Panel();
        exitSourceAtHeaderLabel = new SectionHeaderLabel();
        exitSourceAtChoicesPanel = new FlowLayoutPanel();
        exitSourceImmediateRadio = new FlatOptionRadioButton();
        exitSourceNextBarRadio = new FlatOptionRadioButton();
        exitSourceNextBeatRadio = new FlatOptionRadioButton();
        exitSourceNextCueRadio = new FlatOptionRadioButton();
        exitSourceExitCueRadio = new FlatOptionRadioButton();
        rightSidePanel = new Panel();
        markerOptionsPanel = new MarkerOptionsPanel();
        playlistSelectorPanel = new Panel();
        playlistSeparator = new Panel();
        playlistHeaderLabel = new SectionHeaderLabel();
        playlistScrollPanel = new Panel();
        playlistListLayout = new TableLayoutPanel();
        playlistToolTip = new DarkToolTip(components);
        actionBar = new Panel();
        brandLogoPictureBox = new PictureBox();
        copyrightLinkLabel = new LinkLabel();
        actionControlsPanel = new FlowLayoutPanel();
        detailedLogCheckBox = new FlatOptionCheckBox();
        compactFileNumbersCheckBox = new FlatOptionCheckBox();
        topMostCheckBox = new FlatOptionCheckBox();
        reloadButton = new RoundedButton();
        exportButton = new RoundedButton();
        waapiStatusBar = new WaapiStatusBar();
        SuspendLayout();
        waveformHostPanel.SuspendLayout();
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
        rightSidePanel.SuspendLayout();
        playlistSelectorPanel.SuspendLayout();
        playlistScrollPanel.SuspendLayout();
        actionBar.SuspendLayout();
        actionControlsPanel.SuspendLayout();
        ((System.ComponentModel.ISupportInitialize)brandLogoPictureBox).BeginInit();
        //
        // waveformHostPanel
        //
        waveformHostPanel.AllowDrop = true;
        waveformHostPanel.BackColor = UiColors.WaveformScrollTrack;
        waveformHostPanel.Controls.Add(waveformView);
        waveformHostPanel.Controls.Add(waveformHorizontalScrollBar);
        waveformHostPanel.Dock = DockStyle.Top;
        waveformHostPanel.Height = 220;
        waveformHostPanel.Name = "waveformHostPanel";
        waveformHostPanel.TabIndex = 1;
        waveformHostPanel.DragEnter += EditorTextBox_DragEnter;
        waveformHostPanel.DragDrop += EditorTextBox_DragDrop;
        //
        // waveformView
        //
        waveformView.AllowDrop = true;
        waveformView.BackColor = UiColors.WaveformBack;
        waveformView.Dock = DockStyle.Fill;
        waveformView.Name = "waveformView";
        waveformView.TabIndex = 1;
        waveformView.DragEnter += EditorTextBox_DragEnter;
        waveformView.DragDrop += EditorTextBox_DragDrop;
        //
        // waveformHorizontalScrollBar
        //
        waveformHorizontalScrollBar.Dock = DockStyle.Bottom;
        waveformHorizontalScrollBar.Height = 15;
        waveformHorizontalScrollBar.Name = "waveformHorizontalScrollBar";
        waveformHorizontalScrollBar.TabIndex = 2;
        //
        // transportBar
        //
        transportBar.Dock = DockStyle.Top;
        transportBar.Name = "transportBar";
        transportBar.TabIndex = 2;
        transportBar.CommandInvoked += TransportBar_CommandInvoked;
        //
        // editorTextBox
        //
        editorTextBox.AllowDrop = true;
        editorTextBox.BackColor = UiColors.LogBack;
        editorTextBox.BorderStyle = BorderStyle.None;
        editorTextBox.DetectUrls = false;
        editorTextBox.Dock = DockStyle.Fill;
        editorTextBox.Font = AppFonts.CreateLogFont(10F);
        editorTextBox.ForeColor = UiColors.LogDefault;
        editorTextBox.HideSelection = false;
        editorTextBox.Name = "editorTextBox";
        editorTextBox.ReadOnly = true;
        editorTextBox.ScrollBars = RichTextBoxScrollBars.Vertical;
        editorTextBox.TabIndex = 0;
        editorTextBox.WordWrap = true;
        editorTextBox.DragEnter += EditorTextBox_DragEnter;
        editorTextBox.DragDrop += EditorTextBox_DragDrop;
        // logButtonPanel
        //
        logButtonPanel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        logButtonPanel.BackColor = UiColors.LogBack;
        logButtonPanel.FlowDirection = FlowDirection.RightToLeft;
        logButtonPanel.Name = "logButtonPanel";
        logButtonPanel.Padding = new Padding(2, 0, 2, 2);
        logButtonPanel.Size = new Size(82, 28);
        logButtonPanel.TabIndex = 1;
        logButtonPanel.WrapContents = false;
        logButtonPanel.Controls.Add(logDownloadButton);
        logButtonPanel.Controls.Add(logCopyButton);
        logButtonPanel.Controls.Add(logClearButton);
        //
        // logClearButton
        //
        logClearButton.AccessibleName = "Clear log";
        logClearButton.Margin = new Padding(2, 0, 0, 1);
        logClearButton.Name = "logClearButton";
        logClearButton.Size = new Size(24, 24);
        logClearButton.TabIndex = 0;
        playlistToolTip.SetToolTip(logClearButton, "ログをクリア");
        logClearButton.Click += LogClearButton_Click;
        //
        // logCopyButton
        //
        logCopyButton.AccessibleName = "Copy log";
        logCopyButton.Margin = new Padding(2, 0, 0, 1);
        logCopyButton.Name = "logCopyButton";
        logCopyButton.Size = new Size(24, 24);
        logCopyButton.TabIndex = 1;
        playlistToolTip.SetToolTip(logCopyButton, "ログをクリップボードへコピー");
        logCopyButton.Click += LogCopyButton_Click;
        //
        // logDownloadButton
        //
        logDownloadButton.AccessibleName = "Download log";
        logDownloadButton.Margin = new Padding(2, 0, 0, 1);
        logDownloadButton.Name = "logDownloadButton";
        logDownloadButton.Size = new Size(24, 24);
        logDownloadButton.TabIndex = 2;
        playlistToolTip.SetToolTip(logDownloadButton, "ログをファイルへ保存");
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
        logAreaPanel.Controls.Add(rightSidePanel);
        //
        // rightSidePanel??????Playlist????????????????
        //
        rightSidePanel.Dock = DockStyle.Right;
        rightSidePanel.Name = "rightSidePanel";
        rightSidePanel.Size = new Size(457, 100);
        rightSidePanel.TabIndex = 1;
        rightSidePanel.Controls.Add(transitionTimePanel);
        rightSidePanel.Controls.Add(playlistSelectorPanel);
        rightSidePanel.Controls.Add(markerOptionsPanel);
        //
        // markerOptionsPanel
        //
        markerOptionsPanel.AllowDrop = true;
        markerOptionsPanel.Dock = DockStyle.Bottom;
        markerOptionsPanel.Name = "markerOptionsPanel";
        markerOptionsPanel.Size = new Size(457, 156);
        markerOptionsPanel.TabIndex = 2;
        markerOptionsPanel.DragEnter += EditorTextBox_DragEnter;
        markerOptionsPanel.DragDrop += EditorTextBox_DragDrop;
        //
        // transitionTimePanel
        //
        transitionTimePanel.AllowDrop = true;
        transitionTimePanel.Dock = DockStyle.Fill;
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
        transitionSettingsPanel.AutoScroll = false;
        transitionSettingsPanel.Dock = DockStyle.Fill;
        transitionSettingsPanel.FlowDirection = FlowDirection.LeftToRight;
        transitionSettingsPanel.Name = "transitionSettingsPanel";
        transitionSettingsPanel.Padding = new Padding(1, 0, 0, 0);
        transitionSettingsPanel.TabIndex = 0;
        transitionSettingsPanel.WrapContents = true;
        transitionSettingsPanel.Controls.Add(fadeInSectionPanel);
        transitionSettingsPanel.Controls.Add(fadeOutSectionPanel);
        transitionSettingsPanel.Controls.Add(exitSourceAtSectionPanel);
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
        fadeInSectionPanel.Size = new Size(108, 190);
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
        fadeInChoicesPanel.Size = new Size(108, 164);
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
        fadeInNoneRadio.AutoSize = false;
        fadeInNoneRadio.Height = 30;
        fadeInNoneRadio.Checked = true;
        fadeInNoneRadio.Font = new Font("Yu Gothic UI", 8.5F);
        fadeInNoneRadio.Margin = new Padding(3, 1, 3, 1);
        fadeInNoneRadio.Name = "fadeInNoneRadio";
        fadeInNoneRadio.TabIndex = 0;
        fadeInNoneRadio.Tag = 0D;
        fadeInNoneRadio.Text = "None";
        fadeInNoneRadio.CheckedChanged += FadeInTimeRadio_CheckedChanged;
        //
        // fadeInOneSecondRadio
        //
        fadeInOneSecondRadio.AutoSize = false;
        fadeInOneSecondRadio.Height = 30;
        fadeInOneSecondRadio.Font = new Font("Yu Gothic UI", 8.5F);
        fadeInOneSecondRadio.Margin = new Padding(3, 1, 3, 1);
        fadeInOneSecondRadio.Name = "fadeInOneSecondRadio";
        fadeInOneSecondRadio.TabIndex = 1;
        fadeInOneSecondRadio.Tag = 1D;
        fadeInOneSecondRadio.Text = "1.0 Sec.";
        fadeInOneSecondRadio.CheckedChanged += FadeInTimeRadio_CheckedChanged;
        //
        // fadeInThreeSecondsRadio
        //
        fadeInThreeSecondsRadio.AutoSize = false;
        fadeInThreeSecondsRadio.Height = 30;
        fadeInThreeSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        fadeInThreeSecondsRadio.Margin = new Padding(3, 1, 3, 1);
        fadeInThreeSecondsRadio.Name = "fadeInThreeSecondsRadio";
        fadeInThreeSecondsRadio.TabIndex = 2;
        fadeInThreeSecondsRadio.Tag = 3D;
        fadeInThreeSecondsRadio.Text = "3.0 Sec.";
        fadeInThreeSecondsRadio.CheckedChanged += FadeInTimeRadio_CheckedChanged;
        //
        // fadeInSixSecondsRadio
        //
        fadeInSixSecondsRadio.AutoSize = false;
        fadeInSixSecondsRadio.Height = 30;
        fadeInSixSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        fadeInSixSecondsRadio.Margin = new Padding(3, 1, 3, 1);
        fadeInSixSecondsRadio.Name = "fadeInSixSecondsRadio";
        fadeInSixSecondsRadio.TabIndex = 3;
        fadeInSixSecondsRadio.Tag = 6D;
        fadeInSixSecondsRadio.Text = "6.0 Sec.";
        fadeInSixSecondsRadio.CheckedChanged += FadeInTimeRadio_CheckedChanged;
        //
        // fadeInNineSecondsRadio
        //
        fadeInNineSecondsRadio.AutoSize = false;
        fadeInNineSecondsRadio.Height = 30;
        fadeInNineSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        fadeInNineSecondsRadio.Margin = new Padding(3, 1, 3, 1);
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
        fadeOutSectionPanel.Size = new Size(108, 190);
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
        transitionTimeChoicesPanel.Size = new Size(108, 164);
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
        transitionTimeHalfSecondRadio.AutoSize = false;
        transitionTimeHalfSecondRadio.Height = 30;
        transitionTimeHalfSecondRadio.Checked = true;
        transitionTimeHalfSecondRadio.Font = new Font("Yu Gothic UI", 8.5F);
        transitionTimeHalfSecondRadio.Margin = new Padding(3, 1, 3, 1);
        transitionTimeHalfSecondRadio.Name = "transitionTimeHalfSecondRadio";
        transitionTimeHalfSecondRadio.TabIndex = 0;
        transitionTimeHalfSecondRadio.Tag = 0.5D;
        transitionTimeHalfSecondRadio.Text = "0.5 Sec.";
        transitionTimeHalfSecondRadio.CheckedChanged += TransitionTimeRadio_CheckedChanged;
        //
        // transitionTimeOneSecondRadio
        //
        transitionTimeOneSecondRadio.AutoSize = false;
        transitionTimeOneSecondRadio.Height = 30;
        transitionTimeOneSecondRadio.Font = new Font("Yu Gothic UI", 8.5F);
        transitionTimeOneSecondRadio.Margin = new Padding(3, 1, 3, 1);
        transitionTimeOneSecondRadio.Name = "transitionTimeOneSecondRadio";
        transitionTimeOneSecondRadio.TabIndex = 1;
        transitionTimeOneSecondRadio.Tag = 1D;
        transitionTimeOneSecondRadio.Text = "1.0 Sec.";
        transitionTimeOneSecondRadio.CheckedChanged += TransitionTimeRadio_CheckedChanged;
        //
        // transitionTimeThreeSecondsRadio
        //
        transitionTimeThreeSecondsRadio.AutoSize = false;
        transitionTimeThreeSecondsRadio.Height = 30;
        transitionTimeThreeSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        transitionTimeThreeSecondsRadio.Margin = new Padding(3, 1, 3, 1);
        transitionTimeThreeSecondsRadio.Name = "transitionTimeThreeSecondsRadio";
        transitionTimeThreeSecondsRadio.TabIndex = 2;
        transitionTimeThreeSecondsRadio.Tag = 3D;
        transitionTimeThreeSecondsRadio.Text = "3.0 Sec.";
        transitionTimeThreeSecondsRadio.CheckedChanged += TransitionTimeRadio_CheckedChanged;
        //
        // transitionTimeSixSecondsRadio
        //
        transitionTimeSixSecondsRadio.AutoSize = false;
        transitionTimeSixSecondsRadio.Height = 30;
        transitionTimeSixSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        transitionTimeSixSecondsRadio.Margin = new Padding(3, 1, 3, 1);
        transitionTimeSixSecondsRadio.Name = "transitionTimeSixSecondsRadio";
        transitionTimeSixSecondsRadio.TabIndex = 3;
        transitionTimeSixSecondsRadio.Tag = 6D;
        transitionTimeSixSecondsRadio.Text = "6.0 Sec.";
        transitionTimeSixSecondsRadio.CheckedChanged += TransitionTimeRadio_CheckedChanged;
        //
        // transitionTimeNineSecondsRadio
        //
        transitionTimeNineSecondsRadio.AutoSize = false;
        transitionTimeNineSecondsRadio.Height = 30;
        transitionTimeNineSecondsRadio.Font = new Font("Yu Gothic UI", 8.5F);
        transitionTimeNineSecondsRadio.Margin = new Padding(3, 1, 3, 1);
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
        exitSourceAtSectionPanel.Size = new Size(108, 190);
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
        exitSourceAtChoicesPanel.Size = new Size(108, 164);
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
        exitSourceImmediateRadio.AutoSize = false;
        exitSourceImmediateRadio.Height = 30;
        exitSourceImmediateRadio.Font = new Font("Yu Gothic UI", 8.5F);
        exitSourceImmediateRadio.Margin = new Padding(3, 1, 3, 1);
        exitSourceImmediateRadio.Name = "exitSourceImmediateRadio";
        exitSourceImmediateRadio.TabIndex = 0;
        exitSourceImmediateRadio.Tag = PlaylistExitSourceMode.Immediate;
        exitSourceImmediateRadio.Text = "Immediate";
        exitSourceImmediateRadio.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
        //
        // exitSourceNextBarRadio
        //
        exitSourceNextBarRadio.AutoSize = false;
        exitSourceNextBarRadio.Height = 30;
        exitSourceNextBarRadio.Checked = true;
        exitSourceNextBarRadio.Font = new Font("Yu Gothic UI", 8.5F);
        exitSourceNextBarRadio.Margin = new Padding(3, 1, 3, 1);
        exitSourceNextBarRadio.Name = "exitSourceNextBarRadio";
        exitSourceNextBarRadio.TabIndex = 1;
        exitSourceNextBarRadio.Tag = PlaylistExitSourceMode.NextBar;
        exitSourceNextBarRadio.Text = "Next Bar";
        exitSourceNextBarRadio.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
        //
        // exitSourceNextBeatRadio
        //
        exitSourceNextBeatRadio.AutoSize = false;
        exitSourceNextBeatRadio.Height = 30;
        exitSourceNextBeatRadio.Font = new Font("Yu Gothic UI", 8.5F);
        exitSourceNextBeatRadio.Margin = new Padding(3, 1, 3, 1);
        exitSourceNextBeatRadio.Name = "exitSourceNextBeatRadio";
        exitSourceNextBeatRadio.TabIndex = 2;
        exitSourceNextBeatRadio.Tag = PlaylistExitSourceMode.NextBeat;
        exitSourceNextBeatRadio.Text = "Next Beat";
        exitSourceNextBeatRadio.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
        //
        // exitSourceNextCueRadio
        //
        exitSourceNextCueRadio.AutoSize = false;
        exitSourceNextCueRadio.Height = 30;
        exitSourceNextCueRadio.Font = new Font("Yu Gothic UI", 8.5F);
        exitSourceNextCueRadio.Margin = new Padding(3, 1, 3, 1);
        exitSourceNextCueRadio.Name = "exitSourceNextCueRadio";
        exitSourceNextCueRadio.TabIndex = 3;
        exitSourceNextCueRadio.Tag = PlaylistExitSourceMode.NextCue;
        exitSourceNextCueRadio.Text = "Next Cue";
        exitSourceNextCueRadio.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
        //
        // exitSourceExitCueRadio
        //
        exitSourceExitCueRadio.AutoSize = false;
        exitSourceExitCueRadio.Height = 30;
        exitSourceExitCueRadio.Font = new Font("Yu Gothic UI", 8.5F);
        exitSourceExitCueRadio.Margin = new Padding(3, 1, 3, 1);
        exitSourceExitCueRadio.Name = "exitSourceExitCueRadio";
        exitSourceExitCueRadio.TabIndex = 4;
        exitSourceExitCueRadio.Tag = PlaylistExitSourceMode.ExitCue;
        exitSourceExitCueRadio.Text = "Exit Cue";
        exitSourceExitCueRadio.CheckedChanged += ExitSourceAtRadio_CheckedChanged;
        //
        // playlistSelectorPanel
        //
        playlistSelectorPanel.Dock = DockStyle.Right;
        playlistSelectorPanel.AllowDrop = true;
        playlistSelectorPanel.Name = "playlistSelectorPanel";
        playlistSelectorPanel.Size = new Size(240, 100);
        playlistSelectorPanel.TabIndex = 1;
        playlistSelectorPanel.Controls.Add(playlistScrollPanel);
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
        playlistScrollPanel.Controls.Add(playlistHeaderLabel);
        playlistScrollPanel.DragEnter += EditorTextBox_DragEnter;
        playlistScrollPanel.DragDrop += EditorTextBox_DragDrop;
        //
        // playlistListLayout
        //
        playlistListLayout.AutoSize = true;
        playlistListLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        playlistListLayout.AllowDrop = true;
        playlistListLayout.ColumnCount = 2;
        playlistListLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        playlistListLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        playlistListLayout.Dock = DockStyle.Top;
        playlistListLayout.GrowStyle = TableLayoutPanelGrowStyle.AddRows;
        playlistListLayout.Name = "playlistListLayout";
        playlistListLayout.RowCount = 0;
        playlistListLayout.TabIndex = 0;
        playlistListLayout.DragEnter += EditorTextBox_DragEnter;
        playlistListLayout.DragDrop += EditorTextBox_DragDrop;
        //
        // actionBar?????????????????
        //
        actionBar.Dock = DockStyle.Bottom;
        actionBar.Height = 44;
        actionBar.Name = "actionBar";
        actionBar.Padding = new Padding(10, 6, 8, 6);
        actionBar.TabIndex = 2;
        actionBar.Controls.Add(brandLogoPictureBox);
        actionBar.Controls.Add(copyrightLinkLabel);
        actionBar.Controls.Add(actionControlsPanel);
        //
        // brandLogoPictureBox
        //
        brandLogoPictureBox.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        brandLogoPictureBox.BackColor = Color.Transparent;
        brandLogoPictureBox.Location = new Point(10, 6);
        brandLogoPictureBox.Name = "brandLogoPictureBox";
        brandLogoPictureBox.Size = new Size(214, 32);
        brandLogoPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        brandLogoPictureBox.TabStop = false;
        //
        // copyrightLinkLabel
        //
        copyrightLinkLabel.Anchor = AnchorStyles.Left | AnchorStyles.Bottom;
        copyrightLinkLabel.AutoEllipsis = true;
        copyrightLinkLabel.BackColor = Color.Transparent;
        copyrightLinkLabel.Font = new Font("Yu Gothic UI", 7.5F);
        copyrightLinkLabel.LinkBehavior = LinkBehavior.HoverUnderline;
        copyrightLinkLabel.Location = new Point(232, 16);
        copyrightLinkLabel.Name = "copyrightLinkLabel";
        copyrightLinkLabel.Size = new Size(300, 22);
        copyrightLinkLabel.TabIndex = 0;
        copyrightLinkLabel.TabStop = true;
        copyrightLinkLabel.Text = "© 2026 MIYABI GAME AUDIO INC.  Version 1.00 β  GitHub";
        copyrightLinkLabel.TextAlign = ContentAlignment.BottomLeft;
        copyrightLinkLabel.LinkArea = new LinkArea(47, 6);
        copyrightLinkLabel.LinkClicked += CopyrightLinkLabel_LinkClicked;
        //
        // actionControlsPanel
        //
        actionControlsPanel.AutoSize = true;
        actionControlsPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        actionControlsPanel.Dock = DockStyle.Right;
        actionControlsPanel.FlowDirection = FlowDirection.RightToLeft;
        actionControlsPanel.Name = "actionControlsPanel";
        actionControlsPanel.Padding = new Padding(0, 0, 0, 0);
        actionControlsPanel.TabIndex = 1;
        actionControlsPanel.WrapContents = false;
        actionControlsPanel.Controls.Add(exportButton);
        actionControlsPanel.Controls.Add(reloadButton);
        actionControlsPanel.Controls.Add(topMostCheckBox);
        actionControlsPanel.Controls.Add(compactFileNumbersCheckBox);
        actionControlsPanel.Controls.Add(detailedLogCheckBox);
        //
        // detailedLogCheckBox
        //
        detailedLogCheckBox.AutoSize = true;
        detailedLogCheckBox.Checked = true;
        detailedLogCheckBox.CheckState = CheckState.Checked;
        detailedLogCheckBox.Font = new Font("Yu Gothic UI", 9F);
        detailedLogCheckBox.Margin = new Padding(0, 8, 8, 0);
        detailedLogCheckBox.Name = "detailedLogCheckBox";
        detailedLogCheckBox.TabIndex = 0;
        detailedLogCheckBox.Text = "Debug Log";
        detailedLogCheckBox.UseVisualStyleBackColor = true;
        detailedLogCheckBox.CheckedChanged += DetailedLogCheckBox_CheckedChanged;
        //
        // compactFileNumbersCheckBox
        //
        compactFileNumbersCheckBox.AutoSize = true;
        compactFileNumbersCheckBox.Checked = true;
        compactFileNumbersCheckBox.CheckState = CheckState.Checked;
        compactFileNumbersCheckBox.Font = new Font("Yu Gothic UI", 9F);
        compactFileNumbersCheckBox.Margin = new Padding(0, 8, 8, 0);
        compactFileNumbersCheckBox.Name = "compactFileNumbersCheckBox";
        compactFileNumbersCheckBox.TabIndex = 1;
        compactFileNumbersCheckBox.Text = "Compact File Numbers";
        compactFileNumbersCheckBox.UseVisualStyleBackColor = true;
        compactFileNumbersCheckBox.CheckedChanged += CompactFileNumbersCheckBox_CheckedChanged;
        //
        // topMostCheckBox
        //
        topMostCheckBox.AutoSize = true;
        topMostCheckBox.Font = new Font("Yu Gothic UI", 9F);
        topMostCheckBox.Margin = new Padding(0, 8, 8, 0);
        topMostCheckBox.Name = "topMostCheckBox";
        topMostCheckBox.TabIndex = 2;
        topMostCheckBox.Text = "Always on Top";
        topMostCheckBox.UseVisualStyleBackColor = true;
        topMostCheckBox.CheckedChanged += TopMostCheckBox_CheckedChanged;
        //
        // reloadButton
        //
        reloadButton.Enabled = false;
        reloadButton.Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold);
        reloadButton.Margin = new Padding(0, 0, 8, 0);
        reloadButton.Name = "reloadButton";
        reloadButton.Size = new Size(108, 32);
        reloadButton.TabIndex = 3;
        reloadButton.Text = "RELOAD";
        reloadButton.Click += ReloadButton_Click;
        //
        // exportButton
        //
        exportButton.Enabled = false;
        exportButton.Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold);
        exportButton.Margin = Padding.Empty;
        exportButton.Name = "exportButton";
        exportButton.Size = new Size(108, 32);
        exportButton.TabIndex = 4;
        exportButton.Text = "EXPORT";
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
        // Dock ?: Fill ? Bottom???????? Top??????????????
        Controls.Add(logAreaPanel);
        Controls.Add(actionBar);
        Controls.Add(waapiStatusBar);
        Controls.Add(transportBar);
        Controls.Add(waveformHostPanel);
        ForeColor = UiColors.WindowFore;
        MaximizeBox = true;
        MinimizeBox = true;
        MinimumSize = new Size(480, 320);
        Name = "Form1";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "MGA Wwise IMImporter - Version 1.00 β";
        ((System.ComponentModel.ISupportInitialize)brandLogoPictureBox).EndInit();
        waveformHostPanel.ResumeLayout(false);
        actionControlsPanel.ResumeLayout(false);
        actionControlsPanel.PerformLayout();
        actionBar.ResumeLayout(false);
        actionBar.PerformLayout();
        logButtonPanel.ResumeLayout(false);
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
        rightSidePanel.ResumeLayout(false);
        logAreaPanel.ResumeLayout(false);
        ResumeLayout(false);
    }

    #endregion
}
