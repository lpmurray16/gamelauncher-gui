using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed partial class SettingsModel
{
    // Bound only by the appearance handler, so other Settings forms remain independent.
    private ThemeSettings? _savedTheme;
    public ThemeSettings SavedTheme => _savedTheme ??= _themes.Get();
    public ThemeSettings Theme => SavedTheme;
    public string? ThemeLoadWarning => _themes.LoadWarning;

    public IActionResult OnPostAppearance([Bind(Prefix = "Theme")] ThemeSettings appearance)
    {
        Tab = "appearance";
        foreach (var key in ModelState.Keys.Where(x => !x.Equals("Theme", StringComparison.OrdinalIgnoreCase) && !x.StartsWith("Theme.", StringComparison.OrdinalIgnoreCase)).ToList())
            ModelState.Remove(key);
        if (!ModelState.IsValid) { LoadDesktopPreferences(); return Page(); }
        try
        {
            _themes.Save(appearance);
            TempData["Notice"] = "Appearance saved.";
            return ReturnToSettings();
        }
        catch (Exception error) when (IsExpected(error))
        { ShowError(error); LoadDesktopPreferences(); return Page(); }
    }
}
