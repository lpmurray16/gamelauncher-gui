# Launchpad Companion — Android

Native Kotlin / Jetpack Compose companion with QR and manual pairing. Open **this directory** in Android Studio; it is a separate Gradle project, not a .NET solution project. There is no fake library, bundled credential, discovery service, or test scaffold.

## Release 1.2

Version name **1.2**, version code **2** (previous release: 1.0/code 1). `assembleRelease --no-daemon` succeeded with JBR 21.0.11 and Gradle 8.13, including Kotlin compilation, Android resource packaging and release vital lint. The unsigned artifact is `../../artifacts/android/Launchpad-Companion-1.2-unsigned.apk`; it is **not installable until signed**. The owner will use Android Studio → Build → Generate Signed App Bundle / APK → APK, with the same release key as v1. No signing credentials were read or stored. No installation or device walkthrough was performed.

Includes the background-art Now playing tray, compact PC header/actions, Tips sections, explicit Launchpad/Launchpad Companion naming, and opt-in shutdown/countdown controls. See [release notes](../../docs/releases/1.6.0.md). Earlier source-only implementation notes below describe their original verification state; this release build supersedes their no-build claims, not their outstanding device checks.

The existing reused Gradle daemon failed to start AAPT2 with Windows error 740; a fresh single-use daemon (`--no-daemon`) succeeded with the standard Maven AAPT2. No permanent AAPT2 override or system changes were needed.

## Toolchain

- Android SDK Platform **36**, minimum device Android **8.0 / API 26**.
- Android Gradle Plugin **8.13.2**, Gradle **8.13**, Kotlin + Compose compiler plugin **2.3.10**; Java/Kotlin bytecode target **17**.
- **Gradle JDK must be 17–23** (Android Studio → Settings → Build, Execution, Deployment → Build Tools → Gradle → Gradle JDK). JetBrains Runtime 21 (e.g. `~/.jdks/jbr-21.x`) works; bytecode still targets 17 because no toolchain is pinned. For terminal builds, set `JAVA_HOME` to such a JDK first. Java 8 is too old for AGP, and JDK 24+ (including a bundled JDK 25) is **incompatible with this pinned Gradle 8.13**. JDK installation/configuration is left to the owner; nothing was installed here.
- SDK Build Tools 35.0.0 is AGP's default. Configure `sdk.dir` in your untracked `local.properties` if Studio does not find the SDK.
- When ready, owner may run `gradlew.bat assembleDebug` from this directory. APK: `app/build/outputs/apk/debug/app-debug.apk`. This command has **not** been run. Opening/syncing in Studio can download dependencies; no sync, restore, build, test, emulator, or app execution was performed during implementation.

The genuine `gradlew`, `gradlew.bat`, and wrapper JAR were downloaded from Gradle's **v8.13.0** source tag. Wrapper JAR SHA-256 was matched against Gradle's published checksum:

`81a82aaea5abcc8ff68b3dfcb58b3c3c429378efd98e7433460610fecd7ae45f`

The distribution SHA-256 is pinned in `gradle-wrapper.properties`. The Gradle distribution itself was not downloaded/executed.

### Physical phone workflow (owner action)

After selecting JDK 17 and SDK 36, connect your already-authorized Android phone by USB, select it in Android Studio's device selector, and use Run for the `app` configuration. Alternatively, install the owner-built debug APK using your normal trusted installation workflow. Keep the phone and PC on the same private network: USB debugging does not route companion traffic, and `127.0.0.1` on the phone means the phone, not Windows. Avoid guest Wi-Fi/client isolation. The assistant has not invoked Gradle, Android Studio sync, ADB, installation, or launch. Repeat the source-review checklist below on that physical phone before treating this as verified.

## Pair and use

