using System.Diagnostics;
using System.Runtime.InteropServices;
using Mix.Audio;
using Mix.Core;
using Mix.Platform;
using System.Security.Cryptography;
using System.Text;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Windows.Media.Audio;
using Windows.Media.Render;
using Windows.Storage;

static class RoutingProbe
{
    public static int Play()
    {
        string file = Path.Combine(Path.GetTempPath(), "mix-route-player-" + Environment.ProcessId + ".wav");
        try
        {
            using (var writer = new BinaryWriter(File.Create(file)))
            {
                writer.Write("RIFF"u8); writer.Write(96036); writer.Write("WAVEfmt "u8); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(48000); writer.Write(96000);
                writer.Write((short)2); writer.Write((short)16); writer.Write("data"u8); writer.Write(96000); writer.Write(new byte[96000]);
            }
            var created = AudioGraph.CreateAsync(new AudioGraphSettings(AudioRenderCategory.Media)).AsTask().GetAwaiter().GetResult();
            Require(created.Status == AudioGraphCreationStatus.Success, "Default-output AudioGraph creation failed.");
            using var graph = created.Graph;
            var outputResult = graph.CreateDeviceOutputNodeAsync().AsTask().GetAwaiter().GetResult();
            Require(outputResult.Status == AudioDeviceNodeCreationStatus.Success, "Default-output node creation failed.");
            using var output = outputResult.DeviceOutputNode;
            var storage = StorageFile.GetFileFromPathAsync(file).AsTask().GetAwaiter().GetResult();
            var inputResult = graph.CreateFileInputNodeAsync(storage).AsTask().GetAwaiter().GetResult();
            Require(inputResult.Status == AudioFileNodeCreationStatus.Success, "Silent input creation failed.");
            using var input = inputResult.FileInputNode;
            input.LoopCount = null;
            input.AddOutgoingConnection(output);
            graph.Start();
            Console.WriteLine("playing");
            Console.ReadLine();
            return 0;
        }
        finally { File.Delete(file); }
    }

