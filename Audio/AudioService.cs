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
    readonly Dictionary<string, string?> executablePaths = new();
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
        Action<string, string, string>? bindingRecovered = null)
    {
        this.bindings = new(bindings);
        this.reconnectRecognized = reconnectRecognized is null ? new() : new(reconnectRecognized);
        this.bindingRecovered = bindingRecovered;
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
        reconnectRecognized[channel] = id != null && MixRules.Discover(channel, choices.Where(d => d.Id == id)) == id;
        Rebuild();
    });
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
                reconnectRecognized[channel] = MixRules.Discover(channel, choices.Where(d => d.Id == id)) == id;
            var resolved = MixRules.ResolveBinding(channel, hasBinding, id, reconnectRecognized.GetValueOrDefault(channel), choices);
            if (id != null && resolved != null && resolved != id) bindingRecovered?.Invoke(channel, id, resolved);
            bindings[channel] = resolved;
            if (!hasBinding && bindings[channel] != null) reconnectRecognized[channel] = true;
        }
        foreach (var id in bindings.Values.Where(x => x != null).Distinct())
        {
            if (!choices.Any(x => x.Id == id)) continue;
            var device = enumerator.GetDevice(id!);
            devices[id!] = device;
            device.AudioEndpointVolume.OnVolumeNotification += VolumeChanged;
            var manager = device.AudioSessionManager;
            manager.OnSessionCreated += SessionCreated;
            var collection = manager.Sessions;
            for (int i = 0; i < collection.Count; i++)
            {
                var session = collection[i];
                if (session.State == AudioSessionState.AudioSessionStateExpired) { session.Dispose(); continue; }
                var key = id + "|" + session.GetSessionInstanceIdentifier;
                session.RegisterEventClient(new SessionEvents(() => Post(() => Publish()), Refresh));
                sessions.Add(key, session);
                try { using var process = Process.GetProcessById((int)session.GetProcessID); executablePaths[key] = process.MainModule?.FileName; }
                catch { executablePaths[key] = null; }
            }
        }
        Publish();
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
                rows.Add(new(key, key[..key.IndexOf('|')], $"{name} · PID {session.GetProcessID}", session.SimpleAudioVolume.Volume, session.SimpleAudioVolume.Mute, executablePaths.GetValueOrDefault(key)));
            }
            catch { }
        Changed?.Invoke(new(choices, levels.ToArray(), rows.ToArray(), error));
    }
    static void Try(Action action) { try { action(); } catch { } }
    void Clear()
    {
        foreach (var s in sessions.Values) Try(s.Dispose);
        sessions.Clear();
        executablePaths.Clear();
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
