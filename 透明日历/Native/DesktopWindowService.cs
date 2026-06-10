using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TransparentCalendar.Native;

public static class DesktopWindowService
{
    private const int WmSpawnWorker = 0x052C;

    public static bool AttachToDesktop(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            var progman = FindWindow("Progman", null);
            if (progman != IntPtr.Zero)
            {
                SendMessageTimeout(progman, WmSpawnWorker, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);
            }

            var worker = FindDesktopWorker();
            if (worker == IntPtr.Zero)
            {
                worker = progman;
            }

            if (worker == IntPtr.Zero)
            {
                return false;
            }

            SetParent(hwnd, worker);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr FindDesktopWorker()
    {
        var result = IntPtr.Zero;
        EnumWindows((topHandle, _) =>
        {
            var shellView = FindWindowEx(topHandle, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView == IntPtr.Zero)
            {
                return true;
            }

            result = FindWindowEx(IntPtr.Zero, topHandle, "WorkerW", null);
            return result == IntPtr.Zero;
        }, IntPtr.Zero);

        return result;
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        int flags,
        int timeout,
        out IntPtr result);
}
