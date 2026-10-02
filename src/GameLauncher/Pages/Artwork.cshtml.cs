using System.ComponentModel;
using System.Net.Http;
using GameLauncher.Data;
using GameLauncher.Domain;
using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Pages;

public sealed class ArtworkPageModel : UiPageModel
{
    private readonly LibraryService _library;
    private readonly Services.ArtworkService _artwork;
    private readonly Services.SteamGridDbClient _provider;
    private readonly Services.FolderPicker _picker;
    private readonly IDbContextFactory<LibraryDbContext> _factory;

    public ArtworkPageModel(LibraryService library, Services.ArtworkService artwork,
        Services.SteamGridDbClient provider, Services.FolderPicker picker,
        IDbContextFactory<LibraryDbContext> factory)
    { _library = library; _artwork = artwork; _provider = provider; _picker = picker; _factory = factory; }

    public LibraryEntry? Entry { get; private set; }
    [BindProperty] public Guid Id { get; set; }
    public string ProviderSearch { get; private set; } = "";
    public List<ProviderMatch> Matches { get; private set; } = new();
    public List<ArtworkOption> CoverOptions { get; private set; } = new();
    public List<ArtworkOption> HeroOptions { get; private set; } = new();
    public int SelectedGameId { get; private set; }
    public string? SelectedGameTitle { get; private set; }
    public bool HasSearched { get; private set; }
    public bool HasSelectedGame => SelectedGameId > 0;

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        await LoadAsync(id);
        if (Entry is null) return NotFound();
        
        // Auto-search on first load using the entry title (only if we haven't searched yet)
        if (!HasSearched && Entry is not null)
        {
            try 
            { 
                Matches = await _provider.SearchAsync(Entry.Title, HttpContext.RequestAborted); 
                ProviderSearch = Entry.Title;
                HasSearched = true; 
            }
            catch (Exception error) when (IsExpectedProvider(error)) 
            { 
                ShowError(error); 
            }
        }
        
