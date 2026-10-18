using PeripheralBattery.Core;
using PeripheralBattery.Protocols;

namespace PeripheralBattery.UI;

/// <summary>添加/编辑一台设备。</summary>
public sealed class DeviceEditForm : Form
{
    private readonly DeviceProfile _profile;
    private readonly DeviceBatteryService _service;

    private readonly TextBox _name = new() { Width = 260 };
    private readonly TextBox _vid = new() { Width = 90, Text = "046D" };
    private readonly TextBox _pid = new() { Width = 90, Text = "C547" };
    private readonly ComboBox _protocol = new() { Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _options = new()
    {
        Multiline = true,
        Height = 110,
        Width = 420,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9f)
    };
    private readonly Label _description = new() { AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(440, 0) };

    public DeviceProfile Result => _profile;

    public DeviceEditForm(DeviceProfile? existing, DeviceBatteryService service)
    {
        _service = service;
        _profile = existing?.Clone() ?? new DeviceProfile { Protocol = "mchose_qhw" };
        Text = existing == null ? "添加设备" : "编辑设备";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(470, 430);
        Font = new Font("Microsoft YaHei UI", 9f);

        foreach (var protocol in BatteryProtocolRegistry.All)
            _protocol.Items.Add(protocol.DisplayName);
        _protocol.SelectedIndexChanged += (_, _) => UpdateDescription();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 8,
            AutoSize = false
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 90));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "名称", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(_name, 1, 0);

        layout.Controls.Add(new Label { Text = "VID / PID", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        var ids = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        ids.Controls.Add(_vid);
        ids.Controls.Add(new Label { Text = "/", AutoSize = true, Padding = new Padding(4, 6, 4, 0) });
        ids.Controls.Add(_pid);
        ids.Controls.Add(new Label { Text = "（十六进制）", AutoSize = true, ForeColor = Color.DimGray, Padding = new Padding(6, 6, 0, 0) });
        layout.Controls.Add(ids, 1, 1);

        layout.Controls.Add(new Label { Text = "协议", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 2);
        layout.Controls.Add(_protocol, 1, 2);

        layout.Controls.Add(new Label { Text = "说明", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 3);
        layout.Controls.Add(_description, 1, 3);

        layout.Controls.Add(new Label { Text = "参数", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 4);
        layout.Controls.Add(_options, 1, 4);

        var hint = new Label
        {
            Text = "每行一个 key=value，例如 deviceIndex=1 或 batteryOffset=1。留空使用默认值。",
            AutoSize = true,
            ForeColor = Color.DimGray
        };
        layout.Controls.Add(hint, 1, 5);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Height = 46, Padding = new Padding(10) };
        var cancel = new Button { Text = "取消", Width = 80, DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "保存", Width = 80 };
        var test = new Button { Text = "测试读取", Width = 96 };
        ok.Click += (_, _) => SaveAndClose();
        test.Click += (_, _) => TestRead();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        buttons.Controls.Add(test);

        Controls.Add(layout);
        Controls.Add(buttons);

        _name.Text = _profile.Name;
        if (_profile.Vid != 0) _vid.Text = _profile.Vid.ToString("X4");
        if (_profile.Pid != 0) _pid.Text = _profile.Pid.ToString("X4");
        var index = BatteryProtocolRegistry.All.ToList().FindIndex(p => p.Id == _profile.Protocol);
        _protocol.SelectedIndex = Math.Max(0, index);
        _options.Text = string.Join(Environment.NewLine, _profile.Options.Select(kv => $"{kv.Key}={kv.Value}"));
        UpdateDescription();
    }

    private void UpdateDescription()
    {
        var id = BatteryProtocolRegistry.All.ElementAtOrDefault(_protocol.SelectedIndex)?.Id;
        _description.Text = BatteryProtocolRegistry.Find(id ?? "")?.Description ?? "";
    }

    private void SaveAndClose()
    {
        if (string.IsNullOrWhiteSpace(_name.Text))
        {
            MessageBox.Show("请填写设备名称", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!ushort.TryParse(_vid.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out var vid) ||
            !ushort.TryParse(_pid.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out var pid))
        {
            MessageBox.Show("VID / PID 需要是十六进制，例如 046D", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _profile.Name = _name.Text.Trim();
        _profile.Vid = vid;
        _profile.Pid = pid;
        _profile.Protocol = BatteryProtocolRegistry.All[_protocol.SelectedIndex].Id;
        _profile.Options = ParseOptions(_options.Text);

        DialogResult = DialogResult.OK;
        Close();
    }

    private static Dictionary<string, string> ParseOptions(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf('=');
            if (index <= 0) continue;
            var key = line[..index].Trim();
            var value = line[(index + 1)..].Trim();
            if (key.Length > 0) result[key] = value;
        }

        return result;
    }

    private void TestRead()
    {
        if (!ushort.TryParse(_vid.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out var vid) ||
            !ushort.TryParse(_pid.Text.Trim(), System.Globalization.NumberStyles.HexNumber, null, out var pid))
        {
            MessageBox.Show("VID / PID 格式不正确", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var probe = _profile.Clone();
        probe.Vid = vid;
        probe.Pid = pid;
        probe.Protocol = BatteryProtocolRegistry.All[_protocol.SelectedIndex].Id;
        probe.Options = ParseOptions(_options.Text);

        try
        {
            Cursor = Cursors.WaitCursor;
            _service.RefreshInterfaces();
            var reading = _service.Read(probe);
            MessageBox.Show(reading.Ok ? "读取成功：" + reading.Describe() : "读取失败：" + reading.Error,
                "测试读取", MessageBoxButtons.OK, reading.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            Cursor = Cursors.Default;
        }
    }
}
