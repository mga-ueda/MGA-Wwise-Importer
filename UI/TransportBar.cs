using System.Drawing.Drawing2D;

namespace MgaWwiseIMImporter.UI;

internal enum TransportCommand
{
    TogglePlayback,
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
}

internal readonly record struct TransportPositionInfo(
    double Bpm,
    int Numerator,
    int Denominator,
    int Bar,
    int Beat,
    int Subdivision,
    TimeSpan Time);

/// <summary>波形操作のショートカットをアイコンで実行するフラットなトランスポートバー。</summary>
internal sealed class TransportBar : UserControl
{
    private readonly FlowLayoutPanel _groups = new();
    private readonly DarkToolTip _toolTip = new();
    private readonly TransportPositionDisplay _positionDisplay = new();
    private readonly Dictionary<TransportCommand, TransportIconButton> _commandButtons = [];
    private readonly TransportIconButton _playButton;

    public TransportBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);

        AutoScroll = true;
        BackColor = UiColors.ForControlBack(UiColors.TransportBack);
        Height = 40;
        Padding = new Padding(8, 5, 8, 5);
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
            "TRANSPORT",
            (TransportCommand.TogglePlayback, TransportIcon.PlayPause, "再生 / 一時停止  [Space]"),
            (TransportCommand.JumpToBar, TransportIcon.JumpToBar, "小節番号を指定して移動  [G]"));

        AddGroup(
            "NAVIGATION",
            (TransportCommand.GoToStart, TransportIcon.GoToStart, "先頭へ移動  [Ctrl+Home]"),
            (TransportCommand.PreviousRegion, TransportIcon.PreviousRegion, "前のリージョン境界  [Ctrl+←]"),
            (TransportCommand.PreviousBar, TransportIcon.PreviousBar, "前の小節  [Home]"),
            (TransportCommand.PreviousPage, TransportIcon.PreviousPage, "前の表示ページ  [Page Up]"),
            (TransportCommand.NextPage, TransportIcon.NextPage, "次の表示ページ  [Page Down]"),
            (TransportCommand.NextBar, TransportIcon.NextBar, "次の小節  [End]"),
            (TransportCommand.NextRegion, TransportIcon.NextRegion, "次のリージョン境界  [Ctrl+→]"),
            (TransportCommand.GoToEnd, TransportIcon.GoToEnd, "末尾へ移動  [Ctrl+End]"));

        AddGroup(
            "TIME ZOOM",
            (TransportCommand.TimeZoomIn, TransportIcon.TimeZoomIn, "時間軸を拡大  [↑]"),
            (TransportCommand.TimeZoomOut, TransportIcon.TimeZoomOut, "時間軸を縮小  [↓]"),
            (TransportCommand.TimeZoomMax, TransportIcon.TimeZoomMax, "時間軸を最大拡大  [Ctrl+↑]"),
            (TransportCommand.TimeZoomReset, TransportIcon.TimeZoomReset, "時間軸を全体表示  [Ctrl+↓]"));

        AddGroup(
            "AMP ZOOM",
            (TransportCommand.AmpZoomIn, TransportIcon.AmpZoomIn, "振幅を拡大  [Shift+↑]"),
            (TransportCommand.AmpZoomOut, TransportIcon.AmpZoomOut, "振幅を縮小  [Shift+↓]"),
            (TransportCommand.AmpZoomMax, TransportIcon.AmpZoomMax, "振幅を最大拡大  [Ctrl+Shift+↑]"),
            (TransportCommand.AmpZoomReset, TransportIcon.AmpZoomReset, "振幅を既定に戻す  [Ctrl+Shift+↓]"));

        ApplyColors();
    }

    public event EventHandler<TransportCommand>? CommandInvoked;

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

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        using var border = new Pen(UiColors.ForControlBack(UiColors.TransportBorder));
        e.Graphics.DrawLine(border, 0, 0, ClientSize.Width, 0);
        e.Graphics.DrawLine(border, 0, ClientSize.Height - 1, ClientSize.Width, ClientSize.Height - 1);
    }

    private TransportIconButton AddGroup(
        string title,
        params (TransportCommand Command, TransportIcon Icon, string ToolTip)[] definitions)
    {
        const int buttonHeight = 30;
        const int buttonPitch = 31;
        using var groupFont = new Font("Yu Gothic UI", 7F, FontStyle.Bold);
        var labelWidth = TextRenderer.MeasureText(
            title,
            groupFont,
            Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 2;
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
            var button = new TransportIconButton(definition.Icon)
            {
                AccessibleName = definition.ToolTip,
                Margin = new Padding(0, 0, 1, 0),
                Tag = definition.Command,
            };
            button.Click += (_, _) => CommandInvoked?.Invoke(this, definition.Command);
            _toolTip.SetToolTip(button, definition.ToolTip);
            buttons.Controls.Add(button);
            _commandButtons[definition.Command] = button;
            first ??= button;
        }

        group.Controls.Add(buttons);
        group.Controls.Add(label);
        _groups.Controls.Add(group);
        return first!;
    }
}

internal sealed class TransportPositionDisplay : Control
{
    private TransportPositionInfo? _position;

    public TransportPositionDisplay()
    {
        AccessibleName = "Tempo, time signature, musical position and elapsed time";
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
        var bpm = position is { } p
            ? Math.Round(p.Bpm).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : "---";
        var signature = position is { } signaturePosition
            ? $"{signaturePosition.Numerator}/{signaturePosition.Denominator}"
            : "--/--";
        var musicalPosition = position is { } musical
            ? $"{Math.Max(0, musical.Bar):000}:{musical.Beat}:{musical.Subdivision}"
            : "000:1:1";
        var elapsed = position?.Time ?? TimeSpan.Zero;
        var hours = Math.Max(0L, (long)elapsed.TotalHours);
        var time = $"{hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}.{elapsed.Milliseconds:000}";

        using var iconPen = new Pen(ForeColor, 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        using var iconBrush = new SolidBrush(ForeColor);

        var iconTop = Math.Max(0f, (Height - 30f) / 2f);
        DrawQuarterNote(g, iconPen, iconBrush, 5, iconTop + 7f);
        DrawText(g, bpm, new Rectangle(22, 0, 32, Height));
        DrawText(g, signature, new Rectangle(64, 0, 38, Height));
        DrawText(g, musicalPosition, new Rectangle(107, 0, 74, Height));
        DrawText(g, time, new Rectangle(186, 0, 124, Height));
    }

    protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
    {
        // 描画座標はピクセル基準なので、DPI拡大時も右側に空白を増やさない。
        base.ScaleControl(factor, specified & ~BoundsSpecified.Width);
    }

    private void DrawText(Graphics g, string text, Rectangle bounds)
    {
        TextRenderer.DrawText(
            g,
            text,
            Font,
            bounds,
            ForeColor,
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
        TabStop = true;
        UseVisualStyleBackColor = false;
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint,
            true);
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

    public TransportIcon Icon { get; }
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
