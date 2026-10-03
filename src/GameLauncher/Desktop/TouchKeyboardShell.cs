using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace GameLauncher.Desktop;

// Desktop fallback for systems where InputPane.TryShow accepts but displays nothing.
// ITipInvocation is undocumented and may change with Windows updates.
// Invocation reference: https://superuser.com/a/1742459 (Julius Hardt; references torvin).
// Visibility uses the documented IFrameworkInputPane.Location API.
internal static class TouchKeyboardShell
{
    public static bool IsVisible()
    {
        var pane = new FrameworkInputPane();
        try
        {
            Marshal.ThrowExceptionForHR(((IFrameworkInputPane)pane).Location(out var bounds));
            return bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
        }
        finally { Marshal.ReleaseComObject(pane); }
    }

    public static async Task ShowAsync(Func<bool> canContinue)
    {
        if (!canContinue() || IsVisible()) return;
        try { Toggle(); }
        catch (COMException error) when (error.HResult == unchecked((int)0x80040154))
        {
            // Bootstrap only Microsoft's fixed system executable; never search PATH.
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                "microsoft shared", "ink", "TabTip.exe");
            if (!File.Exists(path)) throw new FileNotFoundException("Windows touch keyboard executable is missing.", path);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            await Task.Delay(1000);
            if (canContinue() && !IsVisible()) Toggle();
        }
    }

    public static void Hide()
    {
        if (IsVisible()) Toggle();
    }

    private static void Toggle()
    {
        var host = new InputHost();
        try { ((ITipInvocation)host).Toggle(GetDesktopWindow()); }
        finally { Marshal.ReleaseComObject(host); }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [ComImport, Guid("4CE576FA-83DC-4F88-951C-9D0782B4E376")]
    private class InputHost { }

    [ComImport, Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr window);
    }

    [ComImport, Guid("D5120AA3-46BA-44C5-822D-CA8092C1FC72")]
    private class FrameworkInputPane { }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [ComImport, Guid("5752238B-24F0-495A-82F1-2FD593056796"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFrameworkInputPane
    {
        [PreserveSig] int Advise(IntPtr window, IntPtr handler, out uint cookie);
        [PreserveSig] int AdviseWithHWND(IntPtr window, IntPtr handler, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int Location(out NativeRect bounds);
    }
}
