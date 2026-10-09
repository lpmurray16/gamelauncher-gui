using System.ComponentModel;
using System.Net.Http;
using GameLauncher.Data;
using GameLauncher.Domain;
using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Pages;

public sealed partial class EditModel
{
    private readonly Services.ArtworkService _artwork;
    private readonly Services.SteamGridDbClient _provider;
    private readonly Services.FolderPicker _picker;
    private readonly IDbContextFactory<LibraryDbContext> _factory;

    public EditModel(LibraryService library, Services.ArtworkService artwork,
        Services.SteamGridDbClient provider, Services.FolderPicker picker,
        IDbContextFactory<LibraryDbContext> factory)
    { _library = library; _artwork = artwork; _provider = provider; _picker = picker; _factory = factory; }

    public LibraryEntry? Entry { get; private set; }
    public string? ArtworkNotice { get; private set; }
    public string ProviderSearch { get; private set; } = "";
    public List<ProviderMatch> Matches { get; private set; } = new();
    public List<ArtworkOption> CoverOptions { get; private set; } = new();
    public List<ArtworkOption> HeroOptions { get; private set; } = new();
    public int SelectedGameId { get; private set; }
    public string? SelectedGameTitle { get; private set; }
    public bool HasSearched { get; private set; }
    public bool HasSelectedGame => SelectedGameId > 0;

    public async Task<IActionResult> OnPostSearchAsync([FromForm] string providerSearch)
    {
        await LoadArtworkAsync();
        if (Entry is null) return NotFound();
        ProviderSearch = providerSearch;
        try { Matches = await _provider.SearchAsync(providerSearch, HttpContext.RequestAborted); HasSearched = true; }
        catch (Exception error) when (IsExpectedProvider(error)) { ShowError(error); }
        // Clear previous game selection when new search
        SelectedGameId = 0;
        SelectedGameTitle = null;
        CoverOptions.Clear();
        HeroOptions.Clear();
        return await ArtworkPageAsync();
    }

    public async Task<IActionResult> OnPostPickAsync([FromForm] int providerGameId,
        [FromForm] string? providerTitle)
    {
        await LoadArtworkAsync();
        if (Entry is null) return NotFound();
        if (providerGameId <= 0) { ModelState.AddModelError(string.Empty, "Choose a matched game first."); return await ArtworkPageAsync(); }
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
                ArtworkNotice = "No artwork found for that game.";
        }
        catch (Exception error) when (IsExpectedProvider(error)) { ShowError(error); }
        return await ArtworkPageAsync();
    }

    public async Task<IActionResult> OnPostDownloadAsync([FromForm] string imageUrl,
        [FromForm] ArtworkKind kind, [FromForm] int providerGameId, [FromForm] string? providerTitle)
    {
        await LoadArtworkAsync();
        if (Entry is null) return NotFound();
        if (!Enum.IsDefined(kind)) kind = ArtworkKind.Cover;
        if (string.IsNullOrWhiteSpace(imageUrl))
        { ModelState.AddModelError(string.Empty, "Choose an artwork image first."); return await ArtworkPageAsync(); }
        try
        {
            var (stream, contentType) = await _provider.DownloadAsync(imageUrl, HttpContext.RequestAborted);
            await using (stream) await _artwork.SaveDownloadAsync(Id!.Value, kind, stream, contentType);
            if (!string.IsNullOrWhiteSpace(providerTitle))
            {
                await using var db = await _factory.CreateDbContextAsync();
                var title = providerTitle.Trim();
                await db.Entries.Where(x => x.Id == Id).ExecuteUpdateAsync(u => u
                    .SetProperty(e => e.ProviderName, Services.SteamGridDbClient.Name)
                    .SetProperty(e => e.ProviderTitle, title)
                    .SetProperty(e => e.ProviderGameId, providerGameId > 0 ? providerGameId : (int?)null));
            }
            await LoadArtworkAsync();
            ArtworkNotice = kind == ArtworkKind.Cover ? "Cover artwork applied." : "Background artwork applied.";
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
        return await ArtworkPageAsync();
    }

    public async Task<IActionResult> OnPostLocalAsync([FromForm] ArtworkKind kind)
    {
        await LoadArtworkAsync();
        if (Entry is null) return NotFound();
        if (!Enum.IsDefined(kind)) kind = ArtworkKind.Cover;
        try
        {
            var path = await _picker.PickImageAsync();
            if (path is not null)
            {
                await _artwork.ImportLocalAsync(Id!.Value, kind, path);
                await LoadArtworkAsync();
                ArtworkNotice = $"{kind} artwork saved.";
            }
        }
        catch (Exception error) when (IsExpectedArtwork(error)) { ShowError(error); }
        return await ArtworkPageAsync();
    }

    public async Task<IActionResult> OnPostClearAsync([FromForm] ArtworkKind kind)
    {
        await LoadArtworkAsync();
        if (Entry is null) return NotFound();
        if (!Enum.IsDefined(kind)) kind = ArtworkKind.Cover;
        try { await _artwork.ClearAsync(Id!.Value, kind); ArtworkNotice = $"{kind} artwork removed."; }
        catch (Exception error) when (IsExpectedArtwork(error)) { ShowError(error); return await ArtworkPageAsync(); }
        await LoadArtworkAsync();
        return await ArtworkPageAsync();
    }

    public async Task<IActionResult> OnPostSearchAgainAsync()
    {
        await LoadArtworkAsync();
        if (Entry is null) return NotFound();
        // Reset to fresh search state
        ProviderSearch = "";
        Matches.Clear();
        CoverOptions.Clear();
        HeroOptions.Clear();
        SelectedGameId = 0;
        SelectedGameTitle = null;
        return await ArtworkPageAsync();
    }

    private async Task LoadArtworkAsync()
    {
        // Artwork forms do not post the entry fields; their validation is independent.
        ModelState.Clear();
        Entry = Id.HasValue ? await _library.GetAsync(Id.Value) : null;
        if (Entry is not null && string.IsNullOrEmpty(ProviderSearch)) ProviderSearch = Entry.Title;
    }

    private async Task<IActionResult> ArtworkPageAsync()
    {
        if (Request.Headers["X-Entry-Artwork"] == "true")
            return Partial("_EntryArtwork", this);

        // Ordinary POST fallback; the enhanced UI replaces only the artwork region.
        if (Entry is not null)
        {
            Title = Entry.Title; TargetPath = Entry.TargetPath; Category = Entry.Category;
            Arguments = Entry.Arguments; WorkingDirectory = Entry.WorkingDirectory;
            TrackingExecutablePath = Entry.TrackingExecutablePath; IsFavorite = Entry.IsFavorite;
            CompanionIds = await _library.GetCompanionIdsAsync(Entry.Id);
        }
        return await EditorPageAsync();
    }

    private static bool IsExpectedProvider(Exception error) => error is ArgumentException or InvalidOperationException
        or HttpRequestException or TaskCanceledException or Win32Exception;
    private static bool IsExpectedArtwork(Exception error) => UiPageModel.IsExpected(error)
        || error is HttpRequestException or TaskCanceledException;
}
