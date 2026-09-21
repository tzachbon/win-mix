using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Mix.Audio;
using Mix.Core;
using Mix.Platform;
using Mix.UI;

namespace Mix;

public partial class App : Application
{
    readonly Preferences preferences;
    KeyboardBindings activeKeyboard;
    DispatcherQueue dispatcher = null!;
    Instance? instance;
    AudioService? audio;
    DesktopHost? host;
    MixerWindow? mixer;
    OverlayWindow? overlay;
    UpdateController? updates;
    AudioState state = new([], [], [], null);
    bool exiting;
    public App()
    {
        if (Environment.GetCommandLineArgs().Contains("--shutdown")) Environment.Exit(Instance.Shutdown());
        UnhandledException += (_, args) =>
        {
            try { Directory.CreateDirectory(Preferences.DirectoryPath); File.WriteAllText(Path.Combine(Preferences.DirectoryPath, "last-error.txt"), args.Exception.ToString()); }
            catch { }
        };
        InitializeComponent();
        preferences = Preferences.Load();
        activeKeyboard = preferences.Keyboard;
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        dispatcher = DispatcherQueue.GetForCurrentThread();
        instance = new Instance(() => Queue(Quit), () => Queue(() => OpenMixer(false)));
        updates = new UpdateController();
        updates.Changed += value => Queue(() => mixer?.SetUpdateState(value));
        updates.Cleanup();
        audio = new AudioService(preferences.Bindings);
        audio.Changed += value => Queue(() => Update(value));
        host = new DesktopHost(activeKeyboard);
        host.Command += command => Queue(() => Handle(command));
        try { host.Start(); }
        catch (Exception ex) { OpenMixer(false); mixer!.SetError("Quick controls could not start: " + ex.Message); }
        audio.Refresh();
        if (Environment.GetCommandLineArgs().Contains("--updated"))
        {
            updates.MarkUpdated();
            OpenMixer(true);
        }
        else if (!Environment.GetCommandLineArgs().Contains("--background")) OpenMixer(false);
    }
    void Queue(Action action) => dispatcher.TryEnqueue(() => { if (!exiting) action(); });
    void Update(AudioState value)
    {
        state = value;
        bool discovered = false;
        foreach (var level in value.Channels)
            if (!preferences.Bindings.ContainsKey(level.Key) && level.DeviceId != null)
            { preferences.Bindings[level.Key] = level.DeviceId; discovered = true; }
        if (discovered) Save();
        mixer?.Update(value);
        overlay?.Update(value, preferences.Selected);
    }
    void Save()
    {
        try { preferences.Save(); }
        catch (Exception ex) { OpenMixer(false); mixer!.SetError("Could not save settings: " + ex.Message); }
    }
    void OpenMixer(bool settings)
    {
        if (mixer == null)
        {
            mixer = new MixerWindow();
            mixer.ConfigureKeyboard(activeKeyboard, owner => host!.RecordAsync(owner), () => host!.CancelRecording(), ApplyKeyboard);
            mixer.UpdateRequested += async () => await updates!.RunAsync();
            mixer.UpdateCanceled += () => updates!.Cancel();
            mixer.LevelChanged += (key, value) => audio!.SetLevel(key, value);
            mixer.MuteRequested += key => audio!.ToggleMute(key);
            mixer.SessionLevelChanged += (key, value) => audio!.SetSession(key, volume: value);
            mixer.SessionMuteChanged += (key, value) => audio!.SetSession(key, mute: value);
            mixer.DeviceChanged += (key, id) => { preferences.Bindings[key] = id; Save(); audio!.Bind(key, id); };
            mixer.StartupChanged += enabled =>
            {
                try { Preferences.SetStartup(enabled); }
                catch (Exception ex) { mixer.SetError("Could not update startup: " + ex.Message); }
                mixer.SetStartupEnabled(Preferences.StartupEnabled);
            };
        }
        mixer.Update(state);
        mixer.SetShortcutUsed(preferences.HasUsedShortcut);
        mixer.SetStartupEnabled(Preferences.StartupEnabled);
        if (updates != null) mixer.SetUpdateState(updates.State);
        mixer.ShowSettings(settings);
        mixer.Activate();
    }
    async Task<string?> ApplyKeyboard(KeyboardBindings bindings)
    {
        string? error = await preferences.ApplyKeyboardAsync(bindings, value => host!.SetBindingsAsync(value));
        if (error == null)
        {
            activeKeyboard = bindings;
            mixer?.SetKeyboardBindings(bindings);
            overlay?.SetKeyboardBindings(bindings);
        }
        return error;
    }
    void Handle(HostCommand command)
    {
        string channel = MixRules.Channels[preferences.Selected];
        switch (command.Kind)
        {
            case "Show":
                if (overlay == null) { overlay = new OverlayWindow(); overlay.BoundsChanged += bounds => host!.SetBounds(bounds); }
                overlay.SetKeyboardBindings(activeKeyboard);
                overlay.Update(state, preferences.Selected);
                host!.SetBounds(overlay.ShowAtBottom());
                if (!preferences.HasUsedShortcut)
                {
                    preferences.HasUsedShortcut = true;
                    mixer?.SetShortcutUsed(true);
                    Save();
                }
                break;
            case "Hide": overlay?.AppWindow.Hide(); Save(); break;
            case "Left": Select(MixRules.MoveSelection(preferences.Selected, -1)); break;
            case "Right": Select(MixRules.MoveSelection(preferences.Selected, 1)); break;
            case "Select": Select(command.Value); break;
            case "Up": audio!.Adjust(channel, 1); break;
            case "Down": audio!.Adjust(channel, -1); break;
            case "Adjust": audio!.Adjust(channel, command.Value); break;
            case "Mute": audio!.ToggleMute(channel); break;
            case "Mixer": OpenMixer(false); break;
            case "Settings": OpenMixer(true); break;
            case "Refresh": audio!.Refresh(); break;
            case "Exit": Quit(); break;
        }
    }
    void Select(int selected)
    {
        preferences.Selected = Math.Clamp(selected, 0, 3);
        overlay?.Update(state, preferences.Selected);
    }
    void Quit()
    {
        if (exiting) return;
        exiting = true;
        updates?.Dispose();
        host?.Dispose();
        audio?.Dispose();
        overlay?.Destroy();
        mixer?.Destroy();
        // Keep the instance marker alive until process exit so installers wait for the process.
        GC.KeepAlive(instance);
        Environment.Exit(0);
    }
}
