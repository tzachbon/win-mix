using Mix.Core;
using Mix.Platform;

static class AppRoutingTests
{
    public static void Run()
    {
        var directory = Path.Combine(Path.GetTempPath(), "mix-app-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string file = Path.Combine(directory, "settings.json");
        try
        {
            var preferences = new Preferences();
            string executable = Path.Combine(directory, "Player.exe");
            Check(preferences.SetAppChannel(executable, "Chat", file) == null, "offline assignment saves");
            var loaded = Preferences.Load(file);
            Check(loaded.AppChannels[executable.ToUpperInvariant()] == "Chat", "assignment survives restart and path casing");
            Check(loaded.Bindings.Count == 0 && loaded.Keyboard.SameAs(KeyboardBindings.Default), "assignment preserves existing settings");
            Check(loaded.SetAppChannel(executable, "Media", directory) != null, "failed save is visible");
            Check(loaded.AppChannels[executable] == "Chat", "failed save restores assignment");
            File.WriteAllText(file, """{"Bindings":{"Game":"saved-output"},"AppChannels":"wrong shape"}""");
            loaded = Preferences.Load(file);
            Check(loaded.AppChannels.Count == 0 && loaded.Bindings["Game"] == "saved-output", "malformed app settings preserve device settings");
            File.WriteAllText(file, """{"AppChannels":{"relative.exe":"Game","C:\\Player.exe":"Master","C:\\Bad.exe":123,"C:\\Chat.exe":"Chat"}}""");
            loaded = Preferences.Load(file);
            Check(loaded.AppChannels.Count == 1 && loaded.AppChannels[@"C:\Chat.exe"] == "Chat", "invalid paths and channels are discarded");
            string?[] roles = [null, "old-media", "old-chat"];
            bool fail = true;
            try
            {
                AppRoutingRules.ApplyRoles(role => roles[role], (role, value) =>
                {
                    if (role == 1 && fail) { fail = false; throw new Exception("device removed"); }
                    roles[role] = value;
                }, ["new", "new", "new"]);
                throw new Exception("missing routing failure");
            }
            catch (InvalidOperationException error)
            {
                Check(error.Message.Contains("restored"), "partial failure reports verified restoration");
            }
            Check(roles.SequenceEqual(new string?[] { null, "old-media", "old-chat" }), "partial failure restores distinct roles and absent override");
            int writes = 0;
            try
            {
                AppRoutingRules.ApplyRoles(role => throw new IOException("read failed"), (_, _) => writes++, ["new", "new", "new"]);
                throw new Exception("snapshot failure was hidden");
            }
            catch (IOException) { }
            Check(writes == 0, "failed snapshot must not write any role");
            try
            {
                AppRoutingRules.ApplyRoles(role => roles[role], (_, _) => throw new IOException("write failed"), ["new", "new", "new"]);
                throw new Exception("rollback failure was hidden");
            }
            catch (AggregateException error) { Check(error.Message.Contains("could not be fully restored"), "rollback failure remains explicit"); }
            var session = new SessionLevel("session", "source", "Player", .5f, false, executable, 123, 100, true);
            Check(AppRoutingRules.SameProcess(session, 0, 123, 100, false), "matching single process is routable");
            Check(!AppRoutingRules.SameProcess(session, 0, 123, 101, false), "reused PID is rejected");
            Check(!AppRoutingRules.SameProcess(session, 0x889000D, 123, 100, false), "multi-process status is rejected");
            Check(!AppRoutingRules.SameProcess(session, 0, 123, 100, true), "exited process is rejected");
            Check(!AppRoutingRules.SameProcess(session with { SystemSounds = true }, 0, 123, 100, false), "renamed system session is rejected");
            var attempts = new AppRoutingAttempts();
            Check(attempts.TryBegin(session, "Chat", "chat-output"), "first assignment attempts routing");
            Check(!attempts.TryBegin(session with { Key = "migrated", DeviceId = "chat-output" }, "Chat", "chat-output"), "session migration cannot cause a routing loop");
            Check(attempts.TryBegin(session, "Chat", "replacement-output"), "replacement endpoint retries saved assignment");
            Check(attempts.TryBegin(session with { ProcessStarted = 101 }, "Chat", "replacement-output"), "new process generation gets its assignment");
            attempts.Retry(executable.ToUpperInvariant());
            Check(attempts.TryBegin(session, "Chat", "replacement-output"), "explicit reassignment can retry failures");
        }
        finally { Directory.Delete(directory, true); }
        Console.WriteLine("App channel settings checks passed.");
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
