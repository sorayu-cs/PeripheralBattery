using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace PeripheralBattery.UI;

/// <summary>托盘图标：双层电量圆环（外环第一台设备，内环第二台设备）。</summary>
public static class IconFactory
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static readonly Color Green = Color.FromArgb(62, 224, 127);
    private static readonly Color Orange = Color.FromArgb(245, 158, 11);
    private static readonly Color Red = Color.FromArgb(239, 68, 68);
    private static readonly Color Track = Color.FromArgb(74, 76, 86);

    public static Icon Create(int? first, int? second)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            // 外环：第一台设备
            DrawRing(g, new Rectangle(2, 2, 28, 28), first, 3.6f);
            // 内环：第二台设备
            DrawRing(g, new Rectangle(9, 9, 14, 14), second, 3.2f);
        }

        return ToIcon(bitmap);
    }

    private static void DrawRing(Graphics g, Rectangle rect, int? percent, float width)
    {
        using (var track = new Pen(Track, width))
        {
            g.DrawEllipse(track, rect);
        }

        if (percent is null) return;

        var color = percent >= 60 ? Green : percent >= 30 ? Orange : Red;
        var sweep = 360f * Math.Clamp(percent.Value, 0, 100) / 100f;
        if (sweep >= 360f) sweep = 359.9f;

        using var pen = new Pen(color, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, rect, -90f, sweep);
    }

    /// <summary>exe 图标：蓝色电池。</summary>
    public static Icon CreateBatteryIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var body = new SolidBrush(Color.FromArgb(52, 152, 219));
            using var outline = new Pen(Color.White, 2f);
            var rect = new Rectangle(5, 8, 20, 16);
            g.FillRectangle(body, rect);
            g.DrawRectangle(outline, rect);
            g.FillRectangle(new SolidBrush(Color.White), 25, 13, 3, 6);
        }

        return ToIcon(bitmap);
    }

    private static Icon ToIcon(Bitmap bitmap)
    {
        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }
}
