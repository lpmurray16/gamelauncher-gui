using System.Diagnostics;
using System.IO;

namespace GameLauncher.Services;

public sealed class AppPaths
{
    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncher");
    public string DatabasePath => Path.Combine(DataDirectory, "library.db");
    public string ArtworkDirectory => Path.Combine(DataDirectory, "Artwork");
    public string WebViewDirectory => Path.Combine(DataDirectory, "WebView2");
    public string LogPath => Path.Combine(DataDirectory, "startup.log");
    public AppPaths() => Directory.CreateDirectory(DataDirectory);

    public void OpenInExplorer(string location)
    {
        // Accept only known storage locations, never a caller-supplied path or command.
        var path = location switch
        {
            "data" => DataDirectory,
            "database" => DatabasePath,
            "artwork" => ArtworkDirectory,
            _ => throw new ArgumentException("Choose a valid local storage location.")
        };
        bool selectFile = location == "database";
        if (selectFile)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("The library database could not be found.");
        }
        else
        {
            // Artwork may not have been used yet; create only our own storage folder.
            Directory.CreateDirectory(path);
        }
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            Arguments = selectFile ? $"/select,\"{path}\"" : $"\"{path}\"",
            UseShellExecute = true
        });
    }
}
