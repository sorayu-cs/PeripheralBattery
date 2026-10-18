using PeripheralBattery.Core;
using PeripheralBattery.Hid;

namespace PeripheralBattery.Protocols;

/// <summary>
/// 罗技 HID++ 2.0：在接收器的长报文接口（usage 0xFF00/0x0002，20 字节）上，
/// 用 report 0x11 与设备索引通信，读取特性 0x1004（电池电压）或 0x1000（统一电量）。
/// </summary>
public sealed class LogitechHidppProtocol : IBatteryProtocol
{
    public string Id => "logitech_hidpp";
    public string DisplayName => "罗技 HID++ 2.0";
    public string Description => "适用于罗技 Lightspeed 接收器（如 GPW）。参数：deviceIndex（默认 1）、usagePage/usage。";

    // GPW 的 G HUB 曲线：约 3.50 V 空、3.944 V 满（实测 3855 mV 对应官方 80%）。
    // 早期版本误用了 4186 mV 满电的通用曲线，导致读数偏低约 20%。
    private static readonly (int Mv, int Pct)[] VoltageCurve =
    {
        (3944, 100), (3500, 0)
    };

    private sealed class VoltageCache
    {
        public int Millivolts;
        public int Percent;
        public bool Charging;
        public DateTime At;
        public int SuspectCount;
    }

    private readonly object _cacheLock = new();
    private readonly Dictionary<string, VoltageCache> _cache = new();

    public BatteryReading Read(DeviceProfile profile, IReadOnlyList<HidInterface> interfaces)
    {
        var usagePage = profile.GetUShort("usagePage", 0xFF00);
        var usage = profile.GetUShort("usage", 0x0002);

        var target = interfaces.FirstOrDefault(x =>
            x.Vid == profile.Vid && x.Pid == profile.Pid &&
            x.UsagePage == usagePage && x.Usage == usage &&
            x.OutputLen >= 20);
        target ??= interfaces.FirstOrDefault(x =>
            x.Vid == profile.Vid && x.Pid == profile.Pid &&
            x.UsagePage == 0xFF00 && x.OutputLen >= 20);

        if (target == null)
            return BatteryReading.Fail("未找到接收器的长报文接口（usage 0xFF00/0x0002）");

        try
        {
            using var dev = new HidDevice(target.Path, target.InputLen, target.OutputLen, target.FeatureLen);
            var deviceIndex = profile.GetInt("deviceIndex", 1);

            // 0x1004：电池电压（Battery Voltage）
            var featureIndex = QueryFeatureIndex(dev, deviceIndex, 0x1004);
            if (featureIndex > 0)
            {
                // 每次刷新只读一次，避免反复唤醒鼠标增加耗电；
                // 休眠时的异常低电压通过缓存过滤掉。
                var report = Request(dev, deviceIndex, featureIndex, 0x00, Array.Empty<byte>(), 2);
                var mv = 0;
                var flags = 0;
                if (report is { Length: >= 7 })
                {
                    mv = report[4] | (report[5] << 8);
                    flags = report[6];
                }

                return ApplyVoltage(profile, deviceIndex, mv, flags);
            }

            // 0x1000：统一电量（部分新设备）
            var unified = QueryFeatureIndex(dev, deviceIndex, 0x1000);
            if (unified > 0)
            {
                var report = Request(dev, deviceIndex, unified, 0x00, Array.Empty<byte>(), 3);
                if (report != null && report.Length >= 6)
                {
                    var level = report[4];
                    var status = report[5];
                    if (level <= 100)
                    {
                        return new BatteryReading
                        {
                            Percent = level,
                            Charging = status is 1 or 2 or 3,
                            Source = "HID++ 0x1000 · 百分比"
                        };
                    }
                }
            }

            return BatteryReading.Fail("鼠标未响应，移动一下鼠标唤醒后重试");
        }
        catch (Exception ex)
        {
            return BatteryReading.Fail("读取失败：" + ex.Message);
        }
    }

