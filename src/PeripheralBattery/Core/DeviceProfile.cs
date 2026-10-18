using System.Globalization;

namespace PeripheralBattery.Core;

/// <summary>一台设备的配置。协议通过字符串标识，便于以后扩展。</summary>
public sealed class DeviceProfile
{
    public string Name { get; set; } = "";
    public ushort Vid { get; set; }
    public ushort Pid { get; set; }
    public string Protocol { get; set; } = "logitech_hidpp";
    public bool Enabled { get; set; } = true;
    public Dictionary<string, string> Options { get; set; } = new();

    public string Key => $"{Vid:X4}:{Pid:X4}:{Protocol}:{Name}";

    public string VidPid => $"{Vid:X4}:{Pid:X4}";

    public int GetInt(string key, int fallback)
    {
        if (Options.TryGetValue(key, out var raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            return v;
        return fallback;
    }

    public ushort GetUShort(string key, ushort fallback)
    {
        if (!Options.TryGetValue(key, out var raw)) return fallback;
        raw = raw.Trim();
        if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            raw = raw[2..];
        if (ushort.TryParse(raw, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)) return v;
        return fallback;
    }

    public double GetDouble(string key, double fallback)
    {
        if (Options.TryGetValue(key, out var raw) &&
            double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            return v;
        return fallback;
    }

    public string GetString(string key, string fallback = "") =>
        Options.TryGetValue(key, out var raw) ? raw : fallback;

    public DeviceProfile Clone() => new()
    {
        Name = Name,
        Vid = Vid,
        Pid = Pid,
        Protocol = Protocol,
        Enabled = Enabled,
        Options = new Dictionary<string, string>(Options)
    };
}
