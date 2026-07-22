using System.Drawing.Drawing2D;

namespace MgaWwiseIMImporter.UI;

internal enum TransportCommand
{
    TogglePlayback,
    JumpToBar,
    GoToStart,
    PreviousPlaylist,
    PreviousBar,
    PreviousPage,
    NextPage,
    NextBar,
    NextPlaylist,
    GoToEnd,
    TimeZoomIn,
    TimeZoomOut,
    TimeZoomMax,
    TimeZoomReset,
    AmpZoomIn,
    AmpZoomOut,
    AmpZoomMax,
    AmpZoomReset,
}

internal readonly record struct TransportPositionInfo(
    double Bpm,
    int Numerator,
    int Denominator,
    int Bar,
    int Beat,
    int Subdivision,
    TimeSpan Time,
    bool HasMusicalPosition = true);

/// <summary>波形操作のショートカットをアイコンで実行するフラットなトランスポートバー。</summary>
internal sealed class TransportBar : UserControl
{
    private readonly FlowLayoutPanel _groups = new();
    private readonly DarkToolTip _toolTip = new();
    private readonly TransportPositionDisplay _positionDisplay = new();
    private readonly Dictionary<TransportCommand, TransportIconButton> _commandButtons = [];
    private readonly System.Windows.Forms.Timer _commandRepeatTimer = new();
    private readonly List<(Label Label, Func<string> TitleProvider)> _groupLabels = [];
    private readonly TransportIconButton _playButton;
    private TransportCommand? _heldCommand;
    private TransportIconButton? _heldButton;
    private bool _repeatStarted;
    private bool _waveOnlyViewStepTips;
    private bool _waveOnlyMarkerTips;

    public TransportBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        AutoScroll = true;
        BackColor = UiColors.ForControlBack(UiColors.TransportBack);
        // ボタン 30 + 上下 Padding 3（正方形化後の余白過多を避ける）。
        Height = 36;
        Padding = new Padding(8, 3, 8, 3);
        TabStop = false;

        _groups.AutoSize = true;
        _groups.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _groups.BackColor = BackColor;
        _groups.FlowDirection = FlowDirection.LeftToRight;
        _groups.Location = new Point(Padding.Left, Padding.Top);
        _groups.Margin = Padding.Empty;
        _groups.MinimumSize = new Size(1, 30);
        _groups.Padding = Padding.Empty;
        _groups.WrapContents = false;
        Controls.Add(_groups);
        _groups.Controls.Add(_positionDisplay);

        _playButton = AddGroup(
            () => UiStrings.LabelTransportGroup,
            repeatOnHold: false,
            (TransportCommand.TogglePlayback, TransportIcon.PlayPause),
            (TransportCommand.JumpToBar, TransportIcon.JumpToBar));

        AddGroup(
            () => UiStrings.LabelNavigationGroup,
            repeatOnHold: true,
            (TransportCommand.GoToStart, TransportIcon.GoToStart),
            (TransportCommand.PreviousPage, TransportIcon.PreviousPage),
            (TransportCommand.PreviousPlaylist, TransportIcon.PreviousRegion),
            (TransportCommand.PreviousBar, TransportIcon.PreviousBar),
            (TransportCommand.NextBar, TransportIcon.NextBar),
            (TransportCommand.NextPlaylist, TransportIcon.NextRegion),
            (TransportCommand.NextPage, TransportIcon.NextPage),
            (TransportCommand.GoToEnd, TransportIcon.GoToEnd));

        AddGroup(
            () => UiStrings.LabelTimeZoomGroup,
            repeatOnHold: true,
            (TransportCommand.TimeZoomIn, TransportIcon.TimeZoomIn),
            (TransportCommand.TimeZoomOut, TransportIcon.TimeZoomOut),
            (TransportCommand.TimeZoomMax, TransportIcon.TimeZoomMax),
            (TransportCommand.TimeZoomReset, TransportIcon.TimeZoomReset));

        AddGroup(
            () => UiStrings.LabelAmpZoomGroup,
            repeatOnHold: true,
            (TransportCommand.AmpZoomIn, TransportIcon.AmpZoomIn),
            (TransportCommand.AmpZoomOut, TransportIcon.AmpZoomOut),
            (TransportCommand.AmpZoomMax, TransportIcon.AmpZoomMax),
            (TransportCommand.AmpZoomReset, TransportIcon.AmpZoomReset));

