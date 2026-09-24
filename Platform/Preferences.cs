using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Mix.Core;

namespace Mix.Platform;

sealed class Preferences
{
    public Dictionary<string,string?> Bindings { get; set; } = new();
    public int Selected { get; set; } = 2;
    public bool HasUsedShortcut { get; set; }
    public KeyboardBindings Keyboard { get; set; } = KeyboardBindings.Default;
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Mix.Native");
    public static string FilePath => Path.Combine(DirectoryPath,"settings.json");
    public static Preferences Load(string? path = null)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path ?? FilePath)) as JsonObject;
            if (root is null)
                return new();

            JsonNode? keyboard = root[nameof(Keyboard)]?.DeepClone();
            root.Remove(nameof(Keyboard));
            var p = root.Deserialize<Preferences>();
            if (p?.Bindings is not null)
            {
                try
                {
                    var parsed = keyboard?.Deserialize<KeyboardBindings>();
                    p.Keyboard = parsed is not null && parsed.Validate() is null ? parsed : KeyboardBindings.Default;
                }
                catch
                {
                    p.Keyboard = KeyboardBindings.Default;
                }
                p.Selected = Math.Clamp(p.Selected, 0, 3);
                return p;
            }
        }
        catch { }
        return new();
    }
    public void Save(string? path = null)
    {
        string target = path ?? FilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        File.WriteAllText(target+".tmp",JsonSerializer.Serialize(this));
        File.Move(target+".tmp",target,true);
    }

    public async Task<string?> ApplyKeyboardAsync(KeyboardBindings candidate, Func<KeyboardBindings,Task> activate, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(activate);
        if (candidate.Validate() is { } validationError)
            return validationError;

        KeyboardBindings previous = Keyboard;
        Keyboard = candidate;
        try
        {
            Save(path);
        }
        catch (Exception error)
        {
            Keyboard = previous;
            return $"Could not save keyboard bindings: {error.Message}";
        }

        try
        {
            await activate(candidate);
            return null;
        }
        catch (Exception error)
        {
            return $"Saved, but could not activate keyboard bindings: {error.Message}";
        }
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
