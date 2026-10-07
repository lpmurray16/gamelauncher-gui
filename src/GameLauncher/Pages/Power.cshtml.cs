using System;
using System.Security;
using GameLauncher.Contracts;
using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed class PowerModel(PcPowerService power) : UiPageModel
{
    [BindProperty] public bool AllowRemoteShutdown { get; set; }
    public string ComputerName => Environment.MachineName;
    public PowerStatusDto Status { get; private set; } = null!;

    public void OnGet() => Load();
    public IActionResult OnGetStatus() => new JsonResult(power.Status());

    private void Load()
    {
        AllowRemoteShutdown = power.AllowRemoteShutdown;
        Status = power.Status();
    }

    public IActionResult OnPostPermission()
    {
        if (!ModelState.IsValid) { Load(); return Page(); }
        try
        {
            power.SetRemotePermission(AllowRemoteShutdown);
            TempData["Notice"] = AllowRemoteShutdown
                ? "Launchpad Companion on paired phones may now request PC shutdown. Use only a trusted private network."
                : "Remote shutdown disabled. Any remote countdown was cancelled.";
            return RedirectToPage();
        }
        catch (Exception error) when (IsExpected(error) || error is SecurityException)
        { ShowError(error); Load(); return Page(); }
    }

    public IActionResult OnPostShutdown()
    {
        try { power.Schedule(); return RedirectToPage(); }
        catch (InvalidOperationException error) { ShowError(error); Load(); return Page(); }
    }

    public IActionResult OnPostCancel()
    {
        try { power.Cancel(); return RedirectToPage(); }
        catch (InvalidOperationException error) { ShowError(error); Load(); return Page(); }
    }
}