        _commandRepeatTimer.Tick += (_, _) => RepeatHeldCommand();
        ApplyColors();
        UiStrings.LanguageChanged += (_, _) => ApplyLocalizedToolTips();
        TightenVerticalLayout();
    }

    public event EventHandler<TransportCommand>? CommandInvoked;
    public event EventHandler? CommandHoldEnded;

    /// <summary>NAVIGATION / ZOOM ボタンがマウスで押下中か。</summary>
    public bool IsCommandHeld => _heldCommand.HasValue;

    /// <summary>
    /// 全グループを横スクロールなしで表示するために必要な幅。
    /// 左右 Padding と各グループの Margin も含む。
    /// </summary>
    public int RequiredWidth =>
        Padding.Horizontal
        + _groups.Controls
            .Cast<Control>()
            .Sum(control => control.Width + control.Margin.Horizontal);

    public bool IsPlaying
    {
        get => _playButton.IsPlaying;
        set => _playButton.IsPlaying = value;
    }

    public void SetPosition(TransportPositionInfo? position)
    {
        _positionDisplay.Position = position;
    }

    /// <summary>
    /// 小節ジャンプ／小節または表示ステップ／Playlist ナビの有効状態。
    /// 無効時は <see cref="UiColors.TransportDisabledFore"/> でグレーアウト。
    /// </summary>
    public void SetNavigationAvailability(
        bool jumpToBarEnabled,
        bool previousNextBarEnabled,
        bool playlistNavigationEnabled,
        bool waveOnlyViewStepTips = false,
        bool waveOnlyMarkerTips = false)
    {
        SetCommandEnabled(TransportCommand.JumpToBar, jumpToBarEnabled);
        SetCommandEnabled(TransportCommand.PreviousBar, previousNextBarEnabled);
        SetCommandEnabled(TransportCommand.NextBar, previousNextBarEnabled);
        SetCommandEnabled(TransportCommand.PreviousPlaylist, playlistNavigationEnabled);
        SetCommandEnabled(TransportCommand.NextPlaylist, playlistNavigationEnabled);

        if (_waveOnlyViewStepTips == waveOnlyViewStepTips
            && _waveOnlyMarkerTips == waveOnlyMarkerTips)
        {
            return;
        }

        _waveOnlyViewStepTips = waveOnlyViewStepTips;
        _waveOnlyMarkerTips = waveOnlyMarkerTips;
        ApplyLocalizedToolTips();
    }

    private void SetCommandEnabled(TransportCommand command, bool enabled)
    {
        if (_commandButtons.TryGetValue(command, out var button)
            && button.Enabled != enabled)
        {
            button.Enabled = enabled;
        }
    }

    /// <summary>表示言語に合わせてツールチップ・グループ見出し・アクセシビリティ名を付け直す。</summary>
    public void ApplyLocalizedToolTips()
    {
        _toolTip.ApplyTheme();
        foreach (var (command, button) in _commandButtons)
        {
            var tip = UiStrings.TipForTransportCommand(
                command,
                _waveOnlyViewStepTips,
                _waveOnlyMarkerTips);
            button.AccessibleName = tip;
            _toolTip.SetToolTip(button, tip);
        }

        foreach (var (label, titleProvider) in _groupLabels)
        {
            RelayoutGroupLabel(label, titleProvider());
        }

        _positionDisplay.AccessibleName = UiStrings.AccessibleTransportPositionDisplay;
        TightenVerticalLayout();
    }

    /// <summary>グループ見出しの文言変更に合わせてラベル幅とボタングループ位置を更新する。</summary>
    private static void RelayoutGroupLabel(Label label, string title)
    {
        label.Text = title;
        if (label.Parent is not Panel group)
        {
            return;
        }

        var labelWidth = TextRenderer.MeasureText(
            title,
            label.Font,
            Size.Empty,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + 4;
        labelWidth = Math.Max(labelWidth, 8);
        label.Width = labelWidth;

        FlowLayoutPanel? buttons = null;
        foreach (Control child in group.Controls)
        {
            if (child is FlowLayoutPanel flow)
            {
                buttons = flow;
                break;
            }
        }

        if (buttons is null)
        {
            return;
        }

        buttons.Left = labelWidth;
        group.Width = labelWidth + buttons.Width;
    }

    /// <summary>キーボード操作中のボタンをマウスオーバーと同じ表示にする。</summary>
    public void BeginShortcutFeedback(TransportCommand command)
    {
        if (_commandButtons.TryGetValue(command, out var button))
        {
            button.BeginShortcutFeedback();
        }
    }

    /// <summary>キーボード操作表示をホバー色からフェードアウトする。</summary>
    public void EndShortcutFeedback(TransportCommand command)
    {
        if (_commandButtons.TryGetValue(command, out var button))
        {
            button.EndShortcutFeedback();
        }
    }

    /// <summary>
    /// マウスホイール／スクロール操作に対応するボタンを点灯し、直ちにフェードアウトする。
    /// 連続操作時は呼び出すたびに点灯レベルを戻す。
    /// </summary>
    public void PulseCommandFeedback(TransportCommand command)
    {
        if (_commandButtons.TryGetValue(command, out var button))
        {
            button.BeginShortcutFeedback();
            button.EndShortcutFeedback();
        }
    }

    public void ApplyColors()
    {
        BackColor = UiColors.ForControlBack(UiColors.TransportBack);
        _groups.BackColor = BackColor;
        _positionDisplay.ApplyColors();

        foreach (var group in _groups.Controls.OfType<Panel>())
        {
            group.BackColor = BackColor;
            foreach (Control control in group.Controls)
            {
                control.BackColor = BackColor;
                if (control is Label label)
                {
                    label.ForeColor = UiColors.TransportSectionFore;
                }
                else if (control is FlowLayoutPanel buttons)
                {
                    buttons.BackColor = BackColor;
                    foreach (var button in buttons.Controls.OfType<TransportIconButton>())
                    {
                        button.ApplyColors();
                    }
                }
            }
        }

        Invalidate();
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        // 描画座標はピクセル基準なので、DPI拡大時も右側に空白を増やさない。
        base.ScaleControl(factor, specified & ~BoundsSpecified.Width);
        // アイコン正方形化で縦が短くなった分、バー高さとグループ高さを詰める。
        TightenVerticalLayout();
    }

    /// <summary>
    /// コマンドボタンの正方形辺に合わせて、グループ行・位置表示・バー全体の高さを揃える。
    /// </summary>
    private void TightenVerticalLayout()
    {
        var side = _playButton.Height;
        if (side <= 0)
        {
            side = 30;
        }

        foreach (Control group in _groups.Controls)
        {
            if (group is TransportPositionDisplay position)
            {
                if (position.Height != side)
                {
                    position.Height = side;
                }

                continue;
            }

            if (group is not Panel panel)
            {
                continue;
            }

            if (panel.Height != side)
            {
                panel.Height = side;
            }

            foreach (Control child in panel.Controls)
            {
                if (child.Height != side)
                {
                    child.Height = side;
                }
            }
        }

        _groups.MinimumSize = new Size(1, side);
        var desiredHeight = Math.Max(36, Padding.Vertical + side);
        if (Height != desiredHeight)
        {
            Height = desiredHeight;
        }

        // Dock レイアウト後に潰れた場合のガード。
        if (Height < 24)
        {
            Height = desiredHeight;
        }

        _groups.Location = new Point(Padding.Left, Padding.Top);
        Visible = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TightenVerticalLayout();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var border = new Pen(UiColors.ForControlBack(UiColors.TransportBorder));
        e.Graphics.DrawLine(border, 0, 0, ClientSize.Width, 0);
        e.Graphics.DrawLine(border, 0, ClientSize.Height - 1, ClientSize.Width, ClientSize.Height - 1);
    }

    private TransportIconButton AddGroup(
        Func<string> titleProvider,
        bool repeatOnHold,
        params (TransportCommand Command, TransportIcon Icon)[] definitions)
    {
        const int buttonHeight = 30;
        const int buttonPitch = 31;
        var title = titleProvider();
        using var groupFont = new Font("Yu Gothic UI", 7F, FontStyle.Bold);
        var labelWidth = TextRenderer.MeasureText(
            title,
            groupFont,
            Size.Empty,
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + 4;
        labelWidth = Math.Max(labelWidth, 8);
        var groupWidth = labelWidth + definitions.Length * buttonPitch;
        var group = new Panel
        {
            AutoSize = false,
            BackColor = BackColor,
            Margin = new Padding(0, 0, 4, 0),
            Padding = Padding.Empty,
            Size = new Size(groupWidth, buttonHeight),
        };
        var label = new Label
        {
            BackColor = BackColor,
            Font = new Font(groupFont.FontFamily, groupFont.Size, groupFont.Style),
            ForeColor = UiColors.TransportSectionFore,
            Location = Point.Empty,
            Padding = new Padding(0, 0, 2, 0),
            Size = new Size(labelWidth, buttonHeight),
            Text = title,
            TextAlign = ContentAlignment.MiddleRight,
        };
        _groupLabels.Add((label, titleProvider));
        var buttons = new FlowLayoutPanel
        {
            AutoSize = false,
            BackColor = BackColor,
            FlowDirection = FlowDirection.LeftToRight,
            Location = new Point(labelWidth, 0),
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            Size = new Size(definitions.Length * buttonPitch, buttonHeight),
            WrapContents = false,
        };

        TransportIconButton? first = null;
        foreach (var definition in definitions)
        {
            var tip = UiStrings.TipForTransportCommand(
                definition.Command,
                _waveOnlyViewStepTips,
                _waveOnlyMarkerTips);
            var button = new TransportIconButton(definition.Icon)
            {
                AccessibleName = tip,
                Margin = new Padding(0, 0, 1, 0),
                Tag = definition.Command,
            };
            if (repeatOnHold)
            {
                button.MouseDown += (_, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        BeginCommandHold(definition.Command, button);
                    }
                };
                button.MouseUp += (_, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        EndCommandHold();
                    }
                };
                button.MouseLeave += (_, _) => EndCommandHold();
                button.MouseCaptureChanged += (_, _) =>
                {
                    if (!button.Capture)
                    {
                        EndCommandHold();
                    }
                };
            }
            else
            {
                button.Click += (_, _) => CommandInvoked?.Invoke(this, definition.Command);
            }

            _toolTip.SetToolTip(button, tip);
            buttons.Controls.Add(button);
            _commandButtons[definition.Command] = button;
            first ??= button;
        }

        group.Controls.Add(buttons);
        group.Controls.Add(label);
        _groups.Controls.Add(group);
        return first!;
    }

    private void BeginCommandHold(TransportCommand command, TransportIconButton button)
    {
        EndCommandHold();
        _heldCommand = command;
        _heldButton = button;
        _repeatStarted = false;
        _commandRepeatTimer.Interval = (SystemInformation.KeyboardDelay + 1) * 250;
        _commandRepeatTimer.Start();
        CommandInvoked?.Invoke(this, command);
    }

    private void RepeatHeldCommand()
    {
        if (_heldCommand is not { } command
            || _heldButton is not { Enabled: true }
            || (MouseButtons & MouseButtons.Left) == 0)
        {
            EndCommandHold();
            return;
        }

        if (!_repeatStarted)
        {
            _repeatStarted = true;
            // Windows の KeyboardSpeed: 0=約2.5回/秒、31=約30回/秒。
            var repeatsPerSecond =
                2.5d + SystemInformation.KeyboardSpeed * (30d - 2.5d) / 31d;
            _commandRepeatTimer.Interval = Math.Max(
                20,
                (int)Math.Round(1000d / repeatsPerSecond));
        }

        CommandInvoked?.Invoke(this, command);
    }

    private void EndCommandHold()
    {
        if (_heldCommand is null)
        {
            return;
        }

        _commandRepeatTimer.Stop();
        _heldCommand = null;
        _heldButton = null;
        _repeatStarted = false;
        CommandHoldEnded?.Invoke(this, EventArgs.Empty);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _commandRepeatTimer.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }
}

