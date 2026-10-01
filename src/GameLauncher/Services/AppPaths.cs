using System.IO;

namespace GameLauncher.Services;

public sealed class AppPaths
{
    public string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncher");
    public string DatabasePath => Path.Combine(DataDirectory, "library.db");
    public string WebViewDirectory => Path.Combine(DataDirectory, "WebView2");
    public string LogPath => Path.Combine(DataDirectory, "startup.log");
    public AppPaths() => Directory.CreateDirectory(DataDirectory);
}
