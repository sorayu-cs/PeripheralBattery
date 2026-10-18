namespace PeripheralBattery.Core;

/// <summary>
/// 开机自启：在「启动」文件夹里创建快捷方式（.lnk）。
/// 任务管理器 →「启动」里能直接看到并启用/禁用。
/// </summary>
public static class Autostart
{
    public const string ShortcutName = "外设电量监控.lnk";

    public static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), ShortcutName);

    public static string ExecutablePath =>
        Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "PeripheralBattery.exe");

    public static bool IsEnabled => File.Exists(ShortcutPath);

    public static bool Enable(out string error)
    {
        error = "";
        try
        {
            var exe = ExecutablePath;
            if (!File.Exists(exe))
            {
                error = "找不到程序文件：" + exe;
                return false;
            }

            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                error = "系统缺少 WScript.Shell";
                return false;
            }

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(ShortcutPath);
            shortcut.TargetPath = exe;
            shortcut.Arguments = "--minimized";
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
            shortcut.Description = "外设电量监控";
            shortcut.IconLocation = exe + ",0";
            shortcut.Save();

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool Disable(out string error)
    {
        error = "";
        try
        {
            if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
