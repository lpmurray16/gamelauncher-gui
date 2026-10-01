using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameLauncher.Domain;
using GameLauncher.Services;
using Microsoft.AspNetCore.Mvc;

namespace GameLauncher.Pages;

public sealed class ScanModel : UiPageModel
{
    private readonly ScannerService _scanner;
    private readonly FolderPicker _picker;
    private readonly LibraryService _library;
    public ScanModel(ScannerService scanner, FolderPicker picker, LibraryService library) { _scanner = scanner; _picker = picker; _library = library; }
    [BindProperty(SupportsGet = true)] public LibraryCategory Category { get; set; } = LibraryCategory.Games;
    [BindProperty] public string Root { get; set; } = "";
    [BindProperty] public bool Recursive { get; set; } = true;
    [BindProperty] public Guid ScanId { get; set; }
    [BindProperty] public List<Guid> SelectedIds { get; set; } = new();
    public ScanResult? Result { get; private set; }
    public void OnGet() => Category = SafeCategory(Category);
    public async Task<IActionResult> OnPostBrowseAsync()
    {
        Category = SafeCategory(Category);
        ModelState.Clear();
        try { var path = await _picker.PickAsync(); if (path is not null) Root = path; }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); }
        return Page();
    }
    public async Task<IActionResult> OnPostScanAsync()
    {
        Category = SafeCategory(Category);
        ModelState.Clear();
        if (string.IsNullOrWhiteSpace(Root)) { ModelState.AddModelError(string.Empty, "Choose a folder before scanning."); return Page(); }
        try
        {
            Result = await _scanner.ScanAsync(Root, Recursive, HttpContext.RequestAborted);
            ScanId = Result.Id;
            SelectedIds.Clear();
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); }
        return Page();
    }
    public async Task<IActionResult> OnPostImportAsync()
    {
        Category = SafeCategory(Category);
        ModelState.Clear();
        Result = _scanner.GetResult(ScanId);
        if (Result is null) { ModelState.AddModelError(string.Empty, "This scan has expired. Scan the folder again to review fresh results."); return Page(); }
        Root = Result.Root;
        if (SelectedIds.Count == 0) { ModelState.AddModelError(string.Empty, "Select at least one launch file to import."); return Page(); }
        try
        {
            var count = await _library.ImportAsync(ScanId, SelectedIds, Category);
            TempData["Notice"] = $"Imported {count} entries. Existing entries were left unchanged.";
            return RedirectToPage("/Index", new { category = Category });
        }
        catch (Exception error) when (IsExpected(error)) { ShowError(error); return Page(); }
    }
}
