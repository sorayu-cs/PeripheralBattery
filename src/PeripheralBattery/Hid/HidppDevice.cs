using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PeripheralBattery.Hid;

/// <summary>
/// 专用于罗技 HID++ 的轻量封装，句柄方式与 probe 验证通过的实现一致：
/// 写用 HidD_SetOutputReport（失败回退 WriteFile），读用独立 overlapped 句柄。
/// </summary>
public sealed class HidppDevice : IDisposable
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string fileName, int access, int share,
        IntPtr security, int creation, int flags, IntPtr template);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_SetOutputReport(SafeFileHandle device, byte[] buffer, int length);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(SafeFileHandle handle, byte[] buffer, int length, out int written, IntPtr overlapped);

    private readonly string _path;
    private readonly int _inputLength;
    private readonly object _readLock = new();
    private SafeFileHandle? _writeHandle;
    private FileStream? _readStream;
    private bool _disposed;

    public HidppDevice(string path, int inputLength)
    {
        _path = path;
        _inputLength = Math.Max(64, inputLength);

        _writeHandle = CreateFile(path, unchecked((int)0xC0000000), 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (_writeHandle.IsInvalid)
        {
            _writeHandle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (_writeHandle.IsInvalid)
                throw new IOException("无法打开 HID 接口，错误码 " + Marshal.GetLastWin32Error());
        }
    }

    public void Send(byte[] packet)
    {
        if (_writeHandle == null || _writeHandle.IsInvalid)
            throw new IOException("句柄已关闭");

        if (HidD_SetOutputReport(_writeHandle, packet, packet.Length)) return;
        if (WriteFile(_writeHandle, packet, packet.Length, out _, IntPtr.Zero)) return;

        throw new IOException("发送 HID 报文失败，错误码 " + Marshal.GetLastWin32Error());
    }

    public byte[]? Read(int timeoutMs)
    {
        lock (_readLock)
        {
            if (_disposed) return null;

            if (_readStream == null)
            {
                var handle = CreateFile(_path, unchecked((int)0x80000000), 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
                if (handle.IsInvalid) return null;
                _readStream = new FileStream(handle, FileAccess.Read, _inputLength, true);
            }

            var buffer = new byte[_inputLength];
            using var cts = new CancellationTokenSource(Math.Max(timeoutMs, 1));
            try
            {
                var task = _readStream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                if (!task.Wait(timeoutMs + 50)) return null;

                var count = task.Result;
                if (count <= 0) return null;

                var result = new byte[count];
                Array.Copy(buffer, result, count);
                return result;
            }
            catch
            {
                return null;
            }
        }
    }

    public void Drain(int budgetMs = 60)
    {
        var end = Environment.TickCount64 + budgetMs;
        while (Environment.TickCount64 < end)
        {
            if (Read(10) == null) return;
        }
    }

    public void Dispose()
    {
        lock (_readLock)
        {
            if (_disposed) return;
            _disposed = true;
            try { _readStream?.Dispose(); } catch { /* ignore */ }
            _readStream = null;
            try { _writeHandle?.Dispose(); } catch { /* ignore */ }
            _writeHandle = null;
        }
    }
}
