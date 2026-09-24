using Mix.Audio;
using Mix.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System.Diagnostics;

internal static class Program
{
    static readonly string[] EndpointChannels = ["Game", "Chat", "Media", "Master"];
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);
    readonly record struct EndpointState(float Volume, bool Muted);

    [MTAThread]
    static async Task<int> Main(string[] args)
    {
        if (args.Length > 1 || (args.Length == 1 && args[0] != "--exercise"))
        {
            Console.Error.WriteLine("Usage: AudioProbe [--exercise]");
            return 2;
        }

        try
        {
            return args.Length == 0 ? await ReadOnlyAsync() : await ExerciseAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    static async Task<int> ReadOnlyAsync()
    {
        using var service = new AudioService(new Dictionary<string, string?>());
        var state = await NextStateAsync(service, _ => true, service.Refresh, "initial snapshot");
        Console.WriteLine($"mode=read-only endpoints={state.Devices.Length}");
        foreach (var device in state.Devices)
            Console.WriteLine($"device id={device.Id} name=\"{device.Name}\"");
        foreach (var level in state.Channels)
            Console.WriteLine($"channel={level.Key} available={level.Available} id={level.DeviceId ?? "-"} volume={level.Volume:F3} muted={level.Muted}");
        if (FindLevel(state, "Master") is { Available: true, DeviceId: not null } master)
        {
            string? recoveredId = null;
            using var recovering = new AudioService(
                new() { ["Master"] = "AUDIOPROBE_STALE_" + Guid.NewGuid().ToString("N") },
                new() { ["Master"] = true },
                (channel, _, newId) => { if (channel == "Master") recoveredId = newId; });
            var recovered = await NextStateAsync(recovering, _ => true, recovering.Refresh, "read-only Master recovery");
            var level = FindLevel(recovered, "Master");
            Require(level is { Available: true } && level.DeviceId == master.DeviceId && recoveredId == master.DeviceId,
                "Recognized stale Master binding did not reconnect and report recovery.");
            Console.WriteLine("recovery=PASS channel=Master");
        }
        else Console.WriteLine("recovery=SKIP no active Master endpoint");
        Console.WriteLine("PASS snapshot");
        return 0;
    }

    static async Task<int> ExerciseAsync()
    {
        using var service = new AudioService(new Dictionary<string, string?>());
        using var enumerator = new MMDeviceEnumerator();
        var first = await NextStateAsync(service, _ => true, service.Refresh, "initial snapshot");
        var ids = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var channel in EndpointChannels)
        {
            var level = FindLevel(first, channel) ?? throw new InvalidOperationException($"{channel} endpoint row is missing.");
            if (!level.Available || level.DeviceId is null)
                throw new InvalidOperationException($"{channel} endpoint was not discovered.");
            ids.Add(channel, level.DeviceId);
        }
        Require(ids.Values.Distinct(StringComparer.Ordinal).Count() == EndpointChannels.Length, "Endpoint channels did not resolve to distinct endpoints.");

        var original = ReadEndpoints(enumerator, ids);
        foreach (var channel in EndpointChannels)
        {
            var level = FindLevel(first, channel)!;
            Require(Near(level.Volume, original[channel].Volume, .003f) && level.Muted == original[channel].Muted,
                $"Initial service snapshot differs from independent readback for {channel}.");
        }

        Exception? failure = null;
        try
        {
            Console.WriteLine("mode=exercise");
            foreach (var channel in EndpointChannels)
                Console.WriteLine($"endpoint channel={channel} id={ids[channel]} volume={original[channel].Volume:F3} muted={original[channel].Muted}");

            foreach (var channel in EndpointChannels)
            {
                var before = ReadEndpoints(enumerator, ids);
                int steps = before[channel].Volume <= .96f ? 2 : -2;
                float expected = MixRules.Clamp(before[channel].Volume + steps * .02f);
                var snapshot = await NextStateAsync(service,
                    s => FindLevel(s, channel) is { } l && Near(l.Volume, expected, .012f),
                    () => service.Adjust(channel, steps), $"{channel} level adjustment");
                var after = await WaitForEndpointAsync(enumerator, ids, channel, e => Near(e.Volume, expected, .012f));
                CheckVolumeChange(channel, expected, before, after);
                Console.WriteLine($"adjust channel={channel} steps={steps} service={FindLevel(snapshot, channel)!.Volume:F3} independent={after[channel].Volume:F3} PASS");
            }

            foreach (var channel in EndpointChannels)
            {
                var before = ReadEndpoints(enumerator, ids);
                bool expectedMute = !before[channel].Muted;
                var snapshot = await NextStateAsync(service,
                    s => FindLevel(s, channel) is { } l && l.Muted == expectedMute && Near(l.Volume, before[channel].Volume, .003f),
                    () => service.ToggleMute(channel), $"{channel} mute toggle");
                var after = ReadEndpoints(enumerator, ids);
                CheckMuteChange(channel, expectedMute, before, after);
                Console.WriteLine($"mute channel={channel} muted={after[channel].Muted} volume={after[channel].Volume:F3} PASS");

                var restoreSnapshot = await NextStateAsync(service,
                    s => FindLevel(s, channel) is { } l && l.Muted == before[channel].Muted && Near(l.Volume, before[channel].Volume, .003f),
                    () => service.ToggleMute(channel), $"{channel} mute restore");
                var restored = ReadEndpoints(enumerator, ids);
                CheckUnchanged(before, restored, $"{channel} mute restore");
                Require(FindLevel(restoreSnapshot, channel)!.Muted == before[channel].Muted, $"{channel} mute snapshot did not restore.");
            }

            await ExerciseOwnedSessionAsync(service, enumerator, ids["Game"]);

            const string notificationChannel = "Game";
            var externalBefore = ReadEndpoints(enumerator, ids);
            int externalSteps = externalBefore[notificationChannel].Volume <= .96f ? 1 : -1;
            float externalExpected = MixRules.Clamp(externalBefore[notificationChannel].Volume + externalSteps * .02f);
            var notification = NextStateAsync(service,
                s => FindLevel(s, notificationChannel) is { } l && Near(l.Volume, externalExpected, .012f),
                () => SetEndpoint(enumerator, ids[notificationChannel], externalExpected), "external volume notification");
            var notified = await notification;
            var externalAfter = await WaitForEndpointAsync(enumerator, ids, notificationChannel, e => Near(e.Volume, externalExpected, .012f));
            CheckVolumeChange(notificationChannel, externalExpected, externalBefore, externalAfter);
            Require(Near(FindLevel(notified, notificationChannel)!.Volume, externalExpected, .012f), "Service snapshot missed the external volume value.");
            Console.WriteLine($"notification channel={notificationChannel} service={FindLevel(notified, notificationChannel)!.Volume:F3} independent={externalAfter[notificationChannel].Volume:F3} PASS");

            var beforeBinding = ReadEndpoints(enumerator, ids);
            const string unavailableId = "AUDIOPROBE_UNAVAILABLE_";
            string syntheticId = unavailableId + Guid.NewGuid().ToString("N");
            var unavailable = await NextStateAsync(service,
                s => FindLevel(s, "Game") is { Available: false } l && l.DeviceId == syntheticId,
                () => service.Bind("Game", syntheticId), "synthetic unavailable binding");
            var afterBinding = ReadEndpoints(enumerator, ids);
            CheckUnchanged(beforeBinding, afterBinding, "synthetic unavailable binding");
            Console.WriteLine($"binding channel=Game available={FindLevel(unavailable, "Game")!.Available} id={syntheticId} PASS");

            var restoredBinding = await NextStateAsync(service,
                s => FindLevel(s, "Game") is { Available: true } l && l.DeviceId == ids["Game"],
                () => service.Bind("Game", ids["Game"]), "binding restore");
            Require(FindLevel(restoredBinding, "Game")!.DeviceId == ids["Game"], "Game binding did not restore.");
            Console.WriteLine("binding-restore=PASS");
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        try
        {
            await RestoreAsync(service, enumerator, ids, original);
        }
        catch (Exception ex)
        {
            failure = failure is null ? ex : new AggregateException(failure, ex);
        }

        if (failure is not null) throw failure;
        Console.WriteLine("PASS exercise");
        return 0;
    }

    static async Task<AudioState> NextStateAsync(AudioService service, Func<AudioState, bool> matches, Action action, string step)
    {
        var completion = new TaskCompletionSource<AudioState>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(AudioState state)
        {
            if (state.Error is not null) completion.TrySetException(new InvalidOperationException($"{step}: {state.Error}"));
            else if (matches(state)) completion.TrySetResult(state);
        }

        service.Changed += OnChanged;
        try
        {
            action();
            return await completion.Task.WaitAsync(Timeout);
        }
        catch (TimeoutException ex)
        {
            throw new TimeoutException($"Timed out waiting for {step}.", ex);
        }
        finally
        {
            service.Changed -= OnChanged;
        }
    }

    static async Task<Dictionary<string, EndpointState>> WaitForEndpointAsync(
        MMDeviceEnumerator enumerator, IReadOnlyDictionary<string, string> ids, string channel, Func<EndpointState, bool> matches)
    {
        var started = DateTime.UtcNow;
        Dictionary<string, EndpointState> state;
        do
        {
            state = ReadEndpoints(enumerator, ids);
            if (matches(state[channel])) return state;
            await Task.Delay(40);
        } while (DateTime.UtcNow - started < Timeout);
        throw new TimeoutException($"Independent readback timed out for {channel}.");
    }

    static Dictionary<string, EndpointState> ReadEndpoints(MMDeviceEnumerator enumerator, IReadOnlyDictionary<string, string> ids)
    {
        var result = new Dictionary<string, EndpointState>(StringComparer.Ordinal);
        foreach (var (channel, id) in ids)
        {
            using var device = enumerator.GetDevice(id);
            result.Add(channel, new(device.AudioEndpointVolume.MasterVolumeLevelScalar, device.AudioEndpointVolume.Mute));
        }
        return result;
    }

    static async Task ExerciseOwnedSessionAsync(AudioService service, MMDeviceEnumerator enumerator, string deviceId)
    {
        using var device = enumerator.GetDevice(deviceId);
        using var output = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);

        int processId = Environment.ProcessId;
        bool IsOwnSession(AudioState state) => state.Sessions.Any(s =>
            s.DeviceId == deviceId && s.Name.EndsWith($"PID {processId}", StringComparison.Ordinal));
        var started = await NextStateAsync(service, IsOwnSession, () =>
        {
            output.Init(new SilenceWaveProvider());
            output.Play();
        }, "owned silent session OnSessionCreated notification");
        var ownRows = started.Sessions.Where(s => s.DeviceId == deviceId &&
            s.Name.EndsWith($"PID {processId}", StringComparison.Ordinal)).ToArray();
        Require(ownRows.Length == 1, $"Expected one owned silent session on Game, found {ownRows.Length}.");
        var own = ownRows[0];
        string instanceId = SessionInstanceId(own);
        var original = ReadSession(enumerator, deviceId, instanceId);
        Require(Near(own.Volume, original.Volume, .003f) && own.Muted == original.Muted,
            "Service session snapshot differs from independent ISimpleAudioVolume readback.");

        Exception? failure = null;
        try
        {
            float serviceTarget = original.Volume <= .96f ? original.Volume + .02f : original.Volume - .02f;
            var serviceState = await NextStateAsync(service,
                s => FindSession(s, own.Key) is { } row && Near(row.Volume, serviceTarget, .003f),
                () => service.SetSession(own.Key, volume: serviceTarget), "owned session service volume");
            var serviceReadback = ReadSession(enumerator, deviceId, instanceId);
            Require(Near(serviceReadback.Volume, serviceTarget, .003f), "Service session volume did not match independent ISimpleAudioVolume readback.");
            Require(Near(FindSession(serviceState, own.Key)!.Volume, serviceReadback.Volume, .003f), "Service session snapshot differs from independent readback.");
            Console.WriteLine($"session-service key={own.Key} volume={serviceReadback.Volume:F3} mute={serviceReadback.Muted} PASS");

            bool expectedMute = !serviceReadback.Muted;
            var muteState = await NextStateAsync(service,
                s => FindSession(s, own.Key) is { } row && row.Muted == expectedMute && Near(row.Volume, serviceReadback.Volume, .003f),
                () => service.SetSession(own.Key, mute: expectedMute), "owned session mute");
            var muteReadback = ReadSession(enumerator, deviceId, instanceId);
            Require(muteReadback.Muted == expectedMute && Near(muteReadback.Volume, serviceReadback.Volume, .003f),
                "Service session mute changed volume or did not reach independent ISimpleAudioVolume readback.");
            Require(FindSession(muteState, own.Key)!.Muted == expectedMute, "Service session mute snapshot is incorrect.");

            var unmutedState = await NextStateAsync(service,
                s => FindSession(s, own.Key) is { } row && row.Muted == original.Muted && Near(row.Volume, serviceReadback.Volume, .003f),
                () => service.SetSession(own.Key, mute: original.Muted), "owned session mute restore");
            var unmutedReadback = ReadSession(enumerator, deviceId, instanceId);
            Require(unmutedReadback.Muted == original.Muted && Near(unmutedReadback.Volume, serviceReadback.Volume, .003f),
                "Service session mute did not restore while preserving volume.");
            Require(FindSession(unmutedState, own.Key)!.Muted == original.Muted, "Service session mute restore snapshot is incorrect.");
            Console.WriteLine($"session-mute key={own.Key} muted={unmutedReadback.Muted} volume={unmutedReadback.Volume:F3} PASS");

            var notification = NextStateAsync(service,
                s => FindSession(s, own.Key) is { } row && Near(row.Volume, original.Volume, .003f),
                () => WriteSession(enumerator, deviceId, instanceId, original), "owned session external volume notification");
            var notified = await notification;
            var notificationReadback = ReadSession(enumerator, deviceId, instanceId);
            Require(Near(notificationReadback.Volume, original.Volume, .003f), "External session write did not reach independent ISimpleAudioVolume readback.");
            Require(Near(FindSession(notified, own.Key)!.Volume, notificationReadback.Volume, .003f), "AudioService missed the external owned-session volume notification.");
            Console.WriteLine($"session-notification key={own.Key} volume={notificationReadback.Volume:F3} PASS");
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        var restoreErrors = new List<Exception>();
        try
        {
            await NextStateAsync(service,
                s => FindSession(s, own.Key) is { } row && Near(row.Volume, original.Volume, .003f) && row.Muted == original.Muted,
                () => service.SetSession(own.Key, volume: original.Volume, mute: original.Muted), "owned session service restore");
        }
        catch (Exception ex) { restoreErrors.Add(ex); }
        try { WriteSession(enumerator, deviceId, instanceId, original); }
        catch (Exception ex) { restoreErrors.Add(ex); }
        try
        {
            var restored = ReadSession(enumerator, deviceId, instanceId);
            Require(Near(restored.Volume, original.Volume, .003f) && restored.Muted == original.Muted,
                "Owned session volume or mute did not restore.");
        }
        catch (Exception ex) { restoreErrors.Add(ex); }
        Console.WriteLine($"session-restore={(restoreErrors.Count == 0 ? "PASS" : "FAIL")} serviceErrors={restoreErrors.Count}");

        if (restoreErrors.Count != 0)
            failure = failure is null
                ? new AggregateException("Owned session cleanup failed.", restoreErrors)
                : new AggregateException(new[] { failure }.Concat(restoreErrors));
        if (failure is not null) throw failure;
    }

    static SessionLevel? FindSession(AudioState state, string key) => state.Sessions.FirstOrDefault(session => session.Key == key);

    static string SessionInstanceId(SessionLevel session)
    {
        int separator = session.Key.IndexOf('|');
        if (separator < 0 || separator == session.Key.Length - 1)
            throw new InvalidOperationException("Owned audio session key has no instance ID.");
        return session.Key[(separator + 1)..];
    }

    static EndpointState ReadSession(MMDeviceEnumerator enumerator, string deviceId, string instanceId) =>
        ReadOrWriteSession(enumerator, deviceId, instanceId, null);

    static void WriteSession(MMDeviceEnumerator enumerator, string deviceId, string instanceId, EndpointState state) =>
        ReadOrWriteSession(enumerator, deviceId, instanceId, state);

    static EndpointState ReadOrWriteSession(MMDeviceEnumerator enumerator, string deviceId, string instanceId, EndpointState? write)
    {
        using var device = enumerator.GetDevice(deviceId);
        var manager = device.AudioSessionManager;
        try
        {
            var sessions = manager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if (!string.Equals(session.GetSessionInstanceIdentifier, instanceId, StringComparison.Ordinal)) continue;
                if (write is { } desired)
                {
                    session.SimpleAudioVolume.Volume = desired.Volume;
                    session.SimpleAudioVolume.Mute = desired.Muted;
                }
                return new(session.SimpleAudioVolume.Volume, session.SimpleAudioVolume.Mute);
            }
        }
        finally { manager.Dispose(); }
        throw new InvalidOperationException("Owned audio session disappeared before independent readback.");
    }

    static void SetEndpoint(MMDeviceEnumerator enumerator, string id, float volume)
    {
        using var device = enumerator.GetDevice(id);
        device.AudioEndpointVolume.MasterVolumeLevelScalar = volume;
    }

    static async Task RestoreAsync(AudioService service, MMDeviceEnumerator enumerator,
        IReadOnlyDictionary<string, string> ids, IReadOnlyDictionary<string, EndpointState> original)
    {
        var serviceRestoreErrors = new List<Exception>();
        try
        {
            await NextStateAsync(service,
                s => FindLevel(s, "Game") is { Available: true } l && l.DeviceId == ids["Game"],
                () => service.Bind("Game", ids["Game"]), "cleanup binding restore");
        }
        catch (Exception ex) { serviceRestoreErrors.Add(ex); }

        foreach (var channel in EndpointChannels)
        {
            try
            {
                await NextStateAsync(service,
                    s => FindLevel(s, channel) is { } l && Near(l.Volume, original[channel].Volume, .003f),
                    () => service.SetLevel(channel, original[channel].Volume), $"cleanup {channel} volume restore");
                if (ReadEndpoints(enumerator, ids)[channel].Muted != original[channel].Muted)
                    await NextStateAsync(service,
                        s => FindLevel(s, channel) is { } l && l.Muted == original[channel].Muted,
                        () => service.ToggleMute(channel), $"cleanup {channel} mute restore");
            }
            catch (Exception ex) { serviceRestoreErrors.Add(ex); }
        }

        var directRestoreErrors = new List<Exception>();
        foreach (var channel in EndpointChannels)
        {
            try
            {
                using var device = enumerator.GetDevice(ids[channel]);
                device.AudioEndpointVolume.MasterVolumeLevelScalar = original[channel].Volume;
                device.AudioEndpointVolume.Mute = original[channel].Muted;
            }
            catch (Exception ex) { directRestoreErrors.Add(new InvalidOperationException($"Direct restore failed for {channel}: {ex.Message}", ex)); }
        }

        foreach (var channel in EndpointChannels)
        {
            try
            {
                using var device = enumerator.GetDevice(ids[channel]);
                var restored = new EndpointState(device.AudioEndpointVolume.MasterVolumeLevelScalar, device.AudioEndpointVolume.Mute);
                Require(Near(restored.Volume, original[channel].Volume, .012f) && restored.Muted == original[channel].Muted,
                    $"Original volume or mute did not restore for {channel}.");
            }
            catch (Exception ex) { directRestoreErrors.Add(ex); }
        }

        Console.WriteLine($"restore={(directRestoreErrors.Count == 0 ? "PASS" : "FAIL")} channels={EndpointChannels.Length} serviceErrors={serviceRestoreErrors.Count}");
        if (directRestoreErrors.Count != 0)
            serviceRestoreErrors.AddRange(directRestoreErrors);
        if (serviceRestoreErrors.Count != 0)
            throw new AggregateException("Audio state cleanup had errors; see inner messages for direct restore failures.", serviceRestoreErrors);
    }

    static void CheckVolumeChange(string target, float expected, IReadOnlyDictionary<string, EndpointState> before,
        IReadOnlyDictionary<string, EndpointState> after)
    {
        foreach (var channel in EndpointChannels)
        {
            if (channel == target)
            {
                Require(!Near(after[channel].Volume, before[channel].Volume, .003f), $"{target} endpoint did not change.");
                Require(Near(after[channel].Volume, expected, .012f), $"{target} endpoint volume was {after[channel].Volume:F3}, expected about {expected:F3}.");
                Require(after[channel].Muted == before[channel].Muted, $"{target} endpoint mute changed during volume adjustment.");
            }
            else
                Require(Near(after[channel].Volume, before[channel].Volume, .003f) && after[channel].Muted == before[channel].Muted,
                    $"Changing {target} also changed {channel}.");
        }
    }

    static void CheckMuteChange(string target, bool expectedMute, IReadOnlyDictionary<string, EndpointState> before,
        IReadOnlyDictionary<string, EndpointState> after)
    {
        foreach (var channel in EndpointChannels)
        {
            if (channel == target)
                Require(after[channel].Muted == expectedMute && Near(after[channel].Volume, before[channel].Volume, .003f),
                    $"{target} mute toggle changed the wrong state.");
            else
                Require(Near(after[channel].Volume, before[channel].Volume, .003f) && after[channel].Muted == before[channel].Muted,
                    $"Toggling {target} also changed {channel}.");
        }
    }

    static void CheckUnchanged(IReadOnlyDictionary<string, EndpointState> before,
        IReadOnlyDictionary<string, EndpointState> after, string step)
    {
        foreach (var channel in EndpointChannels)
            Require(Near(after[channel].Volume, before[channel].Volume, .003f) && after[channel].Muted == before[channel].Muted,
                $"{step} changed {channel} endpoint state.");
    }

    static Level? FindLevel(AudioState state, string channel) => state.Channels.FirstOrDefault(level => level.Key == channel);
    static bool Near(float left, float right, float tolerance) => Math.Abs(left - right) <= tolerance;
    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    sealed class SilenceWaveProvider : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = new(44100, 16, 2);
        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }
}