internal sealed class TransportPositionDisplay : Control
{
    private TransportPositionInfo? _position;

    public TransportPositionDisplay()
    {
        AccessibleName = UiStrings.AccessibleTransportPositionDisplay;
        BackColor = UiColors.ForControlBack(UiColors.TransportBack);
        ForeColor = UiColors.TransportFore;
        Font = new Font("Yu Gothic UI", 9.5F, FontStyle.Bold);
        Margin = Padding.Empty;
        Size = new Size(312, 30);
        TabStop = false;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint,
            true);
    }

    public TransportPositionInfo? Position
    {
        get => _position;
        set
        {
            if (_position == value)
            {
                return;
            }

            _position = value;
            Invalidate();
        }
    }

    public void ApplyColors()
    {
        BackColor = UiColors.ForControlBack(UiColors.TransportBack);
        ForeColor = UiColors.TransportFore;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var position = Position;
        var hasMusical = position is { HasMusicalPosition: true };
        var musicalFore = hasMusical ? ForeColor : UiColors.TransportDisabledFore;
        var timeFore = position is not null ? ForeColor : UiColors.TransportDisabledFore;

        var bpm = hasMusical && position is { } p
            ? Math.Round(p.Bpm).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "---";
        var signature = hasMusical && position is { } signaturePosition
            ? $"{signaturePosition.Numerator}/{signaturePosition.Denominator}"
            : "--/--";
        var musicalPosition = hasMusical && position is { } musical
            ? $"{Math.Max(0, musical.Bar):000}:{musical.Beat}:{musical.Subdivision}"
            : "000:1:1";
        var elapsed = position?.Time ?? TimeSpan.Zero;
        var hours = Math.Max(0L, (long)elapsed.TotalHours);
        var time = $"{hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}";

        using var iconPen = new Pen(musicalFore, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        using var iconBrush = new SolidBrush(musicalFore);

        var iconTop = Math.Max(0f, (Height - 30f) / 2f);
        DrawQuarterNote(g, iconPen, iconBrush, 5, iconTop + 7f);
        DrawText(g, bpm, new Rectangle(22, 0, 32, Height), musicalFore);
        DrawText(g, signature, new Rectangle(64, 0, 38, Height), musicalFore);
        DrawText(g, musicalPosition, new Rectangle(107, 0, 74, Height), musicalFore);
        DrawText(g, time, new Rectangle(186, 0, 124, Height), timeFore);
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        // 描画座標はピクセル基準なので、DPI拡大時も右側に空白を増やさない。
        base.ScaleControl(factor, specified & ~BoundsSpecified.Width);
    }

    private void DrawText(Graphics g, string text, Rectangle bounds, Color foreColor)
    {
        TextRenderer.DrawText(
            g,
            text,
            Font,
            bounds,
            foreColor,
            TextFormatFlags.Left
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPadding
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.SingleLine);
    }

    private static void DrawQuarterNote(Graphics g, Pen pen, Brush brush, float x, float y)
    {
        g.FillEllipse(brush, x, y + 11, 7, 5);
        g.DrawLine(pen, x + 6, y + 12, x + 6, y);
    }

}

