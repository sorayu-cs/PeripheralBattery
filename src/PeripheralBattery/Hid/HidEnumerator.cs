using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBattery.Hid;

/// <summary>枚举系统里的所有 HID 接口。</summary>
public static class HidEnumerator
{
    public static List<HidInterface> Enumerate()
    {
        var list = new List<HidInterface>();
        var guid = HidNative.HidGuid;
        var set = HidNative.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero,
            HidNative.DIGCF_PRESENT | HidNative.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == HidNative.INVALID_HANDLE_VALUE)
            throw new IOException("SetupDiGetClassDevs 失败，错误码 " + Marshal.GetLastWin32Error());

        try
        {
            for (var i = 0; ; i++)
            {
                var did = new HidNative.SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf<HidNative.SP_DEVICE_INTERFACE_DATA>();
                if (!HidNative.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did))
                    break;

                var need = 0;
                HidNative.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, ref need, IntPtr.Zero);
                if (need <= 0) continue;

                var buf = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!HidNative.SetupDiGetDeviceInterfaceDetail(set, ref did, buf, need, ref need, IntPtr.Zero))
                        continue;

                    var path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4)) ?? "";
                    if (path.Length == 0) continue;

                    var item = Probe(path);
                    if (item != null) list.Add(item);
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
        }
        finally
        {
            HidNative.SetupDiDestroyDeviceInfoList(set);
        }

        return list;
    }

    private static HidInterface Probe(string path)
    {
        using var handle = HidNative.CreateFile(path, 0,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) return null;

        var vid = 0;
        var pid = 0;
        var version = 0;
        var product = "";
        var manufacturer = "";
        var usagePage = 0;
        var usage = 0;
        var inputLen = 0;
        var outputLen = 0;
        var featureLen = 0;

        var attrs = new HidNative.HIDD_ATTRIBUTES { Size = Marshal.SizeOf<HidNative.HIDD_ATTRIBUTES>() };
        if (HidNative.HidD_GetAttributes(handle, ref attrs))
        {
            vid = attrs.VendorID;
            pid = attrs.ProductID;
            version = attrs.VersionNumber;
        }

        product = ReadString(HidNative.HidD_GetProductString, handle);
        manufacturer = ReadString(HidNative.HidD_GetManufacturerString, handle);

        if (HidNative.HidD_GetPreparsedData(handle, out var preparsed) && preparsed != IntPtr.Zero)
        {
            try
            {
                var caps = new HidNative.HIDP_CAPS();
                if (HidNative.HidP_GetCaps(preparsed, ref caps) == HidNative.HIDP_STATUS_SUCCESS)
                {
                    usagePage = caps.UsagePage;
                    usage = caps.Usage;
                    inputLen = caps.InputReportByteLength;
                    outputLen = caps.OutputReportByteLength;
                    featureLen = caps.FeatureReportByteLength;
                }
            }
            finally
            {
                HidNative.HidD_FreePreparsedData(preparsed);
            }
        }

        // 部分设备路径里带 vid/pid，属性读不到时兜底解析
        if (vid == 0 || pid == 0)
        {
            var lower = path.ToLowerInvariant();
            var vi = lower.IndexOf("vid_", StringComparison.Ordinal);
            var pi = lower.IndexOf("pid_", StringComparison.Ordinal);
            if (vi >= 0 && vi + 8 <= lower.Length) vid = Convert.ToUInt16(lower.Substring(vi + 4, 4), 16);
            if (pi >= 0 && pi + 8 <= lower.Length) pid = Convert.ToUInt16(lower.Substring(pi + 4, 4), 16);
        }

        return new HidInterface
        {
            Path = path,
            Vid = (ushort)vid,
            Pid = (ushort)pid,
            Version = (ushort)version,
            UsagePage = (ushort)usagePage,
            Usage = (ushort)usage,
            InputLen = inputLen,
            OutputLen = outputLen,
            FeatureLen = featureLen,
            Product = product,
            Manufacturer = manufacturer
        };
    }

    private static string ReadString(Func<SafeFileHandle, byte[], int, bool> fn, SafeFileHandle handle)
    {
        var buf = new byte[512];
        if (!fn(handle, buf, buf.Length)) return "";
        var s = System.Text.Encoding.Unicode.GetString(buf);
        var z = s.IndexOf('\0');
        return (z >= 0 ? s[..z] : s).Trim();
    }
}
