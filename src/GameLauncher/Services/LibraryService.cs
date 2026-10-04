using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using GameLauncher.Data;
using GameLauncher.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Services;

public sealed class LibraryService(IDbContextFactory<LibraryDbContext> factory, ScannerService scanner, GameStatusMonitor statusMonitor)
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    public const int MaxCompanions = 8;

    public async Task<List<LibraryEntry>> GetCompanionChoicesAsync(Guid? entryId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.Entries.AsNoTracking().Where(x => x.Id != entryId).ToListAsync();
        return entries.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).ToList();
    }

    public async Task<List<Guid>> GetCompanionIdsAsync(Guid entryId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.LaunchCompanions.Where(x => x.EntryId == entryId).Select(x => x.CompanionId).ToListAsync();
    }

    public async Task<List<LibraryEntry>> GetEntriesAsync(LibraryCategory category, string? search = null)
    {
        ValidateCategory(category);
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.Entries.AsNoTracking().Where(x => x.Category == category).ToListAsync();
        if (!string.IsNullOrWhiteSpace(search))
            entries = entries.Where(x => x.Title.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return entries.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<List<LibraryEntry>> SearchAllAsync(string term)
    {
        term = term.Trim();
        if (term.Length == 0) return new();
        if (term.Length > 200) throw new ArgumentException("Search must be 200 characters or fewer.");
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.Entries.AsNoTracking()
            .Where(x => x.Category == LibraryCategory.Games || x.Category == LibraryCategory.Tools || x.Category == LibraryCategory.Emulators)
            .ToListAsync();
        // Match the category search's case-insensitive Unicode title comparison.
        return entries.Where(x => x.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Category).ThenBy(x => x.Id).ToList();
    }

    public async Task<LibraryEntry?> GetAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Entries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id);
    }

    public async Task<Guid> SaveAsync(EntryInput input)
    {
        ValidateCategory(input.Category);
        var title = input.Title?.Trim() ?? "";
        if (title.Length is 0 or > 200) throw new ArgumentException("Enter a title between 1 and 200 characters.");
        var path = LaunchTarget.Normalize(input.TargetPath);
        var trackingPath = ValidateTrackingPath(input.TrackingExecutablePath);
        var arguments = input.Arguments ?? "";
        if (arguments.Length > 8192 || arguments.Contains('\0')) throw new ArgumentException("Launch arguments are invalid or too long.");
        if (arguments.Length > 0 && !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Custom arguments are supported for .exe targets only. Shortcuts keep their own arguments.");
        var directory = string.IsNullOrWhiteSpace(input.WorkingDirectory) ? Path.GetDirectoryName(path)! : input.WorkingDirectory.Trim();
        if (!Path.IsPathFullyQualified(directory) || !Directory.Exists(directory))
            throw new ArgumentException("The working directory must be an existing absolute folder path.");
        directory = Path.GetFullPath(directory);
        if (directory.StartsWith(@"\\", StringComparison.Ordinal) || directory.IndexOf(':', 2) >= 0)
            throw new ArgumentException("The working directory must be on a local drive.");
        var key = path.ToUpperInvariant();
        await _writeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            if (await db.Entries.AnyAsync(x => x.TargetKey == key && x.Id != input.Id))
                throw new ArgumentException("This launch file is already in the library. Edit that entry instead.");
            var entry = input.Id.HasValue
                ? await db.Entries.SingleOrDefaultAsync(x => x.Id == input.Id.Value)
                    ?? throw new InvalidOperationException("This entry no longer exists.")
                : new LibraryEntry();
            var companionIds = input.CompanionIds.Distinct().ToList();
            if (companionIds.Count > MaxCompanions)
                throw new ArgumentException($"Choose no more than {MaxCompanions} companion entries.");
            if (companionIds.Contains(entry.Id))
                throw new ArgumentException("An entry cannot launch itself as a companion.");
            if (await db.Entries.CountAsync(x => companionIds.Contains(x.Id)) != companionIds.Count)
                throw new ArgumentException("A selected companion no longer exists. Review the launch group and save again.");
            var existingCompanions = await db.LaunchCompanions.Where(x => x.EntryId == entry.Id).ToListAsync();
            db.LaunchCompanions.RemoveRange(existingCompanions.Where(x => !companionIds.Contains(x.CompanionId)));
            var existingIds = existingCompanions.Select(x => x.CompanionId).ToHashSet();
            foreach (var companionId in companionIds.Where(x => !existingIds.Contains(x)))
                db.LaunchCompanions.Add(new LaunchCompanion { EntryId = entry.Id, CompanionId = companionId });
            entry.Title = title;
            entry.TargetPath = path;
            entry.TargetKey = key;
            entry.TrackingExecutablePath = trackingPath;
            entry.Arguments = arguments;
            entry.WorkingDirectory = directory;
            entry.Category = input.Category;
            entry.IsFavorite = input.IsFavorite;
            if (!input.Id.HasValue) db.Entries.Add(entry);
            await db.SaveChangesAsync();
            return entry.Id;
        }
        finally { _writeGate.Release(); }
    }

    public async Task SetFavoriteAsync(Guid id, bool isFavorite)
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var changed = await db.Entries.Where(x => x.Id == id)
                .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.IsFavorite, isFavorite));
            if (changed == 0) throw new InvalidOperationException("This entry no longer exists.");
        }
        finally { _writeGate.Release(); }
    }

    public async Task DeleteAsync(Guid id)
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            await db.Entries.Where(x => x.Id == id).ExecuteDeleteAsync();
            // Deliberately no filesystem deletion.
        }
        finally { _writeGate.Release(); }
    }

    public async Task<int> ImportAsync(Guid scanId, IEnumerable<Guid> candidateIds, LibraryCategory category)
    {
        ValidateCategory(category);
        var result = scanner.GetResult(scanId)
            ?? throw new InvalidOperationException("The scan expired. Scan the folder again to refresh the list.");
        var selected = candidateIds.ToHashSet();
        if (selected.Count == 0) throw new ArgumentException("Select at least one candidate to import.");
        if (selected.Except(result.Candidates.Select(x => x.Id)).Any())
            throw new ArgumentException("The selection does not belong to this scan.");
        await _writeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var existing = (await db.Entries.Select(x => x.TargetKey).ToListAsync()).ToHashSet(StringComparer.Ordinal);
            int added = 0;
            foreach (var candidate in result.Candidates.Where(x => selected.Contains(x.Id)))
            {
                var path = LaunchTarget.Normalize(candidate.TargetPath);
                var key = path.ToUpperInvariant();
                if (!existing.Add(key)) continue;
                db.Entries.Add(new LibraryEntry
                {
                    Title = candidate.Title, TargetPath = path, TargetKey = key,
                    WorkingDirectory = candidate.WorkingDirectory, Category = category
                });
                added++;
            }
            await db.SaveChangesAsync();
            return added;
        }
        finally { _writeGate.Release(); }
    }

    public async Task<string> LaunchAsync(Guid id)
    {
        if (!await _launchGate.WaitAsync(0))
            throw new InvalidOperationException("Another launch request is still being sent. Please wait a moment.");
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var entry = await db.Entries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id)
                ?? throw new InvalidOperationException("This entry no longer exists.");
            var companionIds = await db.LaunchCompanions.Where(x => x.EntryId == id)
                .Select(x => x.CompanionId).ToListAsync();
            if (companionIds.Count > MaxCompanions || companionIds.Contains(id))
                throw new InvalidOperationException("This launch group is invalid. Edit the entry and save its companions again.");
            var companions = await db.Entries.AsNoTracking().Where(x => companionIds.Contains(x.Id)).ToListAsync();
            if (companions.Count != companionIds.Count)
                throw new InvalidOperationException("A companion no longer exists. Review this entry's launch group.");

            // Direct companions only; never recurse into their bundles, even if links form a cycle.
            var entries = companions.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Id).Append(entry).ToList();
            var plan = new List<(LibraryEntry Entry, ProcessStartInfo StartInfo)>();
            foreach (var item in entries)
            {
                try { plan.Add((item, PrepareLaunch(item))); }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
                { throw new InvalidOperationException($"Cannot launch {item.Title}: {ex.Message} No launch requests were sent.", ex); }
            }

            var started = new List<LibraryEntry>();
            var skipped = new List<string>();
            string? failure = null;
            foreach (var item in plan)
            {
                if (item.Entry.Id != id && IsExecutableRunning(item.Entry.TargetPath))
                {
                    skipped.Add(item.Entry.Title);
                    continue;
                }
                try
                {
                    using var process = Process.Start(item.StartInfo);
                    started.Add(item.Entry);
                    if (GameStatusMonitor.CanTrack(item.Entry)) statusMonitor.MarkStarting(item.Entry.Id);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    failure = $"Windows could not launch {item.Entry.Title}: {ex.Message} Remaining launch requests were not sent.";
                    break;
                }
            }

            var summary = started.Count == 0 ? "No new launch requests were sent."
                : $"Launch requests sent: {string.Join(", ", started.Select(x => x.Title))}.";
            if (skipped.Count > 0) summary += $" Already running: {string.Join(", ", skipped)}.";
            if (started.Count > 0)
            {
                await _writeGate.WaitAsync();
                try
                {
                    var startedIds = started.Select(x => x.Id).ToList();
                    await db.Entries.Where(x => startedIds.Contains(x.Id))
                        .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.LastLaunchedUtc, (DateTime?)DateTime.UtcNow));
                }
                catch (Exception ex) when (ex is DbUpdateException or Microsoft.Data.Sqlite.SqliteException)
                { throw new InvalidOperationException($"{failure} {summary} Launch history could not be saved: {ex.Message}", ex); }
                finally { _writeGate.Release(); }
            }
            if (failure is not null)
                throw new InvalidOperationException($"{failure} {summary} Programs already started have not been closed.");
            return summary;
        }
        finally { _launchGate.Release(); }
    }

    private static ProcessStartInfo PrepareLaunch(LibraryEntry entry)
    {
        var path = LaunchTarget.Normalize(entry.TargetPath);
        if (!Directory.Exists(entry.WorkingDirectory))
            throw new ArgumentException("The working directory is missing. Edit this entry to locate it.");
        var isExecutable = Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        return new ProcessStartInfo
        {
            FileName = Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase)
                ? LaunchTarget.ReadSteamUrl(path) : path,
            Arguments = isExecutable ? entry.Arguments : "",
            WorkingDirectory = entry.WorkingDirectory,
            UseShellExecute = true
        };
    }

    private static bool IsExecutableRunning(string path)
    {
        // Shortcuts may start a different process: never guess from their filenames.
        if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return false;
        Process[] processes;
        try { processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(path)); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        { return false; }
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited && string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    // Best effort: Windows may deny inspection of elevated processes.
                }
            }
            return false;
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static string? ValidateTrackingPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var path = value.Trim();
        if (path.Length > 32767 || !Path.IsPathFullyQualified(path))
            throw new ArgumentException("The status tracking path must be an absolute .exe path on a local drive.");
        path = Path.GetFullPath(path);
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0
            || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("The status tracking path must be an absolute .exe path on a local drive.");
        if (!File.Exists(path)) throw new ArgumentException("The status tracking executable does not exist.");
        return path;
    }

    private static void ValidateCategory(LibraryCategory category)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentException("Choose a valid library category.");
    }
}