    /// <summary>电压合理性过滤：休眠时的异常低值不采信，沿用上次有效读数；连续 3 次异常才接受新值。</summary>
    private BatteryReading ApplyVoltage(DeviceProfile profile, int deviceIndex, int millivolts, int flags)
    {
        var key = $"{profile.Vid:X4}:{profile.Pid:X4}:{deviceIndex}";
        lock (_cacheLock)
        {
            var hasLast = _cache.TryGetValue(key, out var last);

            if (millivolts is <= 2000 or >= 5000)
            {
                if (hasLast && DateTime.Now - last!.At < TimeSpan.FromMinutes(30))
                {
                    last.At = DateTime.Now;
                    return new BatteryReading
                    {
                        Percent = last.Percent,
                        Millivolts = last.Millivolts,
                        Charging = last.Charging,
                        Source = "HID++ 0x1004 · 沿用上次（设备休眠）"
                    };
                }

                return BatteryReading.Fail("鼠标未响应，移动一下鼠标唤醒后重试");
            }

            var percent = VoltageToPercent(millivolts);
            var charging = (flags & 0x80) != 0;

            if (hasLast)
            {
                var elapsed = DateTime.Now - last!.At;
                if (percent < last.Percent - 15 && elapsed < TimeSpan.FromMinutes(10))
                {
                    last.SuspectCount++;
                    last.At = DateTime.Now;
                    if (last.SuspectCount < 3)
                    {
                        return new BatteryReading
                        {
                            Percent = last.Percent,
                            Millivolts = last.Millivolts,
                            Charging = last.Charging,
                            Source = "HID++ 0x1004 · 沿用上次（电压异常）"
                        };
                    }
                }
                else
                {
                    last.SuspectCount = 0;
                }
            }

            _cache[key] = new VoltageCache
            {
                Millivolts = millivolts,
                Percent = percent,
                Charging = charging,
                At = DateTime.Now,
                SuspectCount = 0
            };

            return new BatteryReading
            {
                Percent = percent,
                Millivolts = millivolts,
                Charging = charging,
                Source = $"HID++ 0x1004 · {millivolts} mV"
            };
        }
    }

    private static int QueryFeatureIndex(HidDevice dev, int deviceIndex, ushort featureId)
    {
        var report = Request(dev, deviceIndex, 0x00, 0x00,
            new byte[] { (byte)(featureId >> 8), (byte)(featureId & 0xFF), 0x00, 0x00 }, 2);
        return report is { Length: > 4 } ? report[4] : 0;
    }

    private static byte[]? Request(HidDevice dev, int deviceIndex, int featureIndex, int function, byte[] args, int attempts = 1)
    {
        var packet = new byte[20];
        packet[0] = 0x11; // 长报文
        packet[1] = (byte)deviceIndex;
        packet[2] = (byte)featureIndex;
        packet[3] = (byte)((function << 4) | 0x0A); // 软件 ID 0xA
        for (var i = 0; i < args.Length && 4 + i < packet.Length; i++) packet[4 + i] = args[i];

        for (var attempt = 0; attempt < Math.Max(attempts, 1); attempt++)
        {
            dev.Drain(120);
            try { dev.Send(packet, useWriteFile: false); }
            catch { continue; }

            var deadline = Environment.TickCount64 + 600;
            while (Environment.TickCount64 < deadline)
            {
                var response = dev.Read(120);
                if (response == null) continue;
                if (response.Length >= 4 && response[0] == 0x11 && response[1] == deviceIndex)
                    return response;
            }
        }

        return null;
    }

    /// <summary>按放电曲线线性插值。</summary>
    public static int VoltageToPercent(int millivolts)
    {
        if (millivolts >= VoltageCurve[0].Mv) return 100;
        if (millivolts <= VoltageCurve[^1].Mv) return 0;

        for (var i = 0; i < VoltageCurve.Length - 1; i++)
        {
            var high = VoltageCurve[i];
            var low = VoltageCurve[i + 1];
            if (millivolts <= high.Mv && millivolts >= low.Mv)
            {
                var ratio = (double)(millivolts - low.Mv) / (high.Mv - low.Mv);
                return (int)Math.Round(low.Pct + (high.Pct - low.Pct) * ratio);
            }
        }

        return 0;
    }
}
