using System.Text.Json;
using Microsoft.Win32;

namespace Mix.Platform;

sealed class Preferences
{
    public Dictionary<string,string?> Bindings { get; set; } = new();
    public int Selected { get; set; } = 2;
    public bool HasUsedShortcut { get; set; }
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mix.Native");
    public static string FilePath => Path.Combine(DirectoryPath,"settings.json");
    public static Preferences Load()
    {
        try { var p=JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)); if(p?.Bindings!=null) {p.Selected=Math.Clamp(p.Selected,0,2);return p;} } catch { }
        return new();
    }
    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(FilePath+".tmp",JsonSerializer.Serialize(this));
        File.Move(FilePath+".tmp",FilePath,true);
    }
    const string RunKey="Software\\Microsoft\\Windows\\CurrentVersion\\Run";
    public static bool StartupEnabled { get { using var key=Registry.CurrentUser.OpenSubKey(RunKey); return key?.GetValue("Mix.Native") is string; } }
    public static void SetStartup(bool enabled)
    {
        using var key=Registry.CurrentUser.CreateSubKey(RunKey);
        if(enabled) key.SetValue("Mix.Native",$"\"{Environment.ProcessPath}\" --background");
        else key.DeleteValue("Mix.Native",false);
    }
}
