namespace MgaWwiseIMImporter.UI;

/// <summary>
/// エディタ下の WAAPI / Wwise 接続ステータス表示。
/// </summary>
internal sealed class WaapiStatusBar : Panel
{
    private readonly Label _titleLabel;
    private readonly Label _versionLabel;
    private readonly Label _sepAfterVersion;
    private readonly Label _projectNameLabel;
    private readonly Label _sepAfterProject;
    private readonly Label _pathLabel;
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
    private bool _projectNameClickable;
    private bool _projectNameHovered;

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

        var detailFont = new Font("Yu Gothic UI", 9F);
        _versionLabel = CreateDetailLabel(detailFont);
        _sepAfterVersion = CreateDetailLabel(detailFont);
        _sepAfterVersion.Text = " - ";
        _projectNameLabel = CreateDetailLabel(detailFont);
        _projectNameLabel.MouseEnter += ProjectNameLabel_MouseEnter;
        _projectNameLabel.MouseLeave += ProjectNameLabel_MouseLeave;
        _projectNameLabel.Click += ProjectNameLabel_Click;
        _sepAfterProject = CreateDetailLabel(detailFont);
        _sepAfterProject.Text = " - ";
        _pathLabel = CreateDetailLabel(detailFont);

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
        Controls.Add(_pathLabel);
        Controls.Add(_sepAfterProject);
        Controls.Add(_projectNameLabel);
        Controls.Add(_sepAfterVersion);
        Controls.Add(_versionLabel);
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
            ApplyToolTips();
            LayoutLabels();
        };
    }

    /// <summary>Keep Target（鍵アイコン）の変更。</summary>
    public event EventHandler? KeepTargetChanged;

    /// <summary>ロック中プロジェクト名のクリック（開く／前面化）。</summary>
    public event EventHandler? ProjectNameClick;

    /// <summary>プロジェクト名リンクがクリック可能なら、クリック相当のイベントを発火する。</summary>
    public bool TryInvokeProjectNameClick()
    {
        if (!_projectNameClickable)
        {
            return false;
        }

        ProjectNameClick?.Invoke(this, EventArgs.Empty);
        return true;
    }

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
        ApplyDetailLabelBackColors();
        _keepStateLabel.ForeColor = UiColors.StatusBarTitleFore;
        _keepStateLabel.BackColor = BackColor;
        ApplyKeepLockColors();
        ApplyProjectNameColors();

        if (_badgeText == UiStrings.WaapiBadgeConnect)
        {
            SetBadgeConnected();
            ApplyPathForeColor(connected: true);
        }
        else if (_badgeText == UiStrings.WaapiBadgeDisconnect)
        {
            SetBadgeDisconnected();
            ApplyPathForeColor(connected: false);
        }
        else
        {
            SetBadgeNeutral();
            _pathLabel.ForeColor = UiColors.StatusBarTitleFore;
            _versionLabel.ForeColor = UiColors.StatusBarTitleFore;
            _sepAfterVersion.ForeColor = UiColors.StatusBarTitleFore;
            _sepAfterProject.ForeColor = UiColors.StatusBarTitleFore;
        }

        Invalidate();
    }

    private static Label CreateDetailLabel(Font font) =>
        new()
        {
            AutoSize = true,
            AutoEllipsis = false,
            Text = string.Empty,
            Font = font,
            TabStop = false,
        };

    private void ApplyDetailLabelBackColors()
    {
        var back = BackColor;
        _versionLabel.BackColor = back;
        _sepAfterVersion.BackColor = back;
        _projectNameLabel.BackColor = back;
        _sepAfterProject.BackColor = back;
        _pathLabel.BackColor = back;
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
        _toolTip.ApplyTheme();
        _toolTip.SetToolTip(
            _keepLockButton,
            _keepTargetChecked ? UiStrings.TipKeepTargetLock : UiStrings.TipKeepTargetUnlock);
        _toolTip.SetToolTip(
            _projectNameLabel,
            _projectNameClickable ? UiStrings.TipWwiseProjectNameOpen : string.Empty);
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

    private void ProjectNameLabel_Click(object? sender, EventArgs e)
    {
        if (!_projectNameClickable)
        {
            return;
        }

        ProjectNameClick?.Invoke(this, EventArgs.Empty);
    }

    private void ProjectNameLabel_MouseEnter(object? sender, EventArgs e)
    {
        if (!_projectNameClickable)
        {
            return;
        }

        _projectNameHovered = true;
        ApplyProjectNameColors();
    }

    private void ProjectNameLabel_MouseLeave(object? sender, EventArgs e)
    {
        _projectNameHovered = false;
        ApplyProjectNameColors();
    }

    private void ApplyProjectNameColors()
    {
        if (_projectNameClickable)
        {
            _projectNameLabel.ForeColor = _projectNameHovered
                ? UiColors.ActionLinkHoverFore
                : UiColors.ActionLinkFore;
            _projectNameLabel.Cursor = Cursors.Hand;
            return;
        }

        _projectNameLabel.ForeColor = UiColors.StatusBarDetailFore;
        _projectNameLabel.Cursor = Cursors.Default;
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

    private void ApplyPathForeColor(bool connected)
    {
        var error = !connected || _selectionMissing;
        var fore = error
            ? UiColors.StatusBarErrorDetailFore
            : UiColors.StatusBarDetailFore;
        _pathLabel.ForeColor = fore;
        if (!_projectNameClickable)
        {
            // 接続詳細の区切り・版は通常色。パスだけエラー色にし得る。
            var detailFore = connected ? UiColors.StatusBarDetailFore : UiColors.StatusBarErrorDetailFore;
            _versionLabel.ForeColor = detailFore;
            _sepAfterVersion.ForeColor = detailFore;
            _sepAfterProject.ForeColor = detailFore;
        }
        else
        {
            _versionLabel.ForeColor = connected
                ? UiColors.StatusBarDetailFore
                : UiColors.StatusBarErrorDetailFore;
            _sepAfterVersion.ForeColor = _versionLabel.ForeColor;
            _sepAfterProject.ForeColor = connected
                ? UiColors.StatusBarDetailFore
                : UiColors.StatusBarErrorDetailFore;
        }

        ApplyProjectNameColors();
    }

    public void SetPending()
    {
        _selectionMissing = false;
        _keepLockEnabled = false;
        _showKeepLock = false;
        SetProjectNameClickable(false);
        _badgeText = "…";
        SetBadgeNeutral();
        SetPlainDetail(UiStrings.StatusChecking, UiColors.StatusBarTitleFore);
    }

    public void SetSkipped()
    {
        _selectionMissing = false;
        _keepLockEnabled = false;
        _showKeepLock = false;
        SetProjectNameClickable(false);
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
            ApplyPathForeColor(connected: true);
            SetStructuredDetail(
                result.WwiseVersion,
                result.ProjectName,
                result.HasSelection ? result.SelectedPath : UiStrings.StatusNoneSelected,
                projectNameClickable: false);
        }
        else
        {
            _selectionMissing = false;
            _showKeepLock = false;
            SetProjectNameClickable(false);
            SetBadgeDisconnected();
            ApplyPathForeColor(connected: false);
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
        ApplyPathForeColor(connected: true);
        SetStructuredDetail(
            wwiseVersion,
            projectName,
            string.IsNullOrEmpty(selectedPath) ? UiStrings.StatusNoneSelected : selectedPath,
            projectNameClickable: keepTarget && projectName.Length > 0);
    }

    /// <summary>
    /// WAAPI 切断中でも Keep Target がオンなら、ロック中プロジェクト名と固定パスを維持表示する。
    /// </summary>
    public void UpdateDisconnectedKeepTarget(string projectName, string keptPath)
    {
        _keepLockEnabled = true;
        if (!_keepTargetChecked)
        {
            _keepTargetChecked = true;
            UpdateKeepLockAppearance();
        }

        _selectionMissing = keptPath.Length == 0;
        _showKeepLock = true;
        SetBadgeDisconnected();
        // バッジで切断を示す。固定パス自体はエラー表示にしない。
        _pathLabel.ForeColor = UiColors.StatusBarDetailFore;
        _sepAfterVersion.ForeColor = UiColors.StatusBarDetailFore;
        _sepAfterProject.ForeColor = UiColors.StatusBarDetailFore;
        _versionLabel.ForeColor = UiColors.StatusBarDetailFore;
        SetStructuredDetail(
            wwiseVersion: string.Empty,
            projectName,
            string.IsNullOrEmpty(keptPath) ? UiStrings.StatusNoneSelected : keptPath,
            projectNameClickable: projectName.Length > 0);
    }

    private void SetPlainDetail(string text, Color foreColor)
    {
        _showKeepLock = false;
        _versionLabel.Text = string.Empty;
        _versionLabel.Visible = false;
        _sepAfterVersion.Visible = false;
        _projectNameLabel.Text = string.Empty;
        _projectNameLabel.Visible = false;
        _sepAfterProject.Visible = false;
        _pathLabel.Text = text;
        _pathLabel.Visible = true;
        _pathLabel.ForeColor = foreColor;
        LayoutLabels();
    }

    private void SetStructuredDetail(
        string wwiseVersion,
        string projectName,
        string pathText,
        bool projectNameClickable)
    {
        _showKeepLock = true;
        var hasVersion = !string.IsNullOrWhiteSpace(wwiseVersion);
        if (hasVersion)
        {
            _versionLabel.Text = FormatDisplayVersion(wwiseVersion);
            _versionLabel.Visible = true;
        }
        else
        {
            _versionLabel.Text = string.Empty;
            _versionLabel.Visible = false;
        }

        var hasProject = projectName.Length > 0;
        _projectNameLabel.Text = projectName;
        _projectNameLabel.Visible = hasProject;
        // version の次は project または path。project の次は常に path。
        _sepAfterVersion.Visible = hasVersion;
        _sepAfterProject.Visible = hasProject;
        _pathLabel.Text = pathText;
        _pathLabel.Visible = true;
        SetProjectNameClickable(projectNameClickable && hasProject);
        LayoutLabels();
    }

    private void SetProjectNameClickable(bool clickable)
    {
        _projectNameClickable = clickable;
        if (!clickable)
        {
            _projectNameHovered = false;
        }

        ApplyProjectNameColors();
        ApplyToolTips();
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

        // 製品名だけのときはバージョン未取得（"Wwise vWwise" にしない）。
        if (text.Equals(wwise, StringComparison.OrdinalIgnoreCase)
            || text.Equals("Wwise", StringComparison.OrdinalIgnoreCase))
        {
            return wwise;
        }

        if (text.StartsWith("Wwise v", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("Wwise V", StringComparison.OrdinalIgnoreCase))
        {
            var ver = text["Wwise ".Length..].TrimStart('v', 'V', ' ');
            if (ver.Length == 0
                || ver.Equals(wwise, StringComparison.OrdinalIgnoreCase)
                || ver.Equals("Wwise", StringComparison.OrdinalIgnoreCase))
            {
                return wwise;
            }

            return $"{wwise} v{ver}";
        }

        if (text.StartsWith("Wwise ", StringComparison.OrdinalIgnoreCase))
        {
            var rest = text["Wwise ".Length..].Trim();
            if (rest.StartsWith('v') || rest.StartsWith('V'))
            {
                rest = rest[1..].TrimStart();
            }

            if (rest.Length == 0
                || rest.Equals(wwise, StringComparison.OrdinalIgnoreCase)
                || rest.Equals("Wwise", StringComparison.OrdinalIgnoreCase))
            {
                return wwise;
            }

            return $"{wwise} v{rest}";
        }

        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            var ver = text[1..].TrimStart();
            return ver.Length > 0 ? $"{wwise} v{ver}" : wwise;
        }

        return $"{wwise} v{text}";
    }

    private void LayoutLabels()
    {
        // Text / Visible 変更直後の PreferredSize 遅れ対策。
        // NoPadding だと Yu Gothic UI で実描画より狭く測られ "WAAPI"→"WAA" のように見える。
        const TextFormatFlags measureFlags =
            TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;

        static Size Measure(string text, Font font)
        {
            var size = TextRenderer.MeasureText(
                string.IsNullOrEmpty(text) ? " " : text,
                font,
                Size.Empty,
                measureFlags);
            // 末尾グリフのはみ出し余裕。
            return new Size(size.Width + 2, size.Height);
        }

        var titleSize = Measure(_titleLabel.Text, _titleLabel.Font);
        _titleLabel.AutoSize = false;
        _titleLabel.Size = titleSize;

        var titleMidY = Math.Max(0, (ClientSize.Height - _titleLabel.Height) / 2);
        _titleLabel.Location = new Point(Padding.Left, titleMidY);

        const int padX = 8;
        const int padY = 3;
        // Yu Gothic UI はメトリクス上の中央より文字が下に見えるため、塗りだけ少し下げる。
        const int fillNudgeY = 2;
        var badgeTextSize = TextRenderer.MeasureText(
            _badgeText,
            _badgeFont,
            Size.Empty,
            measureFlags);
        var textTop = titleMidY
            + Math.Max(0, (_titleLabel.Height - badgeTextSize.Height) / 2);
        var badgeWidth = badgeTextSize.Width + padX * 2;
        var badgeLeft = _titleLabel.Left + _titleLabel.Width + 8;
        _badgeTextBounds = new Rectangle(badgeLeft, textTop, badgeWidth, badgeTextSize.Height);
        _badgeFillBounds = new Rectangle(
            badgeLeft,
            textTop - padY + fillNudgeY,
            badgeWidth,
            badgeTextSize.Height + padY * 2);

        // 省略なし。全文＋末尾鍵＋状態ラベルをそのまま並べる。
        const int gapBeforeLock = 6;
        const int gapBeforeState = 2;
        var x = _badgeFillBounds.Right + 12;
        x = PlaceDetailLabel(_versionLabel, x, Measure);
        x = PlaceDetailLabel(_sepAfterVersion, x, Measure);
        x = PlaceDetailLabel(_projectNameLabel, x, Measure);
        x = PlaceDetailLabel(_sepAfterProject, x, Measure);
        x = PlaceDetailLabel(_pathLabel, x, Measure);

        _keepLockButton.Visible = _showKeepLock;
        _keepLockButton.Enabled = _keepLockEnabled;
        _keepStateLabel.Visible = _showKeepLock;
        if (_showKeepLock)
        {
            _keepStateLabel.Text = _keepTargetChecked
                ? UiStrings.KeepTargetOnLabel
                : UiStrings.KeepTargetOffLabel;
            var stateSize = Measure(_keepStateLabel.Text, _keepStateLabel.Font);
            _keepStateLabel.AutoSize = false;
            _keepStateLabel.Size = stateSize;

            var lockLeft = x + gapBeforeLock;
            var lockTop = Math.Max(0, (ClientSize.Height - _keepLockButton.Height) / 2);
            _keepLockButton.Location = new Point(lockLeft, lockTop);

            var stateMidY = Math.Max(0, (ClientSize.Height - _keepStateLabel.Height) / 2);
            _keepStateLabel.Location = new Point(
                _keepLockButton.Right + gapBeforeState,
                stateMidY);
        }

        Invalidate();
    }

    private int PlaceDetailLabel(Label label, int x, Func<string, Font, Size> measure)
    {
        if (!label.Visible || label.Text.Length == 0)
        {
            label.Visible = false;
            return x;
        }

        var size = measure(label.Text, label.Font);
        label.AutoSize = false;
        label.Size = size;
        label.Location = new Point(x, Math.Max(0, (ClientSize.Height - label.Height) / 2));
        label.Visible = true;
        return x + size.Width;
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
