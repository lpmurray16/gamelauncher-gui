using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using GameLauncher.Companion;
using System.Windows.Forms;
using GameLauncher.Data;
using GameLauncher.Desktop;
using GameLauncher.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;

namespace GameLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var instance = new Mutex(true, @"Local\GameLauncher.Desktop", out var firstInstance);
        if (!firstInstance)
        {
            MessageBox.Show("Launchpad is already running. Look for its open window.", "Launchpad");
            return;
        }

        WebApplication? server = null;
        AppPaths? paths = null;
        try
        {
            paths = new AppPaths();
            var sessionKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            using var companion = new CompanionAccess(paths);
            server = CreateServer(paths, sessionKey, companion);
            try { server.StartAsync().GetAwaiter().GetResult(); }
            catch (IOException ex) when (companion.ListeningPort.HasValue)
            {
                // A busy/blocked optional LAN port must not take down the desktop product.
                TryLog(paths, ex);
                server.DisposeAsync().AsTask().GetAwaiter().GetResult();
                companion.SetListener(null, "LAN listener could not start. Check the port/firewall, then restart. " + ex.Message);
                server = CreateServer(paths, sessionKey, companion, allowLan: false);
                server.StartAsync().GetAwaiter().GetResult();
            }
            var addresses = server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("The local server did not report its address.");
            var origin = new Uri(addresses.Addresses.Single(x => x.StartsWith("http://127.0.0.1:", StringComparison.Ordinal)));
            using var window = new LauncherWindow(origin, sessionKey, paths, server.Services.GetRequiredService<DesktopPreferences>());
            server.Services.GetRequiredService<FolderPicker>().Attach(window);
            Application.Run(window);
        }
        catch (Exception ex)
        {
            TryLog(paths, ex);
            MessageBox.Show($"Launchpad could not start.\n\n{ex.Message}\n\nDetails: {paths?.LogPath ?? "Application data folder unavailable"}",
                "Startup error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (server is not null)
            {
                try
                {
                    using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    server.StopAsync(shutdown.Token).GetAwaiter().GetResult();
                    server.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                catch (Exception ex) { TryLog(paths, ex); }
            }
        }
    }

    private static WebApplication CreateServer(AppPaths paths, string sessionKey, CompanionAccess companion, bool allowLan = true)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = [],
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            ContentRootPath = AppContext.BaseDirectory,
            EnvironmentName = Environments.Production
        });
        // A desktop app must not inherit externally configured network endpoints.
        builder.Configuration.Sources.Clear();
        var lanPort = allowLan && companion.Enabled ? (int?)companion.Port : null;
        if (allowLan) companion.SetListener(lanPort, companion.ListenerError);
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Listen(IPAddress.Loopback, 0);
            if (lanPort.HasValue) options.Listen(IPAddress.Any, lanPort.Value);
            options.AddServerHeader = false;
            options.Limits.MaxRequestBodySize = 262144;
        });
        builder.Services.AddRazorPages();
        builder.Services.AddSingleton(companion);
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddSignalR(options => options.MaximumReceiveMessageSize = 4096)
            .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddSingleton<PcPowerService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<PcPowerService>());
        builder.Services.AddSingleton<GameStatusMonitor>();
        builder.Services.AddHostedService(services => services.GetRequiredService<GameStatusMonitor>());
        builder.Services.Configure<FormOptions>(options => options.ValueCountLimit = 4096);
        builder.Services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "GameLauncher.Antiforgery";
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
        });
        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<ScannerService>();
        builder.Services.AddSingleton<FolderPicker>();
        builder.Services.AddSingleton<DesktopPreferences>();
        builder.Services.AddSingleton<BrowserLauncher>();
        builder.Services.AddSingleton<LibraryService>();
        builder.Services.AddSingleton<CollectionService>();
        builder.Services.AddSingleton<CredentialStore>();
        builder.Services.AddHttpClient("sgdb", client =>
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("GameLauncher/0.2 (local library)");
        }).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            MaxConnectionsPerServer = 4
        });
        builder.Services.AddSingleton<SteamGridDbClient>();
        builder.Services.AddScoped<ArtworkService>();
        builder.Services.AddDbContextFactory<LibraryDbContext>(options => options.UseSqlite(
            new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
            { DataSource = paths.DatabasePath, ForeignKeys = true }.ToString()));
        var app = builder.Build();
        try
        {
            using var scope = app.Services.CreateScope();
            using var db = scope.ServiceProvider.GetRequiredService<LibraryDbContext>();
            db.Database.Migrate();
            var artwork = scope.ServiceProvider.GetRequiredService<ArtworkService>();
            artwork.CleanOrphansAsync().GetAwaiter().GetResult();
        }
        catch
        {
            app.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        var expected = Encoding.ASCII.GetBytes(sessionKey);
        app.Use(async (context, next) =>
        {
            if (lanPort.HasValue && context.Connection.LocalPort == lanPort.Value)
            {
                await CompanionEndpoints.HandleLanRequest(context, next, companion);
                return;
            }
            var supplied = context.Request.Headers["X-Launcher-Session"].ToString();
            if (context.Request.Host.Host != "127.0.0.1" ||
                context.Request.Host.Port != context.Connection.LocalPort ||
                supplied.Length != sessionKey.Length ||
                !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(supplied), expected))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Open this library from the Launchpad desktop window.");
                return;
            }
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
                "connect-src 'self'; object-src 'none'; frame-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["Cache-Control"] = "no-store";
            await next(context);
        });
        app.UseExceptionHandler(handler => handler.Run(async context =>
        {
            var failure = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
            if (failure is not null) TryLog(paths, failure.Error);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs"))
            {
                await context.Response.WriteAsJsonAsync(new GameLauncher.Contracts.CommandResponse("The PC could not complete the request. Check Launchpad on Windows."));
                return;
            }
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.WriteAsync("<!doctype html><html><body><h1>That action could not be completed.</h1>" +
                "<p>Your game files have not been removed. Return to the library and try again.</p><a href='/'>Back to library</a></body></html>");
        }));
        app.MapGet("/assets/site.css", () => EmbeddedAsset("site.css", "text/css; charset=utf-8"));
        app.MapGet("/assets/site.js", () => EmbeddedAsset("site.js", "text/javascript; charset=utf-8"));
        app.MapGet("/icon.ico", () => EmbeddedAsset("icon.ico", "image/x-icon"));
        app.MapGet("/artwork/image/{fileName}", (string fileName, AppPaths artworkPaths) =>
        {
            try
            {
                var (fullPath, contentType) = Services.Artwork.Resolve(artworkPaths, fileName);
                return Results.File(fullPath, contentType);
            }
            catch (Exception) { return TypedResults.NotFound(); }
        });
        app.MapGet("/artwork/proxy", async (string url, SteamGridDbClient provider, HttpContext context) =>
        {
            try
            {
                var (stream, contentType) = await provider.DownloadAsync(url, context.RequestAborted);
                if (contentType is not ("image/png" or "image/jpeg" or "image/webp"))
                    return TypedResults.NotFound();
                return Results.Stream(stream, contentType);
            }
            catch (Exception ex)
            {
                TryLog(paths, ex);
                return TypedResults.NotFound();
            }
        });
        app.MapCompanion();
        app.MapRazorPages();
        return app;
    }

    private static IResult EmbeddedAsset(string name, string contentType)
    {
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("GameLauncher." + name);
        return stream is null ? Results.NotFound() : Results.Stream(stream, contentType);
    }

    internal static void TryLog(AppPaths? paths, Exception exception)
    {
        try
        {
            if (paths is not null)
                File.AppendAllText(paths.LogPath, $"{DateTimeOffset.Now:O} {exception}\n\n");
        }
        catch (Exception) { /* Reporting a startup failure must not cause another one. */ }
    }
}
