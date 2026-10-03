using System.ComponentModel;
using System.Runtime.InteropServices;

namespace PimGui.App;

// A single owned icon on the existing UI thread. No worker loop or Windows Forms
// application is needed. Explorer restart recovery keeps a hidden window reachable.
internal sealed class NativeTrayIcon : IDisposable
{
    private const uint CallbackMessage = 0x8000 + 0x51;
    private const nuint SubclassId = 0x505944;
    private readonly nint window;
    private readonly nint icon;
    private readonly uint taskbarCreated;
    private readonly SubclassProcedure procedure;
    private readonly Action restore;
    private readonly Action exit;
    private readonly Func<string> openLabel;
    private readonly Func<string> exitLabel;
    private bool visible;
    private bool disposed;

    public NativeTrayIcon(nint window, string iconPath, Action restore, Action exit, Func<string> openLabel, Func<string> exitLabel)
    {
        this.window = window; this.restore = restore; this.exit = exit;
        this.openLabel = openLabel; this.exitLabel = exitLabel;
        taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        icon = LoadImage(0, iconPath, 1, 0, 0, 0x10 | 0x40); // IMAGE_ICON, LOADFROMFILE, DEFAULTSIZE
        if (icon == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "The PyDeck notification icon could not be loaded.");
        procedure = OnWindowMessage;
        if (!SetWindowSubclass(window, procedure, SubclassId, 0))
        {
            DestroyIcon(icon);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The PyDeck notification icon could not connect to the window.");
        }
    }

    public bool Show()
    {
        if (disposed) return false;
        if (visible) return true;
        var data = Data();
        if (!ShellNotifyIcon(0, ref data)) return false; // NIM_ADD
        data.Version = 4;
        if (!ShellNotifyIcon(4, ref data)) // NIM_SETVERSION
        {
            ShellNotifyIcon(2, ref data);
            return false;
        }
        visible = true;
        return true;
    }

    public void Hide()
    {
        if (!visible) return;
        var data = Data();
        ShellNotifyIcon(2, ref data);
        visible = false;
    }

    private NotifyIconData Data() => new()
    {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = window, Id = 1,
        Flags = 0x1 | 0x2 | 0x4 | 0x80, Callback = CallbackMessage, Icon = icon,
        Tip = "PyDeck", Info = "", InfoTitle = ""
    };

    private nint OnWindowMessage(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference)
    {
        try
        {
            if (!disposed && taskbarCreated != 0 && message == taskbarCreated && visible)
            {
                visible = false;
                if (!Show()) restore();
            }
            else if (!disposed && message == CallbackMessage)
            {
                var notification = (uint)((nuint)lParam & 0xffff);
                if (notification is 0x400 or 0x401 or 0x203) restore(); // NIN_SELECT, NIN_KEYSELECT, double click
                else if (notification == 0x7b) ShowMenu(); // WM_CONTEXTMENU (mouse or keyboard)
                return 0;
            }
            else if (message == 0x82) Dispose(); // WM_NCDESTROY
        }
        catch
        {
            // Managed exceptions must not unwind through a native window procedure.
            // Restore the normal taskbar entry if a notification callback fails.
            try { restore(); } catch { }
        }
        return DefSubclassProc(hwnd, message, wParam, lParam);
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == 0) { restore(); return; }
        try
        {
            AppendMenu(menu, 0, 1, openLabel());
            AppendMenu(menu, 0x800, 0, null); // MF_SEPARATOR
            AppendMenu(menu, 0, 2, exitLabel());
            SetMenuDefaultItem(menu, 1, false);
            GetCursorPos(out var point);
            SetForegroundWindow(window);
            var command = TrackPopupMenuEx(menu, 0x100 | 0x2, point.X, point.Y, window, 0); // RETURNCMD, RIGHTBUTTON
            PostMessage(window, 0, 0, 0);
            if (command == 1) restore();
            else if (command == 2) exit();
            else if (visible) { var data = Data(); ShellNotifyIcon(3, ref data); }
        }
        finally { DestroyMenu(menu); }
    }

    public void Dispose()
    {
        if (disposed) return;
        Hide();
        disposed = true;
        RemoveWindowSubclass(window, procedure, SubclassId);
        DestroyIcon(icon);
        GC.KeepAlive(procedure);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint reference);
    [DllImport("comctl32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(nint window, SubclassProcedure callback, nuint id, nuint reference);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProcedure callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "RegisterWindowMessageW", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string message);
    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadImage(nint instance, string name, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetMenuDefaultItem(nint menu, uint item, [MarshalAs(UnmanagedType.Bool)] bool byPosition);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenuEx(nint menu, uint flags, int x, int y, nint window, nint parameters);
    [DllImport("user32.dll", EntryPoint = "PostMessageW")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nuint wParam, nint lParam);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(nint menu);
}
