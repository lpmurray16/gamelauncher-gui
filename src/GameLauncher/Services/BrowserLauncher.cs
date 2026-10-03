using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace GameLauncher.Services;

// The browser is chosen explicitly for this launcher, not registered as a Windows default.
public sealed class BrowserLauncher
{
    private const string PreferencesKey = @"Software\GameLauncher";
    private const string ValueName = "BrowserExecutable";

    public string? ExecutablePath
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PreferencesKey);
                return key?.GetValue(ValueName) as string;
            }
            catch (SecurityException ex) { throw new InvalidOperationException("Windows denied access to browser preferences: " + ex.Message, ex); }
        }
    }

    public void SetExecutable(string? path)
    {
        var executable = path is null ? null : ValidateExecutable(path);
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PreferencesKey, writable: true)
                ?? throw new InvalidOperationException("Browser preferences could not be opened.");
            if (executable is null) key.DeleteValue(ValueName, throwOnMissingValue: false);
            else key.SetValue(ValueName, executable, RegistryValueKind.String);
            if (ExecutablePath != executable)
                throw new InvalidOperationException("Windows did not retain the browser preference.");
        }
        catch (SecurityException ex) { throw new InvalidOperationException("Windows denied access to browser preferences: " + ex.Message, ex); }
    }

    public void Launch()
    {
        var saved = ExecutablePath;
        if (string.IsNullOrWhiteSpace(saved))
            throw new InvalidOperationException("Choose a browser in Settings first.");
        var executable = ValidateExecutable(saved);
        // Launch only the saved executable, with no shell command or request-supplied arguments.
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false
        });
        if (process is null) throw new InvalidOperationException("Windows did not accept the browser launch request.");
    }

    private static string ValidateExecutable(string path)
    {
        if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose your browser's .exe file.");
        return LaunchTarget.Normalize(path);
    }
}
