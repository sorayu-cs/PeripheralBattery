using System.Runtime.InteropServices;
using PeripheralBattery.Core;
using PeripheralBattery.UI;

namespace PeripheralBattery.Tray;

/// <summary>托盘常驻程序：启动无窗口，只在通知区域显示电量图标。</summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int RegisterWindowMessage(string lpString);

    private readonly AppConfig _config;
    private readonly DeviceBatteryService _service = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly HiddenMessageWindow _showSettingsWindow;
    private readonly SynchronizationContext _uiContext;
    private List<(DeviceProfile Device, BatteryReading Reading)> _readings = new();
    private List<BluetoothBatteryInfo> _bluetooth = new();
    private List<(string Name, BatteryReading Reading)> _display = new();
    private bool _refreshing;
    private bool _exiting;
    private SettingsForm? _settings;
    private DashboardForm? _dashboard;

    public TrayApplicationContext(AppConfig config)
    {
        _config = config;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _menu = new ContextMenuStrip
        {
            Renderer = new DarkMenuRenderer(),
            ShowImageMargin = false,
            BackColor = Color.FromArgb(31, 32, 36),
            ForeColor = Color.FromArgb(230, 231, 234),
            Padding = new Padding(0, 4, 0, 4),
            Font = new Font("Microsoft YaHei UI", 9f)
        };
        _notifyIcon = new NotifyIcon
        {
            Icon = IconFactory.Create(null, null),
            Text = "外设电量监控",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.MouseClick += OnIconClick;
        _notifyIcon.DoubleClick += (_, _) => OpenDashboard();

        // 托盘比面板更新更勤，连接模式切换时图标能尽快跟上
        _timer = new System.Windows.Forms.Timer
        {
            Interval = Math.Min(Math.Max(10, _config.RefreshSeconds), 15) * 1000
        };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        // 监听第二个实例发来的“打开设置”广播
        _showSettingsWindow = new HiddenMessageWindow(RegisterWindowMessage("PeripheralBattery_ShowSettings"), ShowSettings);

        RebuildMenu();
        Refresh();
    }

    /// <summary>无可见界面的消息窗口，用于接收其他实例的广播。</summary>
    private sealed class HiddenMessageWindow : NativeWindow
    {
        private readonly int _message;
        private readonly Action _onMessage;

        public HiddenMessageWindow(int message, Action onMessage)
        {
            _message = message;
            _onMessage = onMessage;
            CreateHandle(new CreateParams
            {
                Caption = "PeripheralBattery.Message",
                X = -32000,
                Y = -32000,
                Width = 1,
                Height = 1
            });
        }

        protected override void WndProc(ref Message m)
        {
            if (_message != 0 && m.Msg == _message)
            {
                _onMessage();
                return;
            }

            base.WndProc(ref m);
        }

        public void Destroy() => DestroyHandle();
    }

    private void OnIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Refresh();
            ShowBalloon();
        }
    }

    public void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;

        Task.Run(() =>
        {
            var readings = _service.ReadAll(_config.Devices);
            var bluetooth = BluetoothBattery.Discover();
            if (_exiting) return;
            _uiContext.Post(_ =>
            {
                _readings = readings;
                _bluetooth = bluetooth;
                ApplyReadings();
                _refreshing = false;
            }, null);
        });
    }

    private void ApplyReadings()
    {
        _display = BuildDisplay();

        // 外环显示第一台设备，内环显示第二台设备（2.4G 或蓝牙，与主面板一致）
        var first = _display.Count > 0 ? _display[0].Reading.Percent : null;
        var second = _display.Count > 1 ? _display[1].Reading.Percent : null;

        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = IconFactory.Create(first, second);
        oldIcon?.Dispose();

        var lines = _display.Select(d =>
            $"{d.Name}：{(d.Reading.Ok ? d.Reading.Percent + "%" : "未连接")}");
        var text = string.Join("  |  ", lines);
        if (text.Length > 120) text = text[..120];
        _notifyIcon.Text = string.IsNullOrWhiteSpace(text) ? "外设电量监控" : text;

        RebuildMenu();
    }

    /// <summary>与主面板相同的显示规则：2.4G 优先，读不到时用同一设备的蓝牙电量。</summary>
    private List<(string Name, BatteryReading Reading)> BuildDisplay()
    {
        var list = new List<(string Name, BatteryReading Reading)>();

        foreach (var (device, reading) in _readings)
        {
            var bt = _bluetooth.FirstOrDefault(b => DeviceMatcher.SameDevice(device.Name, b.Name));
            if (bt != null && (!reading.Ok || reading.Disconnected))
            {
                list.Add((bt.Name, new BatteryReading
                {
                    Percent = bt.Percent,
                    Charging = bt.Charging,
                    Source = "蓝牙 GATT"
                }));
                continue;
            }

            list.Add((device.Name, reading));
        }

        foreach (var bt in _bluetooth)
        {
            if (list.Any(d => DeviceMatcher.SameDevice(d.Name, bt.Name))) continue;
            list.Add((bt.Name, new BatteryReading
            {
                Percent = bt.Percent,
                Charging = bt.Charging,
                Source = "蓝牙 GATT"
            }));
        }

        return list;
    }

    private void ShowBalloon()
    {
        if (!_config.ShowNotifications) return;
        var text = string.Join("\n", _display.Select(d =>
            $"{d.Name}：{(d.Reading.Ok ? d.Reading.Describe() : d.Reading.Error ?? "未连接")}"));
        if (string.IsNullOrWhiteSpace(text)) text = "尚未读取到设备";
        _notifyIcon.BalloonTipTitle = "外设电量";
        _notifyIcon.BalloonTipText = text;
        _notifyIcon.ShowBalloonTip(3000);
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();

        var title = new ToolStripMenuItem("外设电量监控") { Enabled = false };
        _menu.Items.Add(title);

        var dashboard = new ToolStripMenuItem("打开面板");
        dashboard.Click += (_, _) => OpenDashboard();
        _menu.Items.Add(dashboard);

        foreach (var (name, reading) in _display)
        {
            var label = reading.Ok
                ? $"{name}：{reading.Percent}%" + (reading.Charging == true ? "（充电中）" : "")
                : $"{name}：{reading.Error ?? "未连接"}";
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => Refresh();
            _menu.Items.Add(item);
        }

        if (_display.Count == 0)
        {
            _menu.Items.Add(new ToolStripMenuItem("（尚未读取）") { Enabled = false });
        }

        _menu.Items.Add(new ToolStripSeparator());

        var refresh = new ToolStripMenuItem("立即刷新");
        refresh.Click += (_, _) => Refresh();
        _menu.Items.Add(refresh);

        var settings = new ToolStripMenuItem("设置…");
        settings.Click += (_, _) => OpenSettings();
        _menu.Items.Add(settings);

        var autostart = new ToolStripMenuItem("开机自启")
        {
            Checked = Autostart.IsEnabled,
            CheckOnClick = true
        };
        autostart.Click += (_, _) =>
        {
            if (autostart.Checked)
            {
                if (!Autostart.Enable(out var error)) ShowError(error);
            }
            else if (!Autostart.Disable(out var error))
            {
                ShowError(error);
            }

            _config.Autostart = Autostart.IsEnabled;
            _config.Save();
        };
        _menu.Items.Add(autostart);

        _menu.Items.Add(new ToolStripSeparator());

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => ExitApplication();
        _menu.Items.Add(exit);
    }

    public void ShowSettings() => OpenSettings();

    public void ShowDashboard() => OpenDashboard();

    private void OpenDashboard()
    {
        if (_dashboard is { IsDisposed: false })
        {
            if (!_dashboard.Visible) _dashboard.Show();
            if (_dashboard.WindowState == FormWindowState.Minimized)
                _dashboard.WindowState = FormWindowState.Normal;
            _dashboard.Activate();
            _dashboard.BringToFront();
            return;
        }

        _dashboard = new DashboardForm(_config, _service, OpenSettings, ExitApplication);
        _dashboard.Show();
    }

    private void OpenSettings()
    {
        if (_settings is { Visible: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsForm(_config, _service);
        _settings.FormClosed += (_, _) =>
        {
            _settings = null;
            _timer.Interval = Math.Min(Math.Max(10, _config.RefreshSeconds), 15) * 1000;
            Refresh();
        };
        _settings.Show();
        _settings.Activate();
    }

    private void ShowError(string message)
    {
        MessageBox.Show(message, "外设电量监控", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void ExitApplication()
    {
        _exiting = true;
        _timer.Stop();
        _dashboard?.CloseForExit();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _notifyIcon.Dispose();
            _menu.Dispose();
            _showSettingsWindow?.Destroy();
        }

        base.Dispose(disposing);
    }
}
