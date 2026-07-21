namespace MgaWwiseIMImporter.UI;

/// <summary>
/// エディタ下の WAAPI / Wwise 接続ステータス表示。
/// </summary>
internal sealed class WaapiStatusBar : Panel
{
    private readonly Label _titleLabel;
    private readonly Label _detailLabel;
    private readonly TransportIconButton _keepLockButton;
    private readonly Label _keepStateLabel;
    private readonly DarkToolTip _toolTip = new();
    private readonly Font _badgeFont = new("Yu Gothic UI", 9F, FontStyle.Bold);

    private string _badgeText = "—";
    private Color _badgeBack = Color.Transparent;
    private Color _badgeFore = Color.Gray;
    private bool _badgeFilled;
    private Rectangle _badgeFillBounds;
    private Rectangle _badgeTextBounds;
    private bool _selectionMissing;
    private bool _keepTargetChecked;
    private bool _keepLockHovered;
    private bool _keepLockEnabled = true;
    private bool _showKeepLock;

    public WaapiStatusBar()
    {
        Height = 30;
        Dock = DockStyle.Bottom;
        Padding = new Padding(10, 0, 10, 0);
        TabStop = false;
        DoubleBuffered = true;

        _titleLabel = new Label
        {
            AutoSize = true,
            Text = UiStrings.WaapiTitle,
            Font = new Font("Yu Gothic UI", 9F, FontStyle.Bold),
            Location = new Point(10, 7),
            TabStop = false,
        };

        _detailLabel = new Label
        {
            AutoSize = true,
            AutoEllipsis = false,
            Text = string.Empty,
            Font = new Font("Yu Gothic UI", 9F),
            Location = new Point(100, 7),
            TabStop = false,
        };

        _keepLockButton = new TransportIconButton(TransportIcon.Unlock)
        {
            Size = new Size(24, 24),
            TabStop = false,
        };
        _keepLockButton.Click += KeepLockButton_Click;
        _keepLockButton.MouseEnter += (_, _) =>
        {
            _keepLockHovered = true;
            ApplyKeepLockColors();
        };
        _keepLockButton.MouseLeave += (_, _) =>
        {
            _keepLockHovered = false;
            ApplyKeepLockColors();
        };

        _keepStateLabel = new Label
        {
            AutoSize = true,
            Text = UiStrings.KeepTargetOffLabel,
            Font = new Font("Yu Gothic UI", 9F),
            TabStop = false,
        };

        Controls.Add(_keepStateLabel);
        Controls.Add(_keepLockButton);
        Controls.Add(_detailLabel);
        Controls.Add(_titleLabel);
        Resize += (_, _) => LayoutLabels();
        Paint += OnPaint;
        ApplyColors();
        ApplyToolTips();
        SetPending();
        UiStrings.LanguageChanged += (_, _) =>
        {
            if (IsDisposed)
            {
                return;
            }

            UpdateKeepLockAppearance();
            _titleLabel.Text = UiStrings.WaapiTitle;
            LayoutLabels();
        };
    }

    /// <summary>Keep Target（鍵アイコン）の変更。</summary>
    public event EventHandler? KeepTargetChanged;

    public bool KeepTargetChecked
    {
        get => _keepTargetChecked;
        set
        {
            if (_keepTargetChecked == value)
            {
                return;
            }

            _keepTargetChecked = value;
            UpdateKeepLockAppearance();
            LayoutLabels();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _badgeFont.Dispose();
            _toolTip.Dispose();
        }

        base.Dispose(disposing);
    }

    public void ApplyColors()
    {
        BackColor = UiColors.ForControlBack(UiColors.StatusBarBack);
        _titleLabel.ForeColor = UiColors.StatusBarTitleFore;
        _titleLabel.BackColor = BackColor;
        _detailLabel.BackColor = BackColor;
        _keepStateLabel.ForeColor = UiColors.StatusBarTitleFore;
        _keepStateLabel.BackColor = BackColor;
        ApplyKeepLockColors();

        if (_badgeText == UiStrings.WaapiBadgeConnect)
        {
            SetBadgeConnected();
            ApplyDetailForeColor(connected: true);
        }
        else if (_badgeText == UiStrings.WaapiBadgeDisconnect)
        {
            SetBadgeDisconnected();
            ApplyDetailForeColor(connected: false);
        }
        else
        {
            SetBadgeNeutral();
            _detailLabel.ForeColor = UiColors.StatusBarTitleFore;
        }

        Invalidate();
    }

    private void UpdateKeepLockAppearance()
    {
        _keepLockButton.SetIcon(_keepTargetChecked ? TransportIcon.Lock : TransportIcon.Unlock);
        _keepStateLabel.Text = _keepTargetChecked
            ? UiStrings.KeepTargetOnLabel
            : UiStrings.KeepTargetOffLabel;
        ApplyKeepLockColors();
        ApplyToolTips();
    }

