using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameLauncher.Companion;
using GameLauncher.Contracts;
using GameLauncher.Data;
using GameLauncher.Domain;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameLauncher.Services;

// Register once as a singleton and host that same instance. No LibraryService dependency.
public sealed class GameStatusMonitor(
    IDbContextFactory<LibraryDbContext> factory,
    IHubContext<GamesHub> hub,
    ILogger<GameStatusMonitor> logger) : BackgroundService
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, GameStatusChangedDto> _states = new();
    private readonly Dictionary<Guid, long> _startingAt = new();
    private readonly Channel<GameStatusChangedDto> _changes = Channel.CreateUnbounded<GameStatusChangedDto>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
    private long _revision;
    private long? _lastWarningAt;

    public GameStatus GetStatus(Guid id) => GetStatusSnapshot(id).Status;

    // REST snapshots should include Revision so clients can ignore older queued hub events.
    public GameStatusChangedDto GetStatusSnapshot(Guid id)
    {
        lock (_gate)
            return _states.TryGetValue(id, out var state)
                ? state : new GameStatusChangedDto(id, GameStatus.Stopped, _revision);
    }

    public static bool CanTrack(LibraryEntry entry) => TrackingPath(entry) is not null;

    // Call only after Windows accepts a launch request for a trackable saved entry.
    // No I/O here: a failed hub connection must never turn an accepted launch into a failure.
    public void MarkStarting(Guid id)
    {
        lock (_gate)
        {
            if (_states.TryGetValue(id, out var state) && state.Status == GameStatus.Running) return;
            _startingAt[id] = Stopwatch.GetTimestamp();
            SetStatusLocked(id, GameStatus.Starting);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var broadcaster = BroadcastAsync(stoppingToken);
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            do
            {
                try { await PollAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception error) { Warn(error, "Game status polling failed; retrying."); }
                // Expire launch grace even when a transient database/process failure prevented polling.
                lock (_gate) ExpireStartingLocked();
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            _changes.Writer.TryComplete();
            await broadcaster;
        }
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        long observedRevision;
        lock (_gate) observedRevision = _revision;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        var entries = await db.Entries.AsNoTracking().ToListAsync(cancellationToken);
        var paths = entries.ToDictionary(x => x.Id, TrackingPath);
        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = paths.Values.Where(x => x is not null)
            .Select(x => Path.GetFileNameWithoutExtension(x!)).Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Process[] processes;
            try { processes = Process.GetProcessesByName(name); }
            catch (Exception error) when (IsInspectionFailure(error))
            {
                Warn(error, "Some processes could not be inspected for game status.");
                continue;
            }
            try
            {
                foreach (var process in processes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        if (!process.HasExited && process.MainModule?.FileName is string path)
                            running.Add(Path.GetFullPath(path));
                    }
                    catch (Exception error) when (IsInspectionFailure(error))
                    {
                        // Elevated/protected processes and processes exiting mid-inspection are best effort.
                        // Never match just by name: that can mistake an unrelated executable for the game.
                        if (error is not InvalidOperationException)
                            Warn(error, "Some processes could not be inspected for game status.");
                    }
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }

        lock (_gate)
        {
            foreach (var (id, path) in paths)
            {
                if (path is not null && running.Contains(path))
                {
                    _startingAt.Remove(id);
                    SetStatusLocked(id, GameStatus.Running);
                }
                else if (!_startingAt.ContainsKey(id)) SetStatusLocked(id, GameStatus.Stopped);
                // An accepted launch gets its full grace period, not an immediate false Stopped.
            }
            foreach (var id in _states.Keys.Where(id => !paths.ContainsKey(id)
                         && _states[id].Revision <= observedRevision).ToArray())
            {
                _states.Remove(id);
                _startingAt.Remove(id);
            }
        }
    }

    private void ExpireStartingLocked()
    {
        foreach (var id in _startingAt.Where(x => Stopwatch.GetElapsedTime(x.Value) >= TimeSpan.FromSeconds(20))
                     .Select(x => x.Key).ToArray())
        {
            _startingAt.Remove(id);
            SetStatusLocked(id, GameStatus.Stopped);
        }
    }

    private void SetStatusLocked(Guid id, GameStatus status)
    {
        var previous = _states.TryGetValue(id, out var state) ? state.Status : GameStatus.Stopped;
        if (previous == status) return; // Do not announce initial Stopped for every library entry.
        var change = new GameStatusChangedDto(id, status, ++_revision);
        _states[id] = change;
        _changes.Writer.TryWrite(change); // Serialized with state mutation; single reader preserves order.
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var change in _changes.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await hub.Clients.All.SendAsync("GameStatusChanged", change, timeout.Token);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                catch (Exception error) { Warn(error, "Game status broadcast failed; clients should refresh their snapshot."); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private void Warn(Exception error, string message)
    {
        lock (_gate)
        {
            if (_lastWarningAt is long last && Stopwatch.GetElapsedTime(last) < TimeSpan.FromMinutes(1)) return;
            _lastWarningAt = Stopwatch.GetTimestamp();
        }
        logger.LogWarning(error, "{StatusWarning}", message);
    }

    private static bool IsInspectionFailure(Exception error) => error is
        Win32Exception or InvalidOperationException or NotSupportedException or UnauthorizedAccessException
        or IOException or System.Security.SecurityException or ArgumentException;

    private static string? TrackingPath(LibraryEntry entry)
    {
        // Do not check File.Exists here: existence is validated only when a nonempty override is saved.
        var path = entry.TrackingExecutablePath ?? entry.TargetPath;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return null;
            path = Path.GetFullPath(path);
            if (path.StartsWith(@"\\", StringComparison.Ordinal)
                || path.IndexOf(':', 2) >= 0 || !Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                return null;
            return path;
        }
        catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
        { return null; }
    }
}
