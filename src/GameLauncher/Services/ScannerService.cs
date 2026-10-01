using System.Collections.Concurrent;
using System.IO;
using System.Text.RegularExpressions;

namespace GameLauncher.Services;

public sealed record ScanCandidate(Guid Id, string Title, string TargetPath, string WorkingDirectory);
public sealed record ScanResult(Guid Id, string Root, IReadOnlyList<ScanCandidate> Candidates,
    IReadOnlyList<string> Warnings, bool Truncated);

public sealed class ScannerService
{
    private const int MaxCandidates = 2000;
    private const int MaxVisited = 100000;
    private readonly ConcurrentDictionary<Guid, (DateTime Created, ScanResult Result)> _results = new();
    private readonly SemaphoreSlim _scanLock = new(1, 1);
    private static readonly string[] Noise =
        ["unins", "uninstall", "crashreport", "crashpad", "vcredist", "vc_redist", "dxsetup", "dxwebsetup"];

    public ScanResult? GetResult(Guid id)
    {
        Prune();
        return _results.TryGetValue(id, out var cached) ? cached.Result : null;
    }

    public async Task<ScanResult> ScanAsync(string root, bool recursive, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root.Trim()))
            throw new ArgumentException("Choose an absolute local folder path.");
        root = Path.GetFullPath(root.Trim());
        if (root.StartsWith(@"\\", StringComparison.Ordinal) || root.IndexOf(':', 2) >= 0 || !Directory.Exists(root))
            throw new ArgumentException("Choose an existing folder on a local drive.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Choose the real folder, rather than a folder link or junction.");
        if (!await _scanLock.WaitAsync(0, cancellationToken))
            throw new InvalidOperationException("A scan is already running. Please let it finish first.");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            ScanResult result;
            try { result = await Task.Run(() => Scan(root, recursive, timeout.Token), timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new InvalidOperationException("The scan took too long. Please choose a smaller folder."); }
            Prune();
            _results[result.Id] = (DateTime.UtcNow, result);
            return result;
        }
        finally { _scanLock.Release(); }
    }

    private void Prune()
    {
        foreach (var item in _results)
            if (DateTime.UtcNow - item.Value.Created > TimeSpan.FromMinutes(30))
                _results.TryRemove(item.Key, out _);
        foreach (var item in _results.OrderByDescending(x => x.Value.Created).Skip(8))
            _results.TryRemove(item.Key, out _);
    }

    private static ScanResult Scan(string root, bool recursive, CancellationToken cancellationToken)
    {
        var candidates = new List<ScanCandidate>();
        var warnings = new List<string>();
        var folders = new Stack<string>();
        folders.Push(root);
        int visited = 0;
        bool truncated = false;
        void Warn(string value) { if (warnings.Count < 30) warnings.Add(value); }

        while (folders.Count > 0 && !truncated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = folders.Pop();
            try
            {
                // Reparse points are never followed: avoids loops and escaping a selected tree.
                foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (++visited > MaxVisited) { truncated = true; break; }
                    try
                    {
                        var attributes = File.GetAttributes(path);
                        if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue;
                        if ((attributes & FileAttributes.Directory) != 0)
                        {
                            if (recursive) folders.Push(path);
                            continue;
                        }
                        if (!LaunchTarget.Supports(path)) continue;
                        var name = Path.GetFileNameWithoutExtension(path);
                        if (Noise.Any(x => name.StartsWith(x, StringComparison.OrdinalIgnoreCase))) continue;
                        var normalized = LaunchTarget.Normalize(path);
                        var title = Regex.Replace(name.Replace('_', ' ').Replace('-', ' '), @"\s+", " ").Trim();
                        if (title.Length == 0) title = "Untitled";
                        if (title.Length > 200) title = title[..200];
                        candidates.Add(new(Guid.NewGuid(), title, normalized, Path.GetDirectoryName(normalized)!));
                        if (candidates.Count >= MaxCandidates) { truncated = true; break; }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                    { Warn($"Skipped {Path.GetFileName(path)}: {ex.Message}"); }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { Warn($"Could not read {folder}: {ex.Message}"); }
        }
        if (truncated) Warn("Scan limit reached. Import these results, then scan a smaller subfolder for remaining files.");
        return new(Guid.NewGuid(), root, candidates.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ToList(), warnings, truncated);
    }
}
