using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace GameLauncher.Services;

public sealed record ProviderMatch(int Id, string Name, string Kinds, bool Verified);
public sealed record ArtworkOption(string ImageUrl, string ThumbnailUrl, string Author);

public sealed class SteamGridDbClient(IHttpClientFactory http, CredentialStore credentials, ILogger<SteamGridDbClient> logger)
{
    public const string Name = "SteamGridDB";
    private const string Base = "https://www.steamgriddb.com/api/v2/";

    private HttpClient Create()
    {
        var key = credentials.Read(CredentialStore.SteamGridDb)
            ?? throw new InvalidOperationException("Add your SteamGridDB API key in Settings first.");
        var client = http.CreateClient("sgdb");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
        return client;
    }

    private HttpClient CreateForCdn()
    {
        // CDN downloads don't require authentication - use a client without the API key
        var client = http.CreateClient("sgdb");
        client.DefaultRequestHeaders.Authorization = null;
        return client;
    }

    public async Task<List<ProviderMatch>> SearchAsync(string term, CancellationToken cancellationToken = default)
    {
        term = term.Trim();
        if (term.Length is 0 or > 200) throw new ArgumentException("Enter a search term of 1–200 characters.");
        var client = Create();
        var response = await client.GetAsync(Base + "search/autocomplete/" + Uri.EscapeDataString(term), cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await ErrorAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<SearchResponse>(cancellationToken);
        return body?.Data?.Select(x => new ProviderMatch(x.Id, x.Name,
            string.Join(", ", x.Types ?? []), x.Verified)).Take(12).ToList() ?? [];
    }

    public async Task<List<ArtworkOption>> CoversAsync(int gameId, CancellationToken cancellationToken = default)
        => await AssetsAsync("grids", gameId, "dimensions=600x900,342x482,660x930", cancellationToken);

    public async Task<List<ArtworkOption>> HeroesAsync(int gameId, CancellationToken cancellationToken = default)
        => await AssetsAsync("heroes", gameId, "dimensions=1920x620,1600x650,3840x1240", cancellationToken);

    private async Task<List<ArtworkOption>> AssetsAsync(string segment, int gameId, string filter,
        CancellationToken cancellationToken)
    {
        if (gameId <= 0) throw new ArgumentException("Choose a game match first.");
        var client = Create();
        var response = await client.GetAsync(
            $"{Base}{segment}/game/{gameId}?{filter}&types=static&nsfw=false&limit=12&mimes=image/png,image/jpeg,image/webp", cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw await ErrorAsync(response, cancellationToken);
        var body = await response.Content.ReadFromJsonAsync<AssetsResponse>(cancellationToken);
        return body?.Data?.Where(x => x.Url.StartsWith("https://", StringComparison.Ordinal))
            .Select(x => new ArtworkOption(x.Url, string.IsNullOrEmpty(x.Thumb) ? x.Url : x.Thumb,
                x.Author?.Name ?? "")).ToList() ?? [];
    }

    public async Task<(Stream Stream, string ContentType)> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new ArgumentException("Only HTTPS artwork URLs can be downloaded.");
        // SteamGridDB serves artwork via AWS S3 (s3.amazonaws.com/steamgriddb/...), 
        // not directly from steamgriddb.com. Accept both the API domain and known CDN hosts.
        var allowedHosts = new[] { "steamgriddb.com", "s3.amazonaws.com" };
        if (!allowedHosts.Any(h => uri.Host.EndsWith(h, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Only SteamGridDB-hosted artwork can be downloaded.");
        var client = CreateForCdn();
        var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("CDN download failed: {StatusCode} for URL: {Url}", response.StatusCode, url);
            throw await ErrorAsync(response, cancellationToken);
        }
        var type = response.Content.Headers.ContentType?.MediaType ?? "";
        return (await response.Content.ReadAsStreamAsync(cancellationToken), type);
    }

    private static async Task<Exception> ErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        response.Content?.Dispose();
        return response.StatusCode switch
        {
            System.Net.HttpStatusCode.Unauthorized => new InvalidOperationException(
                "SteamGridDB rejected the API key. Check it in Settings."),
            System.Net.HttpStatusCode.TooManyRequests => new InvalidOperationException(
                "SteamGridDB rate limit reached. Wait a moment and try again."),
            System.Net.HttpStatusCode.NotFound => new InvalidOperationException("No results found."),
            _ => new InvalidOperationException($"SteamGridDB request failed ({(int)response.StatusCode}).")
        };
    }

    public async Task<string> CheckKeyAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            // Search for a well-known title as a read-only connectivity check.
            _ = await SearchAsync("halo", cancellationToken);
            return "ok";
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            return ex.Message;
        }
    }

    private sealed record SearchResponse([property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("data")] List<SearchItem>? Data);
    private sealed record SearchItem([property: JsonPropertyName("id")] int Id,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("types")] List<string>? Types,
        [property: JsonPropertyName("verified")] bool Verified);
    private sealed record AssetsResponse([property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("data")] List<AssetItem>? Data);
    private sealed record AssetItem([property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("thumb")] string? Thumb,
        [property: JsonPropertyName("author")] AssetAuthor? Author);
    private sealed record AssetAuthor([property: JsonPropertyName("name")] string? Name);
}
