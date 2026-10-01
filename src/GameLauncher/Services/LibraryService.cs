using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using GameLauncher.Data;
using GameLauncher.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Services;

public sealed class LibraryService(IDbContextFactory<LibraryDbContext> factory, ScannerService scanner)
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task<List<LibraryEntry>> GetEntriesAsync(LibraryCategory category, string? search = null)
    {
        ValidateCategory(category);
        await using var db = await factory.CreateDbContextAsync();
        var entries = await db.Entries.AsNoTracking().Where(x => x.Category == category).ToListAsync();
        if (!string.IsNullOrWhiteSpace(search))
            entries = entries.Where(x => x.Title.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        return entries.OrderByDescending(x => x.IsFavorite).ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToList();
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
            entry.Title = title;
            entry.TargetPath = path;
            entry.TargetKey = key;
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

    public async Task LaunchAsync(Guid id)
    {
        var entry = await GetAsync(id) ?? throw new InvalidOperationException("This entry no longer exists.");
        var path = LaunchTarget.Normalize(entry.TargetPath);
        if (!Directory.Exists(entry.WorkingDirectory))
            throw new ArgumentException("The working directory is missing. Edit this entry to locate it.");
        var isExecutable = Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase);
        var target = Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase)
            ? LaunchTarget.ReadSteamUrl(path) : path;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = target,
                Arguments = isExecutable ? entry.Arguments : "",
                WorkingDirectory = entry.WorkingDirectory,
                UseShellExecute = true
            });
        }
        catch (Win32Exception ex)
        { throw new InvalidOperationException($"Windows could not launch this entry: {ex.Message}", ex); }
        await _writeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            await db.Entries.Where(x => x.Id == id)
                .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.LastLaunchedUtc, (DateTime?)DateTime.UtcNow));
        }
        catch (Exception ex) when (ex is DbUpdateException or Microsoft.Data.Sqlite.SqliteException)
        { throw new InvalidOperationException("The launch request was sent, but its history could not be saved.", ex); }
        finally { _writeGate.Release(); }
    }

    private static void ValidateCategory(LibraryCategory category)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentException("Choose a valid library category.");
    }
}
