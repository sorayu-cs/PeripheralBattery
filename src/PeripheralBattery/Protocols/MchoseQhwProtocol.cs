using PeripheralBattery.Core;
using PeripheralBattery.Hid;

namespace PeripheralBattery.Protocols;

/// <summary>
/// 迈从（MCHOSE）QHW 无线协议：在 usage 0x0001/0x0000 的 64 字节厂商接口上，
/// 主机发送 0x55 帧，设备回复 0xAA 帧。
/// 电量查询：55 03 00 38 38 → AA 03 00 38 26 00 00 00 &lt;len&gt; &lt;data...&gt;
/// </summary>
public sealed class MchoseQhwProtocol : IBatteryProtocol
{
    public string Id => "mchose_qhw";
    public string DisplayName => "迈从 QHW 无线（K99 V3 等）";
    public string Description => "参数：usagePage（默认 0001）、usage（默认 0000）、batteryOffset（电量字节偏移，默认 1）。";

    public BatteryReading Read(DeviceProfile profile, IReadOnlyList<HidInterface> interfaces)
    {
        var usagePage = profile.GetUShort("usagePage", 0x0001);
        var usage = profile.GetUShort("usage", 0x0000);

        var target = interfaces.FirstOrDefault(x =>
            x.Vid == profile.Vid && x.Pid == profile.Pid &&
            x.UsagePage == usagePage && x.Usage == usage && x.OutputLen >= 65);
        target ??= interfaces.FirstOrDefault(x =>
            x.Vid == profile.Vid && x.Pid == profile.Pid && x.OutputLen >= 65);

        if (target == null)
            return BatteryReading.Fail("未找到迈从无线接口（usage 0001/0000，65 字节）");

        try
        {
            using var dev = new HidDevice(target.Path, target.InputLen, target.OutputLen, target.FeatureLen);

            // 优先读 0x05 状态帧：data 段第 31 字节低 7 位是电量（85/86…），bit7 是充电标志
            // （未充电 0x55，充电中 0xD5）；data 段从完整帧的偏移 10 开始。
            var status = Query(dev, 0x05, target.OutputLen);
            if (status != null)
            {
                var index = 10 + profile.GetInt("batteryOffset", 31);
                if (index >= 0 && index < status.Length)
                {
                    var raw = status[index];
                    var percent = raw & 0x7F;
                    if (percent is > 0 and <= 100)
                    {
                        // 直接使用设备上报的实际值，不做 10% 取整
                        return new BatteryReading
                        {
                            Percent = percent,
                            Charging = (raw & 0x80) != 0,
                            Source = $"QHW 0x05 · 原始 {percent}"
                        };
                    }
                }
            }

            // 0x05 状态帧失败通常意味着键盘切到了蓝牙模式或已关机，
            // 不再回退 0x03（那边读到的是过期/错误值，例如 49），
            // 让上层显示断开状态并改用蓝牙电量。
            return BatteryReading.Fail("键盘未响应（可能已切到蓝牙模式或休眠）");
        }
        catch (Exception ex)
        {
            return BatteryReading.Fail("读取失败：" + ex.Message);
        }
    }

    private static byte[]? Query(HidDevice dev, byte command, int outputLength)
    {
        var frame = BuildFrame(command, new byte[] { 0x38, 0x38 }, Math.Max(outputLength, 65));
        for (var attempt = 0; attempt < 2; attempt++)
        {
            dev.Drain(100);
            try { dev.Send(frame, useWriteFile: true); }
            catch { continue; }

            var deadline = Environment.TickCount64 + 900;
            while (Environment.TickCount64 < deadline)
            {
                var response = dev.Read(150);
                if (response == null) continue;
                if (response.Length > 10 && response[1] == 0xAA && response[2] == command)
                    return response;
            }
        }

        return null;
    }

    private static byte[] BuildFrame(byte command, byte[] args, int length)
    {
        var frame = new byte[length];
        frame[0] = 0x00;  // report ID
        frame[1] = 0x55;  // 主机帧头
        frame[2] = command;
        frame[3] = 0x00;
        Array.Copy(args, 0, frame, 4, Math.Min(args.Length, length - 4));
        return frame;
    }
}