internal enum TransportIcon
{
    PlayPause,
    JumpToBar,
    GoToStart,
    PreviousRegion,
    PreviousBar,
    PreviousPage,
    NextPage,
    NextBar,
    NextRegion,
    GoToEnd,
    TimeZoomIn,
    TimeZoomOut,
    TimeZoomMax,
    TimeZoomReset,
    AmpZoomIn,
    AmpZoomOut,
    AmpZoomMax,
    AmpZoomReset,
    Clear,
    Copy,
    Download,
    Folder,
    Save,
    Delete,
    Lock,
    Unlock,
}

internal sealed class TransportIconButton : Button
{
    private const double ShortcutFadeDurationMs = 180d;
    private readonly System.Windows.Forms.Timer _shortcutFadeTimer = new() { Interval = 16 };
    private bool _hovered;
    private bool _pressed;
    private bool _isPlaying;
    private double _shortcutFeedbackLevel;
    private long _shortcutFadeStartTickMs;

    public TransportIconButton(TransportIcon icon)
    {
        Icon = icon;
        AccessibleRole = AccessibleRole.PushButton;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Size = new Size(30, 30);
        TabStop = false;
        UseVisualStyleBackColor = false;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint,
            true);
        // クリックでフォーカスを奪わず、上下キーの波形拡縮を阻害しない。
        SetStyle(ControlStyles.Selectable, false);
        _shortcutFadeTimer.Tick += (_, _) => UpdateShortcutFeedbackFade();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        if (!Enabled)
        {
            _hovered = false;
            _pressed = false;
            _shortcutFeedbackLevel = 0d;
            _shortcutFadeTimer.Stop();
        }

