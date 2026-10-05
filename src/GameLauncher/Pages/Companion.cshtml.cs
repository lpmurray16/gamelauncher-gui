using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using GameLauncher.Companion;
using Microsoft.AspNetCore.Mvc;
using QRCoder;

namespace GameLauncher.Pages;

public sealed class CompanionModel(CompanionAccess access) : UiPageModel
{
    [BindProperty] public bool Enabled { get; set; }
    [BindProperty] public int Port { get; set; } = 5180;
    public CompanionAccess Access => access;
    public IReadOnlyList<CompanionAccess.LanAddress> Addresses { get; private set; } = [];
    public sealed record PairingQr(string Host, string Label, string Image);
    public IReadOnlyList<PairingQr> PairingQrs { get; private set; } = [];
    public string? PairingCode { get; private set; }
    public DateTimeOffset? PairingExpires { get; private set; }

    public void OnGet() => Load();

    private void Load()
    {
        Enabled = access.Enabled;
        Port = access.Port;
        try { Addresses = CompanionAccess.GetLanAddresses(); }
        catch (System.Net.NetworkInformation.NetworkInformationException ex) { ShowError(ex); }
    }

    public IActionResult OnPostSave()
    {
        if (!ModelState.IsValid) { Load(); return Page(); }
        try
        {
            access.SaveSettings(Enabled, Port);
            TempData["Notice"] = Enabled
                ? "Companion preferences saved. Restart Launchpad after enabling or changing the port."
                : "Companion access disabled and live connections closed. Restart to close the listening socket.";
            return RedirectToPage();
        }
        catch (Exception ex) when (IsExpected(ex) || ex is SecurityException or CryptographicException)
        { ShowError(ex); Load(); return Page(); }
    }

    public IActionResult OnPostPair()
    {
        ModelState.Clear();
        Load();
        try
        {
            var pairing = access.OpenPairing();
            PairingCode = pairing.Code;
            PairingExpires = pairing.Expires;
            // Use the bound port, not a saved preference awaiting restart. No token enters this payload.
            if (access.ListeningPort is int activePort)
            {
                PairingQrs = Addresses.Select(address =>
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        format = "gamelauncher-pair", formatVersion = 1,
                        host = address.Host, port = activePort, code = pairing.Code,
                        deviceId = access.DeviceId.ToString("D"), deviceName = access.Device.Name,
                        protocolVersion = access.Device.ProtocolVersion
                    });
                    var png = PngByteQRCodeHelper.GetQRCode(payload, QRCodeGenerator.ECCLevel.M, 6);
                    return new PairingQr(address.Host, address.Label, "data:image/png;base64," + Convert.ToBase64String(png));
                }).ToArray();
            }
        }
        catch (Exception ex) when (IsExpected(ex)) { ShowError(ex); }
        return Page();
    }

    public IActionResult OnPostRevoke()
    {
        ModelState.Clear();
        try
        {
            access.RevokePairings();
            TempData["Notice"] = "All phone pairings revoked and live connections closed. Pair each phone again to restore access.";
            return RedirectToPage();
        }
        catch (Exception ex) when (IsExpected(ex) || ex is CryptographicException)
        { ShowError(ex); Load(); return Page(); }
    }
}
