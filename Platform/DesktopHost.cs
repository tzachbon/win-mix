using System.ComponentModel;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Mix.Core;

namespace Mix.Platform;

record HostCommand(string Kind, int Value = 0);
sealed class DesktopHost : IDisposable
{
    readonly Thread thread;
    readonly KeyboardInput input;
    Gesture gesture => input.Gesture;
    readonly ConcurrentQueue<Action> operations = new();
    TaskCompletionSource<int[]?>? recording;
    nint recordingOwner;
    readonly Native.Hook keyboard, mouse;
    readonly Native.WndProc wndProc;
    readonly ManualResetEventSlim ready = new();
    readonly object boundsLock = new();
    Native.Rect[] bounds = [];
    nint hwnd, keyHook, mouseHook, trayIcon;
    uint taskbar;
    Exception? failure;
    int hovered = -1;
    public event Action<HostCommand>? Command;
    public DesktopHost(KeyboardBindings bindings)
    {
        input = new(bindings);
        keyboard = Keyboard; mouse = Mouse; wndProc = WindowProc;
        thread = new Thread(Run) { IsBackground = true, Name = "Mix input and tray" };
    }
    public void Start()
    {
        thread.Start();
        if (!ready.Wait(5000)) throw new TimeoutException("Input initialization timed out.");
        if (failure != null) throw failure;
    }
    public void SetBounds(Native.Rect[] rectangles) { lock (boundsLock) bounds = rectangles; }
    Task OnHost(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!thread.IsAlive || failure != null || hwnd == 0)
            return Task.FromException(new InvalidOperationException("The keyboard host is unavailable."));
        operations.Enqueue(() =>
        {
            lock (completion)
            {
                if (completion.Task.IsCompleted) return;
                try { action(); completion.TrySetResult(); }
                catch (Exception ex) { completion.TrySetException(ex); }
            }
        });
        if (!Native.PostMessageW(hwnd, 0x8003, 0, 0))
            completion.TrySetException(new InvalidOperationException("Could not contact the keyboard host."));
        return AwaitOperation(completion);
    }
    static async Task AwaitOperation(TaskCompletionSource completion)
    {
        try { await completion.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        catch (TimeoutException)
        {
            // Either cancel a queued operation, or observe the action that already ran.
            lock (completion)
                if (!completion.Task.IsCompleted) { completion.TrySetCanceled(); throw; }
            await completion.Task;
        }
    }
    public Task SetBindingsAsync(KeyboardBindings bindings) => OnHost(() =>
    {
        if (bindings.Validate() is { } error) throw new ArgumentException(error);
        EndRecording();
        input.SetBindings(bindings);
        hovered = -1;
        Send("Hide");
        Tray(1);
    });
    public async Task<int[]?> RecordAsync(nint owner)
    {
        var result = new TaskCompletionSource<int[]?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await OnHost(() =>
        {
            EndRecording();
            if (Native.GetForegroundWindow() != owner) { result.TrySetResult(null); return; }
            recording = result;
            recordingOwner = owner;
            input.BeginRecording();
            Send("Hide");
        });
        return await result.Task;
    }
    public async void CancelRecording()
    {
        try { await OnHost(EndRecording); }
        catch { /* A stopped host no longer captures input. */ }
    }
    void EndRecording()
    {
        input.CancelRecording();
        input.Recorder.TakeCancelled();
        input.Recorder.TakeCompleted();
        recording?.TrySetResult(null);
        recording = null;
        recordingOwner = 0;
    }
    void Send(string kind, int value = 0) => Command?.Invoke(new(kind, value));
    void Reset()
    {
        EndRecording();
        input.InitializeHeld(Enumerable.Range(8,247).Where(k => (Native.GetAsyncKeyState(k) & 0x8000) != 0));
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
            recording?.TrySetResult(null);
            if (keyHook != 0) Native.UnhookWindowsHookEx(keyHook);
            if (mouseHook != 0) Native.UnhookWindowsHookEx(mouseHook);
            if (hwnd != 0) { Tray(2); Native.WTSUnRegisterSessionNotification(hwnd); Native.DestroyWindow(hwnd); }
            if (trayIcon != 0) Native.DestroyIcon(trayIcon);
        }
    }
    void Tray(uint operation)
    {
        string tip = "Win Mix · Hold " + gesture.Bindings.OpeningLabel;
        var icon = new Native.NotifyIcon { Size=(uint)Marshal.SizeOf<Native.NotifyIcon>(), Hwnd=hwnd, Id=1, Flags=7, Callback=0x8001,
            Icon=trayIcon != 0 ? trayIcon : Native.LoadIconW(0,(nint)32512), Tip=tip.Length < 128 ? tip : tip[..124] + "…", Info="", InfoTitle="" };
        Native.Shell_NotifyIconW(operation,ref icon);
    }
    nint WindowProc(nint h,uint m,nuint w,nint l)
    {
        if (m == taskbar && taskbar != 0) Tray(0);
        if (m == 0x8003) { while (operations.TryDequeue(out var operation)) operation(); return 0; }
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
        if (recording != null && Native.GetForegroundWindow() != recordingOwner) EndRecording();
        var result=input.Key((int)key.Key,message==0x100 || message==0x104,(key.Flags&0x10)!=0);
        if (input.Recorder.TakeCompleted() is { } keys)
        {
            recording?.TrySetResult(keys);
            recording = null;
            recordingOwner = 0;
        }
        if (input.Recorder.TakeCancelled()) EndRecording();
        if(result.Action is { } action)
        {
            if(action is GestureAction.Show or GestureAction.Left or GestureAction.Right) hovered=-1;
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
            Native.Rect[] rectangles; lock(boundsLock) rectangles=bounds;
            var p=mouseData.Point;
            int selected=-1;
            for(int i=0; i<rectangles.Length; i++)
            {
                var r=rectangles[i];
                if(p.X>=r.Left && p.X<r.Right && p.Y>=r.Top && p.Y<r.Bottom) { selected=MixRules.QuickOrder[i]; break; }
            }
            if(selected!=hovered) { hovered=selected; if(selected>=0) Send("Select",selected); }
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
