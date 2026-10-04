using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using GameLauncher.Domain;
using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed class EditModel : UiPageModel
{
    private readonly LibraryService _library;
    public EditModel(LibraryService library) => _library = library;
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

    private async Task<IActionResult> EditorPageAsync()
    {
        try { CompanionChoices = await _library.GetCompanionChoicesAsync(Id); }
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
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); }
        return await EditorPageAsync();
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return await EditorPageAsync();
        try
        {
            await _library.SaveAsync(new EntryInput(Id, Title, TargetPath, Arguments, WorkingDirectory, Category, IsFavorite, CompanionIds, TrackingExecutablePath));
            TempData["Notice"] = Id.HasValue ? "Entry updated." : "Entry added to your library.";
            return RedirectToPage("/Index", new { category = Category });
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
            if (!ConfirmRemoval) { ModelState.AddModelError(string.Empty, "Confirm that you want to remove this library entry."); return await EditorPageAsync(); }
            await _library.DeleteAsync(Id.Value);
            TempData["Notice"] = "Entry removed. Your local files have not been changed.";
            return RedirectToPage("/Index", new { category = Category });
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); return await EditorPageAsync(); }
    }
}
