# Local Game Launcher

## Direction

Build a dependable, Windows-first desktop game launcher using established .NET tooling. Prioritize straightforward maintenance, useful documentation, and an easy installation experience for nontechnical friends.

## Chosen stack

- C# application logic.
- ASP.NET Core Razor Pages for the HTML/CSS interface.
- A lightweight WinForms desktop shell hosting WebView2.
- Entity Framework Core with SQLite for persistent library data.
- A self-contained .NET application packaged with Inno Setup into one distributable setup executable.

The single-file requirement is about distribution: users download one setup executable, install, and open a normal desktop shortcut. Installed application files, runtime files, the database, and cached artwork may exist separately. Users should not need developer tools or manual database setup. WebView2 availability must be checked and its prerequisite installation handled when needed.

## Product scope

### Games

- User-selected local folders, with optional recursive scanning.
- Discover executable and shortcut candidates: .exe, .lnk, and supported .url files.
- Review candidates before importing; scanning must never execute discovered files.
- Filter common noise such as uninstallers, redistributables, and crash reporters.
- Manual entry creation and editing.
- Launch targets with configurable arguments and working directories.
- Preserve custom titles, artwork, and launch configuration on rescans.
- Repair entries whose files have moved.

No Steam, Epic, or other storefront account/library integrations. Launching an existing local shortcut is allowed, including approved protocol URLs such as steam:// links; these still require the corresponding protocol handler or launcher on the user's machine.

### Emulators

- A separate Emulators section.
- Emulator profiles specifying executable, working directory, and argument template.
- Associate ROM folders and supported file extensions with profiles.
- Keep emulator applications distinct from the ROM entries they launch.
- Pass arguments safely without constructing arbitrary shell commands.

### Tools

- A separate Tools section using the shared library and launch infrastructure.

### Metadata and artwork

- Optional API-based game metadata and artwork lookup.
- Provider selection remains open pending authentication, terms, redistribution, and coverage review.
- Present candidate matches for ambiguous titles.
- Cache downloaded artwork where provider terms allow.
- Preserve manual overrides.
- Keep browsing and launching functional offline.
- Never embed private developer API credentials in the distributed application.

## Interface direction

Use Armoury Crate as inspiration for a polished, artwork-focused launcher, not its hardware-management or promotional features.

- Dark desktop dashboard.
- Navigation for Games, Emulators, Tools, and Settings.
- Search, add/scan controls, box-art grid, and a prominent Play action.
- Game details and configurable launch settings.
- Clear feedback for scans and downloads.

## Architecture and safety

- Keep domain and application services independent of Razor Pages and the desktop shell.
- Bind the embedded ASP.NET Core server to loopback only, using an available port.
- Protect privileged endpoints against unauthorized requests, cross-site requests, and untrusted navigation; loopback binding alone is not authentication.
- Keep filesystem scanning and launching in backend services.
- Validate stored launch targets and explicitly allow supported URL protocols.
- Separate a library entry's identity and metadata from its launch target.
- Coordinate server startup and shutdown with the desktop window lifecycle.
- Store writable application data under the user's local application-data directory by default.
- Uninstallation must never delete the user's games.
- Upgrades must preserve library data and settings.

## First implementation milestone

Deliver a vertical slice:

1. Start the local server and open the desktop window.
2. Select a folder and discover launch candidates.
3. Review and import selected candidates into SQLite.
4. Display saved library entries.
5. Launch a selected executable or supported shortcut.

Follow with metadata/artwork, emulator profiles, and richer organization. Verify actual Windows packaging and prerequisite handling before sharing a release.

## Development constraints

Do not create test suites for this project. The user handles builds and application launches and reports behavior or errors; the assistant implements changes and reviews code without running builds or launching the app unless explicitly requested. No build, runtime, or installer verification has been performed yet.
