using System.Text.Json;
using Microsoft.Win32;
using Mix.Core;

namespace Mix.Platform;

sealed class Preferences
{
    public Dictionary<string,string?> Bindings { get; set; } = new();
    public Dictionary<string,bool> ReconnectRecognized { get; set; } = new();
    public int Selected { get; set; } = 2;
    public bool HasUsedShortcut { get; set; }
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mix.Native");
    public static string FilePath => Path.Combine(DirectoryPath,"settings.json");
    public static Preferences Load()
    {
        try { var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)); if(p?.Bindings!=null) {p.ReconnectRecognized ??= new();p.Selected=Math.Clamp(p.Selected,0,3);return p;} } catch { }
        return new();
    }
    public void SetBinding(string channel, string? id, DeviceChoice[] devices)
    {
        Bindings[channel] = id;
        ReconnectRecognized[channel] = id != null && MixRules.Discover(channel, devices.Where(d => d.Id == id)) == id;
    }
    public bool RecoverBinding(string channel, string oldId, string newId)
    {
        if (!ReconnectRecognized.GetValueOrDefault(channel) || !Bindings.TryGetValue(channel, out var saved) || saved != oldId)
            return false;
        Bindings[channel] = newId;
        return true;
    }
    public bool Sync(AudioState state)
    {
        bool changed = false;
        foreach (var level in state.Channels)
        {
            if (level.DeviceId == null) continue;
            if (!Bindings.ContainsKey(level.Key))
            { Bindings[level.Key] = level.DeviceId; changed = true; }
            if (!ReconnectRecognized.ContainsKey(level.Key) && Bindings[level.Key] == level.DeviceId && state.Devices.Any(d => d.Id == level.DeviceId))
            {
                ReconnectRecognized[level.Key] = MixRules.Discover(level.Key, state.Devices.Where(d => d.Id == level.DeviceId)) == level.DeviceId;
                changed = true;
            }
        }
        return changed;
    }
    public void Save(string? path = null)
    {
        path ??= FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this));
        File.Move(path+".tmp",path,true);
    }
    const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    const string ApprovalKey="Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";
    public static bool StartupEnabled
    {
        get
        {
            using var key=Registry.CurrentUser.OpenSubKey(RunKey);
            using var approval=Registry.CurrentUser.OpenSubKey(ApprovalKey);
            return key?.GetValue("Mix.Native") is string && IsStartupApproved(approval?.GetValue("Mix.Native"));
        }
    }
    // Windows stores a DWORD state followed by a timestamp. Unknown states stay off.
    internal static bool IsStartupApproved(object? value) => value == null ||
        value is byte[] { Length: 12 } bytes && System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes) is 2 or 6;
    public static void SetStartup(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(RunKey);
        if(enabled)
        {
            using var approval=Registry.CurrentUser.OpenSubKey(ApprovalKey,true);
            key.SetValue("Mix.Native",$"\"{Environment.ProcessPath}\" --background");
            approval?.DeleteValue("Mix.Native",false);
        }
        else key.DeleteValue("Mix.Native",false);
    }
}
