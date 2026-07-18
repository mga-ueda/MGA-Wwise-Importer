namespace MgaWwiseIMImporter.UI;

/// <summary>
/// 波形表示範囲専用の、常時表示する細い水平スクロールバー。
/// </summary>
internal sealed class ThinHorizontalScrollBar : Control
{
    private const int HorizontalInset = 3;
    private const int MinimumThumbWidth = 24;
    private const int ThumbHeight = 8;

    private double _viewStart;
    private double _viewSpan = 1d;
    private bool _hovered;
    private bool _dragging;
    private int _dragOffsetX;

    public ThinHorizontalScrollBar()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.UserPaint,
            true);
        Height = 15;
        TabStop = false;
        Cursor = Cursors.Default;
        SetStyle(ControlStyles.Selectable, false);
    }

    public event EventHandler<double>? ScrollRequested;

    public event EventHandler? ScrollCompleted;

    public void SetViewport(double viewStart, double viewSpan)
    {
        _viewSpan = Math.Clamp(viewSpan, 0d, 1d);
        if (!_dragging)
        {
            _viewStart = Math.Clamp(viewStart, 0d, Math.Max(0d, 1d - _viewSpan));
        }
        Invalidate();
    }

    public void ApplyColors() => Invalidate();

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(UiColors.ForControlBack(UiColors.WaveformScrollTrack));

        var thumb = GetThumbBounds();
        if (thumb.Width <= 0 || thumb.Height <= 0)
        {
            return;
        }

        var color = _hovered
            ? UiColors.WaveformScrollThumbHover
            : UiColors.WaveformScrollThumb;
        using var brush = new SolidBrush(UiColors.ForControlBack(color));
        e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var capSize = Math.Min(thumb.Height, thumb.Width);
        if (thumb.Width <= capSize)
        {
            e.Graphics.FillEllipse(brush, thumb);
            return;
        }

        e.Graphics.FillRectangle(
            brush,
            thumb.Left + capSize / 2,
            thumb.Top,
            thumb.Width - capSize,
            thumb.Height);
        e.Graphics.FillEllipse(brush, thumb.Left, thumb.Top, capSize, thumb.Height);
        e.Graphics.FillEllipse(
            brush,
            thumb.Right - capSize,
            thumb.Top,
            capSize,
            thumb.Height);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var thumb = GetThumbBounds();
        if (thumb.IsEmpty)
        {
            return;
        }

        if (thumb.Contains(e.Location))
        {
            _dragging = true;
            _dragOffsetX = e.X - thumb.Left;
            Capture = true;
            Invalidate();
            return;
        }

        var page = _viewSpan;
        RequestScroll(e.X < thumb.Left ? _viewStart - page : _viewStart + page);
        ScrollCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            RequestScroll(StartFromThumbLeft(e.X - _dragOffsetX));
            return;
        }

        var hovered = GetThumbBounds().Contains(e.Location);
        if (_hovered != hovered)
        {
            _hovered = hovered;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_dragging)
        {
            return;
        }

        _dragging = false;
        Capture = false;
        _hovered = GetThumbBounds().Contains(e.Location);
        Invalidate();
        ScrollCompleted?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_dragging && _hovered)
        {
            _hovered = false;
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        var notches = Math.Max(1d, Math.Abs(e.Delta) / 120d);
        var distance = _viewSpan * 0.1d * notches;
        RequestScroll(_viewStart + (e.Delta < 0 ? distance : -distance));
        ScrollCompleted?.Invoke(this, EventArgs.Empty);
    }

    private Rectangle GetTrackBounds() => new(
        HorizontalInset,
        Math.Max(0, (ClientSize.Height - ThumbHeight) / 2),
        Math.Max(0, ClientSize.Width - HorizontalInset * 2),
        Math.Min(ThumbHeight, ClientSize.Height));

    private Rectangle GetThumbBounds()
    {
        var track = GetTrackBounds();
        if (track.Width <= 0 || track.Height <= 0)
        {
            return Rectangle.Empty;
        }

        if (_viewSpan >= 1d - 1e-9)
        {
            return track;
        }

        var thumbWidth = Math.Clamp(
            (int)Math.Round(track.Width * _viewSpan),
            Math.Min(MinimumThumbWidth, track.Width),
            track.Width);
        var travel = Math.Max(0, track.Width - thumbWidth);
        var maxStart = Math.Max(0d, 1d - _viewSpan);
        var ratio = maxStart > 1e-12 ? _viewStart / maxStart : 0d;
        var left = track.Left + (int)Math.Round(travel * ratio);
        return new Rectangle(left, track.Top, thumbWidth, track.Height);
    }

    private double StartFromThumbLeft(int thumbLeft)
    {
        var track = GetTrackBounds();
        var thumb = GetThumbBounds();
        var travel = Math.Max(0, track.Width - thumb.Width);
        if (travel == 0)
        {
            return 0d;
        }

        var pixel = Math.Clamp(thumbLeft - track.Left, 0, travel);
        return pixel / (double)travel * Math.Max(0d, 1d - _viewSpan);
    }

    private void RequestScroll(double viewStart)
    {
        var clamped = Math.Clamp(viewStart, 0d, Math.Max(0d, 1d - _viewSpan));
        if (Math.Abs(clamped - _viewStart) < 1e-12)
        {
            return;
        }

        _viewStart = clamped;
        Invalidate();
        ScrollRequested?.Invoke(this, clamped);
    }
}
