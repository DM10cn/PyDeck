using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PimGui.App;

// OverlappedPresenter can retain a non-client resize strip after hiding its title
// bar. Keep the resize style, but let XAML paint the whole restored window.
// https://learn.microsoft.com/windows/win32/winmsg/wm-nccalcsize
// https://learn.microsoft.com/windows/win32/dwm/customframe
internal sealed class NativeBorderlessFrame : IDisposable
{
    private const nuint SubclassId = 0x50594246; // Separate from NativeTrayIcon.
    private readonly nint window;
    private readonly SubclassProcedure procedure;
    private bool disposed;

    public NativeBorderlessFrame(nint window)
    {
        this.window = window;
        procedure = OnWindowMessage;
        if (!SetWindowSubclass(window, procedure, SubclassId, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The borderless window frame could not be initialized.");
        // Recalculate non-client geometry without moving, resizing or activating.
        if (!SetWindowPos(window, 0, 0, 0, 0, 0, 0x20 | 0x10 | 0x04 | 0x02 | 0x01))
        {
            var error = Marshal.GetLastWin32Error();
            Dispose();
            throw new Win32Exception(error, "The borderless window frame could not be refreshed.");
        }
    }

    private nint OnWindowMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        try
        {
            if (!disposed && message == 0x83 && lParam != 0) // WM_NCCALCSIZE
            {
                // Both RECT and NCCALCSIZE_PARAMS begin with the proposed window
                // rectangle. Returning it unchanged removes the native top strip.
                if (IsZoomed(hwnd))
                {
                    // A maximized resizable HWND extends beyond the work area.
                    // Keep application content inside the monitor's usable bounds.
                    var bounds = Marshal.PtrToStructure<Rect>(lParam);
                    var monitor = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>() };
                    if (GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor))
                    {
                        bounds.Left = Math.Max(bounds.Left, monitor.Work.Left);
                        bounds.Top = Math.Max(bounds.Top, monitor.Work.Top);
                        bounds.Right = Math.Min(bounds.Right, monitor.Work.Right);
                        bounds.Bottom = Math.Min(bounds.Bottom, monitor.Work.Bottom);
                        Marshal.StructureToPtr(bounds, lParam, false);
                    }
                }
                return 0;
            }
            if (!disposed && message == 0x84 && !IsZoomed(hwnd) && (GetWindowLong(hwnd, -16) & 0x00040000) != 0) // WM_NCHITTEST, WS_THICKFRAME
            {
                var hit = ResizeHitTest(hwnd, lParam);
                if (hit != 0) return hit;
            }
            if (message == 0x82) Dispose(); // WM_NCDESTROY
        }
        catch
        {
            // Never let a managed exception unwind through the native procedure.
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private static nint ResizeHitTest(nint hwnd, nint coordinates)
    {
        if (!GetWindowRect(hwnd, out var bounds)) return 0;
        var x = unchecked((short)((long)coordinates & 0xffff));
        var y = unchecked((short)(((long)coordinates >> 16) & 0xffff));
        if (x < bounds.Left || x >= bounds.Right || y < bounds.Top || y >= bounds.Bottom) return 0;
        var dpi = GetDpiForWindow(hwnd);
        var padding = GetSystemMetricsForDpi(92, dpi); // SM_CXPADDEDBORDER
        var horizontal = GetSystemMetricsForDpi(32, dpi) + padding; // SM_CXSIZEFRAME
        var vertical = GetSystemMetricsForDpi(33, dpi) + padding; // SM_CYSIZEFRAME
        var left = x < bounds.Left + horizontal;
        var right = x >= bounds.Right - horizontal;
        var top = y < bounds.Top + vertical;
        var bottom = y >= bounds.Bottom - vertical;
        // HTTOPLEFT/RIGHT, HTBOTTOMLEFT/RIGHT, HTLEFT/RIGHT, HTTOP/BOTTOM.
        if (top) return left ? 13 : right ? 14 : 12;
        if (bottom) return left ? 16 : right ? 17 : 15;
        return left ? 10 : right ? 11 : 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        RemoveWindowSubclass(window, procedure, SubclassId);
        GC.KeepAlive(procedure);
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProcedure callback, nuint id, nuint reference);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsZoomed(nint window);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(nint window, out Rect rectangle);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
