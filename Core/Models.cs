namespace Mix.Core;

public record DeviceChoice(string Id, string Name);
public record Level(string Key, string Name, string? DeviceId, float Volume, bool Muted, bool Available);
public record SessionLevel(string Key, string DeviceId, string Name, float Volume, bool Muted, string? ExecutablePath = null);
public record AudioState(DeviceChoice[] Devices, Level[] Channels, SessionLevel[] Sessions, string? Error);

public static class MixRules
{
    public static readonly string[] Channels = ["Game", "Chat", "Media", "Master"];
    public static readonly int[] QuickOrder = [3, 0, 1, 2];
    public static int MoveSelection(int current, int direction) => QuickOrder[Math.Clamp(Array.IndexOf(QuickOrder, current) + direction, 0, QuickOrder.Length - 1)];
    public static float Clamp(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : throw new ArgumentOutOfRangeException(nameof(value));
    public static string? Discover(string channel, IEnumerable<DeviceChoice> devices)
    {
        string prefix = channel switch { "Game" => "SteelSeries Sonar - Gaming (", "Chat" => "SteelSeries Sonar - Chat (", "Media" => "SteelSeries Sonar - Media (", _ => "" };
        var matches = devices.Where(d => channel == "Master"
            ? d.Name.StartsWith("Headphones (", StringComparison.OrdinalIgnoreCase) && d.Name.Contains("Arctis Nova Pro Wireless", StringComparison.OrdinalIgnoreCase)
            : d.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
        return matches.Length == 1 ? matches[0].Id : null;
    }
    public static string? ResolveBinding(string channel, bool hasBinding, string? savedId, bool reconnectRecognized, DeviceChoice[] devices)
    {
        if (!hasBinding) return Discover(channel, devices);
        if (savedId == null || !reconnectRecognized || devices.Any(d => d.Id == savedId)) return savedId;
        return Discover(channel, devices) ?? savedId;
    }
}
