# Android companion — manual-pairing MVP

## Status and scope

This change adds an opt-in Windows LAN API and a native Kotlin/Jetpack Compose Android companion. **Current progress, fixes, and next steps are tracked in [`WORKLOG.md`](WORKLOG.md)** — read it first when picking this work up. As of 2026-10-04 the owner has built/run the Android app on a physical phone and paired it with one PC; most of the acceptance checklist below is still open. Source review is not runtime verification.

The existing Windows project has not moved. Windows builds remain independent of Android tooling; `RunBuild.bat`, `RunLocalProj.bat`, and the installer retain their original entry points. The solution contains Windows + Contracts, not the Android Gradle project. The installer dependency-notice collector filters NuGet package entries instead of treating the new project reference as a package.

MVP scope: manual pairing, multiple independent PCs, covers, launching existing Games entries (including their configured launch companions), and live process status. Automatic discovery and game stopping are deferred. No cloud service, storefront integration, arbitrary remote executable path, router configuration, or background Windows service is introduced.

## First connection

1. Close the old Windows launcher, build with `RunBuild.bat`, then launch with `RunLocalProj.bat`. Back up the library data before exercising the new migration.
2. Open **Settings → Manage companion access**. Enable LAN access, keep port **5180** unless it is occupied, save, and restart the launcher.
3. The page shows the active port and local IPv4 addresses. Choose the address on the same trusted network as your phone, not a VPN/virtual-adapter address.
4. If needed, explicitly install the narrowly scoped firewall rule described below. Do not disable Windows Firewall.
5. Open `src/GameLauncher.Companion` in Android Studio. Follow its README for the pinned SDK/JDK/Gradle configuration, then run the app on your already-authorized physical phone.
6. Generate a code on the Windows companion page. In Android, add the computer using its host/IP, port, and the code. Codes expire after two minutes; one successful exchange or five failed attempts consumes the code. Generate a separate fresh code for each phone.
7. Repeat on a second PC. Swipe between computer pages; each owns its credentials, connection and game state.

The desktop must remain running. The phone talks over LAN even when USB is attached for debugging. Guest Wi-Fi/AP isolation and VPN routing can prevent communication. IPv6-only networks and public Internet addresses are not supported by this first version.

## Firewall — explicit administrator action only

The installer remains per-user/non-elevated and does not add firewall exceptions. In **administrator PowerShell**, from the repository root, substitute the executable you actually run:

```powershell
.\scripts\Set-CompanionFirewall.ps1 -ProgramPath 'C:\full\path\to\GameLauncher.exe' -Port 5180
```

For the normal per-user installation, the executable is under `%LOCALAPPDATA%\Programs\GameLauncher\GameLauncher.exe`. Debug builds use `src\GameLauncher\bin\Debug\net10.0-windows\GameLauncher.exe`. Do not permit the dotnet host or all executables. The script checks the path, requires the GameLauncher.exe filename, and scopes its rule to **inbound TCP + exact executable + exact port + Private network profile + LocalSubnet + blocked edge traversal**. It reads the created rule back. `-WhatIf` previews without changing anything.

Other existing rules remain untouched: a broad Windows-generated allowance can still permit traffic independently. Inspect/remove such broad GameLauncher rules yourself. The app additionally rejects non-local IPv4 source addresses, but a private source address is not authentication and does not establish the Windows network profile.

Before moving/uninstalling the app or changing ports, remove the explicit rule with its original path/port:

```powershell
.\scripts\Set-CompanionFirewall.ps1 -ProgramPath 'C:\full\path\to\GameLauncher.exe' -Port 5180 -Remove
```

The non-elevated uninstaller cannot remove an administrator-created firewall rule. It still preserves library data/preferences. Neither script is executed automatically by this change.

## Security boundaries

- Desktop Razor UI stays on its session-authenticated, random loopback port with antiforgery on forms. Companion requests do not receive access to those pages, browsing, artwork proxies, scanning, or library mutation handlers.
- An optional IPv4 LAN listener uses the same process and service instances. A LAN bind failure falls back to desktop-only operation and is shown on the companion page. Port/enable changes need restart; disabling rejects new requests and aborts existing hub connections immediately.
- Device identity persists per Windows user under `HKCU\Software\GameLauncher\Companion`, alongside enabled/port preferences. It is a GUID, not an IP. Different user profiles are different launcher installations.
- Each Windows installation owns a random 256-bit bearer token protected at rest with Windows DPAPI CurrentUser in `%LOCALAPPDATA%\GameLauncher\companion-token.dat`. Only a locally generated, short-lived code can exchange it. Pairing attempts are limited installation-wide to prevent changing IP addresses from bypassing the code's attempt budget.
- Device identity is public on the LAN. Games, covers, commands and SignalR require the bearer header. Tokens in URLs are not accepted. No cookie auth/CORS is enabled for the API; browser Origin requests are rejected. Bearer-authenticated native commands intentionally do not use Razor antiforgery cookies. Local pairing/settings/revocation forms still do.
- Phones paired to one installation share its installation credential in this MVP. **Revoke all phone pairings** rotates it and aborts active hubs; it does not revoke another PC's credential. Removing a PC in Android is local removal, not server-side revocation.
- **LAN HTTP is unencrypted.** Codes, tokens, artwork, and commands can be intercepted; identity checks are not cryptographic server authentication. Use only a trusted network. Never forward the port, enable UPnP, or treat this as an Internet service. TLS/server identity pinning is future hardening, not an implemented guarantee.
- The Android app permits cleartext for manually entered LAN hosts because Android's static domain configuration cannot enumerate arbitrary user IPs. Its networking layer restricts destinations and disables credential-bearing redirects. This permission applies to this application only, not other apps/device-wide networking.
- Remote launch accepts only a GUID in the route and no request body. The existing `LibraryService.LaunchAsync` resolves paths/arguments/bundles from SQLite and uses existing validation and `UseShellExecute` behavior. API errors do not disclose Windows filesystem paths.