    public static int Run()
    {
        string observer = Path.Combine(AppContext.BaseDirectory, "mix-route-observer-" + Guid.NewGuid().ToString("N") + ".exe");
        File.Copy(Environment.ProcessPath!, observer);
        string settings = Path.Combine(Path.GetTempPath(), "mix-routing-settings-" + Guid.NewGuid().ToString("N") + ".json");
        Process? player = null;
        AudioService? service = null;
        string?[]? original = null;
        using var policy = new AudioRouting();
        using var endpoints = new MMDeviceEnumerator();
        var levels = new Dictionary<string,(float Volume, bool Mute)>();
        string defaults = ObserveDefaults(observer);
        AudioState? state = null;
        try
        {
            service = new AudioService(new());
            service.Changed += value => Volatile.Write(ref state, value);
            service.Refresh();
            Wait(() => Volatile.Read(ref state)?.Channels.All(x => x.Available) == true, "available channels");
            var snapshot = Volatile.Read(ref state)!;
            foreach (var device in snapshot.Devices)
            {
                using var endpoint = endpoints.GetDevice(device.Id);
                levels[device.Id] = (endpoint.AudioEndpointVolume.MasterVolumeLevelScalar, endpoint.AudioEndpointVolume.Mute);
            }
            player = StartPlayer();
            uint pid = (uint)player.Id;
            Wait(() => Volatile.Read(ref state)!.Sessions.Any(x => x.ProcessId == pid && x.Routable), "owned session discovery including unbound outputs");
            original = Enumerable.Range(0, 3).Select(role => policy.Read(pid, role)).ToArray();
            Console.WriteLine("PASS owned Default-output player discovered; original role preferences captured");
            string path = Environment.ProcessPath!;
            foreach (string channel in new[] { "Game", "Chat", "Media" })
            {
                string target = snapshot.Channels.Single(x => x.Key == channel).DeviceId!;
                service.ConfigureAppChannels(new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) { [path] = channel }, path);
                Wait(() => Enumerable.Range(0, 3).All(role => StringComparer.OrdinalIgnoreCase.Equals(policy.Read(pid, role), AudioRouting.Wrap(target))), "saved three-role assignment to " + channel);
                Wait(() => ActualOn(target, pid), "live session move to " + channel);
                Wait(() => Volatile.Read(ref state)!.Sessions.Any(x => x.ProcessId == pid && x.DeviceId == target), "service observes " + channel);
                Require(Enumerable.Range(0, 3).All(role => StringComparer.OrdinalIgnoreCase.Equals(policy.Read(pid, role), AudioRouting.Wrap(target))), "Role preference mismatch.");
                Console.WriteLine("PASS live Default-output session moved to " + channel + "; all three roles verified");
            }
            StopPlayer(player); player.Dispose(); player = null;
            service.Dispose(); service = null;
            player = StartPlayer();
            uint newPid = (uint)player.Id;
            Require(newPid != pid, "Player PID did not change.");
            string media = snapshot.Channels.Single(x => x.Key == "Media").DeviceId!;
            Wait(() => ActualOn(media, newPid), "saved Windows preference after new-PID restart without routing writes");
            Console.WriteLine("PASS Windows preference survives player restart without a routing write");

            StopPlayer(player); player.Dispose(); player = null;
            state = null;
            string chat = snapshot.Channels.Single(x => x.Key == "Chat").DeviceId!;
            var preferences = new Preferences();
            Require(preferences.SetAppChannel(path, "Chat", settings) == null, "Offline assignment did not save.");
            service = new AudioService(new(), appChannels: Preferences.Load(settings).AppChannels);
            service.Changed += value => Volatile.Write(ref state, value);
            service.Refresh();
            player = StartPlayer();
            uint offlinePid = (uint)player.Id;
            Wait(() => ActualOn(chat, offlinePid), "offline assignment applied on first audio session");
            Console.WriteLine("PASS offline saved assignment applied to new playback process");
            foreach (var pair in levels)
            {
                using var endpoint = endpoints.GetDevice(pair.Key);
                Require(Math.Abs(endpoint.AudioEndpointVolume.MasterVolumeLevelScalar - pair.Value.Volume) < .001 && endpoint.AudioEndpointVolume.Mute == pair.Value.Mute,
                    "Endpoint volume or mute changed.");
            }
            Require(defaults == ObserveDefaults(observer), "Independent global default output changed.");
            Console.WriteLine("PASS endpoint levels, mute, and global default outputs unchanged");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("FAIL routing: " + error.Message);
            Console.Error.WriteLine("snapshot error=" + Volatile.Read(ref state)?.Error);
            if (player != null)
            {
                var own = Volatile.Read(ref state)?.Sessions.Where(x => x.ProcessId == (uint)player.Id || x.ExecutablePath == Environment.ProcessPath).ToArray() ?? [];
                foreach (var row in own) Console.Error.WriteLine($"owned session routable={row.Routable} hasPath={row.ExecutablePath != null} identity={row.ProcessStarted != 0} system={row.SystemSounds}");
            }
            foreach (var failure in Volatile.Read(ref state)?.RoutingErrors?.Values ?? []) Console.Error.WriteLine("worker: " + failure);
            return 1;
        }
        finally
        {
            service?.Dispose();
            try
            {
                if (original != null)
                {
                    if (player == null || player.HasExited) { player?.Dispose(); player = StartPlayer(); }
                    policy.Restore((uint)player.Id, original);
                    Require(Enumerable.Range(0, 3).All(role => policy.Read((uint)player.Id, role) == original[role]), "Original preferences failed to restore.");
                    Console.WriteLine("PASS original per-role preferences restored and independently read back");
                }
            }
            finally
            {
                if (player != null) { StopPlayer(player); player.Dispose(); }
                File.Delete(observer); File.Delete(settings); File.Delete(settings + ".tmp");
            }
        }
    }

    static Process StartPlayer()
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--route-player");
        var process = Process.Start(start)!;
        var ready = process.StandardOutput.ReadLineAsync();
        if (!ready.Wait(TimeSpan.FromSeconds(8)) || ready.Result != "playing" || process.HasExited)
        {
            StopPlayer(process); process.Dispose();
            throw new Exception("Owned player did not start.");
        }
        return process;
    }

    public static string Defaults()
    {
        using var enumerator = new MMDeviceEnumerator();
        string value = string.Join('|', Enum.GetValues<Role>().Select(role =>
        { using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, role); return device.ID; }));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    static string ObserveDefaults(string executable)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        start.ArgumentList.Add("--routing-observer");
        using var process = Process.Start(start)!;
        string? result = process.StandardOutput.ReadLine();
        Require(process.WaitForExit(5000) && process.ExitCode == 0 && result?.Length == 64, "Independent defaults observer failed.");
        return result!;
    }

    static void StopPlayer(Process process)
    {
        if (process.HasExited) return;
        process.StandardInput.WriteLine("stop"); process.StandardInput.Flush();
        if (!process.WaitForExit(5000)) { process.Kill(); process.WaitForExit(); }
    }

    static bool ActualOn(string endpoint, uint pid)
    {
        foreach (var session in SessionCatalog.Read(endpoint))
            if (session.GetProcessId(out var found) == 0 && found == pid && session.GetState(out var state) == 0 && state == AudioSessionState.AudioSessionStateActive) return true;
        return false;
    }

    static void Wait(Func<bool> condition, string description)
    {
        var watch = Stopwatch.StartNew();
        do { if (condition()) return; Thread.Sleep(50); } while (watch.Elapsed < TimeSpan.FromSeconds(10));
        throw new TimeoutException(description);
    }

    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
