# Game Launcher

A Windows-first, local-file game launcher built with **C# / .NET 10, Razor Pages, WinForms + WebView2, Entity Framework Core, and SQLite**.

**Status:** v1.0.0 Windows x64 Release publish and Inno Setup installer compilation succeeded. The packaged application, installation, upgrade, uninstall, and missing-WebView2 flow still require a manual walkthrough. The project owner normally runs builds and launches; this release packaging was explicitly requested. No test suite is included or planned.

## First slice

- Games, Emulators, and Tools sections with search and favorites.
- Add, edit, and remove library entries without deleting their files.
- Native folder picker; recursive scanning with review before import.
- `.exe`, `.lnk`, and a restricted set of `.url` targets.
- Duplicate-safe imports that preserve existing entries.
- Configurable executable arguments and working directory.
- Launch history (last launch-request time, not playtime tracking).
- Local SQLite persistence with an initial EF Core migration.
- Dark desktop interface, scan feedback, and transient bottom notifications.

Also included: SteamGridDB/local artwork, custom collections, global search, bundled launches, keyboard/controller navigation, fullscreen and optional Windows startup. The header browser shortcut saves a browser executable for this launcher only. Bundled launch options are collapsed by default in Add/Edit entry.

Emulators currently means **emulator executables**, not ROM profiles. Metadata descriptions, saved scan roots, and ROM association remain future work. There is no sample library or fabricated artwork.

## Build and run — for the project owner

Requirements:

- Windows with the .NET 10 SDK (`global.json` requests 10.0.303 or a newer compatible .NET 10 feature band).
- Microsoft Edge WebView2 Evergreen Runtime. Development launches report its download location if missing; the release installer handles this prerequisite.
- Network access for the first NuGet restore, which `dotnet build` performs automatically.

From the repository root:

```sh
dotnet build GameLauncher.slnx
dotnet run --project src/GameLauncher/GameLauncher.csproj --no-build
```

For double-click shortcuts, use the batch files in the repository root:

- **RunBuild.bat** — builds the Debug configuration and keeps the console open so you can read the result.
- **RunLocalProj.bat** — launches the existing Debug executable without building or restoring. Run RunBuild first if no build exists or you want to include source changes.

Both scripts resolve paths relative to their own location, so they can also be called from another working directory.

Alternatively, open the solution in a .NET-10-capable IDE and select GameLauncher as the startup project.

The application opens its own desktop window. There is no normal browser URL to use: the embedded server is bound to a random loopback port and only requests authenticated by the desktop shell are accepted.

### First manual walkthrough

1. Open Games and choose **Scan a folder**.
2. Browse to a small, known folder containing a trusted executable or shortcut.
3. Scan, review the results, select an entry, and import it.
4. Edit its display name or launch options if needed.
5. Launch it, then close and reopen Game Launcher to check persistence.
6. Try a favorite, a search, and removal. Removal must leave the target file untouched.

Share build errors or screenshots/behavior to iterate. Release publishing has been executed successfully; this manual runtime walkthrough has not been performed by the assistant.

## Launch target rules

- `.exe`: launched directly through Windows with stored arguments and working directory; no intermediate command shell is constructed.
- `.lnk`: handed to Windows, preserving the shortcut's own target and arguments. Only import shortcuts you trust: shortcuts can invoke scripts, shells, and other applications. Their working-directory behavior can be governed by the shortcut itself.
- `.url`: parsed and validated at import and again at launch. Only `steam://rungameid/<numeric ID>` is allowed in this first version. Steam must be installed to handle the link; there is no Steam account or library integration.
- Custom arguments are accepted only for `.exe` entries.
- UNC/device paths are not accepted. Scans skip reparse points/junctions, system entries, and common helper/uninstaller names, but manual review is still necessary.
- Scans are bounded to 2,000 candidates, 100,000 visited entries, and two minutes. Select a smaller folder if a limit is reached. Results are temporary and expire after 30 minutes or application shutdown.
- Titles are initially inferred from filenames; metadata matching is not implemented yet.

## Bundled launch groups

In **Edit entry → Bundled launch group**, select up to eight existing library entries to launch alongside that entry. For Eden + BetterJoy, add both to the library, edit Eden, select BetterJoy, and save. Eden's existing Play buttons (including featured and search results) use the bundle; launching BetterJoy independently does not launch Eden unless you explicitly configure that direction too.

- Direct companions start in A–Z order, then the primary entry. Each uses its own saved arguments and working directory. Requests are sent back-to-back; the launcher does not wait for controller initialization or program readiness.
- A companion `.exe` is skipped when its exact executable path can be confirmed as already running. Detection is best effort; shortcuts, bootstrap launchers, and processes Windows cannot inspect may start again.
- Companion bundles are not traversed, so circular links cannot cause recursive launches. No programs are automatically closed.
- All launch files and working directories are checked before sending any launch requests. If Windows rejects a later request, remaining requests stop and the error identifies requests already sent. There is no process rollback.
- Uncheck a companion and save to unlink it. Removing a library entry removes its incoming/outgoing bundle links, never its local files.
- Bundle links are persisted by an additive EF migration on startup. This feature and migration are source-reviewed, not yet build/runtime-verified by the assistant.

