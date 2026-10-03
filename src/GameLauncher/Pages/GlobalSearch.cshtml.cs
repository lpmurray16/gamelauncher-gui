using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameLauncher.Domain;
using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed class GlobalSearchModel : UiPageModel
{
    private readonly LibraryService _library;
    private readonly CollectionService _collections;
    public GlobalSearchModel(LibraryService library, CollectionService collections) => (_library, _collections) = (library, collections);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    public List<LibraryEntry> Entries { get; private set; } = new();
    public bool HasQuery => !string.IsNullOrWhiteSpace(Q);
    public List<LibraryCollection> Collections { get; private set; } = new();
    public HashSet<(Guid CollectionId, Guid EntryId)> Memberships { get; private set; } = new();

    public async Task OnGetAsync() => await LoadAsync();

    // Collections load on every render path, including error re-renders after a POST.
    private async Task LoadAsync()
    {
        Q = Q?.Trim();
        try
        {
            Collections = await _collections.GetAllAsync();
            if (!HasQuery) return;
            Entries = await _library.SearchAllAsync(Q!);
            Memberships = await _collections.GetMembershipsAsync(Entries.Select(e => e.Id));
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); }
    }

    public async Task<IActionResult> OnPostLaunchAsync(Guid id)
    {
        try { TempData["Notice"] = await _library.LaunchAsync(id); }
        catch (Exception error) when (IsExpected(error))
        {
            ShowError(error);
            await LoadAsync();
            return Page();
        }
        return RedirectToPage(new { q = Q });
    }

    public async Task<IActionResult> OnPostFavoriteAsync(Guid id)
    {
        try
        {
            var entry = await _library.GetAsync(id);
            if (entry is null) return NotFound();
            await _library.SetFavoriteAsync(id, !entry.IsFavorite);
        }
        catch (Exception error) when (IsExpected(error))
        {
            ShowError(error);
            await LoadAsync();
            return Page();
        }
        return RedirectToPage(new { q = Q });
    }

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
        return RedirectToPage(new { q = Q });
    }
}
