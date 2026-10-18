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
    private readonly FlowLayoutPanel _cardsPanel;
    private readonly Label _updated;
    private readonly Label _subtitle;
    private readonly System.Windows.Forms.Timer _timer;
    private bool _busy;
    private bool _closingToTray;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public DashboardForm(AppConfig config, DeviceBatteryService service, Action openSettings, Action exit)
    {
        _config = config;
        _service = service;
        _openSettings = openSettings;
        _exit = exit;

        Text = "外设电量";
        ClientSize = new Size(720, 580);
        MinimumSize = new Size(600, 460);
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
                ? $"2.4G 无线外设 · 每 {Math.Max(1, _config.RefreshSeconds)} 秒自动刷新"
                : $"2.4G 无线外设 · 每 {Math.Max(1, _config.RefreshSeconds / 60)} 分钟自动刷新",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = TextSub,
            Location = new Point(31, 62)
        };
        topPanel.Controls.Add(title);
        topPanel.Controls.Add(_subtitle);

        _cardsPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = BackColorDark,
            Padding = new Padding(16, 8, 16, 8)
        };
        _cardsPanel.Resize += (_, _) => ResizeCards();

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

        Shown += (_, _) =>
        {
            ResizeCards();
            RefreshReadings();
        };
        Resize += (_, _) => ResizeCards();
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
        var width = Math.Max(360, _cardsPanel.ClientSize.Width - 36);
        foreach (Control control in _cardsPanel.Controls)
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
            RebuildCards(results);
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

    private void RebuildCards(IReadOnlyList<(DeviceProfile Device, BatteryReading Reading)> results)
    {
        _cardsPanel.SuspendLayout();
        _cardsPanel.Controls.Clear();
        foreach (var (device, reading) in results)
        {
            _cardsPanel.Controls.Add(new DeviceCard(device, reading)
            {
                Width = Math.Max(360, _cardsPanel.ClientSize.Width - 36)
            });
        }

        if (results.Count == 0)
        {
            _cardsPanel.Controls.Add(new Label
            {
                Text = "还没有设备，点“设置”添加一台。",
                AutoSize = true,
                ForeColor = TextSub,
                Margin = new Padding(16, 24, 0, 0)
            });
        }

        _cardsPanel.ResumeLayout();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
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
