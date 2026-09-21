using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;

namespace Mix.Core;

public sealed class KeyboardBindings : IEquatable<KeyboardBindings>
{
    private readonly ReadOnlyCollection<int> _opening;

    public KeyboardBindings(IReadOnlyList<int> opening, int mute)
    {
        ArgumentNullException.ThrowIfNull(opening);
        _opening = Array.AsReadOnly(opening.ToArray());
        Mute = mute;
    }

    public IReadOnlyList<int> Opening => _opening;
    public int Mute { get; }
    [JsonIgnore]
    public string OpeningLabel => string.Join(" + ", Opening.Select(KeyLabel));
    [JsonIgnore]
    public string MuteLabel => KeyLabel(Mute);

    public static KeyboardBindings Default { get; } = new(new[] { 0xA2, 0xA4 }, 0x4D);

    public string? Validate()
    {
        if (Opening.Count == 0)
            return "The opening shortcut needs at least one key.";
        if (Opening.Any(key => !IsKeyboardKey(key)) || !IsKeyboardKey(Mute))
            return "A shortcut contains an invalid keyboard key.";
        if (Opening.Distinct().Count() != Opening.Count)
            return "The opening shortcut contains duplicate keys.";
        if (Opening.Any(IsReservedActionKey) || IsReservedActionKey(Mute))
            return "Arrow keys and Escape are reserved for mixer controls.";
        if (Opening.Contains(Mute))
            return "The opening and mute shortcuts cannot share a key.";

        var keys = new HashSet<int>(Opening) { Mute };
        bool control = keys.Contains(0x11) || keys.Contains(0xA2) || keys.Contains(0xA3);
        bool alt = keys.Contains(0x12) || keys.Contains(0xA4) || keys.Contains(0xA5);
        if (control && alt && keys.Contains(0x2E))
            return "Ctrl+Alt+Delete is reserved by Windows.";

        bool windows = keys.Contains(0x5B) || keys.Contains(0x5C);
        if (windows && keys.Contains(0x4C))
            return "Win+L is reserved by Windows.";

        return null;
    }

    public bool SameAs(KeyboardBindings? other) => Equals(other);

    public bool Equals(KeyboardBindings? other) => other is not null && Mute == other.Mute &&
        Opening.Count == other.Opening.Count &&
        Opening.OrderBy(key => key).SequenceEqual(other.Opening.OrderBy(key => key));

    public override bool Equals(object? obj) => obj is KeyboardBindings other && Equals(other);

    public override int GetHashCode()
    {
        int[] keys = Opening.ToArray();
        Array.Sort(keys);
        var hash = new HashCode();
        hash.Add(Mute);
        foreach (int key in keys)
            hash.Add(key);
        return hash.ToHashCode();
    }

    public static string KeyLabel(int vk)
    {
        if (vk is >= 0x30 and <= 0x39)
            return ((char)vk).ToString();
        if (vk is >= 0x41 and <= 0x5A)
            return ((char)vk).ToString();
        if (vk is >= 0x60 and <= 0x69)
            return $"Numpad {vk - 0x60}";
        if (vk is >= 0x70 and <= 0x87)
            return $"F{vk - 0x6F}";

        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0C => "Clear",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x13 => "Pause",
            0x14 => "Caps Lock",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "Page Up",
            0x22 => "Page Down",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x29 => "Select",
            0x2A => "Print",
            0x2B => "Execute",
            0x2C => "Print Screen",
            0x2D => "Insert",
            0x2E => "Delete",
            0x2F => "Help",
            0x5B => "Left Win",
            0x5C => "Right Win",
            0x5D => "Menu",
            0x5F => "Sleep",
            0x6A => "Numpad *",
            0x6B => "Numpad +",
            0x6D => "Numpad -",
            0x6E => "Numpad .",
            0x6F => "Numpad /",
            0x90 => "Num Lock",
            0x91 => "Scroll Lock",
            0xA0 => "Left Shift",
            0xA1 => "Right Shift",
            0xA2 => "Left Ctrl",
            0xA3 => "Right Ctrl",
            0xA4 => "Left Alt",
            0xA5 => "Right Alt",
            0xA6 => "Browser Back",
            0xA7 => "Browser Forward",
            0xA8 => "Browser Refresh",
            0xA9 => "Browser Stop",
            0xAA => "Browser Search",
            0xAB => "Browser Favorites",
            0xAC => "Browser Home",
            0xAD => "Mute",
            0xAE => "Volume Down",
            0xAF => "Volume Up",
            0xB0 => "Media Next",
            0xB1 => "Media Previous",
            0xB2 => "Media Stop",
            0xB3 => "Media Play/Pause",
            0xB4 => "Mail",
            0xB5 => "Media Select",
            0xB6 => "Launch App 1",
            0xB7 => "Launch App 2",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            0xDF => "OEM 8",
            0xE2 => "OEM 102",
            _ => $"VK 0x{vk:X2}"
        };
    }

    private static bool IsReservedActionKey(int vk) => vk is 0x1B or >= 0x25 and <= 0x28;

    private static bool IsKeyboardKey(int vk)
    {
        if (vk is 0x10 or 0x11 or 0x12)
            return false;

        return vk is
            >= 0x08 and <= 0x09 or
            >= 0x0C and <= 0x2F or
            >= 0x30 and <= 0x39 or
            >= 0x41 and <= 0x5D or
            0x5F or
            >= 0x60 and <= 0x87 or
            0x90 or 0x91 or
            >= 0x92 and <= 0x96 or
            >= 0xA0 and <= 0xB7 or
            >= 0xBA and <= 0xC0 or
            >= 0xDB and <= 0xDF or
            >= 0xE1 and <= 0xE7 or
            >= 0xE9 and <= 0xFE;
    }
}