## Startup and fullscreen

**Settings → Startup & display** has independent, opt-in settings:

- **Start with Windows** registers the current `GameLauncher.exe` in the current user's `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` key as `GameLauncher`. It starts at sign-in, not before login, and requires no administrator privileges. Windows Startup apps can independently disable the registration; the app does not override that choice. Save again after moving the executable, and disable it before deleting a standalone copy. The installer removes startup registration on uninstall only when it points to that installed copy.
- **Launch in fullscreen** is stored as `LaunchFullscreen` under `HKCU\Software\GameLauncher` and applies on the next app launch. This registry preference is separate from the library data-directory backup. It opens borderless on the current monitor without changing resolution or forcing an always-on-top window.
- **F11** or the shared top-bar fullscreen button switches temporarily; it does not overwrite the saved launch preference. The top-bar close button or **Alt+F4** exits. Windowed bounds/maximized state are restored when leaving fullscreen within the session.

Source-reviewed only: verify save/reopen, F11/controller access to window controls, startup registration/removal and actual sign-in launch on the owner's machine. No startup registration is made merely by installing these source changes.

## Data and privacy

Local application data lives under:

```text
%LOCALAPPDATA%/GameLauncher/
  library.db
  WebView2/
  startup.log       # only created when an error is logged
```

Database migrations run on startup. Close the application before backing up its data. Do not delete the database to work around schema changes: add migrations instead. The initial snapshot/migration were authored without running EF tooling and need validation during the first owner-run launch.

The local server uses a random per-process authentication header supplied by WebView2, host validation, Razor antiforgery tokens on POST forms, and a restrictive content security policy. The webview blocks external navigation, popups, downloads, and permission requests. This protects the local control surface; it is **not** a sandbox for games or trusted shortcuts you choose to launch.

No API credentials or storefront integrations are included. Runtime data, environment files, build output, and IDE state are ignored by Git.

## Project layout

```text
src/GameLauncher/
  Program.cs                 Desktop entry point and local server lifecycle
  Desktop/                   Native WebView2 window
  Domain/                    Library entry model
  Data/                      EF Core context and initial migration
  Services/                  Scanning, launching, persistence, folder picker
  Pages/                     Razor Pages interface
  wwwroot/                   Embedded CSS and JavaScript
  Properties/PublishProfiles/Windows.pubxml
```

One application project keeps the initial implementation simple. Services are separated from page handlers and the window so they can evolve without coupling game management to the UI.

## v1.0 installer

Output: `artifacts/installer/GameLauncher-Setup-1.0.0-win-x64.exe`, with a companion `.sha256` checksum file. Only the setup executable is required for distribution.

- Windows x64 package; Setup requires Windows 10 22H2 or newer.
- Per-user installation to `%LOCALAPPDATA%/Programs/GameLauncher`, without requesting administrator rights.
- Self-contained .NET publish: recipients do not need the .NET SDK or runtime separately. Native dependencies may be extracted at runtime.
- Start menu shortcut, optional desktop shortcut, and Windows Installed apps uninstaller. Startup at sign-in remains opt-in in the application.
- A Microsoft-signed WebView2 Evergreen bootstrapper is bundled and run only if the shared runtime is absent. **Internet is required for that step.** Setup rechecks runtime registration before installing the app; a failed prerequisite shows an error and stops installation.
- Close the launcher before installing/upgrading or uninstalling. A stable installer AppId supports future in-place upgrades; keep it unchanged.
- Uninstall removes installed files and its matching startup registration, but preserves `%LOCALAPPDATA%/GameLauncher`, browser/display preferences, and the shared WebView2 Runtime. It never removes games or user-selected executables.
- Third-party dependency notices ship alongside the app. No project license was selected and no private API keys or personal library files are packaged.
- This release is **unsigned**; Windows may show an unknown-publisher/SmartScreen warning. No signing certificate is configured.

### Rebuild the installer

Prerequisites: .NET 10 SDK and Inno Setup 6 (the first successful build used 6.7.3). From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Build-Installer.ps1
```

The script discovers Inno Setup in standard locations (or accepts `-IsccPath`), cleans only the generated x64 publish directory, publishes Release, verifies the Microsoft signature on the cached/downloaded WebView2 bootstrapper, collects dependency notices, compiles Setup, and writes SHA-256. The project `<Version>` drives the installer version. Build output and prerequisites stay under ignored `artifacts/`.

### Release verification boundary

Verified: Release publish, installer compilation, output version and SHA-256 generation, Microsoft bootstrapper signature. Not yet verified: clean-machine installation, rendered packaged UI, WebView2 installation on a machine without it, upgrade/persistence, startup cleanup, or uninstall. Before broad distribution, run the installer on a clean Windows account/VM, launch it, configure a browser and entry, reinstall to check preservation, then uninstall and confirm the library and game files remain.


## GitHub

The repository is initialized locally on `main`. No remote repository has been created or linked and nothing has been pushed. Repository visibility and licensing remain the owner's choice. Do not publish personal library data or API credentials.
