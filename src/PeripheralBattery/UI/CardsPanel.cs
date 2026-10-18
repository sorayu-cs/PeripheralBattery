using System.Drawing.Drawing2D;

namespace PeripheralBattery.UI;

/// <summary>深色滚动容器：隐藏系统滚动条，用细圆角滑块自绘，风格与面板统一。</summary>
public sealed class CardsPanel : Panel
{
    private static readonly Color PanelBack = Color.FromArgb(22, 23, 26);
    private static readonly Color BarTrack = Color.FromArgb(30, 31, 36);
    private static readonly Color BarThumb = Color.FromArgb(74, 76, 86);

    private readonly FlowLayoutPanel _content;
    private int _offset;

    public FlowLayoutPanel Content => _content;

    public CardsPanel()
    {
        DoubleBuffered = true;
        BackColor = PanelBack;

        _content = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
            BackColor = PanelBack,
            Padding = new Padding(0, 0, 0, 8),
            Location = new Point(0, 0),
            Width = Width
        };
        _content.SizeChanged += (_, _) => UpdateScroll();
        _content.ControlAdded += (_, _) => UpdateScroll();
        _content.ControlRemoved += (_, _) => UpdateScroll();
        Controls.Add(_content);

        Resize += (_, _) => UpdateScroll();
        MouseWheel += (_, e) => ScrollBy(-e.Delta / 120 * 56);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        ScrollBy(-e.Delta / 120 * 56);
        base.OnMouseWheel(e);
    }

    public void UpdateScroll()
    {
        // 右侧留出 12px 给自绘滚动条，避免被内容面板遮住
        _content.Width = Math.Max(0, Width - 12);

        // 直接累加卡片高度与边距，不依赖 FlowLayoutPanel 的异步布局结果
        var total = _content.Padding.Top + _content.Padding.Bottom;
        foreach (Control control in _content.Controls)
            total += control.Height + control.Margin.Top + control.Margin.Bottom;

        _content.Height = Math.Max(total, Height);

        var max = Math.Max(0, _content.Height - Height);
        _offset = Math.Clamp(_offset, 0, max);
        _content.Top = -_offset;
        Invalidate();
    }

    private void ScrollBy(int delta)
    {
        var max = Math.Max(0, _content.Height - Height);
        if (max <= 0) return;

        _offset = Math.Clamp(_offset + delta, 0, max);
        _content.Top = -_offset;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var total = _content.Height;
        if (total <= Height + 8) return;

        var track = new Rectangle(Width - 10, 4, 6, Height - 8);
        var thumbHeight = Math.Max(36, (int)(track.Height * (double)Height / total));
        var maxOffset = total - Height;
        var thumbTop = track.Top + (int)((track.Height - thumbHeight) * (_offset / (double)maxOffset));

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using (var trackPath = Rounded(track, 3))
        using (var trackBrush = new SolidBrush(BarTrack))
            e.Graphics.FillPath(trackBrush, trackPath);

        var thumb = new Rectangle(track.X, thumbTop, track.Width, thumbHeight);
        using (var thumbPath = Rounded(thumb, 3))
        using (var thumbBrush = new SolidBrush(BarThumb))
            e.Graphics.FillPath(thumbBrush, thumbPath);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
