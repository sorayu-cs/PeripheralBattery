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

            // 优先读 0x05 状态帧：第 31 字节是随电量变化的原始值（85/86…），
            // M HUB 会把它按 10% 分档显示（85-94 → 90%）。
            var status = Query(dev, 0x05, target.OutputLen);
            if (status != null)
            {
                var length = status[9];
                var start = 10;
                var count = Math.Min(length, status.Length - start);
                var offset = profile.GetInt("batteryOffset", 31);
                if (count > offset && offset >= 0)
                {
                    var raw = status[start + offset];
                    if (raw is > 0 and <= 100)
                    {
                        // 直接使用设备上报的实际值，不做 10% 取整
                        return new BatteryReading
                        {
                            Percent = raw,
                            Charging = null,
                            Source = $"QHW 0x05 · 原始 {raw}"
                        };
                    }
                }
            }

            // 回退：0x03 设备信息帧
            var info = Query(dev, 0x03, target.OutputLen);
            if (info != null)
            {
                var length = info[9];
                var start = 10;
                var count = Math.Min(length, info.Length - start);
                if (count > 1)
                {
                    var raw = info[start + 1];
                    if (raw is > 0 and <= 100)
                    {
                        return new BatteryReading
                        {
                            Percent = raw,
                            Charging = null,
                            Source = $"QHW 0x03 · 原始 {raw}"
                        };
                    }
                }
            }

            return BatteryReading.Fail("键盘未响应（可能处于休眠，按一下按键再刷新）");
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
