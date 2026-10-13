using System.IO;
using System.Security;
using GameLauncher.Domain;

namespace GameLauncher.Services;

public enum LibraryHealthScan { Artwork, Tracking, LaunchTargets }
public sealed record LibraryHealthIssue(string Message, bool IsUncertain = false);
public sealed record LibraryHealthEntry(Guid Id, string Title, LibraryCategory Category, IReadOnlyList<LibraryHealthIssue> Issues);
public sealed record LibraryHealthReport(LibraryHealthScan Scan, int CheckedEntries, IReadOnlyList<LibraryHealthEntry> Entries);

// Read-only, request-scoped inspection of saved entries. Never registered as a hosted service.
public sealed class LibraryHealthService(AppPaths paths)
{
    public LibraryHealthReport Scan(IEnumerable<LibraryEntry> entries, LibraryHealthScan scan, CancellationToken cancellationToken)
    {
        var results = new List<LibraryHealthEntry>();
        var checkedEntries = 0;
        foreach (var entry in entries.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (scan == LibraryHealthScan.Tracking && entry.Category != LibraryCategory.Games) continue;
            checkedEntries++;
            var issues = new List<LibraryHealthIssue>();
            switch (scan)
            {
                case LibraryHealthScan.Artwork:
                    CheckArtwork(entry.CoverImageFile, "Cover", issues, cancellationToken);
                    CheckArtwork(entry.HeroImageFile, "Background", issues, cancellationToken);
                    break;
                case LibraryHealthScan.Tracking:
                    CheckTracking(entry, issues, cancellationToken);
                    break;
                case LibraryHealthScan.LaunchTargets:
                    CheckLaunchTarget(entry.TargetPath, issues, cancellationToken);
                    break;
            }
            if (issues.Count > 0) results.Add(new(entry.Id, entry.Title, entry.Category, issues));
        }
        return new(scan, checkedEntries, results);
    }

    private void CheckArtwork(string? fileName, string label, List<LibraryHealthIssue> issues, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            issues.Add(new($"{label}: no image assigned."));
            return;
        }
        // Only inspect a single managed filename, never an arbitrary path from the database.
        if (fileName != Path.GetFileName(fileName) || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || !new[] { ".png", ".jpg", ".webp" }.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new($"{label}: invalid managed artwork reference. Choose an image again."));
            return;
        }
        AddFileIssue(Path.Combine(paths.ArtworkDirectory, fileName), $"{label} managed file", issues, token);
    }

    private static void CheckTracking(LibraryEntry entry, List<LibraryHealthIssue> issues, CancellationToken token)
    {
        var hasOverride = !string.IsNullOrWhiteSpace(entry.TrackingExecutablePath);
        // Reuse the monitor's path rules; a blank optional override falls back to the launch .exe.
        var effective = GameStatusMonitor.TrackingPath(new LibraryEntry
        {
            TargetPath = entry.TargetPath,
            TrackingExecutablePath = hasOverride ? entry.TrackingExecutablePath : null
        });
        if (effective is null)
        {
            issues.Add(new(hasOverride
                ? "Tracking executable: invalid path. Choose a full local .exe path supported by status tracking."
                : "Tracking executable: not configured and the launch file cannot be tracked directly. Choose the actual game's local .exe."));
            return;
        }
        AddFileIssue(effective, hasOverride ? "Tracking executable" : "Tracking executable (launch file)", issues, token);
    }

    private static void CheckLaunchTarget(string path, List<LibraryHealthIssue> issues, CancellationToken token)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)
                || path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0
                || !LaunchTarget.Supports(path))
            {
                issues.Add(new("Launch target: invalid path. Choose a full local .exe, .lnk, or supported .url file path."));
                return;
            }
            AddFileIssue(Path.GetFullPath(path), "Launch target", issues, token);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { issues.Add(new("Launch target: invalid file path.")); }
    }

    private static void AddFileIssue(string path, string label, List<LibraryHealthIssue> issues, CancellationToken token)
    {
        var state = InspectFile(path, token);
        var issue = state switch
        {
            FileState.Missing => new LibraryHealthIssue($"{label}: confirmed missing from an accessible location."),
            FileState.NotAFile => new LibraryHealthIssue($"{label}: points to a folder, not a file."),
            FileState.Invalid => new LibraryHealthIssue($"{label}: invalid file path."),
            FileState.Unavailable => new LibraryHealthIssue($"{label}: could not verify — location is inaccessible, offline, or changed during the scan. Reconnect/check access and scan again; not confirmed missing.", true),
            _ => null
        };
        if (issue is not null) issues.Add(issue);
    }

    private enum FileState { Present, Missing, NotAFile, Invalid, Unavailable }

    private static FileState InspectFile(string path, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.Offline) != 0) return FileState.Unavailable;
            if ((attributes & FileAttributes.Directory) != 0) return FileState.NotAFile;
            // A reparse point's existence alone does not establish that its destination is available.
            if ((attributes & FileAttributes.ReparsePoint) != 0) return FileState.Unavailable;
            return FileState.Present;
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        { return ConfirmMissing(path, token); }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        { return FileState.Invalid; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException)
        { return FileState.Unavailable; }
    }

    private static FileState ConfirmMissing(string path, CancellationToken token)
    {
        // File.Exists conflates missing with denied/offline. Walk from an accessible root and
        // confirm an absent component by enumeration; never follow an unavailable mount/junction.
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root) || !new DriveInfo(root).IsReady) return FileState.Unavailable;
            var parent = root;
            var options = new EnumerationOptions { IgnoreInaccessible = false, AttributesToSkip = 0, RecurseSubdirectories = false };
            foreach (var part in full[root.Length..].Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries))
            {
                token.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(parent);
                if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0
                    || (attributes & FileAttributes.Directory) == 0) return FileState.Unavailable;
                var found = false;
                foreach (var child in Directory.EnumerateFileSystemEntries(parent, "*", options))
                {
                    token.ThrowIfCancellationRequested();
                    if (string.Equals(Path.GetFileName(child), part, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                }
                if (!found) return FileState.Missing;
                parent = Path.Combine(parent, part);
            }
            // It reappeared, or a reparse destination could not be read: don't declare it missing.
            return FileState.Unavailable;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or SecurityException
            or ArgumentException or NotSupportedException)
        { return FileState.Unavailable; }
    }
}
