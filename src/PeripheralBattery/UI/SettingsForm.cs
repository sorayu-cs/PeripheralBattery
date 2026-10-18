using System.Runtime.InteropServices;
using PeripheralBattery.Core;
using PeripheralBattery.Protocols;

namespace PeripheralBattery.UI;

/// <summary>深色简约设置界面：设备列表 + 分钟级刷新间隔。</summary>
public sealed class SettingsForm : Form
{
    private static readonly Color BackColorDark = Color.FromArgb(22, 23, 26);
    private static readonly Color BarColor = Color.FromArgb(27, 28, 32);
    private static readonly Color RowColor = Color.FromArgb(31, 32, 36);
    private static readonly Color TextMain = Color.FromArgb(244, 244, 245);
    private static readonly Color TextSub = Color.FromArgb(148, 150, 158);
    private static readonly Color Green = Color.FromArgb(52, 211, 153);
    private static readonly Color Orange = Color.FromArgb(245, 158, 11);
    private static readonly Color Red = Color.FromArgb(239, 68, 68);

    private readonly AppConfig _config;
    private readonly DeviceBatteryService _service;
    private readonly FlowLayoutPanel _rows;
    private readonly TextBox _valueBox;
    private readonly Label _unitLabel;
    private readonly Label _autostartLabel;
    private bool _autostartOn;
    private int _totalSeconds;
    private bool _useSeconds;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public SettingsForm(AppConfig config, DeviceBatteryService service)
    {
        _config = config;
        _service = service;

        Text = "设置";
        ClientSize = new Size(720, 540);
        MinimumSize = new Size(620, 460);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = BackColorDark;
        ForeColor = TextMain;
        Font = new Font("Microsoft YaHei UI", 9f);
        DoubleBuffered = true;

        var topPanel = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = BackColorDark };
        topPanel.Controls.Add(new Label
        {
            Text = "设置",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 22f, FontStyle.Bold),
            ForeColor = TextMain,
            Location = new Point(28, 18)
        });
        topPanel.Controls.Add(new Label
        {
            Text = "管理设备与自动刷新间隔",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 9f),
            ForeColor = TextSub,
            Location = new Point(31, 60)
        });

        _rows = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = BackColorDark,
            Padding = new Padding(16, 4, 16, 8)
        };
        _rows.Resize += (_, _) => ResizeRows();

        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 62, BackColor = BarColor };
        var add = LinkText("＋ 添加设备", 96);
        add.Location = new Point(22, 19);
        add.Click += (_, _) => AddDevice();

        var intervalLabel = new Label
        {
            Text = "刷新间隔",
            AutoSize = true,
            ForeColor = TextSub,
            Font = new Font("Microsoft YaHei UI", 9f),
            Location = new Point(180, 22)
        };
        _totalSeconds = Math.Clamp(_config.RefreshSeconds, 10, 3600);
        // 不是整分钟时默认用“秒”显示，避免出现“1 分钟其实是 70 秒”
        _useSeconds = _totalSeconds % 60 != 0;

        var minus = StepButton("−");
        minus.Location = new Point(250, 18);
        minus.Click += (_, _) => AdjustInterval(-1);

        _valueBox = new TextBox
        {
            Text = "1",
            Width = 42,
            BorderStyle = BorderStyle.None,
            BackColor = RowColor,
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Center,
            TabStop = false,
            Location = new Point(6, 4)
        };
        _valueBox.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        };
        _valueBox.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            ApplyInput();
            e.SuppressKeyPress = true;
        };
        _valueBox.Leave += (_, _) => ApplyInput();

        var boxPanel = new Panel
        {
            Width = 54,
            Height = 26,
            BackColor = RowColor,
            Location = new Point(277, 17)
        };
        boxPanel.Controls.Add(_valueBox);

        var plus = StepButton("＋");
        plus.Location = new Point(334, 18);
        plus.Click += (_, _) => AdjustInterval(1);

        _unitLabel = new Label
        {
            Text = "分钟",
            AutoSize = false,
            Width = 44,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = TextMain,
            BackColor = BarColor,
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
            Location = new Point(366, 18)
        };
        _unitLabel.Click += (_, _) => ToggleUnit();

        _autostartLabel = new Label
        {
            Text = Autostart.IsEnabled ? "开机自启 已开" : "开机自启 已关",
            AutoSize = false,
            Width = 96,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Autostart.IsEnabled ? Green : TextSub,
            BackColor = BarColor,
            Font = new Font("Microsoft YaHei UI", 9f),
            Cursor = Cursors.Hand,
            Location = new Point(414, 18)
        };
        _autostartLabel.Click += (_, _) => ToggleAutostart();
        _autostartOn = Autostart.IsEnabled;

        UpdateIntervalDisplay();

        var close = LinkText("关闭", 54);
        close.Click += (_, _) => Close();
        bottom.Resize += (_, _) => close.Left = bottom.Width - 70;
        close.Top = 19;

        bottom.Controls.Add(add);
        bottom.Controls.Add(intervalLabel);
        bottom.Controls.Add(minus);
        bottom.Controls.Add(boxPanel);
        bottom.Controls.Add(plus);
        bottom.Controls.Add(_unitLabel);
        bottom.Controls.Add(_autostartLabel);
        bottom.Controls.Add(close);

        Controls.Add(_rows);
        Controls.Add(bottom);
        Controls.Add(topPanel);

        FormClosing += (_, _) => SaveInterval();
        Shown += async (_, _) =>
        {
            _autostartOn = Autostart.IsEnabled;
            _autostartLabel.Text = _autostartOn ? "开机自启 已开" : "开机自启 已关";
            _autostartLabel.ForeColor = _autostartOn ? Green : TextSub;
            UpdateIntervalDisplay();
            ResizeRows();
            ActiveControl = null;
            await ReloadAsync();
        };
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
            // 老系统忽略
        }
    }

    private void SaveInterval()
    {
        _config.RefreshSeconds = _totalSeconds;
        _config.Save();
    }

    private Label StepButton(string text)
    {
        var button = new Label
        {
            Text = text,
            AutoSize = false,
            Width = 24,
            Height = 24,
            TextAlign = ContentAlignment.MiddleCenter,
            BackColor = Color.FromArgb(45, 46, 52),
            ForeColor = TextMain,
            Font = new Font("Segoe UI", 12f),
            Cursor = Cursors.Hand
        };
        button.MouseEnter += (_, _) => button.BackColor = Color.FromArgb(58, 59, 66);
        button.MouseLeave += (_, _) => button.BackColor = Color.FromArgb(45, 46, 52);
        return button;
    }

    private void AdjustInterval(int direction)
    {
        var step = _useSeconds ? 10 : 60;
        _totalSeconds = Math.Clamp(_totalSeconds + direction * step, 10, 3600);
        UpdateIntervalDisplay();
    }

    private void ToggleUnit()
    {
        _useSeconds = !_useSeconds;
        if (!_useSeconds)
        {
            // 切到分钟时对齐到整分钟，保证“1 分钟 = 60 秒”
            _totalSeconds = Math.Max(60, (int)Math.Round(_totalSeconds / 60.0) * 60);
        }

        UpdateIntervalDisplay();
    }

    private void ApplyInput()
    {
        if (!int.TryParse(_valueBox.Text.Trim(), out var value))
        {
            UpdateIntervalDisplay();
            return;
        }

        _totalSeconds = _useSeconds
            ? Math.Clamp(value, 10, 3600)
            : Math.Clamp(value, 1, 60) * 60;

        UpdateIntervalDisplay();
    }

    private void ToggleAutostart()
    {
        _autostartOn = !_autostartOn;

        if (_autostartOn)
        {
            if (!Autostart.Enable(out var error))
            {
                MessageBox.Show(error, "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _autostartOn = false;
            }
        }
        else if (!Autostart.Disable(out var error))
        {
            MessageBox.Show(error, "开机自启", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _autostartOn = true;
        }

        _autostartLabel.Text = _autostartOn ? "开机自启 已开" : "开机自启 已关";
        _autostartLabel.ForeColor = _autostartOn ? Green : TextSub;
        _config.Autostart = Autostart.IsEnabled;
        _config.Save();
    }

    private void UpdateIntervalDisplay()
    {
        var value = _useSeconds ? _totalSeconds : Math.Max(1, (int)Math.Round(_totalSeconds / 60.0));
        if (_valueBox.Text != value.ToString()) _valueBox.Text = value.ToString();
        _unitLabel.Text = _useSeconds ? "秒" : "分钟";
    }

    private async Task ReloadAsync()
    {
        var devices = _config.Devices.ToList();
        var results = await Task.Run(() =>
        {
            _service.RefreshInterfaces();
            return devices.Select(d => (Device: d, Reading: _service.Read(d))).ToList();
        });

        _rows.SuspendLayout();
        _rows.Controls.Clear();
        foreach (var (device, reading) in results)
            _rows.Controls.Add(CreateRow(device, reading));
        _rows.ResumeLayout();
        ResizeRows();
    }

    private Panel CreateRow(DeviceProfile device, BatteryReading reading)
    {
        var row = new Panel
        {
            Height = 62,
            Width = Math.Max(420, _rows.ClientSize.Width - 40),
            BackColor = RowColor,
            Margin = new Padding(10, 0, 10, 10)
        };

        var title = device.Name;
        if (!title.Contains("鼠标") && !title.Contains("键盘") && !title.Contains("耳机"))
            title += " " + TypeLabel(device);

        row.Controls.Add(new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold),
            ForeColor = TextMain,
            Location = new Point(18, 10)
        });

        row.Controls.Add(new Label
        {
            Text = reading.Disconnected
                ? $"{device.VidPid} · {BatteryProtocolRegistry.Find(device.Protocol)?.DisplayName ?? device.Protocol} · 已断开"
                : $"{device.VidPid} · {BatteryProtocolRegistry.Find(device.Protocol)?.DisplayName ?? device.Protocol}",
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 8.5f),
            ForeColor = TextSub,
            Location = new Point(18, 34)
        });

        var percent = reading.Percent;
        row.Controls.Add(new Label
        {
            Text = reading.Ok ? $"{percent}%" : "—",
            AutoSize = false,
            Width = 64,
            Height = 26,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = percent is null || reading.Disconnected ? TextSub
                : percent >= 60 ? Green : percent >= 30 ? Orange : Red,
            Location = new Point(row.Width - 220, 17)
        });

        var edit = LinkText("编辑", 40);
        edit.Location = new Point(row.Width - 148, 19);
        edit.Click += (_, _) => EditDevice(device);

        var remove = LinkText("删除", 40);
        remove.Location = new Point(row.Width - 100, 19);
        remove.Click += (_, _) => RemoveDevice(device);

        row.Controls.Add(edit);
        row.Controls.Add(remove);

        return row;
    }

    private static string TypeLabel(DeviceProfile device)
    {
        if (device.Protocol == "logitech_hidpp") return "无线鼠标";
        if (device.Protocol == "mchose_qhw") return "无线键盘";
        if (device.Name.Contains("鼠标")) return "无线鼠标";
        if (device.Name.Contains("键盘")) return "无线键盘";
        if (device.Name.Contains("耳机")) return "无线耳机";
        return "无线设备";
    }

    private Label LinkText(string text, int width)
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

    private void ResizeRows()
    {
        var width = Math.Max(420, _rows.ClientSize.Width - 40);
        foreach (Control row in _rows.Controls)
        {
            row.Width = width;
            foreach (Control child in row.Controls)
            {
                if (child is Label { Width: 40 } && child.Left > 0)
                {
                    // 编辑/删除按钮随行宽重新定位
                    if (child.Text == "编辑") child.Left = width - 148;
                    if (child.Text == "删除") child.Left = width - 100;
                }

                if (child is Label { Width: 64 }) child.Left = width - 220;
            }
        }
    }

    private void AddDevice()
    {
        using var dialog = new DeviceEditForm(null, _service);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        _config.Devices.Add(dialog.Result);
        _config.Save();
        _ = ReloadAsync();
    }

    private void EditDevice(DeviceProfile device)
    {
        using var dialog = new DeviceEditForm(device, _service);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var index = _config.Devices.FindIndex(d => ReferenceEquals(d, device));
        if (index >= 0) _config.Devices[index] = dialog.Result;
        _config.Save();
        _ = ReloadAsync();
    }

    private void RemoveDevice(DeviceProfile device)
    {
        if (MessageBox.Show($"确定删除“{device.Name}”？", "删除设备", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes) return;
        _config.Devices.RemoveAll(d => ReferenceEquals(d, device));
        _config.Save();
        _ = ReloadAsync();
    }
}
