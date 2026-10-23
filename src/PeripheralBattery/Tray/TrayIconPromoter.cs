using System.Reflection;
using System.Runtime.InteropServices;

namespace PeripheralBattery.Tray;

/// <summary>
/// 请求 Windows 把托盘图标显示在任务栏（而不是折叠进隐藏图标区）。
/// Windows 11 的最终显示仍受用户“其他系统托盘图标”设置影响，
/// 这里做的是清除/设置 NIS_HIDDEN 状态。
/// 使用 NOTIFYICONDATA_V2 结构，避免结构体版本不匹配导致图标丢失。
/// </summary>
public static class TrayIconPromoter
{
    private const uint NimModify = 0x00000001;
    private const uint NifState = 0x00000008;
    private const uint NisHidden = 0x00000001;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint message, ref NOTIFYICONDATA_V2 data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA_V2
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
    }

    /// <summary>尝试清除图标的“隐藏”状态。失败时静默返回 false。</summary>
    public static bool TryShow(NotifyIcon icon) => TrySetHidden(icon, false);

    /// <summary>请求把图标折叠进隐藏区。失败时静默返回 false。</summary>
    public static bool TryHide(NotifyIcon icon) => TrySetHidden(icon, true);

    private static bool TrySetHidden(NotifyIcon icon, bool hidden)
    {
        try
        {
            var window = GetPrivateField(icon, "window") as NativeWindow;
            var id = GetPrivateField(icon, "id");
            if (window?.Handle == IntPtr.Zero || id is not uint iconId) return false;

            var data = new NOTIFYICONDATA_V2
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA_V2>(),
                hWnd = window.Handle,
                uID = iconId,
                uFlags = NifState,
                dwState = hidden ? NisHidden : 0,
                dwStateMask = NisHidden
            };

            return Shell_NotifyIcon(NimModify, ref data);
        }
        catch
        {
            return false;
        }
    }

    private static object? GetPrivateField(object target, string name)
    {
        var field = target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        return field?.GetValue(target);
    }
}
