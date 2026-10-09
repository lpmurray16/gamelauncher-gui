using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed class CompanionModel : UiPageModel
{
    public IActionResult OnGet() => RedirectToPage("/Settings", new { tab = "companion" });
}
