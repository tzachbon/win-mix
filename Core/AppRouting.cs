namespace Mix.Core;

public static class AppRoutingRules
{
    public static bool IsChannel(string? channel) => channel is "Game" or "Chat" or "Media";
    public static bool SameProcess(SessionLevel session, int status, uint processId, long started, bool exited) =>
        session.Routable && !session.SystemSounds && status == 0 && !exited && processId != 0 &&
        session.ProcessId == processId && session.ProcessStarted != 0 && session.ProcessStarted == started;

    public static string? NormalizePath(string? path)
    {
        try
        {
            return path != null && Path.IsPathFullyQualified(path) &&
                Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFullPath(path) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    public static void ApplyRoles(Func<int,string?> read, Action<int,string?> write, IReadOnlyList<string?> targets)
    {
        if (targets.Count != 3) throw new ArgumentException("Three render roles are required.", nameof(targets));
        var previous = Enumerable.Range(0, 3).Select(read).ToArray();
        try
        {
            for (int role = 0; role < 3; role++) write(role, targets[role]);
            for (int role = 0; role < 3; role++)
                if (!StringComparer.OrdinalIgnoreCase.Equals(read(role), targets[role]))
                    throw new InvalidOperationException("Windows did not retain the selected output.");
        }
        catch (Exception error)
        {
            var failures = new List<Exception>();
            for (int role = 0; role < 3; role++)
                try
                {
                    write(role, previous[role]);
                    if (!StringComparer.OrdinalIgnoreCase.Equals(read(role), previous[role]))
                        throw new InvalidOperationException("Previous output did not restore.");
                }
                catch (Exception restoreError) { failures.Add(restoreError); }
            if (failures.Count != 0)
                throw new AggregateException("Could not change app output. Previous output could not be fully restored.", new[] { error }.Concat(failures));
            throw new InvalidOperationException("Could not change app output. Previous output restored.", error);
        }
    }
}

public sealed class AppRoutingAttempts
{
    readonly Dictionary<(uint Pid, long Started), (string Path, string Channel, string Device)> attempted = new();

    public bool TryBegin(SessionLevel session, string channel, string device)
    {
        var key = (session.ProcessId, session.ProcessStarted);
        var desired = (session.ExecutablePath!, channel, device);
        if (attempted.TryGetValue(key, out var previous) && previous == desired) return false;
        attempted[key] = desired;
        return true;
    }

    public void Retry(string path)
    {
        foreach (var key in attempted.Where(x => StringComparer.OrdinalIgnoreCase.Equals(x.Value.Path, path)).Select(x => x.Key).ToArray())
            attempted.Remove(key);
    }

    public void Prune(Func<uint,long,bool> alive)
    {
        foreach (var key in attempted.Keys.Where(key => !alive(key.Pid, key.Started)).ToArray()) attempted.Remove(key);
    }
}
