using Microsoft.Win32;
using System.Runtime.Versioning;

namespace VoiceFlow.App;

/// <summary>
/// Launch-at-login via the classic per-user Run key. Registry failures are
/// left to propagate — the caller (the settings UI, like
/// <c>SettingsView.swift</c>'s launchAtLogin setter) is responsible for
/// catching them and reverting the toggle rather than persisting a state the
/// OS never actually applied.
/// </summary>
[SupportedOSPlatform("windows")]
public static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "VoiceFlow";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled)
        {
            string exePath = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not determine the running executable's path.");
            key.SetValue(ValueName, $"\"{exePath}\"");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
