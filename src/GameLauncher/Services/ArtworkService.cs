using System.IO;
using System.Security.Cryptography;
using GameLauncher.Domain;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Services;

public static class Artwork
{
    private const long MaxImageBytes = 20L * 1024 * 1024;

    public static readonly Dictionary<string, string> Signatures = new(StringComparer.OrdinalIgnoreCase)
    {
        ["png"] = "89504E470D0A1A0A",
        ["jpg"] = "FFD8FF",
        ["webp"] = "52494646",
    };

    public static void ValidateExtension(string path)
    {
        var extension = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (!Signatures.ContainsKey(extension))
            throw new ArgumentException("Choose a PNG, JPEG, or WebP image.");
    }

    /// <summary>Validate magic bytes and normalize the file extension to the detected type.</summary>
    public static string DetectExtension(string path)
    {
        ValidateExtension(path);
        var info = new FileInfo(path);
        if (info.Length == 0 || info.Length > MaxImageBytes)
            throw new ArgumentException("Images must be between 1 byte and 20 MB.");
        var header = new byte[12];
        using (var stream = File.OpenRead(path))
        {
            var read = stream.Read(header, 0, header.Length);
            if (read < 4) throw new ArgumentException("That file is not a readable image.");
        }
        foreach (var (extension, hex) in Signatures)
        {
            var marker = Convert.FromHexString(hex);
            if (header.AsSpan(0, marker.Length).SequenceEqual(marker))
            {
                if (extension == "webp" && header[8] != 'W') throw new ArgumentException("That WebP file looks invalid.");
                return extension;
            }
        }
        throw new ArgumentException("That file's contents do not match a supported image format.");
    }

    public static (string FullPath, string ContentType) Resolve(AppPaths paths, string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || Path.GetFileName(fileName) != fileName)
            throw new ArgumentException("Unknown artwork file.");
        var full = Path.Combine(paths.ArtworkDirectory, fileName);
        if (!File.Exists(full)) throw new ArgumentException("Unknown artwork file.");
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var type = extension switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".webp" => "image/webp",
            _ => throw new ArgumentException("Unknown artwork file.")
        };
        return (full, type);
    }
}

public sealed class ArtworkService(IDbContextFactory<Data.LibraryDbContext> factory, AppPaths paths,
    Microsoft.Extensions.Logging.ILogger<ArtworkService> logger)
{
    public async Task<string> ImportLocalAsync(Guid entryId, ArtworkKind kind, string sourcePath)
    {
        var detected = Artwork.DetectExtension(sourcePath);
        return await SetArtworkAsync(entryId, kind, detected, target => File.Copy(sourcePath, target, overwrite: true));
    }

    public async Task<string> SaveDownloadAsync(Guid entryId, ArtworkKind kind, Stream content, string contentType)
    {
        var extension = contentType switch
        {
            "image/png" => "png",
            "image/jpeg" or "image/jpg" => "jpg",
            "image/webp" => "webp",
            _ => throw new ArgumentException("The provider returned an unsupported image type.")
        };
        // Spool to a temp location so we can validate magic bytes before accepting it.
        // Include extension so ValidateExtension can check it.
        var temp = Path.Combine(Path.GetTempPath(), $"gl-{Guid.NewGuid():N}.{extension}");
        try
        {
            await using (var output = File.Create(temp))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await content.ReadAsync(buffer)) > 0)
                {
                    total += read;
                    if (total > 20L * 1024 * 1024)
                        throw new ArgumentException("The downloaded image is too large.");
                    await output.WriteAsync(buffer.AsMemory(0, read));
                }
            }
            return await SetArtworkAsync(entryId, kind, Artwork.DetectExtension(temp), target => File.Move(temp, target, overwrite: true));
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
        }
    }

    public async Task ClearAsync(Guid entryId, ArtworkKind kind)
    {
        await using var db = await factory.CreateDbContextAsync();
        var entry = await db.Entries.SingleOrDefaultAsync(x => x.Id == entryId)
            ?? throw new InvalidOperationException("This entry no longer exists.");
        var oldFile = kind == ArtworkKind.Cover ? entry.CoverImageFile : entry.HeroImageFile;
        if (kind == ArtworkKind.Cover) entry.CoverImageFile = null; else entry.HeroImageFile = null;
        await db.SaveChangesAsync();
        DeleteQuietly(paths, oldFile);
    }

    private async Task<string> SetArtworkAsync(Guid entryId, ArtworkKind kind, string extension,
        Action<string> writeFile)
    {
        string oldFile;
        string newFile;
        Directory.CreateDirectory(paths.ArtworkDirectory);
        await using (var db = await factory.CreateDbContextAsync())
        {
            var entry = await db.Entries.SingleOrDefaultAsync(x => x.Id == entryId)
                ?? throw new InvalidOperationException("This entry no longer exists.");
            oldFile = (kind == ArtworkKind.Cover ? entry.CoverImageFile : entry.HeroImageFile) ?? "";
            // A new URL on every save prevents stale previews and library images.
            newFile = $"{entryId:N}-{(kind == ArtworkKind.Cover ? "cover" : "hero")}-{Guid.NewGuid():N}.{extension}";
            writeFile(Path.Combine(paths.ArtworkDirectory, newFile));
            if (kind == ArtworkKind.Cover) entry.CoverImageFile = newFile; else entry.HeroImageFile = newFile;
            await db.SaveChangesAsync();
        }
        if (!string.IsNullOrEmpty(oldFile) && oldFile != newFile) DeleteQuietly(paths, oldFile);
        return newFile;
    }

    private static void DeleteQuietly(AppPaths paths, string? fileName)
    {
        try
        {
            if (string.IsNullOrEmpty(fileName)) return;
            var full = Path.Combine(paths.ArtworkDirectory, fileName);
            if (File.Exists(full)) File.Delete(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Old artwork cleanup is best-effort; orphaned cache files are harmless.
        }
    }

    // Orphan cleanup on startup: artwork files not referenced by any entry.
    public async Task CleanOrphansAsync()
    {
        try
        {
            Directory.CreateDirectory(paths.ArtworkDirectory);
            await using var db = await factory.CreateDbContextAsync();
            var referenced = (await db.Entries.Where(x => x.CoverImageFile != null || x.HeroImageFile != null)
                .Select(x => x.CoverImageFile).ToListAsync())
                .Concat(await db.Entries.Where(x => x.HeroImageFile != null).Select(x => x.HeroImageFile).ToListAsync())
                .Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(paths.ArtworkDirectory))
                if (!referenced.Contains(Path.GetFileName(file)!))
                    try { File.Delete(file); } catch (IOException) { }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Artwork cleanup skipped.");
        }
    }
}
