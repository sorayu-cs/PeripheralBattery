using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Probe;

public sealed class HidInterface
{
    public string Path;
    public ushort Vid, Pid, Version;
    public ushort UsagePage, Usage;
    public ushort InputLen, OutputLen, FeatureLen;
    public string Product = "", Manufacturer = "", Serial = "";
    public string Error = "";
    public bool Open;
    public string Group;

    public override string ToString() =>
        string.Format("{0:X4}:{1:X4} up={2:X4} u={3:X4} in={4} out={5} feat={6} | {7} | {8}",
            Vid, Pid, UsagePage, Usage, InputLen, OutputLen, FeatureLen,
            string.IsNullOrEmpty(Product) ? "(no name)" : Product, Path);
}

internal static class Native
{
    public const int DIGCF_PRESENT = 0x02;
    public const int DIGCF_DEVICEINTERFACE = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    public struct GUID
    {
        public uint Data1;
        public ushort Data2;
        public ushort Data3;
        public byte B0, B1, B2, B3, B4, B5, B6, B7;
        public GUID(uint a, ushort b, ushort c, byte[] d)
        {
            Data1 = a; Data2 = b; Data3 = c;
            B0 = d[0]; B1 = d[1]; B2 = d[2]; B3 = d[3];
            B4 = d[4]; B5 = d[5]; B6 = d[6]; B7 = d[7];
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public GUID InterfaceClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref GUID classGuid, string enumerator, IntPtr hwndParent, int flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr hDevInfo, IntPtr devInfo, ref GUID interfaceClassGuid, int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr hDevInfo, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize, ref int requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(SafeFileHandle handle, ref HIDD_ATTRIBUTES attributes);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS capabilities);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool HidD_GetProductString(SafeFileHandle handle, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool HidD_GetManufacturerString(SafeFileHandle handle, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool HidD_GetSerialNumberString(SafeFileHandle handle, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetFeature(SafeFileHandle handle, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetOutputReport(SafeFileHandle handle, byte[] buffer, int bufferLength);

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_RANGE
    {
        public ushort UsageMin, UsageMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_NOT_RANGE
    {
        public ushort Usage, Reserved1, StringIndex, Reserved2, DesignatorIndex, Reserved3, DataIndex, Reserved4;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct HIDP_USAGE_UNION
    {
        [FieldOffset(0)] public HIDP_RANGE Range;
        [FieldOffset(0)] public HIDP_NOT_RANGE NotRange;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_VALUE_CAPS
    {
        public ushort UsagePage;
        public byte ReportID;
        public byte IsAlias;
        public ushort BitField;
        public ushort LinkCollection;
        public ushort LinkUsage;
        public ushort LinkUsagePage;
        public byte IsRange;
        public byte IsStringRange;
        public byte IsDesignatorRange;
        public byte IsAbsolute;
        public byte HasNull;
        public byte Reserved;
        public ushort BitSize;
        public ushort ReportCount;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
        public ushort[] Reserved2;
        public uint UnitsExp;
        public uint Units;
        public int LogicalMin;
        public int LogicalMax;
        public int PhysicalMin;
        public int PhysicalMax;
        public HIDP_USAGE_UNION u;
    }

    [DllImport("hid.dll")]
    public static extern int HidP_GetValueCaps(int reportType, [Out] HIDP_VALUE_CAPS[] valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll", EntryPoint = "HidP_GetValueCaps")]
    public static extern int HidP_GetValueCapsPtr(int reportType, IntPtr valueCaps, ref ushort valueCapsLength, IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetReportDescriptor(SafeFileHandle handle, byte[] buffer, int bufferLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern SafeFileHandle CreateFile(string fileName, int desiredAccess, int shareMode, IntPtr securityAttributes, int creationDisposition, int flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, int numberOfBytesToWrite, out int numberOfBytesWritten, IntPtr overlapped);

    public static readonly GUID HidGuid = new GUID(0x4D1E55B2, 0xF16F, 0x11CF, new byte[] { 0x88, 0xCB, 0x00, 0x11, 0x11, 0x00, 0x00, 0x30 });
}

public static class Hid
{
    public static List<HidInterface> Enumerate()
    {
        var list = new List<HidInterface>();
        var guid = Native.HidGuid;
        IntPtr set = Native.SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
            throw new IOException("SetupDiGetClassDevs failed: " + Marshal.GetLastWin32Error());
        try
        {
            for (int i = 0; ; i++)
            {
                var did = new Native.SP_DEVICE_INTERFACE_DATA();
                did.cbSize = Marshal.SizeOf(typeof(Native.SP_DEVICE_INTERFACE_DATA));
                if (!Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did))
                    break;
                int need = 0;
                Native.SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, ref need, IntPtr.Zero);
                if (need <= 0) continue;
                IntPtr buf = Marshal.AllocHGlobal(need);
                try
                {
                    Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                    if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref did, buf, need, ref need, IntPtr.Zero))
                        continue;
                    string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4)) ?? "";
                    if (path.Length == 0) continue;
                    list.Add(ProbeOne(path));
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
        }
        finally { Native.SetupDiDestroyDeviceInfoList(set); }
        return list;
    }

    private static HidInterface ProbeOne(string path)
    {
        var it = new HidInterface { Path = path, Group = GroupKey(path) };
        SafeFileHandle h = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h.IsInvalid)
        {
            it.Error = "open0 err=" + Marshal.GetLastWin32Error();
            return it;
        }
        try
        {
            var attrs = new Native.HIDD_ATTRIBUTES();
            attrs.Size = Marshal.SizeOf(typeof(Native.HIDD_ATTRIBUTES));
            if (Native.HidD_GetAttributes(h, ref attrs))
            {
                it.Vid = attrs.VendorID; it.Pid = attrs.ProductID; it.Version = attrs.VersionNumber;
            }
            it.Product = Wide(Native.HidD_GetProductString, h);
            it.Manufacturer = Wide(Native.HidD_GetManufacturerString, h);
            it.Serial = Wide(Native.HidD_GetSerialNumberString, h);
            if (Native.HidD_GetPreparsedData(h, out IntPtr pre) && pre != IntPtr.Zero)
            {
                try
                {
                    var caps = new Native.HIDP_CAPS();
                    if (Native.HidP_GetCaps(pre, ref caps) == 0x00110000)
                    {
                        it.UsagePage = caps.UsagePage; it.Usage = caps.Usage;
                        it.InputLen = caps.InputReportByteLength;
                        it.OutputLen = caps.OutputReportByteLength;
                        it.FeatureLen = caps.FeatureReportByteLength;
                    }
                }
                finally { Native.HidD_FreePreparsedData(pre); }
            }
            it.Open = true;
        }
        finally { h.Dispose(); }
        return it;
    }

    private static string Wide(Func<SafeFileHandle, byte[], int, bool> fn, SafeFileHandle h)
    {
        byte[] buf = new byte[512];
        if (!fn(h, buf, buf.Length)) return "";
        string s = System.Text.Encoding.Unicode.GetString(buf);
        int z = s.IndexOf('\0');
        return (z >= 0 ? s.Substring(0, z) : s).Trim();
    }

    public static string GroupKey(string path)
    {
        var parts = path.Split('#');
        if (parts.Length < 2) return path;
        var keep = new List<string>();
        foreach (var p in parts[1].Split('&'))
        {
            string low = p.ToLowerInvariant();
            if (low.StartsWith("col") || low.StartsWith("mi_")) continue;
            keep.Add(p);
        }
        return keep.Count == 0 ? parts[1] : string.Join("&", keep);
    }
}

/// <summary>Opened HID interface: separate handles for output writes and async reads.</summary>
public sealed class HidDevice : IDisposable
{
    public string Path;
    public int InputLen, OutputLen, FeatureLen;
    private SafeFileHandle _writeHandle;
    private FileStream _readStream;
    private readonly object _readLock = new object();
    private byte[] _readBuf;

    public HidDevice(string path, int inputLen = 0, int outputLen = 0, int featureLen = 0)
    {
        Path = path;
        InputLen = inputLen; OutputLen = outputLen; FeatureLen = featureLen;
        _writeHandle = Native.CreateFile(path, unchecked((int)0xC0000000), 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (_writeHandle.IsInvalid)
        {
            _writeHandle = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_writeHandle.IsInvalid)
                throw new IOException("CreateFile failed: " + Marshal.GetLastWin32Error());
        }
        if (InputLen == 0 || OutputLen == 0 || FeatureLen == 0)
        {
            if (Native.HidD_GetPreparsedData(_writeHandle, out IntPtr pre) && pre != IntPtr.Zero)
            {
                try
                {
                    var caps = new Native.HIDP_CAPS();
                    if (Native.HidP_GetCaps(pre, ref caps) == 0x00110000)
                    {
                        InputLen = caps.InputReportByteLength;
                        OutputLen = caps.OutputReportByteLength;
                        FeatureLen = caps.FeatureReportByteLength;
                    }
                }
                finally { Native.HidD_FreePreparsedData(pre); }
            }
        }
    }

    public bool CanWrite { get; private set; } = true;

    public byte[] GetFeature(int reportId, int len = 0)
    {
        if (len <= 0) len = FeatureLen > 0 ? FeatureLen : 64;
        var buf = new byte[len];
        buf[0] = (byte)reportId;
        if (!Native.HidD_GetFeature(_writeHandle, buf, len))
            throw new IOException("HidD_GetFeature failed: " + Marshal.GetLastWin32Error());
        return buf;
    }

    public void SetFeature(byte[] data)
    {
        if (!Native.HidD_SetFeature(_writeHandle, data, data.Length))
            throw new IOException("HidD_SetFeature failed: " + Marshal.GetLastWin32Error());
    }

    public void WriteOutput(byte[] data)
    {
        if (Native.HidD_SetOutputReport(_writeHandle, data, data.Length)) return;
        int err = Marshal.GetLastWin32Error();
        // fall back to WriteFile on a non-owning wrapper (do not dispose the shared handle)
        var wrapper = new SafeFileHandle(_writeHandle.DangerousGetHandle(), false);
        using var fs = new FileStream(wrapper, FileAccess.Write, 1, false);
        fs.Write(data, 0, data.Length);
        fs.Flush();
        _ = err;
    }

    /// <summary>直接写中断 OUT 端点（WriteFile），与浏览器 WebHID 的行为一致。</summary>
    public void WriteFileReport(byte[] data)
    {
        if (!Native.WriteFile(_writeHandle, data, data.Length, out int written, IntPtr.Zero))
            throw new IOException("WriteFile failed: " + Marshal.GetLastWin32Error());
    }

    public byte[] Read(int timeoutMs)
    {
        lock (_readLock)
        {
            if (_readStream == null)
            {
                var h = Native.CreateFile(Path, unchecked((int)0x80000000), 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if (h.IsInvalid) return null;
                _readStream = new FileStream(h, FileAccess.Read, Math.Max(1, InputLen), true);
                _readBuf = new byte[Math.Max(64, InputLen)];
            }
            var buf = new byte[_readBuf.Length];
            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                var task = _readStream.ReadAsync(buf, 0, buf.Length, cts.Token);
                if (!task.Wait(timeoutMs + 50)) return null;
                int n = task.Result;
                if (n <= 0) return null;
                var res = new byte[n];
                Array.Copy(buf, res, n);
                return res;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public void Drain()
    {
        for (int i = 0; i < 32; i++)
        {
            var r = Read(10);
            if (r == null) break;
        }
    }

    public void Dispose()
    {
        _readStream?.Dispose();
        _writeHandle?.Dispose();
    }
}
