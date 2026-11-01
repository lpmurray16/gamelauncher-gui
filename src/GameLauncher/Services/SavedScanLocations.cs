using System.IO;
using System.Text.Json;

namespace GameLauncher.Services;

public sealed record SavedScanLocation(Guid Id, string FolderPath, bool Recursive);

public sealed class SavedScanLocations
{
    private readonly object _gate = new();
    private readonly string _path;
    private List<SavedScanLocation> _locations = new();
    public string? LoadWarning { get; private set; }

    public SavedScanLocations(AppPaths paths)
    {
        _path = Path.Combine(paths.DataDirectory, "scan-locations.json");
        try
        {
            if (!File.Exists(_path)) return;
            if (new FileInfo(_path).Length > 262144) throw new ArgumentException("The saved locations file is too large.");
            var locations = JsonSerializer.Deserialize<List<SavedScanLocation>>(File.ReadAllText(_path))
                ?? throw new ArgumentException("The saved locations file is empty.");
            if (locations.Count > 100 || locations.Any(x => x is null || x.Id == Guid.Empty) ||
                locations.Select(x => x.Id).Distinct().Count() != locations.Count)
                throw new ArgumentException("Invalid saved locations.");
            _locations = locations.Select(x => x with { FolderPath = Normalize(x.FolderPath) }).ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            LoadWarning = "Saved locations could not be loaded. The original file has been preserved. " + error.Message;
        }
    }

    public IReadOnlyList<SavedScanLocation> GetAll()
    {
        lock (_gate) return _locations.OrderBy(x => x.FolderPath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public SavedScanLocation Get(Guid id)
    {
        lock (_gate) return _locations.FirstOrDefault(x => x.Id == id)
            ?? throw new ArgumentException("That saved location no longer exists.");
    }

    public void Save(string path, bool recursive)
    {
        path = Normalize(path);
        // Check access explicitly: an unavailable drive is not an empty folder.
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Choose a real local folder, not a file or folder link.");
        lock (_gate)
        {
            var next = _locations.ToList();
            var index = next.FindIndex(x => string.Equals(x.FolderPath, path, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) next[index] = next[index] with { Recursive = recursive };
            else
            {
                if (next.Count >= 100) throw new ArgumentException("You can save up to 100 locations.");
                next.Add(new(Guid.NewGuid(), path, recursive));
            }
            Persist(next);
        }
    }

    public void Remove(Guid id)
    {
        lock (_gate) Persist(_locations.Where(x => x.Id != id).ToList());
    }

    private static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
            throw new ArgumentException("Choose an absolute local folder path.");
        path = Path.GetFullPath(path.Trim());
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0)
            throw new ArgumentException("Choose a folder on a local drive.");
        return Path.TrimEndingDirectorySeparator(path);
    }

    private void Persist(List<SavedScanLocation> next)
    {
        if (LoadWarning is not null)
            throw new InvalidOperationException("Resolve the saved locations file error before changing locations; the original file will not be overwritten.");
        var temporary = _path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, _path, overwrite: true);
            _locations = next;
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}
