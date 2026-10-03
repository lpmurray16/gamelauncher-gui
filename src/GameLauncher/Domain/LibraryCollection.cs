using System;

namespace GameLauncher.Domain;

// Custom categories are shared collections, separate from Games/Emulators/Tools.
public sealed class LibraryCollection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string NameKey { get; set; } = "";
}

public sealed class CollectionEntry
{
    public Guid CollectionId { get; set; }
    public Guid EntryId { get; set; }
}
