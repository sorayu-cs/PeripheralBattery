using System.Runtime.InteropServices;
using System.Text;

namespace PeripheralBattery.Core;

public sealed record BluetoothBatteryInfo(string Name, int Percent, bool Charging);

/// <summary>
/// 读取蓝牙 LE 设备（BTHLE）通过 GATT 电池服务上报的电量。
/// Windows 把 Battery Level（{104EA319-...} PID 2）和充电状态（PID 3）缓存在设备属性里，
/// 这里用 SetupAPI 枚举 BTHLE 设备并读取对应属性。
/// </summary>
public static class BluetoothBattery
{
    private const uint DigcfPresent = 0x2;
    private const uint DigcfAllClasses = 0x4;
    private const uint DevpropTypeByte = 0x3;
    private const uint DevpropTypeFileTime = 0x10;
    private const uint DevpropTypeBoolean = 0x11;
    private const uint DevpropTypeString = 0x12;
    private const int ErrorInsufficientBuffer = 122;

    private static readonly Guid BatteryFmtid = new("104ea319-6ee2-4701-bd47-8ddbf425bbe5");
    private static readonly Guid NameFmtid = new("a45c254e-df1c-4efd-8020-67d146a850e0");
    private static readonly Guid DeviceFmtid = new("83da6326-97a6-4088-9453-a1923f573b29");

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public int DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DEVPROPKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceProperty(IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref DEVPROPKEY propertyKey, out uint propertyType, byte[]? propertyBuffer, uint propertyBufferSize,
        out uint requiredSize, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);

    public static List<BluetoothBatteryInfo> Discover()
    {
        var result = new List<BluetoothBatteryInfo>();
        var guid = Guid.Empty;
        var set = SetupDiGetClassDevs(ref guid, "BTHLE", IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (set == IntPtr.Zero || set == new IntPtr(-1)) return result;

        try
        {
            var data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                // 设备不在场（已断开/移出）时直接跳过
                var present = ReadBoolean(set, ref data, DeviceFmtid, 15);
                if (present == false) continue;

                // 设备节点必须处于“已启动”状态（断开/移出的设备不会置位 DN_STARTED）
                if (CM_Get_DevNode_Status(out var status, out _, (uint)data.DevInst, 0) != 0 ||
                    (status & 0x8) == 0)
                    continue;

                // 只有最近上报过电量更新时间的设备才算真正连接；
                // 配对但未连接的设备没有 PID 7（时间）属性。
                var updated = ReadFileTime(set, ref data, BatteryFmtid, 7);
                if (updated is null || DateTime.UtcNow - updated.Value > TimeSpan.FromMinutes(1)) continue;

                var percent = ReadByte(set, ref data, BatteryFmtid, 2);
                if (percent is null or <= 0 or > 100) continue;

                var charging = ReadBoolean(set, ref data, BatteryFmtid, 3) ?? false;
                var name = ReadString(set, ref data, NameFmtid, 0x0E) ?? "蓝牙设备";
                result.Add(new BluetoothBatteryInfo(name, percent.Value, charging));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }

        return result;
    }

    private static int? ReadByte(IntPtr set, ref SP_DEVINFO_DATA data, Guid fmtid, uint pid)
    {
        if (!TryRead(set, ref data, fmtid, pid, out var type, out var buffer)) return null;
        if (type != DevpropTypeByte || buffer.Length == 0) return null;
        return buffer[0];
    }

    private static DateTime? ReadFileTime(IntPtr set, ref SP_DEVINFO_DATA data, Guid fmtid, uint pid)
    {
        if (!TryRead(set, ref data, fmtid, pid, out var type, out var buffer)) return null;
        if (type != DevpropTypeFileTime || buffer.Length < 8) return null;
        var fileTime = BitConverter.ToInt64(buffer, 0);
        if (fileTime <= 0) return null;
        try
        {
            return DateTime.FromFileTimeUtc(fileTime);
        }
        catch
        {
            return null;
        }
    }

    private static bool? ReadBoolean(IntPtr set, ref SP_DEVINFO_DATA data, Guid fmtid, uint pid)
    {
        if (!TryRead(set, ref data, fmtid, pid, out var type, out var buffer)) return null;
        if (buffer.Length == 0) return null;
        if (type is DevpropTypeBoolean or DevpropTypeByte) return buffer[0] != 0;
        return null;
    }

    private static string? ReadString(IntPtr set, ref SP_DEVINFO_DATA data, Guid fmtid, uint pid)
    {
        if (!TryRead(set, ref data, fmtid, pid, out var type, out var buffer)) return null;
        if (type != DevpropTypeString) return null;
        var text = Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static bool TryRead(IntPtr set, ref SP_DEVINFO_DATA data, Guid fmtid, uint pid,
        out uint type, out byte[] buffer)
    {
        type = 0;
        buffer = Array.Empty<byte>();
        var key = new DEVPROPKEY { fmtid = fmtid, pid = pid };

        if (!SetupDiGetDeviceProperty(set, ref data, ref key, out type, null, 0, out var size, 0))
        {
            if (Marshal.GetLastWin32Error() != ErrorInsufficientBuffer) return false;
        }

        if (size == 0) return false;
        buffer = new byte[size];
        return SetupDiGetDeviceProperty(set, ref data, ref key, out type, buffer, size, out _, 0);
    }
}
