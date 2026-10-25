using Microsoft.Win32;

namespace GhubLite.Core.Services;

public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GhubLite";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) is string;
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (key is null)
        {
            throw new InvalidOperationException("无法写入开机启动项。");
        }

        if (enabled)
        {
            string? path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new InvalidOperationException("无法获取当前程序路径。");
            }

            key.SetValue(ValueName, $"\"{path}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
