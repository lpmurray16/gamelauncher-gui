using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameLauncher.Data;
using GameLauncher.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Services;

public sealed class CollectionService(IDbContextFactory<LibraryDbContext> factory)
{
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public async Task<List<LibraryCollection>> GetAllAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        // Comparer-based ordering cannot be translated to SQL; sort after materializing.
        var collections = await db.Collections.AsNoTracking().ToListAsync();
        return collections.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ThenBy(c => c.Id).ToList();
    }

    public async Task<LibraryCollection?> GetAsync(Guid id)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.Collections.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id);
    }

    public async Task<LibraryCollection?> GetByNameAsync(string name)
    {
        var key = name.Trim().ToUpperInvariant();
        await using var db = await factory.CreateDbContextAsync();
        return await db.Collections.AsNoTracking().SingleOrDefaultAsync(c => c.NameKey == key);
    }

    public async Task<List<LibraryEntry>> GetEntriesAsync(Guid collectionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var entryIds = await db.CollectionEntries
            .AsNoTracking()
            .Where(x => x.CollectionId == collectionId)
            .Select(x => x.EntryId)
            .ToListAsync();

        if (entryIds.Count == 0) return new();

        var entries = await db.Entries
            .AsNoTracking()
            .Where(x => entryIds.Contains(x.Id))
            .ToListAsync();
        return entries.OrderByDescending(x => x.IsFavorite)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<Guid> CreateAsync(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > 60) throw new ArgumentException("Collection name must be 1–60 characters.");
        var key = trimmed.ToUpperInvariant();

        await _writeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            if (await db.Collections.AnyAsync(c => c.NameKey == key))
                throw new ArgumentException("A collection with this name already exists.");

            var collection = new LibraryCollection { Name = trimmed, NameKey = key };
            db.Collections.Add(collection);
            await db.SaveChangesAsync();
            return collection.Id;
        }
        finally { _writeGate.Release(); }
    }

    public async Task RenameAsync(Guid id, string newName)
    {
        var trimmed = newName.Trim();
        if (trimmed.Length is 0 or > 60) throw new ArgumentException("Collection name must be 1–60 characters.");
        var key = trimmed.ToUpperInvariant();

        await _writeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var collection = await db.Collections.SingleOrDefaultAsync(c => c.Id == id)
                ?? throw new InvalidOperationException("This collection no longer exists.");
            if (await db.Collections.AnyAsync(c => c.NameKey == key && c.Id != id))
                throw new ArgumentException("A collection with this name already exists.");

            collection.Name = trimmed;
            collection.NameKey = key;
            await db.SaveChangesAsync();
        }
        finally { _writeGate.Release(); }
    }

    public async Task DeleteAsync(Guid id)
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var collection = await db.Collections.SingleOrDefaultAsync(c => c.Id == id);
            if (collection is null) return;
            db.Collections.Remove(collection);
            await db.SaveChangesAsync();
        }
        finally { _writeGate.Release(); }
    }

    public async Task AddEntryAsync(Guid collectionId, Guid entryId)
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            var collection = await db.Collections.SingleOrDefaultAsync(c => c.Id == collectionId)
                ?? throw new InvalidOperationException("This collection no longer exists.");
            var entry = await db.Entries.SingleOrDefaultAsync(e => e.Id == entryId)
                ?? throw new InvalidOperationException("This entry no longer exists.");
            // Collections are filter sets on the Games dashboard only.
            if (entry.Category != LibraryCategory.Games)
                throw new ArgumentException("Only games can be added to a collection.");

            if (!await db.CollectionEntries.AnyAsync(x => x.CollectionId == collectionId && x.EntryId == entryId))
            {
                db.CollectionEntries.Add(new CollectionEntry { CollectionId = collectionId, EntryId = entryId });
                await db.SaveChangesAsync();
            }
        }
        finally { _writeGate.Release(); }
    }

    public async Task RemoveEntryAsync(Guid collectionId, Guid entryId)
    {
        await _writeGate.WaitAsync();
        try
        {
            await using var db = await factory.CreateDbContextAsync();
            await db.CollectionEntries
                .Where(x => x.CollectionId == collectionId && x.EntryId == entryId)
                .ExecuteDeleteAsync();
        }
        finally { _writeGate.Release(); }
    }

    public async Task<bool> ContainsEntryAsync(Guid collectionId, Guid entryId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.CollectionEntries
            .AsNoTracking()
            .AnyAsync(x => x.CollectionId == collectionId && x.EntryId == entryId);
    }

    public async Task<List<LibraryCollection>> GetCollectionsForEntryAsync(Guid entryId)
    {
        await using var db = await factory.CreateDbContextAsync();
        var collectionIds = await db.CollectionEntries
            .AsNoTracking()
            .Where(x => x.EntryId == entryId)
            .Select(x => x.CollectionId)
            .ToListAsync();

        if (collectionIds.Count == 0) return new();

        var collections = await db.Collections
            .AsNoTracking()
            .Where(c => collectionIds.Contains(c.Id))
            .ToListAsync();
        return collections.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<int> GetEntryCountAsync(Guid collectionId)
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.CollectionEntries
            .AsNoTracking()
            .CountAsync(x => x.CollectionId == collectionId);
    }

    // One query for every collection's count, instead of one query per tab or card.
    // Only games count: an entry moved out of Games keeps its membership but is not shown.
    public async Task<Dictionary<Guid, int>> GetEntryCountsAsync()
    {
        await using var db = await factory.CreateDbContextAsync();
        return await db.CollectionEntries
            .AsNoTracking()
            .Join(db.Entries.Where(e => e.Category == LibraryCategory.Games), m => m.EntryId, e => e.Id, (m, e) => m)
            .GroupBy(x => x.CollectionId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
    }

    // Membership pairs for the given entries, so views can check "in collection" without a query per item.
    public async Task<HashSet<(Guid CollectionId, Guid EntryId)>> GetMembershipsAsync(IEnumerable<Guid> entryIds)
    {
        var ids = entryIds.Distinct().ToList();
        if (ids.Count == 0) return new();
        await using var db = await factory.CreateDbContextAsync();
        var pairs = await db.CollectionEntries
            .AsNoTracking()
            .Where(x => ids.Contains(x.EntryId))
            .Select(x => new { x.CollectionId, x.EntryId })
            .ToListAsync();
        return pairs.Select(x => (x.CollectionId, x.EntryId)).ToHashSet();
    }
}