1. On each Windows PC, open **Settings → Manage companion access**, enable LAN access, save, and restart the Windows launcher. Keep it running. Select the displayed IPv4 address on the phone's trusted Wi-Fi/LAN (not a VPN/virtual adapter). Choose **Generate pairing QR / code**; it expires after two minutes and is consumed after success or five failed attempts. Windows defaults to port 5180, but QR pairing transfers the actual active port (for manual entry, enter the active port shown). See [the parent setup/firewall guide](../../docs/companion.md); do not disable Windows Firewall.
2. On Android, choose **Add PC → Scan QR** and scan the QR displayed on the Windows Companion page for the selected network address. Camera permission is requested only when opening the scanner. Check the scanned **computer name and address**, explicitly check **I trust this network**, then tap **Pair**. Scanning never connects or pairs automatically. Alternatively, choose **Enter manually**, enter just the host/IPv4, the actual port displayed by Windows (no assumed port), and the code, then confirm the same trusted-network warning.
3. Each paired PC has its own connection, retry loop, library, and status. Tap a PC chip or swipe horizontally; its two-column cover grid scrolls vertically inside the pager.
4. **Connected** requires a matching device identity, successful authenticated REST, a connected SignalR hub, and a fresh REST snapshot after hub startup. The library stays alphabetical; running games appear in a floating **Now playing** tray at the bottom of their PC page and retain a green **Playing** badge on their grid card; Starting/Stopping show an orange **Starting…/Closing…** badge. `Stopped` only means "not running", so it is deliberately not labelled. Untrackable entries simply never show a badge (`canTrackStatus` is still parsed but not displayed).
5. Launch sends one request and shows the server message. For a tracked Running game the same button becomes **Stop game**, opening **Close normally** and **Force stop instead…**; Force stop requires a separate warning confirmation. Normal close may leave an exit/save dialog on the PC. Force stop targets only one exact game executable in the current Windows session, not Steam, child processes or bundled apps. Multiple/inaccessible candidates are refused. Shortcuts and Steam URLs need the actual game’s tracking executable set in Windows Edit; a process picker is not included. The client never invents Running/Stopped after a command; it waits for observed status. A lost response warns that the command may have taken effect; POSTs are not automatically retried. Rebuild both apps for these controls; older servers do not support stop.
6. **Reconnect** restarts one PC's session. Failures retry independently with exponential delay (roughly 1–30 seconds plus jitter). Returning to the app rechecks identity, reconnects, and refreshes each PC. Sessions stop while the Activity is paused; this is not a background control service.
7. Library edits refresh every 30 seconds while connected. Status events received during each GET are buffered and replayed over the REST snapshot, with per-game revision filtering within each hub connection. Epoch and connection guards discard obsolete callbacks. `LibraryChanged` is not required/used.
8. Offline PCs retain their last in-memory library with launch disabled and “Was playing” rather than a claim of live status. Library/covers are not persisted to disk. Covers load using the authenticated REST client and show a title placeholder if absent/unavailable.
9. **Remove** forgets that phone's connection/token only; it never deletes games. It does not revoke the token on Windows. Use the PC's token-revocation control when needed. Re-pairing the same device replaces the local connection; pairing a different device adds another page.

### Floating Now playing tray (source-only increment)

Running games no longer reorder the grid. Each PC page pins a wide, rounded tray over the bottom of its library, using the selected game's **hero/background artwork**, not its portrait cover. A dark overlay keeps the title and Stop button readable; background loading is indicated immediately, and missing/failed backgrounds use a neutral placeholder without hiding the game controls. Multiple active games get Previous/Next selectors and a position count, independent of PC swiping. Stopping entries remain visible until stopped; offline entries are labelled last-known and cannot be stopped remotely.

The tray shares the existing normal/force-stop confirmation with grid cards. Its measured height reserves scroll space so the final grid row can be brought above it. It disappears when no entries are Running/Stopping. Older Windows servers without `heroUrl` show the fallback; rebuild **both** apps to serve background images. New `NowPlayingTray.kt` owns this UI. No build, deployment or rendered device check was performed; verify phone font scaling, bottom navigation insets, last-row reachability, missing/slow artwork, multiple running games and independent PC switching.

### PC shutdown (source-only increment)

Rebuild Windows and Android. On your PC, open **Launchpad → Settings → PC power**, enable **Allow shutdown from Launchpad Companion** (off by default), and select **Save power preferences**. These are settings inside Launchpad, not Windows OS Settings. The paired PC page then offers **Shut down PC…**, with confirmation naming the PC and a **15-second** countdown plus **Cancel shutdown**. The countdown runs on the PC: phone disconnection/backgrounding does not cancel it. Closing Windows Launchpad before dispatch cancels it; disabling remote permission/companion access or revoking pairing cancels pending remote countdowns. Once the request reaches Windows, this app cannot cancel it or confirm power-off. Apps are not forced closed; save work first and check the PC if Windows blocks shutdown.

Power status polls while connected; power network calls time out after four seconds, POSTs are never retried automatically, and older Windows versions returning 404 hide power controls without breaking game access. Offline status is explicitly unconfirmed. A paired phone can cancel a locally started countdown too. No restart/Wake-on-LAN is included. No build, APK deployment or shutdown was executed by the assistant; start owner checks by scheduling then cancelling well before expiry.

### QR pairing details and fallback

