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
    private bool _refreshing;
    private bool _exiting;
    private SettingsForm? _settings;
    private DashboardForm? _dashboard;

    public TrayApplicationContext(AppConfig config)
    {
        _config = config;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _menu = new ContextMenuStrip();
        _notifyIcon = new NotifyIcon
        {
            Icon = IconFactory.Create(null, null),
            Text = "外设电量监控",
            Visible = true,
            ContextMenuStrip = _menu
        };
        _notifyIcon.MouseClick += OnIconClick;
        _notifyIcon.DoubleClick += (_, _) => OpenDashboard();

        _timer = new System.Windows.Forms.Timer { Interval = Math.Max(30, _config.RefreshSeconds) * 1000 };
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
            if (_exiting) return;
            _uiContext.Post(_ =>
            {
                _readings = readings;
                ApplyReadings();
                _refreshing = false;
            }, null);
        });
    }

    private void ApplyReadings()
    {
        // 外环显示第一台设备，内环显示第二台设备
        var first = _readings.Count > 0 ? _readings[0].Reading.Percent : null;
        var second = _readings.Count > 1 ? _readings[1].Reading.Percent : null;

        var oldIcon = _notifyIcon.Icon;
        _notifyIcon.Icon = IconFactory.Create(first, second);
        oldIcon?.Dispose();

        var lines = _readings.Select(r =>
            $"{r.Device.Name}：{(r.Reading.Ok ? r.Reading.Percent + "%" : "未连接")}");
        var text = string.Join("  |  ", lines);
        if (text.Length > 120) text = text[..120];
        _notifyIcon.Text = string.IsNullOrWhiteSpace(text) ? "外设电量监控" : text;

        RebuildMenu();
    }

    private void ShowBalloon()
    {
        if (!_config.ShowNotifications) return;
        var text = string.Join("\n", _readings.Select(r =>
            $"{r.Device.Name}：{(r.Reading.Ok ? r.Reading.Describe() : r.Reading.Error ?? "未连接")}"));
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

        foreach (var (device, reading) in _readings)
        {
            var label = reading.Ok
                ? $"{device.Name}：{reading.Percent}%" + (reading.Charging == true ? "（充电中）" : "")
                : $"{device.Name}：{reading.Error ?? "未连接"}";
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => Refresh();
            _menu.Items.Add(item);
        }

        if (_readings.Count == 0)
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
            _timer.Interval = Math.Max(30, _config.RefreshSeconds) * 1000;
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
