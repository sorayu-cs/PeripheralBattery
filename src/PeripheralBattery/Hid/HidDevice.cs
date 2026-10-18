using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBattery.Hid;

/// <summary>
/// 一个已打开的 HID 接口。写句柄与异步读句柄分离，
/// 避免在同一个句柄上「阻塞读 + 并发写」导致进程被驱动杀掉。
/// </summary>
public sealed class HidDevice : IDisposable
{
    public string Path { get; }
    public int InputLen { get; private set; }
    public int OutputLen { get; private set; }
    public int FeatureLen { get; private set; }
    public bool CanWrite { get; private set; }

    private SafeFileHandle _handle;
    private FileStream? _readStream;
    private readonly object _sync = new();
    private bool _disposed;

    public HidDevice(string path, int inputLen = 0, int outputLen = 0, int featureLen = 0)
    {
        Path = path;
        InputLen = inputLen;
        OutputLen = outputLen;
        FeatureLen = featureLen;

        _handle = HidNative.CreateFile(path, HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
        if (_handle.IsInvalid)
        {
            // 系统独占的键鼠顶层集合只能以 0 访问权限打开
            _handle = HidNative.CreateFile(path, 0,
                HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING, 0, IntPtr.Zero);
            CanWrite = false;
        }
        else
        {
            CanWrite = true;
        }

        if (_handle.IsInvalid)
            throw new IOException("无法打开 HID 接口，错误码 " + Marshal.GetLastWin32Error());

        if (InputLen == 0 || OutputLen == 0 || FeatureLen == 0) ReadCaps();
    }

    private void ReadCaps()
    {
        if (!HidNative.HidD_GetPreparsedData(_handle, out var preparsed) || preparsed == IntPtr.Zero) return;
        try
        {
            var caps = new HidNative.HIDP_CAPS();
            if (HidNative.HidP_GetCaps(preparsed, ref caps) != HidNative.HIDP_STATUS_SUCCESS) return;
            InputLen = caps.InputReportByteLength;
            OutputLen = caps.OutputReportByteLength;
            FeatureLen = caps.FeatureReportByteLength;
        }
        finally
        {
            HidNative.HidD_FreePreparsedData(preparsed);
        }
    }

    /// <summary>发送 Output Report。useWriteFile=true 时走中断 OUT（与浏览器 WebHID 一致）。</summary>
    public void Send(byte[] data, bool useWriteFile)
    {
        if (!CanWrite) throw new InvalidOperationException("当前句柄没有写权限");
        if (useWriteFile)
        {
            if (!HidNative.WriteFile(_handle, data, data.Length, out _, IntPtr.Zero))
                throw new IOException("WriteFile 失败，错误码 " + Marshal.GetLastWin32Error());
            return;
        }

        if (!HidNative.HidD_SetOutputReport(_handle, data, data.Length))
            throw new IOException("HidD_SetOutputReport 失败，错误码 " + Marshal.GetLastWin32Error());
    }

    public byte[] GetFeature(int reportId, int length = 0)
    {
        var len = length > 0 ? length : (FeatureLen > 0 ? FeatureLen : 64);
        var buf = new byte[len];
        buf[0] = (byte)reportId;
        if (!HidNative.HidD_GetFeature(_handle, buf, len))
            throw new IOException("读取 Feature Report 失败，错误码 " + Marshal.GetLastWin32Error());
        return buf;
    }

    public void SetFeature(byte[] data)
    {
        if (!HidNative.HidD_SetFeature(_handle, data, data.Length))
            throw new IOException("写入 Feature Report 失败，错误码 " + Marshal.GetLastWin32Error());
    }

    /// <summary>读取一条 Input Report，超时返回 null。</summary>
    public byte[]? Read(int timeoutMs)
    {
        lock (_sync)
        {
            if (_disposed) return null;
            EnsureReader();

            var length = Math.Max(InputLen, 64);
            var buf = new byte[length];

            using var cts = new CancellationTokenSource(Math.Max(timeoutMs, 1));
            try
            {
                var task = _readStream!.ReadAsync(buf, 0, length, cts.Token);
                if (!task.Wait(timeoutMs + 40))
                {
                    ResetReader();
                    return null;
                }

                var n = task.Result;
                if (n <= 0) return null;
                var result = new byte[n];
                Array.Copy(buf, result, n);
                return result;
            }
            catch
            {
                ResetReader();
                return null;
            }
        }
    }

    /// <summary>丢弃驱动里堆积的旧报文。</summary>
    public void Drain(int budgetMs = 120)
    {
        var end = Environment.TickCount64 + budgetMs;
        while (Environment.TickCount64 < end)
        {
            if (Read(20) == null) return;
        }
    }

    private void EnsureReader()
    {
        if (_readStream != null) return;
        var readHandle = HidNative.CreateFile(Path, HidNative.GENERIC_READ,
            HidNative.FILE_SHARE_READ | HidNative.FILE_SHARE_WRITE, IntPtr.Zero, HidNative.OPEN_EXISTING,
            HidNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (readHandle.IsInvalid) return;
        _readStream = new FileStream(readHandle, FileAccess.Read, Math.Max(InputLen, 64), true);
    }

    private void ResetReader()
    {
        try { _readStream?.Dispose(); } catch { /* ignore */ }
        _readStream = null;
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            ResetReader();
            if (!_handle.IsInvalid)
            {
                try { _handle.Dispose(); } catch { /* ignore */ }
            }
        }
    }
}
