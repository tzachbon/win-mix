using System.Diagnostics;
using System.Security.Principal;

namespace Mix.Platform;

sealed class Instance : IDisposable
{
    static readonly string Prefix = "Local\\Mix.Native."+WindowsIdentity.GetCurrent().User!.Value+"."+Process.GetCurrentProcess().SessionId;
    readonly Mutex mutex;
    readonly EventWaitHandle stop,show;
    readonly RegisteredWaitHandle stopWait,showWait;
    public Instance(Action quit,Action open)
    {
        mutex=new Mutex(false,Prefix+".Running",out bool created);
        if(!created)
        {
            if(!Environment.GetCommandLineArgs().Contains("--background"))
                for (int retry=0; retry<40 && !Signal(".Show"); retry++) Thread.Sleep(50);
            Environment.Exit(0);
        }
        stop=new EventWaitHandle(false,EventResetMode.AutoReset,Prefix+".Stop");
        show=new EventWaitHandle(false,EventResetMode.AutoReset,Prefix+".Show");
        stopWait=ThreadPool.RegisterWaitForSingleObject(stop,(_,_)=>quit(),null,Timeout.Infinite,false);
        showWait=ThreadPool.RegisterWaitForSingleObject(show,(_,_)=>open(),null,Timeout.Infinite,false);
    }
    static bool Signal(string suffix) { try { using var e=EventWaitHandle.OpenExisting(Prefix+suffix); return e.Set(); } catch(WaitHandleCannotBeOpenedException) { return false; } }
    public static int Shutdown()
    {
        Signal(".Stop");
        var deadline=DateTime.UtcNow.AddSeconds(5);
        while(DateTime.UtcNow<deadline)
        {
            if(!Mutex.TryOpenExisting(Prefix+".Running",out var existing)) return 0;
            existing.Dispose(); Signal(".Stop"); Thread.Sleep(50);
        }
        return 1;
    }
    public void Dispose() {stopWait.Unregister(null);showWait.Unregister(null);stop.Dispose();show.Dispose();mutex.Dispose();}
}
