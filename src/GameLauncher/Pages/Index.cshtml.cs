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
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "favorites";
    public string SortLabel => Sort switch
    {
        "az" => "A–Z",
        "za" => "Z–A",
        "recent" => "LAST PLAYED · NEWEST FIRST",
        _ => "FAVORITES FIRST · A–Z"
    };
    public List<LibraryEntry> Entries { get; private set; } = new();
    public LibraryEntry? Featured { get; private set; }
    public LibraryEntry? FallbackFeatured { get; private set; }
    public bool FeaturedIsFavorite { get; private set; }
    public int FavoriteCount { get; private set; }
    public int MatchingCount { get; private set; }
    public async Task OnGetAsync() => await LoadAsync();
    private async Task LoadAsync()
    {
        Category = SafeCategory(Category);
        if (Sort is not ("favorites" or "az" or "za" or "recent")) Sort = "favorites";
        try
        {
            var entries = await _library.GetEntriesAsync(Category, Search);
            FavoriteCount = entries.Count(e => e.IsFavorite);
            MatchingCount = entries.Count;
            var filtered = entries.Where(e => !Favorites || e.IsFavorite);
            var ordered = Sort switch
            {
                "az" => filtered.OrderBy(e => e.Title, StringComparer.OrdinalIgnoreCase),
                "za" => filtered.OrderByDescending(e => e.Title, StringComparer.OrdinalIgnoreCase),
                "recent" => filtered.OrderByDescending(e => e.LastLaunchedUtc)
                    .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase),
                _ => filtered.OrderByDescending(e => e.IsFavorite)
                    .ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase)
            };
            Entries = ordered.ThenBy(e => e.Id).ToList();
            Featured = Entries.Where(e => e.LastLaunchedUtc.HasValue).MaxBy(e => e.LastLaunchedUtc)
                ?? Entries.FirstOrDefault(e => e.IsFavorite)
                ?? Entries.FirstOrDefault(e => e.HeroImageFile is not null || e.CoverImageFile is not null)
                ?? Entries.FirstOrDefault();
            FeaturedIsFavorite = Featured?.IsFavorite == true;
            FallbackFeatured = Entries.FirstOrDefault(e => e.IsFavorite && e.Id != Featured?.Id)
                ?? Entries.FirstOrDefault(e => e.Id != Featured?.Id);
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); }
    }
    public async Task<IActionResult> OnPostLaunchAsync(Guid id)
    {
        try { await _library.LaunchAsync(id); TempData["Notice"] = "Launch request sent."; }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); await LoadAsync(); return Page(); }
        return RedirectToPage(new { category = SafeCategory(Category), search = Search, favorites = Favorites, sort = Sort });
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
        return RedirectToPage(new { category = SafeCategory(Category), search = Search, favorites = Favorites, sort = Sort });
    }
}
