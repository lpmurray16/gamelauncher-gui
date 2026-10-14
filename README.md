# Launchpad

A Windows-first, local-file game launcher built with **C# / .NET 10, Razor Pages, WinForms + WebView2, Entity Framework Core, and SQLite**.

**Windows project version: 1.7.7 (build/installer verification pending) · Android: 1.2 (release build complete; owner signing pending).** The previous Windows 1.6.0 Release publish and installer compilation succeeded; app/installer versions and SHA-256 were verified for that release. The Windows installer is unsigned. Launchpad Companion 1.2 compiled successfully with versionCode 2, but its APK must be signed with the existing release key before installation/distribution. No apps were installed or launched. See [release notes](docs/releases/1.6.0.md).

## Features

- **Touch controls:** swipe left/right across the library grid or empty-library area to move through All entries, Favorites and collections in displayed order, without wrapping. Sort/search and viewport restoration use the existing filter links. Hold a card’s artwork/title for about half a second to open its existing actions menu. Buttons/links, menus and dialogs are excluded; vertical scrolling and pinch zoom remain native. Mouse, keyboard and controller inputs retain their existing paths. Included in the 1.6.0 build; no Ally device verification performed; check scrolling versus swiping, release-click suppression, long holds, multi-touch and alternating touch/controller/mouse input after rebuilding.

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

## Artwork-first dashboard layout

The library dashboard uses a compact featured-art strip with a normal-width Play button, tighter heading/filter spacing, and an entry count beside sorting rather than a separate row. Category search names its scope; global search remains separate. Portrait cards retain their selection outline and use a denser layout in short landscape windows. Touch-capable browsers expose card Play/Options without requiring hover, and favorite buttons have larger hit areas. The sidebar is replaced by horizontal Games / Emulators / Tools navigation in the shared top bar, with a separate labeled Settings link. All / Favorites / custom collection filters remain beside the library, not in category navigation; manage collections using the plus button there. Editor and Settings content and launch behavior are unchanged.

This layout pass is source-reviewed only; no build or rendered/Ally verification has been performed. After rebuilding, check the first cover row, long titles, collection-menu overflow, touch actions, and controller focus on the actual handheld. Physical screen resolution is not necessarily the WebView's CSS viewport.

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

The application opens its own desktop window. The Razor UI remains on a random loopback port with desktop-shell authentication; it is not a normal browser site. The optional Android companion adds a separate LAN listener exposing only its API/SignalR routes, not the desktop UI. See [Android companion setup and security](docs/companion.md).

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

## Library health

**Settings → Library health** offers three separate, on-demand scans: artwork, tracking executables (Games only), and launch targets. Nothing scans at startup or changes/removes library entries automatically.

- Artwork checks missing cover/background assignments and the launcher's managed copies; it does not contact providers or decode images.
- Tracking checks explicit tracking paths or the direct launch `.exe` fallback. Shortcuts without a tracking target are flagged; ordinary direct executables do not need a separate override.
- Launch targets checks saved `.exe`, `.lnk`, and `.url` file presence without executing anything or resolving shortcut destinations. It does not validate arguments or working directories.
- Inaccessible/offline locations are reported as **Could not verify**, not confirmed missing. Reconnect drives before rescanning. Slow filesystem calls may take time.
- Results include **Fix in editor** links. Each scan replaces the previous report; return and rescan after edits. Results are not persisted.

Source-reviewed only; not built or exercised in the app.

## Bundled launch groups

In **Edit entry → Bundled launch group**, select up to eight existing library entries to launch alongside that entry. For Eden + BetterJoy, add both to the library, edit Eden, select BetterJoy, and save. Eden's existing Play buttons (including featured and search results) use the bundle; launching BetterJoy independently does not launch Eden unless you explicitly configure that direction too.

- The launch-order list includes the main entry. Drag a handle or use Up/Down to place helpers before or after it; Remove only unlinks that helper. Save the entry to apply changes. Existing bundles retain A–Z companions followed by main until reordered.
- Each row can wait 0–10 seconds **before** its launch request, with at most 10 seconds of configured delays across the bundle. Delays do not establish program readiness. The total cap leaves headroom under the existing companion request timeout, but Windows process startup can still take longer.
- A companion `.exe` and its delay are skipped when its exact executable path can be confirmed as already running. Detection is best effort; shortcuts, bootstrap launchers, and processes Windows cannot inspect may start again.
- Companion bundles are not traversed, so circular links cannot cause recursive launches. No programs are automatically closed.
- All launch files and working directories are checked before sending any launch requests. If Windows rejects a later request, remaining requests stop and the error identifies requests already sent. There is no process rollback.
- Uncheck a companion and save to unlink it. Removing a library entry removes its incoming/outgoing bundle links, never its local files.
- Request cancellation or host shutdown stops remaining delayed launch requests once observed, but does not undo launches already sent or interrupt a Windows process-start call in progress. A disconnected phone may not receive the partial-result summary.
- Bundle order and delays use an additive EF migration on startup. This change is source-reviewed only: no build, migration, launch-order, drag/drop, or handheld runtime verification has been performed.

## Controller navigation

