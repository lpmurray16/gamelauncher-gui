using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameLauncher.Domain;
using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed class IndexModel : UiPageModel
{
    private readonly LibraryService _library;
    public IndexModel(LibraryService library) => _library = library;
    [BindProperty(SupportsGet = true)] public LibraryCategory Category { get; set; } = LibraryCategory.Games;
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public bool Favorites { get; set; }
    public List<LibraryEntry> Entries { get; private set; } = new();
    public int FavoriteCount { get; private set; }
    public int MatchingCount { get; private set; }
    public async Task OnGetAsync() => await LoadAsync();
    private async Task LoadAsync()
    {
        Category = SafeCategory(Category);
        try
        {
            var entries = await _library.GetEntriesAsync(Category, Search);
            FavoriteCount = entries.Count(e => e.IsFavorite);
            MatchingCount = entries.Count;
            Entries = entries.Where(e => !Favorites || e.IsFavorite).OrderByDescending(e => e.IsFavorite).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); }
    }
    public async Task<IActionResult> OnPostLaunchAsync(Guid id)
    {
        try { await _library.LaunchAsync(id); TempData["Notice"] = "Launch request sent."; }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); await LoadAsync(); return Page(); }
        return RedirectToPage(new { category = SafeCategory(Category), search = Search, favorites = Favorites });
    }
    public async Task<IActionResult> OnPostFavoriteAsync(Guid id)
    {
        try
        {
            var entry = await _library.GetAsync(id);
            if (entry is null) return NotFound();
            await _library.SetFavoriteAsync(entry.Id, !entry.IsFavorite);
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); await LoadAsync(); return Page(); }
        return RedirectToPage(new { category = SafeCategory(Category), search = Search, favorites = Favorites });
    }
}