        Invalidate();
        base.OnEnabledChanged(e);
    }

    public TransportIcon Icon { get; private set; }

    public void SetIcon(TransportIcon icon)
    {
        if (Icon == icon)
        {
            return;
        }

        Icon = icon;
        Invalidate();
    }

    public Color HoverBackColor { get; set; }
    public Color PressedBackColor { get; set; }
    public Color AccentColor { get; set; }
    public Color ActiveForeColor { get; set; }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (_isPlaying == value)
            {
                return;
            }

            _isPlaying = value;
            Invalidate();
        }
    }

    public void ApplyColors()
    {
        BackColor = UiColors.ForControlBack(UiColors.TransportBack);
        ForeColor = UiColors.TransportFore;
        HoverBackColor = UiColors.ForControlBack(UiColors.TransportHoverBack);
        PressedBackColor = UiColors.ForControlBack(UiColors.TransportPressedBack);
        AccentColor = Color.Empty;
        ActiveForeColor = UiColors.ForControlBack(UiColors.SeekCyan);
        Invalidate();
    }

    /// <summary>
    /// <see cref="AutoScaleMode.Font"/> はフォントの横／縦メトリクスで倍率が分かれ、
    /// 正方形のアイコンボタンが長方形になる。元が正方形なら短い辺に揃える。
    /// </summary>
    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        var keepSquare = Width == Height;
        base.ScaleControl(factor, specified);
        if (keepSquare && Width != Height)
        {
            var side = Math.Min(Width, Height);
            Size = new Size(side, side);
        }
    }

    public void BeginShortcutFeedback()
    {
        _shortcutFadeTimer.Stop();
        _shortcutFeedbackLevel = 1d;
        Invalidate();
    }

    public void EndShortcutFeedback()
    {
        if (_shortcutFeedbackLevel <= 0d)
        {
            return;
        }

        _shortcutFadeStartTickMs = Environment.TickCount64;
        _shortcutFadeTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _shortcutFadeTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        _hovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hovered = false;
        _pressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = e.Button == MouseButtons.Left;
        Invalidate();
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        _pressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var hoverLevel = Enabled
            ? (_hovered ? 1d : _shortcutFeedbackLevel)
            : 0d;
        var back = _pressed
            ? PressedBackColor
            : BlendColor(BackColor, HoverBackColor, hoverLevel);
        if (_pressed || hoverLevel > 0d)
        {
            var hoverBounds = new Rectangle(3, 3, Width - 6, Height - 6);
            using var hoverBrush = new SolidBrush(back);
            g.FillRectangle(hoverBrush, hoverBounds);
            if (!AccentColor.IsEmpty)
            {
                using var accent = new Pen(AccentColor, 1f);
                g.DrawRectangle(
                    accent,
                    hoverBounds.X + 0.5f,
                    hoverBounds.Y + 0.5f,
                    hoverBounds.Width - 1f,
                    hoverBounds.Height - 1f);
            }
        }

        using var pen = new Pen(
            Enabled ? ForeColor : UiColors.TransportDisabledFore,
            1.8f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        using var brush = new SolidBrush(
            !Enabled
                ? UiColors.TransportDisabledFore
                : Icon == TransportIcon.PlayPause && IsPlaying
                    ? ActiveForeColor
                    : ForeColor);
        var iconState = g.Save();
        g.ScaleTransform(Width / 34f, Height / 36f);
        DrawIcon(g, pen, brush);
        g.Restore(iconState);
    }

    private void UpdateShortcutFeedbackFade()
    {
        var elapsed = Math.Max(0L, Environment.TickCount64 - _shortcutFadeStartTickMs);
        var progress = Math.Clamp(elapsed / ShortcutFadeDurationMs, 0d, 1d);
        _shortcutFeedbackLevel = 1d - progress;
        if (progress >= 1d)
        {
            _shortcutFadeTimer.Stop();
            _shortcutFeedbackLevel = 0d;
        }

        Invalidate();
    }

    private static Color BlendColor(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0d, 1d);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private void DrawIcon(Graphics g, Pen pen, Brush brush)
    {
        const float cx = 17f;
        const float cy = 18f;
        switch (Icon)
        {
            case TransportIcon.PlayPause:
                if (IsPlaying)
                {
                    g.FillRectangle(brush, 12, 11, 4, 14);
                    g.FillRectangle(brush, 19, 11, 4, 14);
                }
                else
                {
                    g.FillPolygon(brush, [new PointF(12, 9), new PointF(25, 18), new PointF(12, 27)]);
                }
                break;
            case TransportIcon.JumpToBar:
                DrawHash(g, pen, 9, 10, 19, 16);
                g.DrawLine(pen, 20, 18, 26, 12);
                g.DrawLine(pen, 26, 12, 26, 17);
                g.DrawLine(pen, 26, 12, 21, 12);
                break;
            case TransportIcon.GoToStart:
            case TransportIcon.GoToEnd:
                var start = Icon == TransportIcon.GoToStart;
                var lineX = start ? 9f : 25f;
                g.DrawLine(pen, lineX, 9, lineX, 27);
                DrawChevron(g, pen, cx + (start ? 2 : -2), cy, start);
                break;
            case TransportIcon.PreviousRegion:
            case TransportIcon.NextRegion:
                var previousRegion = Icon == TransportIcon.PreviousRegion;
                DrawChevron(g, pen, cx + (previousRegion ? 3 : -3), cy, previousRegion);
                g.DrawLine(pen, previousRegion ? 10 : 24, 10, previousRegion ? 10 : 24, 26);
                g.DrawLine(pen, previousRegion ? 13 : 21, 13, previousRegion ? 13 : 21, 23);
                break;
            case TransportIcon.PreviousBar:
            case TransportIcon.NextBar:
                var previousBar = Icon == TransportIcon.PreviousBar;
                DrawChevron(g, pen, cx, cy, previousBar);
                g.DrawLine(pen, previousBar ? 10 : 24, 10, previousBar ? 10 : 24, 26);
                break;
            case TransportIcon.PreviousPage:
            case TransportIcon.NextPage:
                var previousPage = Icon == TransportIcon.PreviousPage;
                DrawChevron(g, pen, cx + (previousPage ? -2 : 2), cy, previousPage);
                DrawChevron(g, pen, cx + (previousPage ? 5 : -5), cy, previousPage);
                break;
            case TransportIcon.TimeZoomIn:
            case TransportIcon.TimeZoomOut:
            case TransportIcon.TimeZoomMax:
            case TransportIcon.TimeZoomReset:
                DrawHorizontalZoomIcon(g, pen);
                DrawZoomModifier(g, pen, brush, Icon, cx, cy);
                break;
            case TransportIcon.AmpZoomIn:
            case TransportIcon.AmpZoomOut:
            case TransportIcon.AmpZoomMax:
            case TransportIcon.AmpZoomReset:
                DrawVerticalZoomIcon(g, pen);
                DrawZoomModifier(g, pen, brush, Icon, cx, cy);
                break;
            case TransportIcon.Clear:
                g.DrawRectangle(pen, 12, 13, 10, 13);
                g.DrawLine(pen, 10, 11, 24, 11);
                g.DrawLine(pen, 14, 8, 20, 8);
                g.DrawLine(pen, 15, 16, 15, 23);
                g.DrawLine(pen, 19, 16, 19, 23);
                break;
            case TransportIcon.Copy:
                g.DrawRectangle(pen, 9, 8, 12, 14);
                g.DrawRectangle(pen, 13, 12, 12, 14);
                break;
            case TransportIcon.Download:
                g.DrawLine(pen, 17, 7, 17, 20);
                g.DrawLines(pen, [new PointF(12, 16), new PointF(17, 21), new PointF(22, 16)]);
                g.DrawLine(pen, 9, 26, 25, 26);
                break;
            // Folder / Save / Delete は 16×16（x 9..25, y 10..26）の正方形グリフに揃える。
            case TransportIcon.Folder:
                g.DrawLines(pen, [
                    new PointF(9, 12),
                    new PointF(9, 10),
                    new PointF(15, 10),
                    new PointF(17, 12),
                    new PointF(25, 12),
                    new PointF(25, 26),
                    new PointF(9, 26),
                    new PointF(9, 12),
                ]);
                g.DrawLine(pen, 9, 15, 25, 15);
                break;
            case TransportIcon.Save:
                // フロッピーディスク
                g.DrawLines(pen, [
                    new PointF(9, 10),
                    new PointF(22, 10),
                    new PointF(25, 13),
                    new PointF(25, 26),
                    new PointF(9, 26),
                    new PointF(9, 10),
                ]);
                g.DrawRectangle(pen, 13, 10, 8, 5);
                g.DrawRectangle(pen, 12, 19, 10, 7);
                break;
            case TransportIcon.Delete:
                // ゴミ箱
                g.DrawLine(pen, 9, 13, 25, 13);
                g.DrawLine(pen, 14, 10, 20, 10);
                g.DrawLines(pen, [
                    new PointF(11, 13),
                    new PointF(12, 26),
                    new PointF(22, 26),
                    new PointF(23, 13),
                ]);
                g.DrawLine(pen, 14, 16, 14, 23);
                g.DrawLine(pen, 17, 16, 17, 23);
                g.DrawLine(pen, 20, 16, 20, 23);
                break;
            case TransportIcon.Lock:
                DrawPadlockBody(g, pen);
                // 閉じたツメ：左右とも胴体に接続
                g.DrawLine(pen, 12.5f, 16f, 12.5f, 12.5f);
                g.DrawArc(pen, 12.5f, 7.5f, 9f, 9f, 180f, 180f);
                g.DrawLine(pen, 21.5f, 12.5f, 21.5f, 16f);
                break;
            case TransportIcon.Unlock:
                DrawPadlockBody(g, pen);
                // 開いたツメ：左だけ接続、右は下向きだが胴体との間に隙間を空ける
                g.DrawLine(pen, 12.5f, 16f, 12.5f, 11.5f);
                g.DrawArc(pen, 12.5f, 6.5f, 9.5f, 9.5f, 180f, 180f);
                g.DrawLine(pen, 22f, 11.5f, 22f, 13.5f);
                break;
        }
    }

    private static void DrawChevron(Graphics g, Pen pen, float centerX, float centerY, bool left)
    {
        var direction = left ? -1f : 1f;
        g.DrawLines(
            pen,
            [
                new PointF(centerX - direction * 4, centerY - 7),
                new PointF(centerX + direction * 3, centerY),
                new PointF(centerX - direction * 4, centerY + 7),
            ]);
    }

    private static void DrawPadlockBody(Graphics g, Pen pen)
    {
        g.DrawRectangle(pen, 10f, 16f, 14f, 11f);
        g.DrawEllipse(pen, 15.5f, 18.5f, 3f, 3f);
        g.DrawLine(pen, 17f, 21.5f, 17f, 24.5f);
    }

    private static void DrawHash(Graphics g, Pen pen, float x, float y, float width, float height)
    {
        g.DrawLine(pen, x + 4, y, x + 2, y + height);
        g.DrawLine(pen, x + 10, y, x + 8, y + height);
        g.DrawLine(pen, x, y + 5, x + width - 5, y + 5);
        g.DrawLine(pen, x, y + 11, x + width - 5, y + 11);
    }

    private static void DrawHorizontalZoomIcon(Graphics g, Pen pen)
    {
        g.DrawLine(pen, 7, 18, 27, 18);
        g.DrawLines(pen, [new PointF(11, 14), new PointF(7, 18), new PointF(11, 22)]);
        g.DrawLines(pen, [new PointF(23, 14), new PointF(27, 18), new PointF(23, 22)]);
    }

    private static void DrawVerticalZoomIcon(Graphics g, Pen pen)
    {
        g.DrawLine(pen, 17, 8, 17, 28);
        g.DrawLines(pen, [new PointF(13, 12), new PointF(17, 8), new PointF(21, 12)]);
        g.DrawLines(pen, [new PointF(13, 24), new PointF(17, 28), new PointF(21, 24)]);
    }

    private static void DrawZoomModifier(
        Graphics g,
        Pen pen,
        Brush brush,
        TransportIcon icon,
        float cx,
        float cy)
    {
        var isIn = icon is TransportIcon.TimeZoomIn or TransportIcon.AmpZoomIn;
        var isOut = icon is TransportIcon.TimeZoomOut or TransportIcon.AmpZoomOut;
        var isMax = icon is TransportIcon.TimeZoomMax or TransportIcon.AmpZoomMax;
        if (isIn || isOut)
        {
            using var badgeBrush = new SolidBrush(Color.FromArgb(220, UiColors.TransportBadgeBack));
            g.FillEllipse(badgeBrush, cx - 5, cy - 5, 10, 10);
            g.DrawLine(pen, cx - 3, cy, cx + 3, cy);
            if (isIn)
            {
                g.DrawLine(pen, cx, cy - 3, cx, cy + 3);
            }
        }
        else if (isMax)
        {
            g.FillRectangle(brush, cx - 3, cy - 3, 6, 6);
        }
        else
        {
            g.DrawEllipse(pen, cx - 4, cy - 4, 8, 8);
        }
    }
}
