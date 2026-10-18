using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PeripheralBattery.Core;

/// <summary>
/// 从 G HUB 的本地 SQLite（settings.db / settings.db-wal）里读取电量与充电状态。
/// G HUB 会把设备状态以 JSON 形式写入键值表，例如：
/// "battery/prox2wirelessmouse/percentage": { "isCharging": true, "percentage": 83, "time": "..." }
/// </summary>
public static class GhubBattery
{
    private static readonly Regex BatteryKey = new(
        "\"battery/[^\"]+/percentage\"\\s*:\\s*\\{([^}]*)\\}",
        RegexOptions.Compiled);

    private static readonly Regex PercentField = new(
        "\"percentage\"\\s*:\\s*(\\d{1,3})",
        RegexOptions.Compiled);

    private static readonly Regex ChargingField = new(
        "\"isCharging\"\\s*:\\s*(true|false)",
        RegexOptions.Compiled);

    private static readonly Regex TimeField = new(
        "\"time\"\\s*:\\s*\"([^\"]+)\"",
        RegexOptions.Compiled);

    public static (int Percent, bool Charging)? Read(int maxAgeMinutes = 15)
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LGHUB");
        if (!Directory.Exists(directory)) return null;

        foreach (var name in new[] { "settings.db-wal", "settings.db" })
        {
            var path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;

            var result = TryReadFile(path, maxAgeMinutes);
            if (result != null) return result;
        }

        return null;
    }

    private static (int Percent, bool Charging)? TryReadFile(string path, int maxAgeMinutes)
    {
        try
        {
            byte[] bytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                // 最新状态通常在文件末尾，读取末尾 1 MB 足够，避免整表加载
                var length = stream.Length;
                var readLength = (int)Math.Min(length, 1024 * 1024);
                stream.Seek(-readLength, SeekOrigin.End);
                bytes = new byte[readLength];
                var offset = 0;
                while (offset < readLength)
                {
                    var read = stream.Read(bytes, offset, readLength - offset);
                    if (read <= 0) break;
                    offset += read;
                }
            }

            var text = Encoding.UTF8.GetString(bytes);
            var matches = BatteryKey.Matches(text);
            if (matches.Count == 0) return null;

            // 取最后一个匹配，即最近写入的状态
            var body = matches[^1].Groups[1].Value;

            var percentMatch = PercentField.Match(body);
            if (!percentMatch.Success) return null;
            var percent = int.Parse(percentMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            if (percent is < 0 or > 100) return null;

            var charging = ChargingField.Match(body) is { Success: true } chargeMatch &&
                           chargeMatch.Groups[1].Value == "true";

            var timeMatch = TimeField.Match(body);
            if (timeMatch.Success &&
                DateTime.TryParse(timeMatch.Groups[1].Value, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var time))
            {
                if (DateTime.UtcNow - time.ToUniversalTime() > TimeSpan.FromMinutes(maxAgeMinutes))
                    return null;
            }

            return (percent, charging);
        }
        catch
        {
            return null;
        }
    }
}
