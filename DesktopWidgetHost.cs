using System.Drawing;
using System.Runtime.InteropServices;

/// <summary>Attaches a frameless window to the wallpaper-level WorkerW used by Explorer.</summary>
static class DesktopWidgetHost
{
    const int GwlStyle = -16;
    const long WsChild = 0x40000000L;
    const long WsPopup = unchecked((long)0x80000000);
    const long WsVisible = 0x10000000L;
    const uint SwpNoSize = 0x0001;
    const uint SwpNoZOrder = 0x0004;
    const uint SwpNoActivate = 0x0010;
    const uint SwpFrameChanged = 0x0020;
    const uint SwpShowWindow = 0x0040;
    const uint SmtoNormal = 0x0000;

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindWindow(string? className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);
    [DllImport("user32.dll", SetLastError = true)] static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetParent(IntPtr child, IntPtr parent);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", SetLastError = true)] static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", SetLastError = true)] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll", SetLastError = true)] static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);
    [DllImport("user32.dll")] static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    public static bool TryAttach(Form form, Point screenLocation)
    {
        var host = FindWallpaperHost();
        if (host == IntPtr.Zero) return false;

        var hwnd = form.Handle;
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        Marshal.SetLastPInvokeError(0);
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr((style & ~WsPopup) | WsChild | WsVisible));
        if (Marshal.GetLastPInvokeError() != 0) return false;

        Marshal.SetLastPInvokeError(0);
        SetParent(hwnd, host);
        if (Marshal.GetLastPInvokeError() != 0) return false;

        var point = new NativePoint { X = screenLocation.X, Y = screenLocation.Y };
        if (!ScreenToClient(host, ref point)) return false;
        return SetWindowPos(hwnd, IntPtr.Zero, point.X, point.Y, form.Width, form.Height,
            SwpNoZOrder | SwpNoActivate | SwpFrameChanged | SwpShowWindow);
    }

    public static Point Detach(Form form)
    {
        var hwnd = form.Handle;
        var screen = TryGetScreenPosition(hwnd, out var current) ? current : form.Location;
        var style = GetWindowLongPtr(hwnd, GwlStyle).ToInt64();
        Marshal.SetLastPInvokeError(0);
        SetWindowLongPtr(hwnd, GwlStyle, new IntPtr((style & ~WsChild) | WsPopup | WsVisible));
        SetParent(hwnd, IntPtr.Zero);
        SetWindowPos(hwnd, new IntPtr(-2), screen.X, screen.Y, form.Width, form.Height,
            SwpNoActivate | SwpFrameChanged | SwpShowWindow);
        SetForegroundWindow(hwnd);
        return screen;
    }

    public static bool IsAttached(Form form) => form.IsHandleCreated && GetParent(form.Handle) != IntPtr.Zero;

    public static bool TryGetScreenPosition(IntPtr hwnd, out Point point)
    {
        if (GetWindowRect(hwnd, out var rect)) { point = new Point(rect.Left, rect.Top); return true; }
        point = Point.Empty;
        return false;
    }

    static IntPtr FindWallpaperHost()
    {
        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return IntPtr.Zero;

        // Ask Explorer to ensure the wallpaper-level WorkerW exists. This shell message is undocumented.
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SmtoNormal, 1000, out _);
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), SmtoNormal, 1000, out _);

        IntPtr host = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            host = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            return false;
        }, IntPtr.Zero);
        return host != IntPtr.Zero ? host : progman;
    }
}
