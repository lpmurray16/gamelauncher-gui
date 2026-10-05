using System.IO;
using GameLauncher.Contracts;
using GameLauncher.Data;
using GameLauncher.Domain;
using GameLauncher.Services;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher.Companion;

public static class CompanionEndpoints
{
    public const string GenerationKey = "CompanionGeneration";

    // Called before the desktop session middleware. Never grant access to desktop
    // pages/assets/actions merely because a request has a valid companion token.
    public static async Task HandleLanRequest(HttpContext context, RequestDelegate next, CompanionAccess access)
    {
        var request = context.Request;
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (!access.IsActive || !CompanionAccess.IsLocalAddress(context.Connection.RemoteIpAddress) ||
            request.Host.Port != context.Connection.LocalPort || request.Headers.ContainsKey("Origin") ||
            request.Query.ContainsKey("access_token"))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        if (!request.Path.StartsWithSegments("/api") && !request.Path.StartsWithSegments("/hubs/games"))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        var publicRequest = (HttpMethods.IsGet(request.Method) && request.Path == "/api/device") ||
            (HttpMethods.IsPost(request.Method) && request.Path == "/api/pair");
        if (!publicRequest)
        {
            if (!access.Authenticate(request.Headers.Authorization.ToString(), out var generation))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new CommandResponse("Pair this phone with Launchpad again."), context.RequestAborted);
                return;
            }
            context.Items[GenerationKey] = generation;
        }
        await next(context);
    }

    private static async Task<IResult> StopGame(Guid id, HttpContext context, LibraryService library, bool force)
    {
        if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
            return Results.BadRequest(new CommandResponse("This command accepts only a saved game ID, with no request body."));
        var entry = await library.GetAsync(id);
        if (entry is null || entry.Category != LibraryCategory.Games) return Results.NotFound();
        try { return Results.Ok(new CommandResponse(GameProcessControl.Stop(entry, force))); }
        catch (InvalidOperationException error)
        {
            // GameProcessControl supplies safe messages, never executable paths or raw Win32 errors.
            return Results.Conflict(new CommandResponse(error.Message));
        }
    }

    public static void MapCompanion(this WebApplication app)
    {
        app.MapGet("/api/device", (CompanionAccess access) => access.Device);
        app.MapPost("/api/pair", (PairingRequest request, CompanionAccess access) =>
        {
            var paired = access.Pair(request.Code);
            return paired is null
                ? Results.Json(new CommandResponse("Invalid or expired code. Generate a new code in Windows after five failed attempts."), statusCode: 401)
                : Results.Ok(paired);
        });
        app.MapGet("/api/games", async (IDbContextFactory<LibraryDbContext> factory, GameStatusMonitor monitor, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var entries = await db.Entries.AsNoTracking().Where(x => x.Category == LibraryCategory.Games).ToListAsync(ct);
            return entries.OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Id).Select(x => new GameDto(
                x.Id, x.Title, x.CoverImageFile is null ? null : $"/api/games/{x.Id:D}/cover?v={Uri.EscapeDataString(x.CoverImageFile)}",
                monitor.GetStatus(x.Id), GameStatusMonitor.CanTrack(x))).ToArray();
        });
        app.MapGet("/api/games/{id:guid}/cover", async (Guid id, IDbContextFactory<LibraryDbContext> factory, AppPaths paths, CancellationToken ct) =>
        {
            await using var db = await factory.CreateDbContextAsync(ct);
            var entry = await db.Entries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.Category == LibraryCategory.Games, ct);
            if (entry?.CoverImageFile is null) return Results.NotFound();
            try
            {
                var (path, type) = Artwork.Resolve(paths, entry.CoverImageFile);
                return Results.File(path, type);
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            { return Results.NotFound(); }
        });
        app.MapPost("/api/games/{id:guid}/launch", async (Guid id, HttpContext context, LibraryService library) =>
        {
            // No launch input model: executable paths, arguments and working directories
            // come exclusively from the trusted Windows database. Reject bodies outright.
            if (context.Request.ContentLength is > 0 || context.Request.Headers.ContainsKey("Transfer-Encoding"))
                return Results.BadRequest(new CommandResponse("This command accepts only a saved game ID, with no request body."));
            var entry = await library.GetAsync(id);
            if (entry is null || entry.Category != LibraryCategory.Games) return Results.NotFound();
            try { return Results.Ok(new CommandResponse(await library.LaunchAsync(id))); }
            catch (InvalidOperationException)
            {
                // Existing desktop errors may contain filesystem paths. Do not expose them on LAN.
                return Results.Conflict(new CommandResponse("Windows could not complete the launch. Check the saved game and bundled launch settings on the PC, then retry."));
            }
        });
        app.MapPost("/api/games/{id:guid}/stop", (Guid id, HttpContext context, LibraryService library) =>
            StopGame(id, context, library, force: false));
        app.MapPost("/api/games/{id:guid}/force-stop", (Guid id, HttpContext context, LibraryService library) =>
            StopGame(id, context, library, force: true));
        app.MapHub<GamesHub>("/hubs/games", options =>
        {
            // Native client uses header authentication, including the WebSocket handshake.
            options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
        });
    }
}
