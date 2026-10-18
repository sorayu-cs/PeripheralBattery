using System.Drawing.Drawing2D;
using System.Drawing.Text;
using PeripheralBattery.Core;

namespace PeripheralBattery.UI;

/// <summary>深色卡片：设备图标 + 名称 + 数据来源 + 大号电量 + 渐变进度条。</summary>
public sealed class DeviceCard : Control
{
    private static readonly Color CardBack = Color.FromArgb(31, 32, 36);
    private static readonly Color CardBorder = Color.FromArgb(46, 47, 53);
    private static readonly Color TextMain = Color.FromArgb(244, 244, 245);
    private static readonly Color TextSub = Color.FromArgb(148, 150, 158);
    private static readonly Color Green = Color.FromArgb(52, 211, 153);
    private static readonly Color Orange = Color.FromArgb(245, 158, 11);
    private static readonly Color Red = Color.FromArgb(239, 68, 68);

    private readonly DeviceProfile _device;
    private BatteryReading _reading;

    public DeviceProfile Device => _device;

    public DeviceCard(DeviceProfile device, BatteryReading reading)
    {
        _device = device;
        _reading = reading;
        DoubleBuffered = true;
        Height = 132;
        Margin = new Padding(12, 0, 12, 12);
        BackColor = Color.FromArgb(22, 23, 26);
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
    }

    public void Update(BatteryReading reading)
    {
        _reading = reading;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var card = new Rectangle(0, 0, Width - 1, Height - 13);
        using (var path = Rounded(card, 14))
        using (var back = new SolidBrush(CardBack))
        using (var border = new Pen(CardBorder))
        {
            g.FillPath(back, path);
            g.DrawPath(border, path);
        }

        var title = _device.Name;
        if (!title.Contains("鼠标") && !title.Contains("键盘") && !title.Contains("耳机"))
            title += " " + DeviceTypeLabel();

        using (var nameFont = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold))
        using (var nameBrush = new SolidBrush(TextMain))
        {
            g.DrawString(title, nameFont, nameBrush, 24, 26);
        }

        using (var subFont = new Font("Microsoft YaHei UI", 8.5f))
        using (var subBrush = new SolidBrush(TextSub))
        {
            var source = _reading.Ok ? _reading.Source : _reading.Error ?? "未连接";
            g.DrawString(Trim(source, 52), subFont, subBrush, 26, 55);
        }

        var percent = _reading.Percent;
        var text = percent?.ToString() ?? "--";
        using (var pctFont = new Font("Segoe UI", 30f, FontStyle.Bold))
        using (var pctBrush = new SolidBrush(percent is null || _reading.Disconnected ? TextSub
                   : percent >= 60 ? Green : percent >= 30 ? Orange : Red))
        {
            var size = g.MeasureString(text, pctFont);
            var x = Width - 88 - size.Width;
            g.DrawString(text, pctFont, pctBrush, x, 14);
            using var unitFont = new Font("Segoe UI", 10.5f);
            using var unitBrush = new SolidBrush(TextSub);
            g.DrawString("%", unitFont, unitBrush, x + size.Width - 2, 44);
        }

        var bar = new Rectangle(24, card.Height - 26, Width - 64, 6);
        DrawBar(g, bar, _reading.Disconnected ? null : percent);
    }

    private string DeviceTypeLabel()
    {
        if (_device.Protocol == "logitech_hidpp") return "无线鼠标";
        if (_device.Protocol == "mchose_qhw") return "无线键盘";
        if (_device.Name.Contains("鼠标")) return "无线鼠标";
        if (_device.Name.Contains("键盘")) return "无线键盘";
        if (_device.Name.Contains("耳机")) return "无线耳机";
        return "无线设备";
    }

    private static void DrawBar(Graphics g, Rectangle rect, int? percent)
    {
        using (var back = new SolidBrush(Color.FromArgb(55, 56, 63)))
        using (var backPath = Rounded(rect, 3))
        {
            g.FillPath(back, backPath);
        }

        if (percent is null or <= 0) return;

        var width = Math.Max(6, (int)(rect.Width * Math.Clamp(percent.Value, 0, 100) / 100.0));
        var fill = new Rectangle(rect.X, rect.Y, width, rect.Height);
        using var gradient = new LinearGradientBrush(fill, Color.FromArgb(52, 211, 153), Color.FromArgb(16, 185, 129), 0f);
        using var fillPath = Rounded(fill, 3);
        g.FillPath(gradient, fillPath);
    }

    private static GraphicsPath Rounded(Rectangle rect, int radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static string Trim(string text, int max)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
