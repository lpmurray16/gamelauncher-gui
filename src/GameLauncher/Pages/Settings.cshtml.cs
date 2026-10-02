using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed class SettingsModel : UiPageModel
{
    private readonly AppPaths _paths;
    private readonly CredentialStore _credentials;
    private readonly SteamGridDbClient _provider;
    public SettingsModel(AppPaths paths, CredentialStore credentials, SteamGridDbClient provider)
    { _paths = paths; _credentials = credentials; _provider = provider; }

    [BindProperty] public string ApiKey { get; set; } = "";
    public string DataDirectory => _paths.DataDirectory;
    public string DatabasePath => _paths.DatabasePath;
    public string ArtworkDirectory => _paths.ArtworkDirectory;
    public bool HasSgdbKey => _credentials.HasKey(CredentialStore.SteamGridDb);

    public void OnGet() { }

    public IActionResult OnPostSave()
    {
        try
        {
            _credentials.Save(CredentialStore.SteamGridDb, ApiKey);
            TempData["Notice"] = "API key saved.";

        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); return Page(); }
        return RedirectToPage();
    }

    public IActionResult OnPostRemove()
    {
        _credentials.Delete(CredentialStore.SteamGridDb);
        TempData["Notice"] = "API key removed.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCheckAsync()
    {
        var result = await _provider.CheckKeyAsync(HttpContext.RequestAborted);
        TempData["Notice"] = result == "ok" ? "SteamGridDB connection looks good." : result;
        return RedirectToPage();
    }
}
