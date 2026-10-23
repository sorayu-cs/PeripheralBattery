using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using PeripheralBattery.Core;
using PeripheralBattery.Hid;

namespace PeripheralBattery.Protocols;

/// <summary>
/// 罗技电量：调用同目录的 HidReader.exe 读取 HID++ 0x1004。
/// 电量取 fn1 第 4 字节；充电状态取 fn1 第 6 字节的 bit0（实测：充电 0x01，未充电 0x00）。
/// </summary>
public sealed class LogitechHidppProtocol : IBatteryProtocol
{
    public string Id => "logitech_hidpp";
    public string DisplayName => "罗技 HID++ 2.0";
    public string Description => "通过 HidReader.exe 读取 0x1004 电量与充电位，不依赖 G HUB。";

    private static readonly Regex Fn0Pattern = new(@"fn0:\s*((?:[0-9A-Fa-f]{2}\s*)+)", RegexOptions.Compiled);
    private static readonly Regex Fn1Pattern = new(@"fn1:\s*((?:[0-9A-Fa-f]{2}\s*)+)", RegexOptions.Compiled);

    private (int Percent, bool Charging, int Millivolts)? _last;
    private DateTime _lastAt = DateTime.MinValue;
    private DateTime _skipUntil = DateTime.MinValue;
    private uint _skipInputTick;

    private static readonly string CachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PeripheralBattery", "logitech_last.txt");

    public BatteryReading Read(DeviceProfile profile, IReadOnlyList<HidInterface> interfaces)
    {
        // 上次查询时鼠标没有响应（休眠）：默认 5 分钟内不再发 HID++ 请求。
        // 但如果这期间用户动过鼠标/键盘，则立刻重试，避免唤醒后还显示休眠。
        if (DateTime.Now < _skipUntil && _last is { } skipped)
        {
            var currentTick = InputActivity.LastInputTick();
            if (currentTick == 0 || currentTick <= _skipInputTick)
                return BuildSleeping(skipped.Percent, skipped.Charging, skipped.Millivolts);

            _skipUntil = DateTime.MinValue;
        }

        if (ReadState(out var percent, out var millivolts, out var charging, out var error))
        {
            _skipUntil = DateTime.MinValue;
            _last = (percent, charging, millivolts);
            _lastAt = DateTime.Now;
            SaveCache(percent, charging);

            return new BatteryReading
            {
                Percent = percent,
                Millivolts = millivolts,
                Charging = charging,
                Source = $"HID++ 0x1004 · {percent}%"
            };
        }

        // 鼠标休眠时 HID++ 无响应，这是无线鼠标的常态，不算断开：
        // 沿用最后一次有效值（进程内缓存，或上次运行写下的文件缓存）。
        _skipUntil = DateTime.Now.AddMinutes(5);
        _skipInputTick = InputActivity.LastInputTick();

        if (_last is { } cached)
        {
            return BuildSleeping(cached.Percent, cached.Charging, cached.Millivolts);
        }

        if (TryLoadCache(out var savedPercent, out var savedCharging))
            return BuildSleeping(savedPercent, savedCharging, 0);

        return BatteryReading.Fail(error ?? "未能读取鼠标电量");
    }

    private static BatteryReading BuildSleeping(int percent, bool charging, int millivolts) => new()
    {
        Percent = percent,
        Millivolts = millivolts,
        Charging = charging,
        Source = $"HID++ 0x1004 · {percent}%（休眠）"
    };

    private static void SaveCache(int percent, bool charging)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, $"{percent}|{charging}");
        }
        catch
        {
            // 忽略缓存写入失败
        }
    }

    private static bool TryLoadCache(out int percent, out bool charging)
    {
        percent = 0;
        charging = false;
        try
        {
            if (!File.Exists(CachePath)) return false;

            var parts = File.ReadAllText(CachePath).Split('|');
            if (parts.Length < 2) return false;
            if (!int.TryParse(parts[0], out percent) || percent is < 0 or > 100) return false;

            charging = parts[1] == "True";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool ReadState(out int percent, out int millivolts, out bool charging, out string? error)
    {
        percent = 0;
        millivolts = 0;
        charging = false;
        error = null;

        var exe = Path.Combine(AppContext.BaseDirectory, "HidReader.exe");
        if (!File.Exists(exe))
        {
            error = "缺少 HidReader.exe";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo(exe, "soc")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                error = "无法启动 HID 读取器";
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(4000);

            var fn1 = Parse(Fn1Pattern, output);
            if (fn1 is not { Length: >= 7 })
            {
                error = "鼠标未响应（移动一下鼠标后重试）";
                return false;
            }

            percent = fn1[4];
            if (percent > 100)
            {
                error = "电量数据异常";
                return false;
            }

            // fn1 flags bit0：0x01 = 充电中，0x00 = 未充电
            charging = (fn1[6] & 0x01) != 0;

            var fn0 = Parse(Fn0Pattern, output);
            if (fn0 is { Length: >= 7 })
                millivolts = fn0[4] | (fn0[5] << 8);

            return true;
        }
        catch (Exception ex)
        {
            error = "读取失败：" + ex.Message;
            return false;
        }
    }

    private static byte[]? Parse(Regex pattern, string output)
    {
        var match = pattern.Match(output);
        if (!match.Success) return null;

        return match.Groups[1].Value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(hex => byte.Parse(hex, NumberStyles.HexNumber))
            .ToArray();
    }
}
