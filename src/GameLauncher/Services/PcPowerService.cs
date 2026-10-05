using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Companion;
using GameLauncher.Contracts;
using Microsoft.Extensions.Hosting;
using Microsoft.Win32;

namespace GameLauncher.Services;

// A countdown belongs to this running launcher, not a persistent OS task.
public sealed class PcPowerService : BackgroundService
{
    private const string RegistryPath = @"Software\GameLauncher\Power";
    public const int CountdownSeconds = 15;
    private readonly CompanionAccess _access;
    private readonly object _gate = new();
    private bool _allowRemote;
    private bool _dispatching;
    private bool _stopped;
    private long? _started;
    private long? _remoteGeneration;
    private long _revision;
    private string _message = "No shutdown scheduled.";

    public PcPowerService(CompanionAccess access)
    {
        _access = access;
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath);
        _allowRemote = key?.GetValue("AllowRemoteShutdown") is int value && value == 1;
    }

    public bool AllowRemoteShutdown { get { lock (_gate) return _allowRemote; } }

    public void SetRemotePermission(bool allow)
    {
        lock (_gate)
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
                ?? throw new InvalidOperationException("Power preferences could not be opened.");
            key.SetValue("AllowRemoteShutdown", allow ? 1 : 0, RegistryValueKind.DWord);
            if (key.GetValue("AllowRemoteShutdown") is not int saved || saved != (allow ? 1 : 0))
                throw new InvalidOperationException("Power preferences could not be verified.");
            _allowRemote = allow;
            if (!allow && _remoteGeneration.HasValue && _started.HasValue)
                ClearPending("Remote shutdown cancelled because permission was disabled.");
            _revision++;
        }
    }

    public PowerStatusDto Status()
    {
        lock (_gate)
        {
            ValidatePending();
            return Snapshot();
        }
    }

    public PowerStatusDto Schedule(long? remoteGeneration = null)
    {
        lock (_gate)
        {
            if (_stopped) throw new InvalidOperationException("Launchpad is closing; no shutdown was scheduled.");
            if (remoteGeneration.HasValue && (!_allowRemote || !_access.IsGenerationCurrent(remoteGeneration.Value)))
                throw new InvalidOperationException("Remote shutdown is disabled or pairing changed. Enable it in Windows Settings → PC power.");
            ValidatePending();
            if (_dispatching) throw new InvalidOperationException("A shutdown request is already being sent to Windows.");
            // Repeated requests never reset or extend an existing countdown.
            if (!_started.HasValue)
            {
                _started = Stopwatch.GetTimestamp();
                _remoteGeneration = remoteGeneration;
                _message = "Shutdown scheduled. Save your work; you can cancel during the countdown.";
                _revision++;
            }
            return Snapshot();
        }
    }

    public PowerStatusDto Cancel()
    {
        lock (_gate)
        {
            if (_started.HasValue) ClearPending("Shutdown cancelled. No shutdown command was sent.");
            else throw new InvalidOperationException("No cancellable Launchpad countdown remains. If shutdown was requested, check Windows on the PC.");
            return Snapshot();
        }
    }

    private PowerStatusDto Snapshot() => new(_allowRemote && _access.IsActive, _started.HasValue,
        _started is long started ? Math.Max(0, (int)Math.Ceiling(CountdownSeconds - Stopwatch.GetElapsedTime(started).TotalSeconds)) : 0,
        _dispatching, _message, _revision);

    private void ClearPending(string message)
    {
        _started = null;
        _remoteGeneration = null;
        _message = message;
        _revision++;
    }

    private void ValidatePending()
    {
        if (_started.HasValue && _remoteGeneration is long generation &&
            (!_allowRemote || !_access.IsGenerationCurrent(generation)))
            ClearPending("Remote shutdown cancelled because companion access or pairing changed.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                Process? process = null;
                lock (_gate)
                {
                    ValidatePending();
                    if (_stopped || stoppingToken.IsCancellationRequested || _started is not long started ||
                        Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(CountdownSeconds)) continue;
                    // Avoid a delayed shutdown immediately after a long suspend/stall.
                    if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(CountdownSeconds + 15))
                    {
                        ClearPending("Shutdown cancelled because the countdown was interrupted. Request it again when ready.");
                        continue;
                    }
                    try
                    {
                        void Dispatch()
                        {
                            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "shutdown.exe"))
                            { UseShellExecute = false, CreateNoWindow = true };
                            // IMPORTANT: /t > 0 implicitly forces apps closed. Count down here,
                            // then use /s /t 0 without /f. Never accept command arguments from a client.
                            start.ArgumentList.Add("/s");
                            start.ArgumentList.Add("/t");
                            start.ArgumentList.Add("0");
                            process = Process.Start(start) ?? throw new InvalidOperationException("Shutdown command did not start.");
                        }
                        // Hold companion authorization through process creation so revocation cannot
                        // slip between the final authorization check and dispatch.
                        if (_remoteGeneration is long generation && !_access.RunIfGenerationCurrent(generation, Dispatch))
                        {
                            ClearPending("Remote shutdown cancelled because companion access or pairing changed.");
                            continue;
                        }
                        if (!_remoteGeneration.HasValue) Dispatch();
                        ClearPending("Shutdown requested from Windows. Apps may block it; power-off is not confirmed.");
                        _dispatching = true;
                    }
                    catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException or SecurityException)
                    {
                        ClearPending("Windows could not start shutdown. Check this account's shutdown permissions on the PC.");
                    }
                }
                if (process is null) continue;
                using (process)
                {
                    try
                    {
                        await process.WaitForExitAsync(stoppingToken).WaitAsync(TimeSpan.FromSeconds(5), stoppingToken);
                        if (process.ExitCode != 0)
                        {
                            lock (_gate)
                            {
                                _message = $"Windows did not accept shutdown (code {process.ExitCode}). Check the PC for blocked apps or permissions.";
                                _revision++;
                            }
                        }
                    }
                    catch (Exception error) when (error is TimeoutException or Win32Exception or InvalidOperationException)
                    {
                        lock (_gate)
                        {
                            _message = "Shutdown request was sent but its result is unavailable. Check Windows; power-off is not confirmed. No retry was made.";
                            _revision++;
                        }
                    }
                    finally { lock (_gate) { _dispatching = false; _revision++; } }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _stopped = true;
            if (_started.HasValue) ClearPending("Shutdown countdown cancelled because Launchpad closed.");
        }
        return base.StopAsync(cancellationToken);
    }
}
