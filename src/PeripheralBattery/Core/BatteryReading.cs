namespace PeripheralBattery.Core;

/// <summary>一次电量读取的结果。</summary>
public sealed class BatteryReading
{
    public int? Percent { get; set; }
    public bool? Charging { get; set; }
    public int? Millivolts { get; set; }
    public string Source { get; set; } = "";
    public string? Error { get; set; }
    public bool Disconnected { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public bool Ok => Percent.HasValue;

    public static BatteryReading Fail(string message) => new() { Error = message };

    public string Describe()
    {
        if (!Ok) return Error ?? "未读取到电量";
        var text = Percent + "%";
        if (Charging == true) text += "（充电中）";
        if (Disconnected) text += "（已断开）";
        if (Millivolts is > 0) text += $" · {Millivolts} mV";
        return text;
    }
}
