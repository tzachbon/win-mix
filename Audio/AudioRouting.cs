using System.Runtime.InteropServices;
using Mix.Core;

namespace Mix.Audio;

// Private Windows ABI documented by EarTrumpet's IAudioPolicyConfigFactory variants.
// https://github.com/File-New-Project/EarTrumpet/tree/master/EarTrumpet/Interop/MMDeviceAPI
sealed class AudioRouting : IDisposable
{
    const string Prefix = @"\\?\SWD#MMDEVAPI#";
    const string Suffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}";
    nint factory;
    readonly ReadEndpoint read;
    readonly WriteEndpoint write;

    public AudioRouting()
    {
        Marshal.ThrowExceptionForHR(RoInitialize(1));
        nint name = 0;
        try
        {
            name = CreateString("Windows.Media.Internal.AudioPolicyConfig");
            var iid = new Guid(Environment.OSVersion.Version.Build >= 21390
                ? "ab3d4648-e242-459f-b02f-541c70306324" : "2a59116d-6c4f-45e0-a74f-707e3fef9258");
            Check(RoGetActivationFactory(name, ref iid, out factory), "initialize app routing");
            var table = Marshal.ReadIntPtr(factory);
            write = Marshal.GetDelegateForFunctionPointer<WriteEndpoint>(Marshal.ReadIntPtr(table, 25 * nint.Size));
            read = Marshal.GetDelegateForFunctionPointer<ReadEndpoint>(Marshal.ReadIntPtr(table, 26 * nint.Size));
        }
        catch { if (factory != 0) Marshal.Release(factory); factory = 0; RoUninitialize(); throw; }
        finally { WindowsDeleteString(name); }
    }

    public string? Read(uint processId, int role)
    {
        nint value = 0;
        try
        {
            Check(read(factory, processId, 0, role, out value), "read this app's output");
            var buffer = WindowsGetStringRawBuffer(value, out var length);
            return length == 0 ? null : Marshal.PtrToStringUni(buffer, checked((int)length));
        }
        finally { WindowsDeleteString(value); }
    }

    public void Move(uint processId, string endpoint) => Restore(processId, Enumerable.Repeat<string?>(Wrap(endpoint), 3).ToArray());
    public void Restore(uint processId, IReadOnlyList<string?> values) =>
        AppRoutingRules.ApplyRoles(role => Read(processId, role), (role, value) => Write(processId, role, value), values);
    public static string Wrap(string endpoint) => Prefix + endpoint + Suffix;

    void Write(uint processId, int role, string? value)
    {
        nint text = CreateString(value);
        try { Check(write(factory, processId, 0, role, text), "change this app's output"); }
        finally { WindowsDeleteString(text); }
    }

    static nint CreateString(string? value)
    {
        if (value == null) return 0;
        Check(WindowsCreateString(value, (uint)value.Length, out var result), "prepare app routing");
        return result;
    }

    static void Check(int result, string operation)
    {
        if (result < 0) throw new InvalidOperationException($"Windows could not {operation} (0x{result:X8}).");
    }

    public void Dispose()
    {
        if (factory == 0) return;
        Marshal.Release(factory);
        factory = 0;
        RoUninitialize();
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int ReadEndpoint(nint self, uint processId, int flow, int role, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int WriteEndpoint(nint self, uint processId, int flow, int role, nint value);
    [DllImport("combase.dll")] static extern int RoInitialize(uint type);
    [DllImport("combase.dll")] static extern void RoUninitialize();
    [DllImport("combase.dll")] static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint result);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] static extern int WindowsCreateString(string value, uint length, out nint result);
    [DllImport("combase.dll")] static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] static extern nint WindowsGetStringRawBuffer(nint value, out uint length);
}