    private void ApplyKeepLockColors()
    {
        var barBack = UiColors.ForControlBack(UiColors.StatusBarBack);
        _keepLockButton.BackColor = barBack;
        _keepLockButton.HoverBackColor = UiColors.ForControlBack(UiColors.TransportHoverBack);
        _keepLockButton.PressedBackColor = UiColors.ForControlBack(UiColors.TransportPressedBack);
        _keepLockButton.AccentColor = Color.Empty;

        if (_keepTargetChecked)
        {
            _keepLockButton.ForeColor = _keepLockHovered
                ? UiColors.KeepTargetLockHoverFore
                : UiColors.KeepTargetLockFore;
            _keepLockButton.ActiveForeColor = UiColors.KeepTargetLockFore;
        }
        else
        {
            _keepLockButton.ForeColor = _keepLockHovered
                ? UiColors.KeepTargetUnlockHoverFore
                : UiColors.KeepTargetUnlockFore;
            _keepLockButton.ActiveForeColor = UiColors.KeepTargetUnlockFore;
        }

        _keepLockButton.Invalidate();
    }

    private void ApplyToolTips()
    {
        _toolTip.SetToolTip(
            _keepLockButton,
            _keepTargetChecked ? UiStrings.TipKeepTargetLock : UiStrings.TipKeepTargetUnlock);
    }

