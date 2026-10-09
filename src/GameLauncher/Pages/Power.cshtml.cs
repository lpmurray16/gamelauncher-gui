using System;
using System.Security;
using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed class PowerModel(PcPowerService power) : UiPageModel
{
    [BindProperty] public bool AllowRemoteShutdown { get; set; }

    public IActionResult OnGet() => ReturnToSettings();
    private IActionResult ReturnToSettings() => RedirectToPage("/Settings", new { tab = "power" });
    private IActionResult PowerError(Exception error)
    {
        TempData["SettingsError"] = error.Message;
        return ReturnToSettings();
    }
    public IActionResult OnGetStatus() => new JsonResult(power.Status());

    public IActionResult OnPostPermission()
    {
        if (!ModelState.IsValid) return PowerError(new InvalidOperationException("Choose a valid remote shutdown preference."));
        try
        {
            power.SetRemotePermission(AllowRemoteShutdown);
            TempData["Notice"] = AllowRemoteShutdown
                ? "Launchpad Companion on paired phones may now request PC shutdown. Use only a trusted private network."
                : "Remote shutdown disabled. Any remote countdown was cancelled.";
            return ReturnToSettings();
        }
        catch (Exception error) when (IsExpected(error) || error is SecurityException)
        { return PowerError(error); }
    }

    public IActionResult OnPostShutdown()
    {
        try { power.Schedule(); return ReturnToSettings(); }
        catch (InvalidOperationException error) { return PowerError(error); }
    }

    public Task<IActionResult> OnPostRestartAsync() => RequestLocalAsync("restart");
    public Task<IActionResult> OnPostSleepAsync() => RequestLocalAsync("sleep");
    public Task<IActionResult> OnPostFirmwareAsync() => RequestLocalAsync("firmware");

    private async Task<IActionResult> RequestLocalAsync(string action)
    {
        try
        {
            TempData["Notice"] = await power.RequestLocalActionAsync(action);
            return ReturnToSettings();
        }
        catch (Exception error) when (IsExpected(error) || error is SecurityException)
        { return PowerError(error); }
    }

    public IActionResult OnPostCancel()
    {
        try { power.Cancel(); return ReturnToSettings(); }
        catch (InvalidOperationException error) { return PowerError(error); }
    }
}
