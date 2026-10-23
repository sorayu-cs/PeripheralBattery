using System.Runtime.InteropServices;
using PeripheralBattery.Core;

namespace PeripheralBattery.UI;

/// <summary>主面板：深色卡片式电量总览。</summary>
public sealed class DashboardForm : Form
{
    private static readonly Color BackColorDark = Color.FromArgb(22, 23, 26);
    private static readonly Color BarColor = Color.FromArgb(27, 28, 32);
    private static readonly Color TextMain = Color.FromArgb(244, 244, 245);
    private static readonly Color TextSub = Color.FromArgb(148, 150, 158);
    private static readonly Color ButtonColor = Color.FromArgb(45, 46, 52);

    private readonly AppConfig _config;
    private readonly DeviceBatteryService _service;
    private readonly Action _openSettings;
    private readonly Action _exit;
    private readonly CardsPanel _cardsPanel;
    private readonly Label _updated;
    private readonly Label _subtitle;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _chargeTimer;
    private readonly System.Windows.Forms.Timer _inputTimer;
    private DateTime _lastInputRefresh = DateTime.MinValue;
    private bool _busy;
    private bool _closingToTray;
    private string _lastSignature = "";
    private bool _autoSized;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public DashboardForm(AppConfig config, DeviceBatteryService service, Action openSettings, Action exit)
    {
        _config = config;
        _service = service;
        _openSettings = openSettings;
        _exit = exit;

        Text = "外设电量";
        ClientSize = new Size(720, 680);
        MinimumSize = new Size(600, 500);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BackColorDark;
        ForeColor = TextMain;
        Font = new Font("Microsoft YaHei UI", 9f);
        DoubleBuffered = true;

        var topPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 94,
            BackColor = BackColorDark
        };

