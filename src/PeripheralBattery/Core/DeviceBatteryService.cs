using PeripheralBattery.Hid;
using PeripheralBattery.Protocols;

namespace PeripheralBattery.Core;

/// <summary>负责枚举接口、调用协议读取所有已启用设备的电量。</summary>
public sealed class DeviceBatteryService
{
    private List<HidInterface> _interfaces = new();
    private long _interfacesAt;
    private readonly object _cacheLock = new();
    private readonly Dictionary<string, BatteryReading> _lastGood = new();

    public IReadOnlyList<HidInterface> Interfaces
    {
        get
        {
            if (Environment.TickCount64 - _interfacesAt > 5000)
            {
                try
                {
                    _interfaces = HidEnumerator.Enumerate();
                    _interfacesAt = Environment.TickCount64;
                }
                catch
                {
                    // 枚举失败时沿用旧列表
                }
            }

            return _interfaces;
        }
    }

    public void RefreshInterfaces()
    {
        try
        {
            _interfaces = HidEnumerator.Enumerate();
            _interfacesAt = Environment.TickCount64;
        }
        catch
        {
            // 忽略
        }
    }

    public BatteryReading Read(DeviceProfile profile)
    {
        var protocol = BatteryProtocolRegistry.Find(profile.Protocol);
        if (protocol == null)
            return BatteryReading.Fail($"未知协议：{profile.Protocol}");

        var reading = protocol.Read(profile, Interfaces);
        var key = profile.Key;

        lock (_cacheLock)
        {
            if (reading.Ok)
            {
                _lastGood[key] = reading;
                return reading;
            }

            // 设备断开时保留最后一次成功读数。
            // 罗技休眠已由协议层返回“（休眠）”且 Ok=true，不会走到这里；
            // 其余“未响应”一律按断开处理，保证键盘切蓝牙时能触发卡片重建。
            if (_lastGood.TryGetValue(key, out var last))
            {
                return new BatteryReading
                {
                    Percent = last.Percent,
                    Millivolts = last.Millivolts,
                    Charging = last.Charging,
                    Source = $"已断开 · 最后 {last.UpdatedAt:HH:mm:ss}",
                    Disconnected = true
                };
            }

            return reading;
        }
    }

    public List<(DeviceProfile Device, BatteryReading Reading)> ReadAll(IEnumerable<DeviceProfile> devices)
    {
        var result = new List<(DeviceProfile, BatteryReading)>();
        foreach (var device in devices.Where(d => d.Enabled))
        {
            result.Add((device, Read(device)));
        }

        return result;
    }
}
