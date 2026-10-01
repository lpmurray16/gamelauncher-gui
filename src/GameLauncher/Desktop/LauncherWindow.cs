using System.Drawing;
using System.IO;
using System.Windows.Forms;
using GameLauncher.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GameLauncher.Desktop;

internal sealed class LauncherWindow : Form
{
    private readonly Uri _origin;
    private readonly string _sessionKey;
    private readonly AppPaths _paths;
    private readonly WebView2 _browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(12, 14, 18) };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill, Text = "Opening your library…", TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.Gainsboro, BackColor = Color.FromArgb(12, 14, 18), Font = new Font("Segoe UI", 14)
    };

    public LauncherWindow(Uri origin, string sessionKey, AppPaths paths)
    {
        _origin = origin;
        _sessionKey = sessionKey;
        _paths = paths;
        Text = "Game Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1320, 880);
        MinimumSize = new Size(820, 600);
        BackColor = Color.FromArgb(12, 14, 18);
        Controls.Add(_browser);
        Controls.Add(_status);
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: _paths.WebViewDirectory);
            if (IsDisposed) return;
            await _browser.EnsureCoreWebView2Async(environment);
            if (IsDisposed) return;
            var core = _browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            core.WebResourceRequested += (_, args) =>
            {
                if (IsLocal(args.Request.Uri))
                    args.Request.Headers.SetHeader("X-Launcher-Session", _sessionKey);
                else
                    args.Response = environment.CreateWebResourceResponse(
                        new MemoryStream(), 403, "Blocked", "Content-Type: text/plain");
            };
            core.NavigationStarting += (_, args) => { if (!IsLocal(args.Uri)) args.Cancel = true; };
            core.FrameNavigationStarting += (_, args) => { if (!IsLocal(args.Uri)) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.NavigationCompleted += (_, args) =>
            {
                _status.Visible = !args.IsSuccess;
                if (!args.IsSuccess) _status.Text = "The library could not load. Close and reopen Game Launcher.";
            };
            core.Navigate(_origin.AbsoluteUri);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            Program.TryLog(_paths, ex);
            MessageBox.Show(this,
                "Microsoft Edge WebView2 Runtime is required. Install the Evergreen Runtime from Microsoft's official WebView2 download page, then reopen Game Launcher.\n\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
                "WebView2 Runtime needed", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            Program.TryLog(_paths, ex);
            MessageBox.Show(this, $"The desktop view could not start.\n\n{ex.Message}\n\nDetails: {_paths.LogPath}",
                "Desktop error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
        }
    }

    private bool IsLocal(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == _origin.Scheme && uri.Host == _origin.Host && uri.Port == _origin.Port
        && string.IsNullOrEmpty(uri.UserInfo);
}
