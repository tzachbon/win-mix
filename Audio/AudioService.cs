using System.Collections.Concurrent;
using System.Diagnostics;
using Mix.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Mix.Audio;

public sealed class AudioService : IDisposable
{
    readonly BlockingCollection<Action> work = new();
    readonly Thread thread;
    readonly Dictionary<string, MMDevice> devices = new();
    readonly Dictionary<string, AudioSessionControl> sessions = new();
    readonly Dictionary<string, IAudioSessionControl2> nativeSessions = new();
    readonly Dictionary<string, SessionLevel> identities = new();
    readonly Dictionary<string, string> routingErrors = new(StringComparer.OrdinalIgnoreCase);
    readonly AppRoutingAttempts attempts = new();
    Dictionary<string,string> appChannels;
    AudioRouting? routing;
    readonly Dictionary<string, string?> bindings;
    readonly Dictionary<string, bool> reconnectRecognized;
    readonly Action<string, string, string>? bindingRecovered;
    MMDeviceEnumerator? enumerator;
    DeviceNotifications? notifications;
    DeviceChoice[] choices = [];
    int refreshPending;
    volatile bool stopping;
    public event Action<AudioState>? Changed;

    public AudioService(Dictionary<string, string?> bindings, Dictionary<string, bool>? reconnectRecognized = null,
        Action<string, string, string>? bindingRecovered = null, IReadOnlyDictionary<string,string>? appChannels = null)
    {
        this.bindings = new(bindings);
        this.reconnectRecognized = reconnectRecognized is null ? new() : new(reconnectRecognized);
        this.bindingRecovered = bindingRecovered;
        this.appChannels = appChannels?.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase) ?? new(StringComparer.OrdinalIgnoreCase);
        thread = new Thread(Run) { IsBackground = true, Name = "Mix audio" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }
    void Run()
    {
        try
        {
            enumerator = new MMDeviceEnumerator();
            notifications = new DeviceNotifications(Refresh);
            enumerator.RegisterEndpointNotificationCallback(notifications);
            foreach (var action in work.GetConsumingEnumerable())
                try { action(); } catch (Exception ex) { Publish(ex.Message); }
        }
        catch (Exception ex) { Publish(ex.Message); }
        finally
        {
            routing?.Dispose();
            Clear();
            if (notifications != null) Try(() => enumerator?.UnregisterEndpointNotificationCallback(notifications));
            enumerator?.Dispose();
        }
    }
    void Post(Action action)
    {
        if (stopping) return;
        try { work.Add(action); } catch (InvalidOperationException) { }
    }
    public void Refresh()
    {
        if (Interlocked.Exchange(ref refreshPending, 1) != 0) return;
        Post(() => { Interlocked.Exchange(ref refreshPending, 0); Rebuild(); });
    }
    public void Bind(string channel, string? id) => Post(() =>
    {
        bindings[channel] = id;
        reconnectRecognized[channel] = id != null && MixRules.Discover(channel, choices) == id;
        Rebuild();
    });
    public void ConfigureAppChannels(IReadOnlyDictionary<string,string> assignments, string? retryPath = null)
    {
        var copy = assignments.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        Post(() =>
        {
            foreach (var path in copy.Keys.Union(appChannels.Keys, StringComparer.OrdinalIgnoreCase))
                if (path.Equals(retryPath, StringComparison.OrdinalIgnoreCase) || copy.GetValueOrDefault(path) != appChannels.GetValueOrDefault(path))
                { attempts.Retry(path); routingErrors.Remove(path); }
            appChannels = copy;
            Rebuild();
        });
    }
    public void SetLevel(string channel, float value) => Post(() =>
    {
        if (Device(channel) is { } device) device.AudioEndpointVolume.MasterVolumeLevelScalar = MixRules.Clamp(value);
        else throw new InvalidOperationException($"{channel} is unavailable.");
        Publish();
    });
    public void Adjust(string channel, int steps) => Post(() =>
    {
        if (Device(channel) is not { } device) throw new InvalidOperationException($"{channel} is unavailable.");
        device.AudioEndpointVolume.MasterVolumeLevelScalar = MixRules.Clamp(device.AudioEndpointVolume.MasterVolumeLevelScalar + steps * .02f);
        Publish();
    });
    public void ToggleMute(string channel) => Post(() =>
    {
        if (Device(channel) is not { } device) throw new InvalidOperationException($"{channel} is unavailable.");
        device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
        Publish();
    });
    public void SetSession(string key, float? volume = null, bool? mute = null) => Post(() =>
    {
        if (!sessions.TryGetValue(key, out var session)) throw new InvalidOperationException("This audio session has closed.");
        if (volume.HasValue) session.SimpleAudioVolume.Volume = MixRules.Clamp(volume.Value);
        if (mute.HasValue) session.SimpleAudioVolume.Mute = mute.Value;
        Publish();
    });
    MMDevice? Device(string key) => bindings.TryGetValue(key, out var id) && id != null ? devices.GetValueOrDefault(id) : null;
    void Rebuild()
    {
        Clear();
        var found = new List<DeviceChoice>();
        var endpoints = enumerator!.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        foreach (var device in endpoints)
            using (device) found.Add(new(device.ID, device.FriendlyName));
        choices = found.ToArray();
        foreach (var channel in MixRules.Channels)
        {
            bool hasBinding = bindings.TryGetValue(channel, out var id);
            if (id != null && choices.Any(d => d.Id == id) && !reconnectRecognized.ContainsKey(channel))
                reconnectRecognized[channel] = MixRules.Discover(channel, choices) == id;
            var resolved = MixRules.ResolveBinding(channel, hasBinding, id, reconnectRecognized.GetValueOrDefault(channel), choices);
            if (id != null && resolved != null && resolved != id) bindingRecovered?.Invoke(channel, id, resolved);
            bindings[channel] = resolved;
            if (!hasBinding && bindings[channel] != null) reconnectRecognized[channel] = true;
        }
        foreach (var id in choices.Select(x => x.Id))
        {
            var device = enumerator.GetDevice(id);
            devices[id] = device;
            device.AudioEndpointVolume.OnVolumeNotification += VolumeChanged;
            var manager = device.AudioSessionManager;
            manager.OnSessionCreated += SessionCreated;
            foreach (var native in SessionCatalog.ReadOrEmpty(id))
            {
                var session = new AudioSessionControl(native);
                if (session.State == AudioSessionState.AudioSessionStateExpired) { session.Dispose(); continue; }
                var key = id + "|" + session.GetSessionInstanceIdentifier;
                session.RegisterEventClient(new SessionEvents(() => Post(() => Publish()), Refresh));
                sessions.Add(key, session);
                nativeSessions[key] = native;
                int status = native.GetProcessId(out var pid);
                long started = 0;
                string? path = null;
                try
                {
                    using var process = Process.GetProcessById(checked((int)pid));
                    started = process.StartTime.ToUniversalTime().Ticks;
                    path = AppRoutingRules.NormalizePath(process.MainModule?.FileName);
                }
                catch { }
                bool system = session.IsSystemSoundsSession;
                identities[key] = new(key, id, "", 0, false, path, pid, started, status == 0 && started != 0 && path != null && !system, system);
            }
        }
        ReconcileApps();
        Publish();
    }
    void ReconcileApps()
    {
        attempts.Prune((pid, started) =>
        {
            try { using var process = Process.GetProcessById(checked((int)pid)); return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == started; }
            catch { return false; }
        });
        foreach (var row in identities.Values.Where(x => x.Routable && sessions[x.Key].State == AudioSessionState.AudioSessionStateActive)
            .GroupBy(x => (x.ProcessId, x.ProcessStarted)).Select(x => x.First()))
        {
            if (!appChannels.TryGetValue(row.ExecutablePath!, out var channel) || !AppRoutingRules.IsChannel(channel)) continue;
            if (Device(channel) is not { State: DeviceState.Active } destination) continue;
            if (!attempts.TryBegin(row, channel, destination.ID)) continue;
            try
            {
                var native = nativeSessions[row.Key];
                int status = native.GetProcessId(out var pid);
                using var process = Process.GetProcessById(checked((int)pid));
                _ = process.SafeHandle;
                if (!AppRoutingRules.SameProcess(row, status, pid, process.StartTime.ToUniversalTime().Ticks, process.HasExited) ||
                    !StringComparer.OrdinalIgnoreCase.Equals(process.MainModule?.FileName, row.ExecutablePath) ||
                    sessions[row.Key].State != AudioSessionState.AudioSessionStateActive)
                    throw new InvalidOperationException("This app's audio session has closed or changed. Restart playback and try again.");
                routing ??= new AudioRouting();
                routing.Move(pid, destination.ID);
                routingErrors.Remove(row.ExecutablePath!);
                Refresh();
            }
            catch (Exception error) { routingErrors[row.ExecutablePath!] = error.Message; }
        }
    }
    void SessionCreated(object sender, IAudioSessionControl session) => Refresh();
    void VolumeChanged(AudioVolumeNotificationData data) => Post(() => Publish());
    void Publish(string? error = null)
    {
        var levels = new List<Level>();
        foreach (var channel in MixRules.Channels)
        {
            var id = bindings.GetValueOrDefault(channel);
            try
            {
                var d = Device(channel);
                levels.Add(new(channel, channel, id, d?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0, d?.AudioEndpointVolume.Mute ?? false, d != null));
            }
            catch { levels.Add(new(channel, channel, id, 0, false, false)); }
        }
        var rows = new List<SessionLevel>();
        foreach (var (key, session) in sessions)
            try
            {
                if (session.State == AudioSessionState.AudioSessionStateExpired) continue;
                string name = session.DisplayName;
                if (session.IsSystemSoundsSession) name = "System sounds";
                else if (string.IsNullOrWhiteSpace(name))
                    try { using var p = Process.GetProcessById((int)session.GetProcessID); name = p.ProcessName; } catch { name = "Application"; }
                rows.Add(identities[key] with { Name = $"{name} · PID {identities[key].ProcessId}", Volume = session.SimpleAudioVolume.Volume, Muted = session.SimpleAudioVolume.Mute });
            }
            catch { }
        Changed?.Invoke(new(choices, levels.ToArray(), rows.ToArray(), error, new Dictionary<string,string>(routingErrors, StringComparer.OrdinalIgnoreCase)));
    }
    static void Try(Action action) { try { action(); } catch { } }
    void Clear()
    {
        foreach (var s in sessions.Values) Try(s.Dispose);
        sessions.Clear();
        nativeSessions.Clear();
        identities.Clear();
        foreach (var d in devices.Values)
        {
            Try(() => d.AudioEndpointVolume.OnVolumeNotification -= VolumeChanged);
            Try(() => d.AudioSessionManager.OnSessionCreated -= SessionCreated);
            Try(d.Dispose);
        }
        devices.Clear();
    }
    public void Dispose() { stopping = true; work.CompleteAdding(); thread.Join(3000); }
}
