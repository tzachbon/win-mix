using System.Runtime.InteropServices;
using NAudio.CoreAudioApi.Interfaces;

namespace Mix.Audio;

// Keep GetProcessId's native success status, which NAudio's wrapper discards.
static class SessionCatalog
{
    public static IAudioSessionControl2[] ReadOrEmpty(string endpoint)
    {
        // One unreadable endpoint must not prevent other outputs from updating.
        try { return Read(endpoint); }
        catch (Exception) { return []; }
    }

    public static IAudioSessionControl2[] Read(string endpoint)
    {
        object? enumerator = null, device = null, manager = null, collection = null;
        try
        {
            enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new("BCDE0395-E52F-467C-8E3D-C4579291692E"), true)!)!;
            ((DeviceEnumerator)enumerator).GetDevice(endpoint, out var found);
            device = found;
            var iid = new Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");
            found.Activate(ref iid, 23, 0, out manager);
            ((SessionManager)manager).GetSessionEnumerator(out var sessions);
            collection = sessions;
            sessions.GetCount(out int count);
            var result = new List<IAudioSessionControl2>();
            for (int index = 0; index < count; index++)
            {
                sessions.GetSession(index, out var control);
                result.Add((IAudioSessionControl2)control);
            }
            return result.ToArray();
        }
        finally
        {
            foreach (var value in new[] { collection, manager, device, enumerator })
                if (value != null) Marshal.ReleaseComObject(value);
        }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface DeviceEnumerator
    {
        void EnumAudioEndpoints();
        void GetDefaultAudioEndpoint();
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string endpoint, out Device device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface Device
    {
        void Activate(ref Guid iid, int context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface SessionManager
    {
        void GetAudioSessionControl();
        void GetSimpleAudioVolume();
        void GetSessionEnumerator(out SessionEnumerator result);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface SessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl control);
    }
}
