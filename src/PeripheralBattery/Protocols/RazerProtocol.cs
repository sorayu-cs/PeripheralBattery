using PeripheralBattery.Core;
using PeripheralBattery.Hid;

namespace PeripheralBattery.Protocols;

/// <summary>
/// 雷蛇（Razer）鼠标的 HID 电量查询（实验性）。
/// 雷蛇设备通常在 usage 0xFF00 上使用 90 字节报文，命令类 0x07 读取电池状态。
/// 不同型号的偏移可能不同，参数可在设置里覆盖。
/// </summary>
public sealed class RazerProtocol : IBatteryProtocol
{
    public string Id => "razer_hid";
    public string DisplayName => "雷蛇 HID（实验性）";
    public string Description =>
        "适用于雷蛇鼠标。默认发送 02 07 00 00（report 0x02，命令 0x07），电量偏移 8。" +
        "若型号不同，可在参数里用 sendHex/offset 覆盖；未验证的型号请先用自定义报文测试。";

    private readonly CustomHidProtocol _custom = new();

    public BatteryReading Read(DeviceProfile profile, IReadOnlyList<HidInterface> interfaces)
    {
        var clone = profile.Clone();
        clone.Protocol = _custom.Id;

        SetDefault(clone, "sendHex", "02 07 00 00");
        SetDefault(clone, "offset", "8");
        SetDefault(clone, "expectPrefix", "02 07");
        SetDefault(clone, "useWriteFile", "false");

        var result = _custom.Read(clone, interfaces);
        if (result.Ok) result.Source = "雷蛇 HID（实验性）";
        return result;
    }

    private static void SetDefault(DeviceProfile profile, string key, string value)
    {
        if (!profile.Options.ContainsKey(key)) profile.Options[key] = value;
    }
}
