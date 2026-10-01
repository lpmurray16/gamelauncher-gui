# Project conventions

- Windows-first C# launcher using Razor Pages, WinForms/WebView2, EF Core, SQLite.
- Do not create test suites. The user runs builds and launches the application; do not build, restore, test, or launch unless explicitly asked.
- Review source and clearly distinguish source review from runtime verification.
- Never scan by executing files. Never delete game files when removing library entries.
- Keep privileged operations behind authenticated, antiforgery-protected local endpoints.
- No storefront account/library integrations or bundled private API keys.
- Do not publish to GitHub, choose a license, or push without authorization.
