namespace GameLauncher.Domain;

public enum LibraryCategory { Games, Emulators, Tools }

public enum ArtworkKind { Cover, Hero }

public sealed class LibraryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string TargetKey { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public LibraryCategory Category { get; set; }
    public bool IsFavorite { get; set; }
    // File names relative to the application artwork directory; null means "fallback art".
    public string? CoverImageFile { get; set; }
    public string? HeroImageFile { get; set; }
    public string? ProviderName { get; set; }
    public string? ProviderTitle { get; set; }
    public int? ProviderGameId { get; set; }
    public DateTime AddedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastLaunchedUtc { get; set; }
}

public sealed record EntryInput(Guid? Id, string Title, string TargetPath,
    string? Arguments, string? WorkingDirectory, LibraryCategory Category, bool IsFavorite);