        return Page();
    }

    public async Task<IActionResult> OnPostSearchAsync([FromForm] string providerSearch)
    {
        await LoadAsync(Id);
        if (Entry is null) return NotFound();
        try { Matches = await _provider.SearchAsync(providerSearch, HttpContext.RequestAborted); ProviderSearch = providerSearch; HasSearched = true; }
        catch (Exception error) when (IsExpectedProvider(error)) { ShowError(error); }
        // Clear previous game selection when new search
        SelectedGameId = 0;
        SelectedGameTitle = null;
        CoverOptions.Clear();
        HeroOptions.Clear();
        return Page();
    }

    public async Task<IActionResult> OnPostPickAsync([FromForm] int providerGameId,
        [FromForm] string? providerTitle)
    {
        await LoadAsync(Id);
        if (Entry is null) return NotFound();
        if (providerGameId <= 0) { ModelState.AddModelError(string.Empty, "Choose a matched game first."); return Page(); }
        try
        {
            // Fetch BOTH cover and hero artwork in parallel
            var coversTask = _provider.CoversAsync(providerGameId, HttpContext.RequestAborted);
            var heroesTask = _provider.HeroesAsync(providerGameId, HttpContext.RequestAborted);
            await Task.WhenAll(coversTask, heroesTask);
            CoverOptions = coversTask.Result;
            HeroOptions = heroesTask.Result;
            SelectedGameId = providerGameId;
            SelectedGameTitle = providerTitle?.Trim();
            if (CoverOptions.Count == 0 && HeroOptions.Count == 0)
                TempData["Notice"] = "No artwork found for that game.";
        }
        catch (Exception error) when (IsExpectedProvider(error)) { ShowError(error); }
        return Page();
    }

    public async Task<IActionResult> OnPostDownloadAsync([FromForm] string imageUrl,
        [FromForm] ArtworkKind kind, [FromForm] int providerGameId, [FromForm] string? providerTitle)
    {
        await LoadAsync(Id);
        if (Entry is null) return NotFound();
        if (!Enum.IsDefined(kind)) kind = ArtworkKind.Cover;
        if (string.IsNullOrWhiteSpace(imageUrl))
        { ModelState.AddModelError(string.Empty, "Choose an artwork image first."); return Page(); }
        try
        {
            var (stream, contentType) = await _provider.DownloadAsync(imageUrl, HttpContext.RequestAborted);
            await using (stream) await _artwork.SaveDownloadAsync(Id, kind, stream, contentType);
            if (!string.IsNullOrWhiteSpace(providerTitle))
            {
                await using var db = await _factory.CreateDbContextAsync();
                var title = providerTitle.Trim();
                await db.Entries.Where(x => x.Id == Id).ExecuteUpdateAsync(u => u
                    .SetProperty(e => e.ProviderName, Services.SteamGridDbClient.Name)
                    .SetProperty(e => e.ProviderTitle, title)
                    .SetProperty(e => e.ProviderGameId, providerGameId > 0 ? providerGameId : (int?)null));
            }
            await LoadAsync(Id);
            TempData["Notice"] = kind == ArtworkKind.Cover ? "Cover artwork applied." : "Background artwork applied.";
            // Re-fetch both types so results stay visible
            var coversTask = _provider.CoversAsync(providerGameId, HttpContext.RequestAborted);
            var heroesTask = _provider.HeroesAsync(providerGameId, HttpContext.RequestAborted);
            await Task.WhenAll(coversTask, heroesTask);
            CoverOptions = coversTask.Result;
            HeroOptions = heroesTask.Result;
            SelectedGameId = providerGameId;
            SelectedGameTitle = providerTitle?.Trim();
        }
        catch (Exception error)
        {
            ShowError(error);
        }
        return Page();
    }

    public async Task<IActionResult> OnPostLocalAsync([FromForm] ArtworkKind kind)
    {
        await LoadAsync(Id);
        if (Entry is null) return NotFound();
        if (!Enum.IsDefined(kind)) kind = ArtworkKind.Cover;
        try
        {
            var path = await _picker.PickImageAsync();
            if (path is not null)
            {
                await _artwork.ImportLocalAsync(Id, kind, path);
                await LoadAsync(Id);
                TempData["Notice"] = $"{kind} artwork saved.";
            }
        }
        catch (Exception error) when (IsExpectedArtwork(error)) { ShowError(error); }
        return Page();
    }

    public async Task<IActionResult> OnPostClearAsync([FromForm] ArtworkKind kind)
    {
        await LoadAsync(Id);
        if (Entry is null) return NotFound();
        if (!Enum.IsDefined(kind)) kind = ArtworkKind.Cover;
        try { await _artwork.ClearAsync(Id, kind); TempData["Notice"] = $"{kind} artwork removed."; }
        catch (Exception error) when (IsExpectedArtwork(error)) { ShowError(error); return Page(); }
        return RedirectToPage(new { id = Id });
    }

    public async Task<IActionResult> OnPostSearchAgainAsync()
    {
        await LoadAsync(Id);
        if (Entry is null) return NotFound();
        // Reset to fresh search state
        ProviderSearch = "";
        Matches.Clear();
        CoverOptions.Clear();
        HeroOptions.Clear();
        SelectedGameId = 0;
        SelectedGameTitle = null;
        return Page();
    }

    private async Task LoadAsync(Guid id)
    {
        Entry = await _library.GetAsync(id);
        if (Entry is not null)
        {
            Id = id;
            if (string.IsNullOrEmpty(ProviderSearch)) ProviderSearch = Entry.Title;
        }
    }

    private static bool IsExpectedProvider(Exception error) => error is ArgumentException or InvalidOperationException
        or HttpRequestException or TaskCanceledException or Win32Exception;
    private static bool IsExpectedArtwork(Exception error) => UiPageModel.IsExpected(error)
        || error is HttpRequestException or TaskCanceledException;
}
