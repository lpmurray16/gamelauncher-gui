using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using GameLauncher.Domain;
using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed partial class EditModel : UiPageModel
{
    private readonly LibraryService _library;
    [BindProperty] public Guid? Id { get; set; }
    [BindProperty, Required, StringLength(200)] public string Title { get; set; } = "";
    [BindProperty, Required, Display(Name = "Executable path")] public string TargetPath { get; set; } = "";
    [BindProperty] public string? TrackingExecutablePath { get; set; }
    [BindProperty] public string? Arguments { get; set; }
    [BindProperty] public string? WorkingDirectory { get; set; }
    [BindProperty, EnumDataType(typeof(LibraryCategory))] public LibraryCategory Category { get; set; }
    [BindProperty] public bool IsFavorite { get; set; }
    [BindProperty] public bool ConfirmRemoval { get; set; }
    [BindProperty] public List<Guid> CompanionIds { get; set; } = new();
    public List<LibraryEntry> CompanionChoices { get; private set; } = new();
    [BindProperty] public List<BundleStepForm> LaunchSteps { get; set; } = new();
    public sealed record BundleRow(Guid EntryId, string Title, int DelaySeconds, int Index);
    public sealed class BundleStepForm
    {
        public Guid EntryId { get; set; }
        [Range(0, 10)] public int DelaySeconds { get; set; }
    }

    private async Task LoadLaunchStepsAsync(Guid entryId)
    {
        LaunchSteps = (await _library.GetLaunchStepsAsync(entryId))
            .Select(x => new BundleStepForm { EntryId = x.EntryId, DelaySeconds = x.DelaySeconds }).ToList();
    }

    private void ReconcileLaunchSteps()
    {
        // Checkbox-only fallback: preserve existing order, add new helpers before main.
        LaunchSteps.RemoveAll(x => x.EntryId != Guid.Empty && !CompanionIds.Contains(x.EntryId));
        foreach (var choice in CompanionChoices.Where(x => CompanionIds.Contains(x.Id)))
            if (!LaunchSteps.Any(x => x.EntryId == choice.Id))
            {
                var mainIndex = LaunchSteps.FindIndex(x => x.EntryId == Guid.Empty);
                LaunchSteps.Insert(mainIndex < 0 ? LaunchSteps.Count : mainIndex, new BundleStepForm { EntryId = choice.Id });
            }
        if (LaunchSteps.Count == 0) LaunchSteps.Add(new BundleStepForm());
    }

    private async Task<IActionResult> EditorPageAsync()
    {
        try
        {
            CompanionChoices = await _library.GetCompanionChoicesAsync(Id);
            ReconcileLaunchSteps();
            Entry = Id.HasValue ? await _library.GetAsync(Id.Value) : null;
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); }
        return Page();
    }
    public async Task<IActionResult> OnGetAsync(Guid? id, LibraryCategory category = LibraryCategory.Games)
    {
        Category = SafeCategory(category);
        if (id is null) return await EditorPageAsync();
        try
        {
            var entry = await _library.GetAsync(id.Value);
            if (entry is null) return NotFound();
            Id = entry.Id; Title = entry.Title; TargetPath = entry.TargetPath; Arguments = entry.Arguments;
            WorkingDirectory = entry.WorkingDirectory; Category = entry.Category; IsFavorite = entry.IsFavorite;
            TrackingExecutablePath = entry.TrackingExecutablePath;
            CompanionIds = await _library.GetCompanionIdsAsync(entry.Id);
            await LoadLaunchStepsAsync(entry.Id);
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); }
        return await EditorPageAsync();
    }
    public async Task<IActionResult> OnPostBrowsePathAsync([FromForm] string kind)
    {
        // This authenticated, antiforgery-protected request only picks a path.
        // Entry validation and saving belong to the separate Save action.
        ModelState.Clear();
        try
        {
            var path = await _picker.PickEntryPathAsync(kind);
            return new JsonResult(new { path });
        }
        catch (Exception error) when (IsExpected(error))
        {
            return new JsonResult(new { error = error.Message }) { StatusCode = 400 };
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return await EditorPageAsync();
        try
        {
            CompanionChoices = await _library.GetCompanionChoicesAsync(Id);
            ReconcileLaunchSteps();
            var steps = LaunchSteps.Select(x => new LaunchStepInput(x.EntryId, x.DelaySeconds)).ToList();
            var savedId = await _library.SaveAsync(new EntryInput(Id, Title, TargetPath, Arguments, WorkingDirectory, Category, IsFavorite, CompanionIds, TrackingExecutablePath, steps));
            TempData["Notice"] = Id.HasValue ? "Entry updated." : "Entry added. You can now choose its artwork below.";
            return RedirectToPage("/Edit", new { id = savedId });
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); return await EditorPageAsync(); }
    }
    public async Task<IActionResult> OnPostDeleteAsync()
    {
        // Removal has its own form; edit-field validation does not apply.
        ModelState.Clear();
        if (!Id.HasValue) return BadRequest();
        try
        {
            var entry = await _library.GetAsync(Id.Value);
            if (entry is null) return NotFound();
            Category = entry.Category; Title = entry.Title; TargetPath = entry.TargetPath;
            Arguments = entry.Arguments; WorkingDirectory = entry.WorkingDirectory; IsFavorite = entry.IsFavorite;
            TrackingExecutablePath = entry.TrackingExecutablePath;
            CompanionIds = await _library.GetCompanionIdsAsync(entry.Id);
            await LoadLaunchStepsAsync(entry.Id);
            if (!ConfirmRemoval) { ModelState.AddModelError(string.Empty, "Confirm that you want to remove this library entry."); return await EditorPageAsync(); }
            await _library.DeleteAsync(Id.Value);
            TempData["Notice"] = "Entry removed. Your local files have not been changed.";
            return RedirectToPage("/Index", new { category = Category });
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); return await EditorPageAsync(); }
    }
}
