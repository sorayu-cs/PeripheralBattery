using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeripheralBattery.Core;

/// <summary>应用配置，保存在 %APPDATA%\PeripheralBattery\config.json。</summary>
public sealed class AppConfig
{
    public int RefreshSeconds { get; set; } = 60;
    public bool Autostart { get; set; }
    public bool StartMinimized { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool TrayIconPinned { get; set; } = true;
    public List<DeviceProfile> Devices { get; set; } = new();

    [JsonIgnore]
    public string FilePath { get; private set; } = DefaultPath();

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PeripheralBattery");

    public static string DefaultPath() => Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(DefaultPath()))
            {
                var json = File.ReadAllText(DefaultPath());
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (cfg != null)
                {
                    cfg.FilePath = DefaultPath();
                    if (cfg.Devices.Count == 0) cfg.Devices = CreateDefaults();
                    cfg.Migrate();
                    return cfg;
                }
            }
        }
        catch
        {
            // 配置损坏时回到默认值，不让托盘程序起不来
        }

        var fresh = new AppConfig { Devices = CreateDefaults() };
        fresh.Save();
        return fresh;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(ConfigDirectory);
            var json = JsonSerializer.Serialize(this, JsonOptions);
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // 保存失败不影响使用
        }
    }

    /// <summary>旧版本配置迁移：迈从电量偏移从 1 改为 31（0x05 状态帧）。</summary>
    private void Migrate()
    {
        var changed = false;
        foreach (var device in Devices)
        {
            if (!string.Equals(device.Protocol, "mchose_qhw", StringComparison.OrdinalIgnoreCase)) continue;
            if (!device.Options.TryGetValue("batteryOffset", out var offset) || offset == "1")
            {
                device.Options["batteryOffset"] = "31";
                changed = true;
            }
        }

        if (changed) Save();
    }

    public static List<DeviceProfile> CreateDefaults() => new()
    {
        new DeviceProfile
        {
            Name = "罗技 G Pro Wireless",
            Vid = 0x046D,
            Pid = 0xC547,
            Protocol = "logitech_hidpp",
            Options = new Dictionary<string, string>
            {
                ["deviceIndex"] = "1",
                ["usagePage"] = "FF00",
                ["usage"] = "0002"
            }
        },
        new DeviceProfile
        {
            Name = "迈从 K99 V3",
            Vid = 0x3837,
            Pid = 0x3033,
            Protocol = "mchose_qhw",
            Options = new Dictionary<string, string>
            {
                ["usagePage"] = "0001",
                ["usage"] = "0000",
                ["batteryOffset"] = "31"
            }
        }
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };
}
