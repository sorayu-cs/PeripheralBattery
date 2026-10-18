namespace PeripheralBattery.Core;

/// <summary>按型号词判断配置设备与蓝牙设备是否同一台（例如都含 “K99”）。</summary>
public static class DeviceMatcher
{
    public static bool SameDevice(string configName, string bluetoothName)
    {
        var tokens = bluetoothName.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        return tokens.Any(token =>
            token.Length >= 3 &&
            !token.All(char.IsDigit) &&
            configName.Contains(token, StringComparison.OrdinalIgnoreCase));
    }
}