- Scanning is QR-only, bundled and offline: JourneyApps ZXing Android Embedded **4.3.0**, with ZXing Core **3.4.1** transitively. No Google Play services, separate scanner app, cloud recognition, deep links, clipboard flow, or discovery is used. No barcode image is saved.
- If permission is denied, the scanner closes back to the pairing dialog with manual entry available. For permanent denial, enable Camera in Android app settings before scanning again. Back/cancellation, missing/busy camera, scanner-launch errors and invalid payloads leave manual entry available without submitting a code.
- Dialog visibility, fields, scanned name/expected device ID, warning and trust state use `rememberSaveable` across activity recreation, including rotation while scanning. Every scan result resets trust; editing any pairing field clears the scanned identity and trust. **Enter manually** also clears the code. Only tapping Pair can submit.
- Payload is a strict JSON object of at most **4096 characters**, with exactly these fields: string `format: "gamelauncher-pair"`; integer `formatVersion: 1`; string `host` (dotted-decimal private/link-local IPv4, no loopback); integer `port` (1024–65535, the active listener); string `code` (exactly eight ASCII digits); string `deviceId` (hyphenated GUID); string `deviceName` (at most 256 characters); integer `protocolVersion: 1`. Private/link-local ranges are 10/8, 172.16/12, 192.168/16 and 169.254/16. Unknown/duplicate/missing fields, wrong token types, decimal/exponent numbers and trailing data are rejected. No expiry is encoded or inferred: Windows decides whether the code is still valid; request a fresh QR/code if it expires.
- Before submitting a scanned code, the model fetches `/api/device` without credentials and compares its GUID with the QR's expected ID. A mismatch stops before `/api/pair`; the existing identity check on the pairing response also remains. This prevents accidental pairing to a different installation at a changed address, **not** active HTTP impersonation.

## LAN and credential boundary

This MVP is **HTTP on a trusted private LAN only**. Cleartext is explicitly enabled only in this Android app's manifest because arbitrary user-entered LAN hosts cannot be enumerated in a static domain allowlist. This setting permits cleartext at the platform level; application validation narrows every actual endpoint:

- Input permits only a simple hostname or IPv4 plus a separate valid port, never a URL, userinfo, path, fragment, or query.
- DNS results are filtered to private IPv4 (10/8, 172.16/12, 192.168/16), link-local (169.254/16), or loopback (127/8). IPv6/public-internet endpoints are not supported. A local result is pinned as the connection's literal IPv4 for REST and WebSocket; DNS changes are reconsidered only on reconnect.
- `/api/device` is fetched **without a token** and its GUID/protocol checked before sending any saved bearer token. A different identity blocks the connection until the user removes/re-pairs it.
- REST and WebSocket disable redirects and proxies. SignalR uses direct WebSockets with negotiation skipped, so negotiate JSON cannot redirect the token to another host. No Azure SignalR/transport fallback support is intended.
- Covers must match `/api/games/{same-game-id}/cover` with an optional simple `?v=` cache version. Remote cover URLs cannot receive credentials. Responses have an 8 MiB limit and image decoding is downsampled.
- Tokens are held only in memory or inside an AES-GCM-encrypted saved-PC document. The randomly generated key is Android Keystore-backed, ciphertext uses random IVs and authenticated context, and backup/device transfer is excluded. Encryption failure is shown, never downgraded to plaintext. A reset action handles damaged data or invalidated keys by removing local ciphertext/key; all PCs must then be paired again.

**Residual risk:** unauthenticated HTTP device identity is not cryptographic authentication. A malicious LAN participant can impersonate an ID, intercept a pairing code or bearer token, or alter traffic. Android Keystore protects tokens at rest, not on the wire. Never expose/forward the port to the internet, never use this over public/untrusted Wi-Fi, and do not interpret local-IP filtering as protection against a compromised LAN/router. TLS plus certificate pinning would be a separate future protocol change.

## Source map

- `Protocol.kt`: wire DTOs/JSON, strict GUID/host validation, pinned LAN client, cancellable bounded HTTP.
- `PcStore.kt`: Android Keystore AES-GCM persistence and reset.
- `PcSession.kt`: independent SignalR Java client, retry/lifecycle, REST-event merge, launch/cover requests.
- `CompanionModel.kt`: paired-PC collection, serialized persistence, pairing/removal/lifecycle.
- `PairingQr.kt`: bounded strict JSON/token/schema validation and QR-only non-loopback IPv4 validation.
- `MainActivity.kt`: Compose onboarding, pairing dialog, PC pager, two-column grids, connection/status controls.
- `Theme.kt`: Material 3 dark scheme mirroring the Windows `site.css` palette (`#101113` background, `#FF8A47` accent, soft `.launch` buttons, success-panel green for Playing).
- `res/mipmap-*`: adaptive launcher icon (rocket launchpad) with background `#070708`, a transparent foreground and an Android 13+ themed monochrome layer; `drawable-nodpi/brand_mark.png` is the same artwork for in-app use. `app/src/main/ic_launcher-playstore.png` is the 512px source copy and is not packaged.

## Dependency/API verification

