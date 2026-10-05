using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;
using System.Windows.Forms;
using GameLauncher.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace GameLauncher.Desktop;

internal sealed class LauncherWindow : Form
{
    private readonly System.Windows.Forms.Timer _keyboardMonitor = new() { Interval = 250 };
    private bool _keyboardRequested;
    private int _keyboardRequestVersion;
    private readonly Uri _origin;
    private readonly string _sessionKey;
    private readonly AppPaths _paths;
    private readonly DesktopPreferences _desktop;
    private bool _fullscreen;
    private Rectangle _windowedBounds;
    private FormWindowState _windowedState;
    private Size _windowedMinimumSize;
    private readonly WebView2 _browser = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(12, 14, 18) };
    private readonly Label _status = new()
    {
        Dock = DockStyle.Fill, Text = "Opening your library…", TextAlign = ContentAlignment.MiddleCenter,
        ForeColor = Color.Gainsboro, BackColor = Color.FromArgb(12, 14, 18), Font = new Font("Segoe UI", 14)
    };

    public LauncherWindow(Uri origin, string sessionKey, AppPaths paths, DesktopPreferences desktop)
    {
        _origin = origin;
        _sessionKey = sessionKey;
        _paths = paths;
        _desktop = desktop;
        _keyboardMonitor.Tick += (_, _) => MonitorTouchKeyboard();
        Text = "Launchpad";
        StartPosition = FormStartPosition.CenterScreen;
        Size = new Size(1320, 880);
        MinimumSize = new Size(820, 600);
        BackColor = Color.FromArgb(12, 14, 18);
        using var iconStream = System.Reflection.Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("GameLauncher.icon.ico");
        if (iconStream is not null) Icon = new Icon(iconStream);
        Controls.Add(_browser);
        Controls.Add(_status);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTitleBarColors();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Reapply after Windows theme or accessibility settings change.
        const int WmThemeChanged = 0x031A;
        const int WmSettingChange = 0x001A;
        if (m.Msg is WmThemeChanged or WmSettingChange) ApplyTitleBarColors();
        const int WmDisplayChange = 0x007E;
        if (m.Msg == WmDisplayChange && _fullscreen) Bounds = Screen.FromHandle(Handle).Bounds;
    }

    private void ApplyTitleBarColors()
    {
        // Documented caption color attributes require Windows 11. Older Windows
        // keeps its native title bar, as does high-contrast mode.
        if (!IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        const int DwmUseImmersiveDarkMode = 20;
        const int DwmCaptionColor = 35;
        const int DwmTextColor = 36;
        bool highContrast = SystemInformation.HighContrast;
        int darkMode = highContrast ? 0 : 1;
        // COLORREF uses 0x00BBGGRR, not ARGB. Match site.css --bg and --text.
        int captionColor = highContrast ? -1 : ColorTranslator.ToWin32(Color.FromArgb(16, 17, 19));
        int textColor = highContrast ? -1 : ColorTranslator.ToWin32(Color.FromArgb(241, 240, 237));
        // Cosmetic best effort: a rejected attribute must not prevent startup.
        _ = DwmSetWindowAttribute(Handle, DwmUseImmersiveDarkMode, ref darkMode, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, DwmCaptionColor, ref captionColor, sizeof(int));
        _ = DwmSetWindowAttribute(Handle, DwmTextColor, ref textColor, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try { if (_desktop.LaunchFullscreen) SetFullscreen(true); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, "Could not read the fullscreen preference. Opening in windowed mode.\n\n" + error.Message,
                "Display preference", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
            // Only fixed UI commands from our authenticated, local top-level page.
            core.Settings.IsWebMessageEnabled = true;
            core.WebMessageReceived += async (_, args) =>
            {
                if (!IsLocal(args.Source) || !ContainsFocus) return;
                // Compare the entire JSON string; reject objects and all other commands.
                if (args.WebMessageAsJson == "\"keyboard.show\"") await ShowTouchKeyboardAsync();
                else if (args.WebMessageAsJson == "\"keyboard.hide\"") HideTouchKeyboard();
                else if (args.WebMessageAsJson == "\"window.fullscreen\"") SetFullscreen(!_fullscreen);
                else if (args.WebMessageAsJson == "\"window.close\"") Close();
            };
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
            core.NavigationStarting += (_, args) =>
            {
                if (!IsLocal(args.Uri)) args.Cancel = true;
                else if (_keyboardRequested) HideTouchKeyboard();
            };
            core.FrameNavigationStarting += (_, args) => { if (!IsLocal(args.Uri)) args.Cancel = true; };
            core.NewWindowRequested += (_, args) => args.Handled = true;
            core.DownloadStarting += (_, args) => args.Cancel = true;
            core.PermissionRequested += (_, args) => args.State = CoreWebView2PermissionState.Deny;
            core.NavigationCompleted += (_, args) =>
            {
                _status.Visible = !args.IsSuccess;
                if (args.IsSuccess) SendWindowState();
                if (!args.IsSuccess) _status.Text = "The library could not load. Close and reopen Launchpad.";
            };
            core.Navigate(_origin.AbsoluteUri);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            Program.TryLog(_paths, ex);
            MessageBox.Show(this,
                "Microsoft Edge WebView2 Runtime is required. Install the Evergreen Runtime from Microsoft's official WebView2 download page, then reopen Launchpad.\n\nhttps://developer.microsoft.com/microsoft-edge/webview2/",
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

    private void SetFullscreen(bool fullscreen)
    {
        if (_fullscreen == fullscreen) return;
        if (fullscreen)
        {
            var screen = Screen.FromHandle(Handle);
            _windowedState = WindowState;
            _windowedBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            _windowedMinimumSize = MinimumSize;
            _fullscreen = true;
            MinimumSize = Size.Empty;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.None;
            Bounds = screen.Bounds;
        }
        else
        {
            _fullscreen = false;
            WindowState = FormWindowState.Normal;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimumSize = _windowedMinimumSize;
            // Clamp to a remaining monitor if one was disconnected while fullscreen.
            var area = Screen.FromRectangle(_windowedBounds).WorkingArea;
            MinimumSize = new Size(Math.Min(_windowedMinimumSize.Width, area.Width),
                Math.Min(_windowedMinimumSize.Height, area.Height));
            var width = Math.Min(_windowedBounds.Width, area.Width);
            var height = Math.Min(_windowedBounds.Height, area.Height);
            Bounds = new Rectangle(Math.Clamp(_windowedBounds.X, area.Left, area.Right - width),
                Math.Clamp(_windowedBounds.Y, area.Top, area.Bottom - height), width, height);
            if (_windowedState == FormWindowState.Maximized) WindowState = FormWindowState.Maximized;
            ApplyTitleBarColors();
        }
        SendWindowState();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // When WebView2 owns the key, site.js sends the allowlisted window command.
        if (keyData == Keys.F11)
        {
            if ((msg.LParam.ToInt64() & (1L << 30)) == 0) SetFullscreen(!_fullscreen);
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void SendWindowState()
    {
        if (IsDisposed || _browser.CoreWebView2 is null || !IsLocal(_browser.Source?.AbsoluteUri ?? "")) return;
        _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "window", fullscreen = _fullscreen }));
    }

    private void SendKeyboardState(string state, string message = "")
    {
        if (IsDisposed || _browser.CoreWebView2 is null || !IsLocal(_browser.Source?.AbsoluteUri ?? "")) return;
        _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "keyboard", state, message }));
    }

    private async Task ShowTouchKeyboardAsync()
    {
        if (_keyboardRequested) return;
        _keyboardRequested = true;
        int version = ++_keyboardRequestVersion;
        SendKeyboardState("requested");
        try
        {
            await TouchKeyboardShell.ShowAsync(() => !IsDisposed &&
                version == _keyboardRequestVersion && ContainsFocus);
            // A shell toggle returning successfully still does not prove visibility.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                if (IsDisposed || version != _keyboardRequestVersion) return;
                if (TouchKeyboardShell.IsVisible())
                {
                    SendKeyboardState("shown", "Windows touch keyboard opened. Your selected Windows keyboard layout is retained.");
                    _keyboardMonitor.Start();
                    return;
                }
                await Task.Delay(250);
            }
            _keyboardRequested = false;
            SendKeyboardState("failed", "The Windows shell request completed, but no visible keyboard was detected. Controller navigation restored.");
        }
        catch (Exception error)
        {
            if (IsDisposed || version != _keyboardRequestVersion) return;
            _keyboardRequested = false;
            Program.TryLog(_paths, error);
            SendKeyboardState("failed", "Windows shell keyboard request failed: " + error.Message);
        }
    }

    private void MonitorTouchKeyboard()
    {
        try
        {
            if (TouchKeyboardShell.IsVisible()) return;
            _keyboardMonitor.Stop();
            _keyboardRequested = false;
            ++_keyboardRequestVersion;
            SendKeyboardState("hidden");
        }
        catch (Exception error)
        {
            // Keep navigation paused if keyboard visibility becomes unknown.
            _keyboardMonitor.Stop();
            Program.TryLog(_paths, error);
            SendKeyboardState("requested", "Cannot check Windows keyboard visibility. Close the keyboard, then press F2. " + error.Message);
        }
    }

    private void HideTouchKeyboard()
    {
        ++_keyboardRequestVersion;
        try
        {
            TouchKeyboardShell.Hide();
            // Closing can animate; resume controller input only after it disappears.
            if (TouchKeyboardShell.IsVisible()) _keyboardMonitor.Start();
            else
            {
                _keyboardMonitor.Stop();
                _keyboardRequested = false;
                SendKeyboardState("hidden");
            }
        }
        catch (Exception error)
        {
            Program.TryLog(_paths, error);
            SendKeyboardState("requested", "Could not dismiss the Windows keyboard: " + error.Message);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        ++_keyboardRequestVersion;
        _keyboardMonitor.Dispose();
        base.OnFormClosed(e);
    }

    private bool IsLocal(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == _origin.Scheme && uri.Host == _origin.Host && uri.Port == _origin.Port
        && string.IsNullOrEmpty(uri.UserInfo);
}
