using System.Runtime.InteropServices;

namespace Mix.Platform;

static class Native
{
    public delegate nint Hook(int code, nuint message, nint data);
    public delegate nint WndProc(nint hwnd, uint message, nuint wParam, nint lParam);
    public delegate nint SubclassProc(nint hwnd, uint message, nuint wParam, nint lParam, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct Message { public nint Hwnd; public uint Id; public nuint WParam; public nint LParam; public uint Time; public Point Point; public uint Private; }
    [StructLayout(LayoutKind.Sequential)] public struct Keyboard { public uint Key, Scan, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential)] public struct Mouse { public Point Point; public uint Data, Flags, Time; public nuint Extra; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct WindowClass { public uint Style; public WndProc Proc; public int ClassExtra, WindowExtra; public nint Instance, Icon, Cursor, Background; public string? Menu; public string Name; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] public struct NotifyIcon
    {
        public uint Size; public nint Hwnd; public uint Id, Flags, Callback; public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst=64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public nint Balloon;
    }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public uint Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern ushort RegisterClassW(ref WindowClass value);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern nint CreateWindowExW(uint ex, string cls, string name, uint style, int x,int y,int w,int h,nint parent,nint menu,nint instance,nint param);
    [DllImport("user32.dll")] public static extern nint DefWindowProcW(nint h,uint m,nuint w,nint l);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern int GetMessageW(out Message m,nint h,uint min,uint max);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref Message m);
    [DllImport("user32.dll")] public static extern nint DispatchMessageW(ref Message m);
    [DllImport("user32.dll")] public static extern bool PostMessageW(nint h,uint m,nuint w,nint l);
    [DllImport("user32.dll")] public static extern void PostQuitMessage(int code);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern nint GetModuleHandleW(string? module);
    [DllImport("user32.dll", SetLastError=true)] public static extern nint SetWindowsHookExW(int id,Hook callback,nint module,uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] public static extern nint CallNextHookEx(nint hook,int code,nuint message,nint data);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] public static extern nint MonitorFromPoint(Point p,uint flags);
    [DllImport("user32.dll")] public static extern bool GetMonitorInfoW(nint monitor,ref MonitorInfo info);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint monitor,int type,out uint x,out uint y);
    [DllImport("user32.dll")] public static extern nint GetWindowLongPtrW(nint h,int index);
    [DllImport("user32.dll")] public static extern nint SetWindowLongPtrW(nint h,int index,nint value);
    [DllImport("user32.dll")] public static extern nint LoadIconW(nint instance,nint name);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern nint LoadImageW(nint instance,string name,uint type,int width,int height,uint flags);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet=CharSet.Unicode)] public static extern bool Shell_NotifyIconW(uint operation,ref NotifyIcon icon);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] public static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern bool AppendMenuW(nint menu,uint flags,nuint id,string text);
    [DllImport("user32.dll")] public static extern uint TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint hwnd,nint rect);
    [DllImport("user32.dll")] public static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("wtsapi32.dll")] public static extern bool WTSRegisterSessionNotification(nint hwnd,uint flags);
    [DllImport("wtsapi32.dll")] public static extern bool WTSUnRegisterSessionNotification(nint hwnd);
    [DllImport("comctl32.dll")] public static extern bool SetWindowSubclass(nint hwnd,SubclassProc callback,nuint id,nuint data);
    [DllImport("comctl32.dll")] public static extern nint DefSubclassProc(nint hwnd,uint message,nuint w,nint l);
}
