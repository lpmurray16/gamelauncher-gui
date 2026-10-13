using System.Data.Common;
using GameLauncher.Data;
using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Pages;

public sealed partial class SettingsModel
{
    public LibraryHealthReport? HealthReport { get; private set; }

    // Razor Pages validates antiforgery tokens for all three POST handlers. No GET/startup scan.
    public Task<IActionResult> OnPostScanArtworkAsync([FromServices] IDbContextFactory<LibraryDbContext> factory) =>
        ScanHealthAsync(factory, LibraryHealthScan.Artwork);

    public Task<IActionResult> OnPostScanTrackingAsync([FromServices] IDbContextFactory<LibraryDbContext> factory) =>
        ScanHealthAsync(factory, LibraryHealthScan.Tracking);

    public Task<IActionResult> OnPostScanLaunchTargetsAsync([FromServices] IDbContextFactory<LibraryDbContext> factory) =>
        ScanHealthAsync(factory, LibraryHealthScan.LaunchTargets);

    private async Task<IActionResult> ScanHealthAsync(IDbContextFactory<LibraryDbContext> factory, LibraryHealthScan scan)
    {
        ModelState.Clear(); // These independent forms submit no artwork key or preferences.
        Tab = "health";
        LoadDesktopPreferences();
        try
        {
            var token = HttpContext.RequestAborted;
            await using var db = await factory.CreateDbContextAsync(token);
            var entries = await db.Entries.AsNoTracking().ToListAsync(token);
            HealthReport = new LibraryHealthService(_paths).Scan(entries, scan, token);
        }
        catch (Exception error) when (IsExpected(error) || error is DbException or System.Security.SecurityException)
        {
            ShowError(error);
        }
        return Page();
    }
}
