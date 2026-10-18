namespace PeripheralBattery.Hid;

/// <summary>一个 HID 顶层集合（setupapi 中的一个设备接口）。</summary>
public sealed class HidInterface
{
    public string Path { get; init; } = "";
    public ushort Vid { get; init; }
    public ushort Pid { get; init; }
    public ushort Version { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public int InputLen { get; init; }
    public int OutputLen { get; init; }
    public int FeatureLen { get; init; }
    public string Product { get; init; } = "";
    public string Manufacturer { get; init; } = "";

    public string VidPid => $"{Vid:X4}:{Pid:X4}";

    public string DisplayName => string.IsNullOrWhiteSpace(Product)
        ? (string.IsNullOrWhiteSpace(Manufacturer) ? "HID 设备" : Manufacturer + " HID 设备")
        : Product;

    public override string ToString() =>
        $"{VidPid} up={UsagePage:X4}/{Usage:X4} in={InputLen} out={OutputLen} feat={FeatureLen} | {DisplayName}";

    /// <summary>同一物理设备的多个接口归为一组。</summary>
    public static string GroupKey(string path)
    {
        var parts = path.Split('#');
        if (parts.Length < 2) return path;
        var keep = new List<string>();
        foreach (var p in parts[1].Split('&'))
        {
            var low = p.ToLowerInvariant();
            if (low.StartsWith("col") || low.StartsWith("mi_")) continue;
            keep.Add(p);
        }
        return keep.Count == 0 ? parts[1] : string.Join("&", keep);
    }
}
