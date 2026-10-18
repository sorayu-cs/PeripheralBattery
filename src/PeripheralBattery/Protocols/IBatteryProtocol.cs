using PeripheralBattery.Core;
using PeripheralBattery.Hid;

namespace PeripheralBattery.Protocols;

/// <summary>电量读取协议。新增设备只需实现该接口并注册。</summary>
public interface IBatteryProtocol
{
    /// <summary>配置里使用的稳定标识。</summary>
    string Id { get; }

    /// <summary>设置界面显示的名称。</summary>
    string DisplayName { get; }

    /// <summary>设置界面里的参数说明。</summary>
    string Description { get; }

    BatteryReading Read(DeviceProfile profile, IReadOnlyList<HidInterface> interfaces);
}

public static class BatteryProtocolRegistry
{
    private static readonly List<IBatteryProtocol> Items = new();

    static BatteryProtocolRegistry()
    {
        Register(new LogitechHidppProtocol());
        Register(new MchoseQhwProtocol());
        Register(new RazerProtocol());
        Register(new CustomHidProtocol());
    }

    public static void Register(IBatteryProtocol protocol)
    {
        if (Items.All(x => x.Id != protocol.Id)) Items.Add(protocol);
    }

    public static IReadOnlyList<IBatteryProtocol> All => Items;

    public static IBatteryProtocol? Find(string id) =>
        Items.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
}