## Process tracking

The monitor samples configured executable identities approximately every two seconds and emits only actual status changes. It recognizes matching processes regardless of who launched them. `.exe` targets work by exact full-path matching; shortcuts and bootstrap launchers need the optional **tracking executable** in the local Edit entry form. That path is tracking metadata only, never a new launch command. No process is executed to scan/detect it.

Starting is a temporary state following accepted launch requests, not proof the game reached its menu. Running requires a path-matched process. Windows can deny inspection of elevated/protected processes; tracking remains best effort. Untracked shortcuts can launch but cannot reliably report Running. Multiple running games are supported. No processes are forcibly terminated; the stop endpoint returns 501 and the phone does not offer it.

Library data remains authoritative on each PC. REST returns complete game state; SignalR carries changes. The Android client refreshes after reconnect/resume and periodically refreshes library metadata rather than maintaining a copy of SQLite.

## Wire contract v1

Shared C# DTOs: `src/GameLauncher.Contracts/CompanionContracts.cs`. Kotlin mirrors the JSON contract; no .NET reference from Android. Property names are camelCase, IDs are GUID strings, statuses are **string** values `Stopped`, `Starting`, `Running`, `Stopping` (not integer enum ordinals).

| Request | Auth | Result |
|---|---|---|
| `GET /api/device` | None | `{deviceId,name,version,protocolVersion:1}` |
| `POST /api/pair` body `{code}` | One-time code | `{device:{...},token}`; 401 if invalid/expired |
| `GET /api/games` | Bearer | Array of `{id,name,coverUrl,status,canTrackStatus}`; Games category only |
| `GET /api/games/{id}/cover` | Bearer | Stored image; 404 when absent |
| `POST /api/games/{id}/launch` no body | Bearer | `{message}`; 404 unknown/non-game, 409 rejected launch, 400 unexpected body |
| `POST /api/games/{id}/stop` | Bearer | 501 `{message}`; not implemented |
| `/hubs/games` | Bearer, native WebSocket | `GameStatusChanged` payload `{gameId,status,revision}` |

`coverUrl` is relative to the paired PC, includes a cache-version value, and is null without artwork. It never contains an absolute Windows path. SignalR revision increases during a Windows process lifetime and resets on restart. It orders events, not persistent database versions. Clients resynchronize on a new connection. The hub has no command methods. Metadata/library changes use periodic REST refresh in this MVP; no `LibraryChanged` event is promised.

## Discovery investigation / next increment

Use standard DNS-SD/mDNS rather than an IP scan or custom broadcast protocol. Android supplies [`NsdManager`](https://developer.android.com/develop/connectivity/wifi/use-nsd); Windows 10+ supplies [`DnsServiceRegister`](https://learn.microsoft.com/en-us/windows/win32/api/windns/nf-windns-dnsserviceregister) with asynchronous registration/deregistration tied to process lifetime.

A future `_gamelauncher._tcp` service can advertise the configured port, device ID and protocol version (never a code/token). Discovery should feed the existing manual pairing flow and match stored PCs by GUID before updating a host. Windows interop lifetime handling, multi-interface choice, Android lifecycle/local-network permissions, and mDNS firewall behavior need physical-device verification, so they are deliberately not added to this MVP. For DHCP changes today, re-pair at the new address; Android should update the same device ID instead of creating duplicate computers.

## Owner-run acceptance checklist

These checks have not been executed by the assistant. No test suite is added.

- Build Windows via RunBuild and Android via Android Studio; check a desktop-only machine does not request Android workloads.
- Start with an existing library: migration preserves games/artwork/bundles/collections. Desktop scanning/editing/launching still works; removing entries leaves files untouched.
- LAN disabled: no LAN listener. Enabled: desktop UI remains inaccessible from phone, `/api/device` works, protected endpoints reject missing/invalid bearer tokens. Expired/consumed codes fail, and a fresh code succeeds.
- Unknown GUID gives 404; launch body is rejected; launching a known game honors the same stored options and bundles as Windows UI.
- Start/exit a tracked game from Windows, Android and manually; observe Playing/reordering and exit transitions without repeated unchanged events. Check untracked shortcut explanation and overridden tracking paths.
- Pair two PCs and confirm their libraries/status/artwork remain independent. Remove/reconnect one without affecting the other.
- Turn Wi-Fi off/on, close/reopen Windows, background/resume Android, and revoke pairings while connected. Offline computers stay saved, launch does not auto-retry, and REST refresh recovers missed status events.
- Change a PC address and pair it again: same GUID updates its endpoint. Change port/restart; verify old firewall rule removal and new restricted rule.
- Occupy the LAN port before startup: desktop should still open and explain the listener failure.
- Verify Android grid vertical scrolling and horizontal PC swiping on the physical phone. Check missing covers, long titles, multiple running games, and no paired computers.
- Rebuild installer and separately verify installation/upgrade/uninstall. Existing previous-release packaging success does not verify this changeset.