        var title = new Label
        {
            Text = "外设电量",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 24f, FontStyle.Bold),
            ForeColor = TextMain,
            Location = new Point(28, 18)
        };
        _subtitle = new Label
        {
            Text = _config.RefreshSeconds < 60
                ? $"2.4G / 蓝牙外设 · 每 {Math.Max(1, _config.RefreshSeconds)} 秒自动刷新"
                : $"2.4G / 蓝牙外设 · 每 {Math.Max(1, _config.RefreshSeconds / 60)} 分钟自动刷新",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = TextSub,
            Location = new Point(31, 62)
        };
        topPanel.Controls.Add(title);
        topPanel.Controls.Add(_subtitle);

        _cardsPanel = new CardsPanel
        {
            Dock = DockStyle.Fill
        };
        _cardsPanel.Content.Padding = new Padding(16, 8, 16, 8);
        _cardsPanel.Content.Resize += (_, _) => ResizeCards();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 56, BackColor = BarColor };

        _updated = new Label
        {
            Text = "上次更新 --:--:--",
            AutoSize = false,
            Width = 150,
            Height = 24,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = TextSub,
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(22, 16),
            Cursor = Cursors.Hand
        };
        _updated.Click += (_, _) => RefreshReadings();
        _updated.MouseEnter += (_, _) => _updated.ForeColor = TextMain;
        _updated.MouseLeave += (_, _) => _updated.ForeColor = TextSub;

        var settings = LinkButton("设置", 46);
        settings.Click += (_, _) => _openSettings();

        var exitButton = LinkButton("退出", 46);
        exitButton.Click += (_, _) => _exit();
        bottom.Resize += (_, _) =>
        {
            exitButton.Left = bottom.Width - 64;
            settings.Left = exitButton.Left - 48;
            exitButton.Top = 16;
            settings.Top = 16;
        };

        bottom.Controls.Add(_updated);
        bottom.Controls.Add(settings);
        bottom.Controls.Add(exitButton);

        Controls.Add(_cardsPanel);
        Controls.Add(bottom);
        Controls.Add(topPanel);

        _timer = new System.Windows.Forms.Timer();
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            RefreshReadings();
            ScheduleNextRefresh();
        };
        ScheduleNextRefresh();

        // 充电状态轮询：60 秒一次，减少 HID++ 唤醒和指示灯闪烁
        _chargeTimer = new System.Windows.Forms.Timer { Interval = 60000 };
        _chargeTimer.Tick += async (_, _) => await PollChargingAsync();

        // 检测到用户动鼠标/按键时立即刷新，解决“唤醒后还显示休眠”
        _inputTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _inputTimer.Tick += async (_, _) =>
        {
            if (!InputActivity.HasNewInput()) return;
            if ((DateTime.Now - _lastInputRefresh).TotalSeconds < 5) return;

            _lastInputRefresh = DateTime.Now;
            await PollChargingAsync();
        };
        _inputTimer.Start();

        Shown += (_, _) =>
        {
            ResizeCards();
            RefreshReadings();
            _chargeTimer.Start();
        };
        Resize += (_, _) => ResizeCards();
    }

    /// <summary>2 秒轮询：电量/充电状态直接更新卡片；连接模式变化（2.4G ↔ 蓝牙）时重建卡片。</summary>
    private async Task PollChargingAsync()
    {
        if (_busy || IsDisposed || !Visible) return;
        _busy = true;
        try
        {
            var readings = await Task.Run(() =>
                _config.Devices.Where(d => d.Enabled)
                    .Select(d => (Device: d, Reading: _service.Read(d)))
                    .ToList());
            var bluetooth = await Task.Run(() => BluetoothBattery.Discover());

            var signature = BuildSignature(readings, bluetooth);
            if (signature != _lastSignature)
            {
                _lastSignature = signature;
                RebuildCards(readings, bluetooth);
                return;
            }

            foreach (var card in _cardsPanel.Content.Controls.OfType<DeviceCard>())
            {
                var match = readings.FirstOrDefault(r => ReferenceEquals(r.Device, card.Device));
                if (match.Reading != null) card.Update(match.Reading);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private static string BuildSignature(
        IReadOnlyList<(DeviceProfile Device, BatteryReading Reading)> readings,
        IReadOnlyList<BluetoothBatteryInfo> bluetooth)
    {
        // 包含“断开”状态（切换 2.4G/蓝牙时变化），但不包含休眠状态，
        // 这样切换模式会立即重建卡片，鼠标休眠时又不会反复闪烁。
        var parts = readings.Select(r => $"{r.Device.Name}|{r.Reading.Disconnected}");
        return string.Join(";", parts) + "||" + string.Join(";", bluetooth.Select(b => b.Name));
    }

    /// <summary>把下一次自动刷新对齐到整分（或间隔的整数倍），手动刷新不影响这个节奏。</summary>
    private void ScheduleNextRefresh()
    {
        var interval = Math.Max(10, _config.RefreshSeconds);
        var now = DateTime.Now;
        var secondsOfDay = now.Hour * 3600 + now.Minute * 60 + now.Second + now.Millisecond / 1000.0;
        var next = (Math.Floor(secondsOfDay / interval) + 1) * interval;
        var delay = (int)((next - secondsOfDay) * 1000);
        if (delay < 500) delay += interval * 1000;

        _timer.Interval = Math.Max(500, delay);
        _timer.Start();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var dark = 1;
            DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));
        }
        catch
        {
            // 老系统不支持深色标题栏时忽略
        }
    }

    private Label LinkButton(string text, int width)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = false,
            Width = width,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = TextSub,
            BackColor = BarColor,
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand
        };
        label.MouseEnter += (_, _) => label.ForeColor = TextMain;
        label.MouseLeave += (_, _) => label.ForeColor = TextSub;
        return label;
    }

    private void ResizeCards()
    {
        var width = Math.Max(360, _cardsPanel.Width - 52);
        foreach (Control control in _cardsPanel.Content.Controls)
        {
            control.Width = width;
        }
    }

    private async void RefreshReadings()
    {
        if (_busy || IsDisposed) return;
        _busy = true;
        _updated.Text = "正在刷新…";

        try
        {
            var results = await Task.Run(() => _service.ReadAll(_config.Devices));
            var bluetooth = await Task.Run(() => BluetoothBattery.Discover());
            _lastSignature = BuildSignature(results, bluetooth);
            RebuildCards(results, bluetooth);
            _updated.Text = "上次更新 " + DateTime.Now.ToString("HH:mm:ss");
        }
        catch (Exception ex)
        {
            _updated.Text = "刷新失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
        }
    }

    private void RebuildCards(IReadOnlyList<(DeviceProfile Device, BatteryReading Reading)> results,
        IReadOnlyList<BluetoothBatteryInfo> bluetooth)
    {
        _cardsPanel.Content.SuspendLayout();
        _cardsPanel.Content.Controls.Clear();
        var width = Math.Max(360, _cardsPanel.Width - 52);

        foreach (var (device, reading) in results)
        {
            // 同一设备已连接蓝牙且 2.4G 读不到（切到蓝牙模式）时，只保留蓝牙卡片
            var hasBluetooth = bluetooth.Any(bt => DeviceMatcher.SameDevice(device.Name, bt.Name));
            if (hasBluetooth && (!reading.Ok || reading.Disconnected)) continue;

            _cardsPanel.Content.Controls.Add(new DeviceCard(device, reading) { Width = width });
        }

        // 2.4G 读取成功时优先显示 2.4G，不再重复显示同一设备的蓝牙卡片
        foreach (var bt in bluetooth)
        {
            if (results.Any(r => r.Reading.Ok && !r.Reading.Disconnected &&
                                 DeviceMatcher.SameDevice(r.Device.Name, bt.Name)))
                continue;

            var device = new DeviceProfile
            {
                Name = bt.Name,
                Protocol = "bluetooth"
            };
            var reading = new BatteryReading
            {
                Percent = bt.Percent,
                Charging = bt.Charging,
                Source = $"蓝牙 GATT · {bt.Percent}%"
            };
            _cardsPanel.Content.Controls.Add(new DeviceCard(device, reading) { Width = width });
        }

        if (_cardsPanel.Content.Controls.Count == 0)
        {
            _cardsPanel.Content.Controls.Add(new Label
            {
                Text = "还没有设备，点“设置”添加一台。",
                AutoSize = true,
                ForeColor = TextSub,
                Margin = new Padding(16, 24, 0, 0)
            });
        }

        _cardsPanel.Content.ResumeLayout();
        _cardsPanel.UpdateScroll();
        FitToContent();
    }

    /// <summary>首次显示时把窗口高度调整到刚好显示全部卡片（不出现滚动条）。</summary>
    private void FitToContent()
    {
        if (_autoSized) return;

        var cards = 0;
        foreach (Control card in _cardsPanel.Content.Controls)
            cards += card.Height + card.Margin.Top + card.Margin.Bottom;
        cards += _cardsPanel.Content.Padding.Top + _cardsPanel.Content.Padding.Bottom;

        var chrome = Math.Max(Height - ClientSize.Height, 20);
        var needed = 92 + 56 + cards + chrome + 4;
        var max = Screen.FromControl(this).WorkingArea.Height - 40;

        Height = Math.Min(Math.Max(needed, MinimumSize.Height), max);
        _autoSized = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _chargeTimer.Stop();
        _inputTimer.Stop();

        if (!_closingToTray && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    /// <summary>由托盘退出时调用，允许真正关闭。</summary>
    public void CloseForExit()
    {
        _closingToTray = true;
        _timer.Stop();
        Close();
    }
}
