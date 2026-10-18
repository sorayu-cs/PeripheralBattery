using System.Runtime.InteropServices;
using PeripheralBattery.Core;
using PeripheralBattery.Tray;

namespace PeripheralBattery;

internal static class Program
{
    private const string MutexName = "PeripheralBattery_SingleInstance";

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string lpString);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr HwndBroadcast = new(0xFFFF);

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            // 界面全部自绘，不需要系统视觉样式；单文件自包含模式下
            // EnableVisualStyles 会写 %TEMP% 激活上下文，受限环境会失败。
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.SetCompatibleTextRenderingDefault(false);

            var config = AppConfig.Load();

            if (args.Any(a => a.Equals("--once", StringComparison.OrdinalIgnoreCase)))
                return RunOnce();

            if (args.Any(a => a.Equals("--icon-preview", StringComparison.OrdinalIgnoreCase)))
            {
                var index = Array.FindIndex(args, a => a.Equals("--icon-preview", StringComparison.OrdinalIgnoreCase));
                var path = index + 1 < args.Length ? args[index + 1] : Path.Combine(Path.GetTempPath(), "pb-icon.png");
                using var icon = UI.IconFactory.Create(80, 90);
                using var bitmap = icon.ToBitmap();
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                using var small = new Bitmap(bitmap, new Size(16, 16));
                small.Save(Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + "-16.png"),
                    System.Drawing.Imaging.ImageFormat.Png);
                return 0;
            }

            using var mutex = new Mutex(true, MutexName, out var created);
            if (!created)
            {
                // 已有实例：广播消息让它把设置窗口弹到前台
                var message = RegisterWindowMessage("PeripheralBattery_ShowSettings");
                if (message != 0) PostMessage(HwndBroadcast, message, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }

            var context = new TrayApplicationContext(config);
            if (args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
                context.ShowSettings();
            else if (args.Any(a => a.Equals("--dashboard", StringComparison.OrdinalIgnoreCase)))
                context.ShowDashboard();

            Application.Run(context);
            GC.KeepAlive(mutex);
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "crash.txt"), ex.ToString());
            }
            catch
            {
                // 忽略
            }

            MessageBox.Show(ex.ToString(), "外设电量监控启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
    private static int RunOnce()
    {
        AttachConsole(-1);
        var config = AppConfig.Load();
        var service = new DeviceBatteryService();
        service.RefreshInterfaces();

        var lines = new List<string>();
        foreach (var device in config.Devices.Where(d => d.Enabled))
        {
            var reading = service.Read(device);
            lines.Add(reading.Ok
                ? $"{device.Name}：{reading.Percent}%{(reading.Charging == true ? "（充电中）" : "")}  [{reading.Source}]"
                : $"{device.Name}：读取失败 - {reading.Error}");
        }

        var text = string.Join(Environment.NewLine, lines);
        if (text.Length == 0) text = "没有启用中的设备。";

        try
        {
            Console.WriteLine(text);
        }
        catch
        {
            // 没有控制台时静默退出，不弹窗阻塞
        }

        return 0;
    }
}
