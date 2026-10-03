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
    private readonly CollectionService _collections;
    public IndexModel(LibraryService library, CollectionService collections) => (_library, _collections) = (library, collections);
    [BindProperty(SupportsGet = true)] public LibraryCategory Category { get; set; } = LibraryCategory.Games;
    [BindProperty(SupportsGet = true)] public Guid? CollectionId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public bool Favorites { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; } = "favorites";
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
    public List<LibraryCollection> Collections { get; private set; } = new();
    public LibraryCollection? CurrentCollection { get; private set; }
    // Counts reflect the current search, like the All entries and Favorites tabs.
    public Dictionary<Guid, int> CollectionCounts { get; private set; } = new();
    public HashSet<(Guid CollectionId, Guid EntryId)> Memberships { get; private set; } = new();
    public bool ShowsCollections => Category == LibraryCategory.Games;
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

            // Collections are named filter sets on the Games dashboard only.
            if (ShowsCollections)
            {
                Collections = await _collections.GetAllAsync();
                Memberships = await _collections.GetMembershipsAsync(entries.Select(e => e.Id));
                CollectionCounts = Memberships.GroupBy(m => m.CollectionId).ToDictionary(g => g.Key, g => g.Count());
                CurrentCollection = CollectionId.HasValue ? Collections.FirstOrDefault(c => c.Id == CollectionId.Value) : null;
            }
            // Stale ids and non-Games categories fall back to the unfiltered tab.
            if (CurrentCollection is null) CollectionId = null;
            // A set tab and the Favorites tab are mutually exclusive filters.
            if (CurrentCollection is not null) Favorites = false;

            var filtered = CurrentCollection is { } set
                ? entries.Where(e => Memberships.Contains((set.Id, e.Id)))
                : entries.Where(e => !Favorites || e.IsFavorite);
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
        try { TempData["Notice"] = await _library.LaunchAsync(id); }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); await LoadAsync(); return Page(); }
        return RedirectToPage(new { category = SafeCategory(Category), collectionId = CollectionId, search = Search, favorites = Favorites, sort = Sort });
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
        return RedirectToPage(new { category = SafeCategory(Category), collectionId = CollectionId, search = Search, favorites = Favorites, sort = Sort });
    }

    // targetCollectionId is deliberately not named "collectionId": that name binds the page's
    // CollectionId property, which must stay the filter tab to return to.
    public async Task<IActionResult> OnPostAddToCollectionAsync(Guid id, Guid targetCollectionId)
    {
        try
        {
            var entry = await _library.GetAsync(id);
            if (entry is null) return NotFound();
            var collection = await _collections.GetAsync(targetCollectionId);
            if (collection is null) return NotFound();

            var inCollection = await _collections.ContainsEntryAsync(targetCollectionId, id);
            if (inCollection)
            {
                await _collections.RemoveEntryAsync(targetCollectionId, id);
                TempData["Notice"] = $"Removed \"{entry.Title}\" from {collection.Name}.";
            }
            else
            {
                await _collections.AddEntryAsync(targetCollectionId, id);
                TempData["Notice"] = $"Added \"{entry.Title}\" to {collection.Name}.";
            }
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); await LoadAsync(); return Page(); }
        return RedirectToPage(new { category = SafeCategory(Category), collectionId = CollectionId, search = Search, favorites = Favorites, sort = Sort });
    }
}
