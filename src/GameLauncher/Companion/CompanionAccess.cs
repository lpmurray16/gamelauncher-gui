using System.Globalization;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using GameLauncher.Contracts;
using GameLauncher.Services;
using Microsoft.Win32;

namespace GameLauncher.Companion;

// Per-Windows-user installation identity/preferences, matching DesktopPreferences.
// No credential is written to the registry or returned by the desktop page.
public sealed class CompanionAccess : IDisposable
{
    private const string RegistryPath = @"Software\GameLauncher\Companion";
    private readonly object _gate = new();
    private readonly string _tokenPath;
    private readonly Dictionary<string, Action> _connections = new();
    private string? _token;
    private string? _code;
    private DateTimeOffset _codeExpiry;
    private int _attempts;
    private long _generation;
    private bool _enabled;
    private int _port;
    private int? _listeningPort;
    private string? _listenerError;

    public Guid DeviceId { get; }
    public bool Enabled { get { lock (_gate) return _enabled; } }
    public int Port { get { lock (_gate) return _port; } }
    public int? ListeningPort { get { lock (_gate) return _listeningPort; } }
    public string? ListenerError { get { lock (_gate) return _listenerError; } }
    public bool IsActive { get { lock (_gate) return _enabled && _listeningPort.HasValue; } }
    public DeviceInfoDto Device => new(DeviceId, Environment.MachineName,
        typeof(CompanionAccess).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.1");

    public CompanionAccess(AppPaths paths)
    {
        _tokenPath = Path.Combine(paths.DataDirectory, "companion-token.dat");
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new InvalidOperationException("Companion preferences could not be opened.");
        if (key.GetValue("DeviceId") is string saved && Guid.TryParse(saved, out var id)) DeviceId = id;
        else
        {
            DeviceId = Guid.NewGuid();
            key.SetValue("DeviceId", DeviceId.ToString("D"));
        }
        _enabled = key.GetValue("Enabled") is int enabled && enabled == 1;
        _port = key.GetValue("Port") is int port && port is >= 1024 and <= 65535 ? port : 5180;
        // Do not read or generate pairing credentials unless the feature is enabled.
        if (_enabled)
        {
            try { LoadToken(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
            {
                _enabled = false;
                _listenerError = "Companion credentials could not be read. Revoke pairings and enable again. " + ex.Message;
            }
        }
    }

    public void SetListener(int? port, string? error = null)
    {
        lock (_gate) { _listeningPort = port; _listenerError = error; }
    }

    public void SaveSettings(bool enabled, int port)
    {
        if (port is < 1024 or > 65535) throw new ArgumentException("Choose a port from 1024 through 65535.");
        Action[] close = [];
        lock (_gate)
        {
            if (enabled && _token is null) LoadToken();
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
                ?? throw new InvalidOperationException("Companion preferences could not be opened.");
            key.SetValue("Port", port, RegistryValueKind.DWord);
            key.SetValue("Enabled", enabled ? 1 : 0, RegistryValueKind.DWord);
            _port = port;
            _enabled = enabled;
            if (!enabled)
            {
                _code = null;
                _generation++;
                close = _connections.Values.ToArray();
                _connections.Clear();
            }
        }
        foreach (var abort in close) abort();
    }

    public (string Code, DateTimeOffset Expires) OpenPairing()
    {
        lock (_gate)
        {
            if (!_enabled || !_listeningPort.HasValue)
                throw new InvalidOperationException("Enable the companion server and restart Game Launcher before pairing.");
            _code = RandomNumberGenerator.GetInt32(100000000).ToString("D8", CultureInfo.InvariantCulture);
            _codeExpiry = DateTimeOffset.UtcNow.AddMinutes(2);
            _attempts = 0;
            return (_code, _codeExpiry);
        }
    }

    public PairingResponse? Pair(string? code)
    {
        lock (_gate)
        {
            if (!_enabled || !_listeningPort.HasValue || _code is null || DateTimeOffset.UtcNow >= _codeExpiry)
                return null;
            // A global five-attempt limit cannot be bypassed by changing the source address.
            _attempts++;
            var valid = code is not null && Equal(code, _code);
            if (!valid || _token is null)
            {
                if (_attempts >= 5) _code = null;
                return null;
            }
            _code = null; // One successful exchange consumes the code.
            return new PairingResponse(Device, _token);
        }
    }

    public bool Authenticate(string authorization, out long generation)
    {
        lock (_gate)
        {
            generation = _generation;
            return _enabled && _listeningPort.HasValue && _token is not null &&
                authorization.StartsWith("Bearer ", StringComparison.Ordinal) && Equal(authorization[7..], _token);
        }
    }

    public bool RegisterConnection(string id, long generation, Action abort)
    {
        lock (_gate)
        {
            if (!_enabled || !_listeningPort.HasValue || generation != _generation) return false;
            _connections[id] = abort;
            return true;
        }
    }

    public void RemoveConnection(string id) { lock (_gate) _connections.Remove(id); }

    public void RevokePairings()
    {
        Action[] close;
        lock (_gate)
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            SaveToken(token); // On disk first; a failed write must not appear to revoke successfully.
            _token = token;
            _code = null;
            _generation++;
            close = _connections.Values.ToArray();
            _connections.Clear();
        }
        foreach (var abort in close) abort();
    }

    private void LoadToken()
    {
        if (!File.Exists(_tokenPath))
        {
            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            SaveToken(token);
            _token = token;
            return;
        }
        var bytes = File.ReadAllBytes(_tokenPath);
        var plain = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
        try
        {
            var token = Encoding.ASCII.GetString(plain);
            if (token.Length != 64 || !token.All(Uri.IsHexDigit))
                throw new CryptographicException("Invalid stored companion credential.");
            _token = token;
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }

    private void SaveToken(string token)
    {
        var bytes = Encoding.ASCII.GetBytes(token);
        try
        {
            var protectedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(_tokenPath + ".tmp", protectedBytes);
            File.Move(_tokenPath + ".tmp", _tokenPath, overwrite: true);
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static bool Equal(string supplied, string expected) => supplied.Length == expected.Length &&
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));

    // This MVP deliberately supports IPv4 LAN addresses, not internet hosts/forwarded headers.
    public static bool IsLocalAddress(IPAddress? address)
    {
        if (address is null) return false;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = address.GetAddressBytes();
        return b[0] == 127 || b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) ||
            (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }

    public static IReadOnlyList<string> GetLanAddresses() => NetworkInterface.GetAllNetworkInterfaces()
        .Where(x => x.OperationalStatus == OperationalStatus.Up && x.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(x => x.GetIPProperties().UnicastAddresses)
        .Select(x => x.Address).Where(x => IsLocalAddress(x) && !IPAddress.IsLoopback(x))
        .Select(x => x.ToString()).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();

    public void Dispose()
    {
        Action[] close;
        lock (_gate)
        {
            _enabled = false;
            close = _connections.Values.ToArray();
            _connections.Clear();
        }
        foreach (var abort in close) abort();
    }
}
