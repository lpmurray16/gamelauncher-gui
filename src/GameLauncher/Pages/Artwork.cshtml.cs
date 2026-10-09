using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

// Keep existing artwork links compatible with the unified editor.
public sealed class ArtworkPageModel : UiPageModel
{
    public IActionResult OnGet(Guid id) => RedirectToPage("/Edit", new { id });
}
