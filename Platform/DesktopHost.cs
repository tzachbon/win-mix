using System.ComponentModel;
using System.Runtime.InteropServices;
using Mix.Core;

namespace Mix.Platform;

record HostCommand(string Kind, int Value = 0);
sealed class DesktopHost : IDisposable
{
    readonly Thread thread;
    readonly Gesture gesture = new();
    readonly Native.Hook keyboard, mouse;
    readonly Native.WndProc wndProc;
    readonly ManualResetEventSlim ready = new();
    readonly object boundsLock = new();
    Native.Rect bounds;
    nint hwnd, keyHook, mouseHook, trayIcon;
    uint taskbar;
    Exception? failure;
    int hovered = -1;
    public event Action<HostCommand>? Command;
    public DesktopHost()
    {
        keyboard = Keyboard; mouse = Mouse; wndProc = WindowProc;
        thread = new Thread(Run) { IsBackground = true, Name = "Mix input and tray" };
    }
    public void Start()
    {
        thread.Start();
        if (!ready.Wait(5000)) throw new TimeoutException("Input initialization timed out.");
        if (failure != null) throw failure;
    }
    public void SetBounds(Native.Rect rect) { lock (boundsLock) bounds = rect; }
    void Send(string kind, int value = 0) => Command?.Invoke(new(kind, value));
    void Reset()
    {
        gesture.InitializeHeld(Enumerable.Range(8,247).Where(k => (Native.GetAsyncKeyState(k) & 0x8000) != 0));
        hovered = -1;
        Send("Hide");
    }
    void Run()
    {
        try
        {
            var instance = Native.GetModuleHandleW(null);
            var cls = new Native.WindowClass { Name = "Mix.Native.Host", Proc = wndProc, Instance = instance };
            Native.RegisterClassW(ref cls);
            hwnd = Native.CreateWindowExW(0x80, cls.Name, "Mix background", 0,0,0,0,0,0,0,instance,0);
            if (hwnd == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            taskbar = Native.RegisterWindowMessageW("TaskbarCreated");
            trayIcon = Native.LoadImageW(0, Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"), 1, 0, 0, 0x10 | 0x40);
            Tray(0);
            Native.WTSRegisterSessionNotification(hwnd,0);
            Reset();
            keyHook = Native.SetWindowsHookExW(13,keyboard,instance,0);
            mouseHook = Native.SetWindowsHookExW(14,mouse,instance,0);
            if (keyHook == 0 || mouseHook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            ready.Set();
            while (Native.GetMessageW(out var message,0,0,0) > 0)
            { Native.TranslateMessage(ref message); Native.DispatchMessageW(ref message); }
        }
        catch (Exception ex) { failure = ex; ready.Set(); }
        finally
        {
            if (keyHook != 0) Native.UnhookWindowsHookEx(keyHook);
            if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
            if (hwnd != 0) { Tray(2); Native.WTSUnRegisterSessionNotification(hwnd); Native.DestroyWindow(hwnd); }
            if (trayIcon != 0) Native.DestroyIcon(trayIcon);
        }
    }
    void Tray(uint operation)
    {
        var icon = new Native.NotifyIcon { Size=(uint)Marshal.SizeOf<Native.NotifyIcon>(), Hwnd=hwnd, Id=1, Flags=7, Callback=0x8001,
            Icon=trayIcon != 0 ? trayIcon : Native.LoadIconW(0,(nint)32512), Tip="Win Mix · Hold Left Ctrl + Left Alt", Info="", InfoTitle="" };
        Native.Shell_NotifyIconW(operation,ref icon);
    }
    nint WindowProc(nint h,uint m,nuint w,nint l)
    {
        if (m == taskbar && taskbar != 0) Tray(0);
        if (m == 0x8002) { Native.PostQuitMessage(0); return 0; }
        if (m == 0x2B1 || (m == 0x218 && (w == 7 || w == 18))) { Reset(); Send("Refresh"); }
        if (m == 0x11) return 1;
        if (m == 0x16 && w != 0) Send("Exit");
        if (m == 0x8001)
        {
            if (l == 0x202 || l == 0x203) Send("Mixer");
            if (l == 0x205)
            {
                var menu=Native.CreatePopupMenu();
                Native.AppendMenuW(menu,0,1,"Mixer"); Native.AppendMenuW(menu,0,2,"Settings"); Native.AppendMenuW(menu,0,3,"Exit");
                Native.GetCursorPos(out var p); Native.SetForegroundWindow(h);
                uint selected=Native.TrackPopupMenu(menu,0x100|2,p.X,p.Y,0,h,0);
                Native.DestroyMenu(menu); Native.PostMessageW(h,0,0,0);
                if(selected>0) Send(selected==1?"Mixer":selected==2?"Settings":"Exit");
            }
        }
        return Native.DefWindowProcW(h,m,w,l);
    }
    nint Keyboard(int code,nuint message,nint data)
    {
        if(code<0) return Native.CallNextHookEx(keyHook,code,message,data);
        var key=Marshal.PtrToStructure<Native.Keyboard>(data);
        var result=gesture.Key((int)key.Key,message==0x100 || message==0x104,(key.Flags&0x10)!=0);
        if(result.Action is { } action)
        {
            if(action==GestureAction.Show) hovered=-1;
            Send(action.ToString());
        }
        return result.Suppress?1:Native.CallNextHookEx(keyHook,code,message,data);
    }
    nint Mouse(int code,nuint message,nint data)
    {
        if(code<0 || !gesture.Active) return Native.CallNextHookEx(mouseHook,code,message,data);
        var mouseData=Marshal.PtrToStructure<Native.Mouse>(data);
        if ((mouseData.Flags&1)!=0) return Native.CallNextHookEx(mouseHook,code,message,data);
        if(message==0x200)
        {
            Native.Rect r; lock(boundsLock) r=bounds;
            var p=mouseData.Point;
            if(p.X>=r.Left && p.X<r.Right && p.Y>=r.Top && p.Y<r.Bottom && r.Right>r.Left)
            {
                int selected=Math.Clamp((p.X-r.Left)*3/(r.Right-r.Left),0,2);
                if(selected!=hovered) { hovered=selected; Send("Select",selected); }
            }
        }
        if(message==0x20A)
        {
            int steps=gesture.Wheel(unchecked((short)(mouseData.Data>>16)));
            if(steps!=0) Send("Adjust",steps);
            return 1;
        }
        return Native.CallNextHookEx(mouseHook,code,message,data);
    }
    public void Dispose() { if(hwnd!=0) Native.PostMessageW(hwnd,0x8002,0,0); if(thread.IsAlive) thread.Join(2000); }
}
