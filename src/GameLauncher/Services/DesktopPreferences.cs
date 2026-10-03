using System;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace GameLauncher.Services;

// Per-user settings only. Startup changes happen only from an explicit Settings POST.
public sealed class DesktopPreferences
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupName = "GameLauncher";
    private const string PreferencesKey = @"Software\GameLauncher";

    public bool StartupRegistered => Read(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(StartupName) is string value && !string.IsNullOrWhiteSpace(value);
    });

    public bool LaunchFullscreen => Read(() =>
    {
        using var key = Registry.CurrentUser.OpenSubKey(PreferencesKey);
        return key?.GetValue("LaunchFullscreen") is int value && value == 1;
    });

    public void SetStartup(bool enabled)
    {
        try
        {
            string? command = null;
            if (enabled)
            {
                var executable = Environment.ProcessPath;
                if (string.IsNullOrEmpty(executable) || !File.Exists(executable) ||
                    !string.Equals(Path.GetFileName(executable), "GameLauncher.exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Start GameLauncher.exe directly before enabling startup. The dotnet development host cannot be registered.");
                command = $"\"{executable}\"";
                // Windows Run values are limited to 260 characters.
                if (command.Length > 260)
                    throw new InvalidOperationException("The executable path is too long for Windows startup. Move the app to a shorter permanent path first.");
            }
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true)
                ?? throw new InvalidOperationException("Windows startup settings could not be opened.");
            if (enabled) key.SetValue(StartupName, command!, RegistryValueKind.String);
            else key.DeleteValue(StartupName, throwOnMissingValue: false);
            var saved = key.GetValue(StartupName) as string;
            if (enabled ? saved != command : saved is not null)
                throw new InvalidOperationException("Windows did not retain the requested startup setting.");
        }
        catch (SecurityException ex) { throw new InvalidOperationException("Windows denied access to startup settings: " + ex.Message, ex); }
    }

    public void SetLaunchFullscreen(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PreferencesKey, writable: true)
                ?? throw new InvalidOperationException("Desktop preferences could not be opened.");
            key.SetValue("LaunchFullscreen", enabled ? 1 : 0, RegistryValueKind.DWord);
            if (LaunchFullscreen != enabled)
                throw new InvalidOperationException("Windows did not retain the fullscreen preference.");
        }
        catch (SecurityException ex) { throw new InvalidOperationException("Windows denied access to desktop preferences: " + ex.Message, ex); }
    }

    private static bool Read(Func<bool> read)
    {
        try { return read(); }
        catch (SecurityException ex) { throw new InvalidOperationException("Windows denied access to desktop preferences: " + ex.Message, ex); }
    }
}
