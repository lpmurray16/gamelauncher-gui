using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;

namespace GameLauncher.Services;

public sealed record ScanFolderSuggestion(string Label, string FolderPath);

// Folder-name heuristics only: no storefront APIs, registry, manifests or accounts.
public static class CommonScanFolders
{
    public static IReadOnlyList<ScanFolderSuggestion> Discover()
    {
        var suggestions = new List<ScanFolderSuggestion>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string label, string parent, string relative)
        {
            if (string.IsNullOrWhiteSpace(parent)) return;
            try
            {
                var path = Path.GetFullPath(Path.Combine(parent, relative));
                if (path.StartsWith(@"\\", StringComparison.Ordinal) || !Directory.Exists(path)) return;
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) return;
                if (seen.Add(path)) suggestions.Add(new(label, path));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or SecurityException)
            {
                // An inaccessible or disappearing suggestion must not block manual browsing.
            }
        }

        foreach (var programFiles in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        })
        {
            Add("Steam games", programFiles, @"Steam\steamapps\common");
            Add("Epic Games", programFiles, "Epic Games");
            Add("GOG Galaxy games", programFiles, @"GOG Galaxy\Games");
            Add("GOG games", programFiles, "GOG Games");
        }

        // Check only these named folders on ready fixed drives, never walk a drive.
        // Custom paths and removable drives remain available through Browse folders.
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    var root = drive.RootDirectory.FullName;
                    Add("Steam library", root, @"SteamLibrary\steamapps\common");
                    Add("Steam games", root, @"Steam\steamapps\common");
                    Add("Epic Games", root, "Epic Games");
                    Add("GOG games", root, "GOG Games");
                    Add("Games folder", root, "Games");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
                {
                    // Skip unavailable drives without failing the scan page.
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Program Files suggestions can still be used if drive discovery fails.
        }

        return suggestions.OrderBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.FolderPath, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
