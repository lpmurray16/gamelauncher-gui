using System.IO;

namespace GameLauncher.Services;

public static class LaunchTarget
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
        { ".exe", ".lnk", ".url" };

    public static bool Supports(string path) => Extensions.Contains(Path.GetExtension(path));

    public static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path.Trim()))
            throw new ArgumentException("Choose an absolute file path, including its drive letter.");
        var result = Path.GetFullPath(path.Trim());
        if (result.StartsWith(@"\\", StringComparison.Ordinal) || result.IndexOf(':', 2) >= 0)
            throw new ArgumentException("Use a file on a local drive, not a network or device path.");
        if (!Supports(result))
            throw new ArgumentException("Only .exe, .lnk, and supported .url files can be added in this version.");
        if (!File.Exists(result))
            throw new ArgumentException("The launch file could not be found. Check its location.");
        if (Path.GetExtension(result).Equals(".url", StringComparison.OrdinalIgnoreCase))
            _ = ReadSteamUrl(result);
        return result;
    }

    // Do not hand arbitrary internet shortcuts to the shell. Only this precise
    // protocol shape is approved for the first version, and it is re-read at launch.
    public static string ReadSteamUrl(string path)
    {
        if (new FileInfo(path).Length > 65536)
            throw new ArgumentException("This internet shortcut is unexpectedly large.");
        bool inSection = false;
        string? target = null;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith('['))
            {
                inSection = line.Equals("[InternetShortcut]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (inSection && line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
            {
                if (target is not null) throw new ArgumentException("The shortcut has multiple URL targets.");
                target = line[4..].Trim();
            }
        }
        const string prefix = "steam://rungameid/";
        if (target is null || !target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only steam://rungameid/<numeric ID> internet shortcuts are supported yet.");
        var id = target[prefix.Length..];
        if (id.Length == 0 || id.Length > 20 || id.Any(c => c < '0' || c > '9'))
            throw new ArgumentException("The Steam shortcut contains an unsupported launch URL.");
        return prefix + id;
    }
}
