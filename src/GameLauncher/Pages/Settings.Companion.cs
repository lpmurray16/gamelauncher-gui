using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using GameLauncher.Companion;
using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace GameLauncher.Pages;

public sealed partial class SettingsModel
{
    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public int Port { get; set; } = 5180;
    public CompanionAccess Access => _companion;
    public IReadOnlyList<CompanionAccess.LanAddress> Addresses { get; private set; } = [];
    public sealed record PairingQr(string Host, string Label, string Image);
    public IReadOnlyList<PairingQr> PairingQrs { get; private set; } = [];
    public string? PairingCode { get; private set; }
    public DateTimeOffset? PairingExpires { get; private set; }

    private void LoadCompanionPreferences()
    {
        Enabled = _companion.Enabled;
        Port = _companion.Port;
        try { Addresses = CompanionAccess.GetLanAddresses(); }
        catch (System.Net.NetworkInformation.NetworkInformationException ex) { ShowError(ex); }
    }

    public IActionResult OnPostCompanionSave()
    {
        Tab = "companion";
        ModelState.Remove(nameof(ApiKey));
        if (!ModelState.IsValid) { LoadDesktopPreferences(); return Page(); }
        try
        {
            _companion.SaveSettings(Enabled, Port);
            TempData["Notice"] = Enabled
                ? "Companion preferences saved. Restart Launchpad after enabling or changing the port."
                : "Companion access disabled and live connections closed. Restart to close the listening socket.";
            return ReturnToSettings();
        }
        catch (Exception ex) when (IsExpected(ex) || ex is SecurityException or CryptographicException)
        { ShowError(ex); LoadDesktopPreferences(); return Page(); }
    }

    public IActionResult OnPostCompanionPair()
    {
        Tab = "companion";
        ModelState.Clear();
        LoadDesktopPreferences();
        try
        {
            var pairing = _companion.OpenPairing();
            PairingCode = pairing.Code;
            PairingExpires = pairing.Expires;
            // Use the bound port, not a saved preference awaiting restart. No token enters this payload.
            if (_companion.ListeningPort is int activePort)
            {
                PairingQrs = Addresses.Select(address =>
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        format = "gamelauncher-pair", formatVersion = 1,
                        host = address.Host, port = activePort, code = pairing.Code,
                        deviceId = _companion.DeviceId.ToString("D"), deviceName = _companion.Device.Name,
                        protocolVersion = _companion.Device.ProtocolVersion
                    });
                    var png = PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.M, 6);
                    return new PairingQr(address.Host, address.Label, "data:image/png;base64," + Convert.ToBase64String(png));
                }).ToArray();
            }
        }
        catch (Exception ex) when (IsExpected(ex)) { ShowError(ex); }
        return Page();
    }

    public IActionResult OnPostCompanionRevoke()
    {
        Tab = "companion";
        ModelState.Clear();
        try
        {
            _companion.RevokePairings();
            TempData["Notice"] = "All phone pairings revoked and live connections closed. Pair each phone again to restore access.";
            return ReturnToSettings();
        }
        catch (Exception ex) when (IsExpected(ex) || ex is CryptographicException)
        { ShowError(ex); LoadDesktopPreferences(); return Page(); }
    }
}
