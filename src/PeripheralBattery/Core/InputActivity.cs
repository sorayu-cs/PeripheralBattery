using System.Runtime.InteropServices;

namespace PeripheralBattery.Core;

/// <summary>检测用户是否有新的鼠标/键盘输入，用于唤醒后立即刷新电量。</summary>
public static class InputActivity
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LASTINPUTINFO
    {
        public uint cbSize;
        public uint dwTime;
    }

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

    private static uint _lastTick;

    /// <summary>读取当前的最后输入时间戳（不改变内部状态）。</summary>
    public static uint LastInputTick()
    {
        try
        {
            var info = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>(),
                dwTime = 0
            };

            return GetLastInputInfo(ref info) ? info.dwTime : 0;
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>自上次调用以来是否出现了新的用户输入。</summary>
    public static bool HasNewInput()
    {
        try
        {
            var info = new LASTINPUTINFO
            {
                cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>(),
                dwTime = 0
            };

            if (!GetLastInputInfo(ref info)) return false;
            if (info.dwTime == _lastTick) return false;

            _lastTick = info.dwTime;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
