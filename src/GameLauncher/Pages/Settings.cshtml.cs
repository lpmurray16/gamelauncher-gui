using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed partial class SettingsModel : UiPageModel
{
    private readonly AppPaths _paths;
    private readonly CredentialStore _credentials;
    private readonly SteamGridDbClient _provider;
    private readonly DesktopPreferences _desktop;
    private readonly BrowserLauncher _browser;
    private readonly FolderPicker _picker;
    private readonly PcPowerService _power;
    private readonly GameLauncher.Companion.CompanionAccess _companion;
    public SettingsModel(AppPaths paths, CredentialStore credentials, SteamGridDbClient provider, DesktopPreferences desktop, BrowserLauncher browser, FolderPicker picker, PcPowerService power, GameLauncher.Companion.CompanionAccess companion)
    { _paths = paths; _credentials = credentials; _provider = provider; _desktop = desktop; _browser = browser; _picker = picker; _power = power; _companion = companion; }

    public string? BrowserExecutable { get; private set; }

    [BindProperty] public string ApiKey { get; set; } = "";
    public string DataDirectory => _paths.DataDirectory;
    public string DatabasePath => _paths.DatabasePath;
    public string ArtworkDirectory => _paths.ArtworkDirectory;
    public bool HasSgdbKey => _credentials.HasKey(CredentialStore.SteamGridDb);

    [BindProperty] public bool StartWithWindows { get; set; }
    [BindProperty] public bool LaunchFullscreen { get; set; }
    [BindProperty] public bool ShowFirmwareRestart { get; set; }
    public bool AllowRemoteShutdown => _power.AllowRemoteShutdown;
    public string ComputerName => Environment.MachineName;
    public GameLauncher.Contracts.PowerStatusDto PowerStatus => _power.Status();

    [BindProperty(SupportsGet = true)] public string? Tab { get; set; }
    public string ActiveTab => Tab switch
    {
        "startup" or "power" or "browser" or "companion" or "artwork" or "storage" or "about" => Tab,
        _ => Request.Query["handler"].ToString() switch
        {
            "PowerMenu" => "power",
            "ChooseBrowser" or "ClearBrowser" or "LaunchBrowser" => "browser",
            "Save" or "Remove" or "Check" => "artwork",
            "OpenLocation" => "storage",
            _ => "startup"
        }
    };

    private IActionResult ReturnToSettings() => RedirectToPage(new { tab = ActiveTab });

    public void OnGet()
    {
        if (TempData["SettingsError"] is string error) ModelState.AddModelError(string.Empty, error);
        LoadDesktopPreferences();
    }

    private void LoadDesktopPreferences()
    {
        LoadCompanionPreferences();
        ShowFirmwareRestart = _power.ShowFirmwareRestart;
        try { BrowserExecutable = _browser.ExecutablePath; }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); }
        try { StartWithWindows = _desktop.StartupRegistered; LaunchFullscreen = _desktop.LaunchFullscreen; }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); }
    }

    public async Task<IActionResult> OnPostChooseBrowserAsync(string? returnUrl)
    {
        ModelState.Clear(); // Browser forms do not submit artwork or desktop preferences.
        try
        {
            var path = await _picker.PickBrowserAsync();
            if (path is not null)
            {
                _browser.SetExecutable(path);
                TempData["Notice"] = "Browser saved. Use the globe button in the header to launch it.";
            }
            return ReturnToLauncher(returnUrl);
        }
        catch (Exception error) when (IsExpected(error))
        { ShowError(error); LoadDesktopPreferences(); return Page(); }
    }

    public IActionResult OnPostClearBrowser()
    {
        ModelState.Clear();
        try
        {
            _browser.SetExecutable(null);
            TempData["Notice"] = "Browser selection cleared. No files were removed.";
            return ReturnToSettings();
        }
        catch (Exception error) when (IsExpected(error))
        { ShowError(error); LoadDesktopPreferences(); return Page(); }
    }

    public IActionResult OnPostLaunchBrowser(string? returnUrl)
    {
        ModelState.Clear();
        try
        {
            _browser.Launch();
            return ReturnToLauncher(returnUrl);
        }
        catch (Exception error) when (IsExpected(error))
        { ShowError(error); LoadDesktopPreferences(); return Page(); }
    }

    private IActionResult ReturnToLauncher(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? LocalRedirect(returnUrl) : ReturnToSettings();

    public IActionResult OnPostPowerMenu()
    {
        ModelState.Remove(nameof(ApiKey));
        if (!ModelState.IsValid) { LoadDesktopPreferences(); return Page(); }
        try
        {
            _power.SetShowFirmwareRestart(ShowFirmwareRestart);
            TempData["Notice"] = "Power menu preference saved.";
            return ReturnToSettings();
        }
        catch (Exception error) when (IsExpected(error) || error is System.Security.SecurityException)
        { ShowError(error); LoadDesktopPreferences(); return Page(); }
    }

    public IActionResult OnPostOpenExplorer(string? returnUrl)
    {
        ModelState.Clear();
        try
        {
            _paths.OpenFileExplorer();
            return ReturnToLauncher(returnUrl);
        }
        catch (Exception error) when (IsExpected(error))
        { ShowError(error); LoadDesktopPreferences(); return Page(); }
    }

    public IActionResult OnPostOpenLocation(string location)
    {
        // This action has its own antiforgery-protected form, unrelated to API-key validation.
        ModelState.Clear();
        try
        {
            _paths.OpenInExplorer(location);
            return ReturnToSettings();
        }
        catch (Exception error) when (IsExpected(error))
        { ShowError(error); LoadDesktopPreferences(); return Page(); }
    }

    public IActionResult OnPostStartup()
    {
        ModelState.Remove(nameof(ApiKey)); // Artwork credentials belong to a separate form.
        if (!ModelState.IsValid) { LoadDesktopPreferences(); return Page(); }
        try
        {
            _desktop.SetStartup(StartWithWindows);
            TempData["Notice"] = StartWithWindows
                ? "Startup registered for your Windows account. Windows Startup apps can still disable it."
                : "Removed from Windows startup.";
            return ReturnToSettings();
        }
        catch (Exception error) when (IsExpected(error))
        { ShowError(error); LoadDesktopPreferences(); return Page(); }
    }

    public IActionResult OnPostDisplay()
    {
        ModelState.Remove(nameof(ApiKey)); // Artwork credentials belong to a separate form.
        if (!ModelState.IsValid) { LoadDesktopPreferences(); return Page(); }
        try
        {
            _desktop.SetLaunchFullscreen(LaunchFullscreen);
            TempData["Notice"] = "Launch display preference saved. Applies next time the app opens; use F11 to switch now.";
            return ReturnToSettings();
        }
        catch (Exception error) when (IsExpected(error))
        { ShowError(error); LoadDesktopPreferences(); return Page(); }
    }

    public IActionResult OnPostSave()
    {
        try
        {
            _credentials.Save(CredentialStore.SteamGridDb, ApiKey);
            TempData["Notice"] = "API key saved.";

        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); LoadDesktopPreferences(); return Page(); }
        return ReturnToSettings();
    }

    public IActionResult OnPostRemove()
    {
        _credentials.Delete(CredentialStore.SteamGridDb);
        TempData["Notice"] = "API key removed.";
        return ReturnToSettings();
    }

    public async Task<IActionResult> OnPostCheckAsync()
    {
        var result = await _provider.CheckKeyAsync(HttpContext.RequestAborted);
        TempData["Notice"] = result == "ok" ? "SteamGridDB connection looks good." : result;
        return ReturnToSettings();
    }
}
