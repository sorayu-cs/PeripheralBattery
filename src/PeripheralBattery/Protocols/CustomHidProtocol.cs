using PeripheralBattery.Core;
using PeripheralBattery.Hid;

namespace PeripheralBattery.Protocols;

/// <summary>
/// 通用 HID 自定义协议：发送一段十六进制报文，按偏移从回复里取电量。
/// 用于给未来设备（含雷蛇）做适配，所有参数都在设置里可改。
/// </summary>
public sealed class CustomHidProtocol : IBatteryProtocol
{
    public string Id => "custom_hid";
    public string DisplayName => "自定义 HID 报文";
    public string Description =>
        "参数：sendHex（发送的十六进制，如 55 03 00 38 38）、useWriteFile（true/false）、expectPrefix（回复前缀十六进制）、" +
        "offset（电量字节偏移）、scale（倍率）、reportId（可选，放在发送首字节）。";

    public BatteryReading Read(DeviceProfile profile, IReadOnlyList<HidInterface> interfaces)
    {
        var usagePage = profile.GetUShort("usagePage", 0);
        var usage = profile.GetUShort("usage", 0);

        var candidates = interfaces.Where(x => x.Vid == profile.Vid && x.Pid == profile.Pid && x.OutputLen > 0).ToList();
        if (candidates.Count == 0) return BatteryReading.Fail("设备未连接");

        var target = candidates.FirstOrDefault(x => x.UsagePage == usagePage && x.Usage == usage)
                     ?? candidates.OrderByDescending(x => x.OutputLen).First();

        try
        {
            var sendHex = profile.GetString("sendHex");
            if (string.IsNullOrWhiteSpace(sendHex))
                return BatteryReading.Fail("未填写 sendHex 参数");

            var payload = ParseHex(sendHex);
            var reportId = profile.GetInt("reportId", -1);
            var useWriteFile = !profile.GetString("useWriteFile", "true").Equals("false", StringComparison.OrdinalIgnoreCase);

            var length = Math.Max(target.OutputLen, payload.Length + Math.Max(reportId, 0) + 1);
            var buffer = new byte[length];
            var start = 0;
            if (reportId >= 0)
            {
                buffer[0] = (byte)reportId;
                start = 1;
            }

            Array.Copy(payload, 0, buffer, start, Math.Min(payload.Length, length - start));

            using var dev = new HidDevice(target.Path, target.InputLen, target.OutputLen, target.FeatureLen);
            dev.Drain(100);
            dev.Send(buffer, useWriteFile);

            var prefix = ParseHex(profile.GetString("expectPrefix"));
            var offset = profile.GetInt("offset", 0);
            var scale = profile.GetDouble("scale", 1.0);

            var deadline = Environment.TickCount64 + 1200;
            while (Environment.TickCount64 < deadline)
            {
                var response = dev.Read(200);
                if (response == null || response.Length == 0) continue;
                if (prefix.Length > 0)
                {
                    var match = true;
                    for (var i = 0; i < prefix.Length; i++)
                    {
                        // 允许跳过首字节 report ID
                        var index = i + (response.Length > prefix.Length && response[0] == 0 ? 1 : 0);
                        if (index >= response.Length || response[index] != prefix[i]) { match = false; break; }
                    }

                    if (!match) continue;
                }

                if (offset >= response.Length)
                    return BatteryReading.Fail($"偏移 {offset} 超出回复长度 {response.Length}");

                var percent = (int)Math.Round(response[offset] * scale);
                if (percent is < 0 or > 100)
                    return BatteryReading.Fail($"电量字段异常（原始值 {response[offset]}）");

                return new BatteryReading
                {
                    Percent = percent,
                    Source = "自定义 HID 报文"
                };
            }

            return BatteryReading.Fail("设备未响应自定义报文");
        }
        catch (Exception ex)
        {
            return BatteryReading.Fail("读取失败：" + ex.Message);
        }
    }

    private static byte[] ParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<byte>();
        var parts = text.Split(new[] { ' ', ',', '-', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var result = new List<byte>(parts.Length);
        foreach (var part in parts)
        {
            var token = part.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? part[2..] : part;
            if (token.Length == 0) continue;
            if (byte.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out var b))
                result.Add(b);
        }

        return result.ToArray();
    }
}
