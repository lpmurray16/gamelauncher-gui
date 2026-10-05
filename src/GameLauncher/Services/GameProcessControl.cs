using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using GameLauncher.Domain;

namespace GameLauncher.Services;

// Accept only a database entry, never a remote PID, executable path or shell command.
internal static class GameProcessControl
{
    public static string Stop(LibraryEntry entry, bool force)
    {
        var path = GameStatusMonitor.TrackingPath(entry)
            ?? throw new InvalidOperationException("Set the game's tracking executable in Windows Edit first. Shortcuts and Steam URLs cannot identify a process to stop.");
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Equals("steam", StringComparison.OrdinalIgnoreCase)
            || name.Equals("steamservice", StringComparison.OrdinalIgnoreCase)
            || string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This entry tracks a launcher, not the game. Set its tracking executable to the actual game in Windows Edit.");

        using var current = Process.GetCurrentProcess();
        Process[] processes;
        try { processes = Process.GetProcessesByName(name); }
        catch (Exception error) when (IsProcessFailure(error))
        { throw new InvalidOperationException("Windows could not inspect the game processes. Check the PC."); }

        try
        {
            var matches = new List<Process>();
            var uncertain = false;
            foreach (var process in processes)
            {
                try
                {
                    // Open and retain the handle before identifying the process, so PID reuse
                    // cannot redirect a later Kill to a new process. Never match by name alone.
                    _ = process.Handle;
                    if (process.HasExited) continue;
                    if (process.SessionId != current.SessionId || process.Id == current.Id) continue;
                    if (process.MainModule?.FileName is not string actual) { uncertain = true; continue; }
                    if (string.Equals(Path.GetFullPath(actual), path, StringComparison.OrdinalIgnoreCase))
                        matches.Add(process);
                }
                catch (InvalidOperationException) { /* Exited during inspection. */ }
                catch (Exception error) when (IsProcessFailure(error)) { uncertain = true; }
            }
            // No action at all when identity is ambiguous; never stop every same-name process.
            if (uncertain)
                throw new InvalidOperationException("Windows could not verify every candidate process. An administrator-run game may need to be closed on the PC. Nothing was stopped.");
            if (matches.Count == 0)
                throw new InvalidOperationException("No matching game process was found in this Windows session. Check the tracking executable on the PC.");
            if (matches.Count != 1)
                throw new InvalidOperationException("More than one matching process is running. Close the game on the PC; remote stop will not guess which instance to close.");

            var target = matches[0];
            bool closeRequested;
            try
            {
                if (target.HasExited) return "The tracked game process has already exited.";
                if (force)
                {
                    target.Kill(entireProcessTree: false);
                    return "Force-stop request sent for the tracked game process only. Playing clears when Windows observes its exit.";
                }
                closeRequested = target.CloseMainWindow();
            }
            catch (Exception error) when (IsProcessFailure(error))
            { throw new InvalidOperationException("Windows could not complete the stop request. The game may have exited already; administrator-run or protected games may need to be closed on the PC."); }
            if (!closeRequested)
                throw new InvalidOperationException("The game has no closeable main window. Close it on the PC, or use Force stop if you accept losing unsaved progress.");
            return "Close request sent. The game may show a save/exit dialog on the PC. If it stays running, you can choose Force stop.";
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static bool IsProcessFailure(Exception error) => error is
        Win32Exception or InvalidOperationException or NotSupportedException or UnauthorizedAccessException
        or IOException or SecurityException or ArgumentException;
}
