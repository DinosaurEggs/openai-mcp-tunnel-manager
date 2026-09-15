using Microsoft.Win32;
using OpenAITunnelManager.Core.Abstractions;

namespace OpenAITunnelManager.Infrastructure.Windows;

public sealed class WindowsAutostartService : IAutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "OpenAITunnelManager";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
        var value = key?.GetValue(ValueName) as string;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var expected = BuildRunValue();
        return string.Equals(value.Trim(), expected, StringComparison.OrdinalIgnoreCase);
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
            ?? throw new InvalidOperationException("无法打开 Windows 启动项注册表");

        if (enabled)
        {
            key.SetValue(ValueName, BuildRunValue(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    private static string BuildRunValue()
    {
        var executable = Environment.ProcessPath
            ?? Path.Combine(AppContext.BaseDirectory, "OpenAITunnelManager.exe");
        return $"\"{Path.GetFullPath(executable)}\"";
    }
}