Pinned direct versions were checked by retrieving their POMs from **Google Maven** (`https://dl.google.com/dl/android/maven2/`) or **Maven Central** (`https://repo.maven.apache.org/maven2/`): AGP 8.13.2; Kotlin/Compose plugins 2.3.10; Compose BOM 2025.10.00; Activity Compose 1.11.0; Lifecycle runtime-compose/viewmodel-ktx 2.9.4; Coroutines Android 1.10.2; SignalR 10.0.0; OkHttp 4.12.0. Compose UI, Foundation and Material3 are versioned by the verified BOM, not independently guessed.

Compatibility and client API references:

- [JourneyApps 4.3.0 Maven Central POM](https://repo.maven.apache.org/maven2/com/journeyapps/zxing-android-embedded/4.3.0/zxing-android-embedded-4.3.0.pom): exact published dependency and bundled ZXing Core version verified without a restore.
- [JourneyApps v4.3.0 README](https://github.com/journeyapps/zxing-android-embedded/blob/v4.3.0/README.md), [ScanContract](https://github.com/journeyapps/zxing-android-embedded/blob/v4.3.0/zxing-android-embedded/src/com/journeyapps/barcodescanner/ScanContract.java), [ScanOptions](https://github.com/journeyapps/zxing-android-embedded/blob/v4.3.0/zxing-android-embedded/src/com/journeyapps/barcodescanner/ScanOptions.java) and [CaptureManager](https://github.com/journeyapps/zxing-android-embedded/blob/v4.3.0/zxing-android-embedded/src/com/journeyapps/barcodescanner/CaptureManager.java): QR format selection, camera runtime permission/denial result, image-saving toggle and orientation APIs checked against release source. Library default API 24+ is below this app's API 26 minimum.

- [AGP 8.13 compatibility, including JDK 17, Gradle 8.13, API 36 support, and 8.13.2 Kotlin 2.3 support](https://developer.android.com/build/releases/agp-8-13-0-release-notes)
- [Gradle 8.13 Java compatibility: JDK 24+ unsupported](https://docs.gradle.org/8.13/userguide/compatibility.html)
- [Kotlin Gradle plugin compatibility](https://kotlinlang.org/docs/gradle-configure-project.html): 2.3.10 covers Gradle 8.13 / AGP 8.13.2.
- [Microsoft SignalR Java overview](https://learn.microsoft.com/en-us/aspnet/core/signalr/java-client?view=aspnetcore-10.0)
- [Exact v10.0.0 builder source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/SignalR/clients/java/signalr/core/src/main/java/com/microsoft/signalr/HttpHubConnectionBuilder.java)
- [Exact v10.0.0 connection source](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/SignalR/clients/java/signalr/core/src/main/java/com/microsoft/signalr/HubConnection.java)

The implementation uses the actual Java `start()`/`stop()` RxJava3 `Completable`, `onClosed`, `on`, `withHeader`, direct-WebSocket option and OkHttp builder callback. It does **not** assume the .NET client's automatic-reconnect APIs exist.

## Self-review / remaining verification

**QR increment is source-reviewed only.** No build, restore, tests, installation or launch was performed for it. Owner checks still needed: successful QR/manual pairing; wrong-device QR (no code submitted); malformed/oversized/unsupported QR; expired code; denied/permanently-denied camera permission; no camera; cancellation; rotation during scanner and after scan; restored confirmation fields/expected ID; explicit trust reset after scans/edits; narrow-screen/accessibility layout. Dependency/manifest merging and runtime camera behavior remain unverified.

**Owner-verified on a physical phone (2026-10-04):** Gradle sync/compile with JBR 21, app install/launch, pairing with one PC, and library (names) loading. Fixes found during that first run are listed in [`docs/WORKLOG.md`](../../docs/WORKLOG.md). Covers and the hidden-Stopped status UI were fixed afterwards and still need a re-run.

Source review traced pairing → encrypted persistence → identity-first reconnect → authenticated REST/hub → buffered status merge → Compose → launch, and independent removal/cancellation. Review fixes included retrying handshake timeouts (rather than treating them as lifecycle cancellation), guarding old callbacks, promptly marking a closed hub offline, preserving terminal pairing errors through cleanup, disabling automatic POST retry, and resetting invalidated Keystore keys. Still **not verified**: covers rendering, live Running/Stopped changes over SignalR, remote launch end-to-end, Keystore persistence across restarts, and Android lifecycle edge cases.

Owner walkthrough still needed: pair two PCs; wrong/expired code; encrypted persistence after restart; covers; live Running/Stopped changes; library edits during refresh; each PC offline/recovery independently; phone pause/resume; PC restart; wrong device at a saved address; revoked token; launch failure/unknown game; remove/re-pair; narrow screen/accessibility. No test suite was added. This Android handoff does not close the parent Windows/server task.
