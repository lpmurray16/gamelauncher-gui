using GameLauncher.Services;

namespace GameLauncher.Pages;

public sealed class SettingsModel : UiPageModel
{
    private readonly AppPaths _paths;
    public SettingsModel(AppPaths paths) => _paths = paths;
    public string DataDirectory => _paths.DataDirectory;
    public string DatabasePath => _paths.DatabasePath;
    public void OnGet() { }
}