    private void KeepLockButton_Click(object? sender, EventArgs e)
    {
        if (!_keepLockEnabled)
        {
            return;
        }

        KeepTargetChecked = !KeepTargetChecked;
        KeepTargetChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetBadgeConnected()
    {
        _badgeText = UiStrings.WaapiBadgeConnect;
        _badgeBack = UiColors.StatusBarConnectedBadgeBack;
        _badgeFore = Color.White;
        _badgeFilled = true;
    }

    private void SetBadgeDisconnected()
    {
        _badgeText = UiStrings.WaapiBadgeDisconnect;
        _badgeBack = UiColors.StatusBarDisconnectedBadgeBack;
        _badgeFore = Color.White;
        _badgeFilled = true;
    }

    private void SetBadgeNeutral()
    {
        _badgeBack = BackColor;
        _badgeFore = UiColors.StatusBarTitleFore;
        _badgeFilled = false;
    }

    private void ApplyDetailForeColor(bool connected)
    {
        _detailLabel.ForeColor = !connected || _selectionMissing
            ? UiColors.StatusBarErrorDetailFore
            : UiColors.StatusBarDetailFore;
    }

    public void SetPending()
    {
        _selectionMissing = false;
        _keepLockEnabled = false;
        _showKeepLock = false;
        _badgeText = "…";
        SetBadgeNeutral();
        SetPlainDetail(UiStrings.StatusChecking, UiColors.StatusBarTitleFore);
    }

    public void SetSkipped()
    {
        _selectionMissing = false;
        _keepLockEnabled = false;
        _showKeepLock = false;
        _badgeText = "—";
        SetBadgeNeutral();
        SetPlainDetail(UiStrings.StatusStartupCheckOff, UiColors.StatusBarTitleFore);
    }

    public void SetResult(WaapiProbeResult result)
    {
        _keepLockEnabled = result.Ok;
        if (result.Ok)
        {
            _selectionMissing = !result.HasSelection;
            SetBadgeConnected();
            ApplyDetailForeColor(connected: true);
            SetConnectedDetail(
                result.WwiseVersion,
                result.ProjectName,
                result.HasSelection ? result.SelectedPath : UiStrings.StatusNoneSelected);
        }
        else
        {
            _selectionMissing = false;
            _showKeepLock = false;
            SetBadgeDisconnected();
            ApplyDetailForeColor(connected: false);
            SetPlainDetail(
                result.Message.Length > 0 ? result.Message : UiStrings.StatusDisconnected,
                UiColors.StatusBarErrorDetailFore);
        }
    }

    /// <summary>
    /// 接続維持中の表示更新。
    /// <paramref name="keepTarget"/> が true のときは表示パスを固定先として扱い、
    /// Wwise 上の選択有無ではエラーにしない（末尾の鍵で固定状態を示す）。
    /// </summary>
    public void UpdateSelection(
        string wwiseVersion,
        string projectName,
        string selectedPath,
        bool keepTarget = false)
    {
        _keepLockEnabled = true;
        if (keepTarget != _keepTargetChecked)
        {
            _keepTargetChecked = keepTarget;
            UpdateKeepLockAppearance();
        }

        _selectionMissing = keepTarget
            ? selectedPath.Length == 0
            : string.IsNullOrEmpty(selectedPath);
        SetBadgeConnected();
        ApplyDetailForeColor(connected: true);
        SetConnectedDetail(
            wwiseVersion,
            projectName,
            string.IsNullOrEmpty(selectedPath) ? UiStrings.StatusNoneSelected : selectedPath);
    }

    private void SetPlainDetail(string text, Color foreColor)
    {
        _showKeepLock = false;
        _detailLabel.Text = text;
        _detailLabel.ForeColor = foreColor;
        LayoutLabels();
    }

    private void SetConnectedDetail(string wwiseVersion, string projectName, string pathText)
    {
        _showKeepLock = true;
        var parts = new List<string> { FormatDisplayVersion(wwiseVersion) };
        if (projectName.Length > 0)
        {
            parts.Add(projectName);
        }

        parts.Add(pathText);
        _detailLabel.Text = string.Join(" - ", parts);
        LayoutLabels();
    }

    /// <summary>表示用に <c>Wwise v2024.1.6</c> 形式へ揃える。</summary>
    private static string FormatDisplayVersion(string wwiseVersion)
    {
        var wwise = UiStrings.LabelWwise;
        if (string.IsNullOrWhiteSpace(wwiseVersion))
        {
            return wwise;
        }

        var text = wwiseVersion.Trim();
        if (text.StartsWith("Wwise v", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Wwise V", StringComparison.OrdinalIgnoreCase))
        {
            return $"{wwise} v" + text["Wwise ".Length..].TrimStart('v', 'V', ' ');
        }

        if (text.StartsWith("Wwise ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = text["Wwise ".Length..].Trim();
            if (rest.StartsWith('v') || rest.StartsWith('V'))
            {
                rest = rest[1..].TrimStart();
            }

            return rest.Length > 0 ? $"{wwise} v{rest}" : wwise;
        }

        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            return $"{wwise} v{text[1..].TrimStart()}";
        }

        return $"{wwise} v{text}";
    }

    private void LayoutLabels()
    {
        var titleMidY = Math.Max(0, (ClientSize.Height - _titleLabel.PreferredHeight) / 2);
        _titleLabel.Location = new Point(Padding.Left, titleMidY);

        const int padX = 8;
        const int padY = 3;
        // Yu Gothic UI はメトリクス上の中央より文字が下に見えるため、塗りだけ少し下げる。
        const int fillNudgeY = 2;
        var textSize = TextRenderer.MeasureText(
            _badgeText,
            _badgeFont,
            Size.Empty,
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        var textTop = titleMidY
            + Math.Max(0, (_titleLabel.PreferredHeight - textSize.Height) / 2);
        var badgeWidth = textSize.Width + padX * 2;
        var badgeLeft = _titleLabel.Right + 8;
        _badgeTextBounds = new Rectangle(badgeLeft, textTop, badgeWidth, textSize.Height);
        _badgeFillBounds = new Rectangle(
            badgeLeft,
            textTop - padY + fillNudgeY,
            badgeWidth,
            textSize.Height + padY * 2);

        // 省略なし。全文＋末尾鍵＋状態ラベルをそのまま並べる。
        const int gapBeforeLock = 6;
        const int gapBeforeState = 2;
        var detailX = _badgeFillBounds.Right + 12;
        _detailLabel.AutoSize = true;
        _detailLabel.AutoEllipsis = false;
        _detailLabel.Location = new Point(detailX, titleMidY);

        _keepLockButton.Visible = _showKeepLock;
        _keepLockButton.Enabled = _keepLockEnabled;
        _keepStateLabel.Visible = _showKeepLock;
        if (_showKeepLock)
        {
            _keepStateLabel.Text = _keepTargetChecked
                ? UiStrings.KeepTargetOnLabel
                : UiStrings.KeepTargetOffLabel;
            var lockLeft = _detailLabel.Right + gapBeforeLock;
            var lockTop = Math.Max(0, (ClientSize.Height - _keepLockButton.Height) / 2);
            _keepLockButton.Location = new Point(lockLeft, lockTop);

            var stateMidY = Math.Max(0, (ClientSize.Height - _keepStateLabel.PreferredHeight) / 2);
            _keepStateLabel.Location = new Point(_keepLockButton.Right + gapBeforeState, stateMidY);
        }

        Invalidate();
    }

    private void OnPaint(object? sender, PaintEventArgs e)
    {
        using var pen = new Pen(UiColors.StatusBarBorder);
        e.Graphics.DrawLine(pen, 0, 0, Width, 0);

        if (_badgeFilled)
        {
            using var brush = new SolidBrush(_badgeBack);
            e.Graphics.FillRectangle(brush, _badgeFillBounds);
        }

        TextRenderer.DrawText(
            e.Graphics,
            _badgeText,
            _badgeFont,
            _badgeTextBounds,
            _badgeFore,
            TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding
            | TextFormatFlags.SingleLine);
    }
}
