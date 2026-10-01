using System;
using System.ComponentModel;
using System.IO;
using Microsoft.AspNetCore.Mvc.RazorPages;
using GameLauncher.Domain;

namespace GameLauncher.Pages;

public abstract class UiPageModel : PageModel
{
    protected static bool IsExpected(Exception error) => error is ArgumentException or InvalidOperationException or IOException or Win32Exception or UnauthorizedAccessException;
    protected void ShowError(Exception error) => ModelState.AddModelError(string.Empty, error.Message);
    protected static LibraryCategory SafeCategory(LibraryCategory category) => Enum.IsDefined(typeof(LibraryCategory), category) ? category : LibraryCategory.Games;
}