- D-pad / left stick: move selection; **A** activates, **B** goes back, **X** opens card options, **Y** toggles favorite.
- **LB / RB:** move selection backward/forward by one-third of the current filtered/sorted entry list, rounded up. The count is recalculated per press, includes offscreen entries, and clamps at the first/last card. Scrolling is smooth; header controls are excluded. With no selected/remembered card, the first press selects the first entry.
- **L2 / R2 (LT / RT):** previous/next category, cycling Games → Emulators → Tools and wrapping. Uses the top bar's category navigation, only on dashboards—not Edit or Settings. Release the trigger before switching again.
- **Select / View / Back:** a quick tap toggles focus between top navigation and the previous content control (F1 does the same). Hold for roughly half a second to focus **global search**, retaining its text. The hold fires once, suppresses the tap action, and does not open the keyboard. Press A on the focused input (or F2) to request the Windows keyboard. Pending holds cancel on focus loss, controller disconnect, or keyboard/modal ownership; release controls before resuming. F11 toggles fullscreen.
- Category tabs and collection filters remain separate. Existing library swipes change All / Favorites / collection filters; LT/RT changes categories only on dashboard pages.
- Horizontal navigation and Select tap/hold are source-reviewed only; rendered handheld layout, controller timing and Windows-keyboard handoff still need owner verification.
- Input requires foreground focus and a neutral/released controller after navigation or reconnect. A jump/category switch does not also activate a card in the same frame.

## Android companion and remote game stop

The optional native Android app supports QR/manual pairing, multiple PCs, game covers, launch commands and live process status. See [setup/security](docs/companion.md) and the [Android README](src/GameLauncher.Companion/README.md). It is built separately in Android Studio and is **not included in the Windows installer**.

- Enable companion access on Windows and pair only over a trusted private LAN. HTTP is unencrypted; never expose the port to the internet. Protected commands require the paired bearer credential.
- **Companion Now playing tray:** running games appear in a floating bottom tray with their hero/background artwork and Stop controls, while the library stays alphabetical. Multiple active games have Previous/Next selectors; missing backgrounds use a placeholder. Rebuild both apps for the new background-image endpoint. Included in Windows 1.6.0 and the Android 1.2 release build; Android signing and rendered/device checks remain pending.
- Running tracked games offer **Stop game → Close normally**. A game may show a save/exit dialog on the PC. **Force stop instead…** requires a separate unsaved-progress confirmation.
- Stopping targets one exact executable match in the launcher's Windows session—not a process tree or bundled apps. Inaccessible/ambiguous matches are refused; Steam and Launchpad themselves are explicitly blocked. Administrator-run/protected games may require closing on the PC. Playing clears based on observed exit, not request acceptance.
- For shortcuts, Steam URLs or bootstrap launchers, set **Edit entry → Status tracking executable** to the actual game's `.exe`. Leave the shortcut as the launch target. A running-process picker and automatic executable discovery are not implemented.
- Both sides need the updated code for Stop controls; this Windows build does not rebuild or replace an existing Android APK. No remote keyboard/UAC approval is implemented.

## PC shutdown

Use the header power icon or open **Launchpad → Settings → PC power** to request shutdown after a **15-second cancellable countdown**. Confirmation names the PC. A countdown banner appears across Windows pages with a Cancel shutdown button.

For Android, first enable **Allow shutdown from Launchpad Companion** on that Windows page (off by default), then use **Shut down PC…** on the paired PC's page. Any authenticated paired phone can cancel a pending countdown, including a locally started one. Included in the Windows 1.6.0 installer and Android 1.2 release build (owner signing pending).

The countdown runs in Launchpad, which must remain open. Disconnecting/backgrounding the phone does not cancel it. Disabling remote permission, disabling companion access or revoking pairing cancels a pending remote countdown. Closing Launchpad cancels undispatched countdowns. After dispatch, cancellation through Launchpad is no longer available. Shutdown requests are not retried automatically; a lost connection does not prove power-off or cancellation.

At expiry, Launchpad invokes the fixed Windows shutdown action with no force flag. Windows policy or unsaved apps may block shutdown. Save your work first. Restart, sleep and Wake-on-LAN are not part of this increment. The owner reported Windows shutdown works. Release builds succeeded; the assistant did not execute shutdown or verify Android shutdown on a device.

## Artwork storage

Choose cover and hero/background images through SteamGridDB or the local image picker. SteamGridDB requires your own configured API key; no private key is bundled.

**Local selections are copied**, not linked, into `%LOCALAPPDATA%/GameLauncher/Artwork`, alongside downloaded images. The database stores the managed filename, not the source path. Moving/deleting the original after import does not break the artwork; editing the original does not update the copy. Replacing/clearing artwork removes the old managed copy, not the source image. Back up the Artwork folder together with the database, and keep unrelated originals outside this managed cache.

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
  Artwork/           # managed copies of imported and downloaded images
  companion-token.dat # protected pairing credential, when companion access is enabled
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

The solution contains the Windows app and `src/GameLauncher.Contracts` (wire DTOs only). `src/GameLauncher.Companion` is a separate native Kotlin/Jetpack Compose Android Gradle project in the same repository, opened in Android Studio—not a MAUI project or a .NET solution build dependency. Windows builds and the installer do not need Android tooling. Kotlin models mirror the documented JSON contracts rather than referencing the .NET assembly.

## v1.6.0 installer

Output: `artifacts/installer/GameLauncher-Setup-1.6.0-win-x64.exe`, with a companion `.sha256` checksum file. Only the setup executable is required for distribution.

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

Release packaging does not commit, push, or publish a GitHub release. Repository visibility and licensing remain the owner’s choice. Do not publish personal library data or API credentials.
