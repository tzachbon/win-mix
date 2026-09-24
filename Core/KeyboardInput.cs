namespace Mix.Core;

// Owns the transition between recording and normal input, including pending keyups.
public sealed class KeyboardInput
{
    readonly HashSet<int> held = new();
    public Gesture Gesture { get; }
    public ShortcutRecorder Recorder { get; } = new();
    public KeyboardInput(KeyboardBindings bindings) => Gesture = new(bindings);

    public void InitializeHeld(IEnumerable<int> keys)
    {
        Recorder.Cancel();
        held.Clear();
        foreach (int key in keys)
            if (key >= 8 && key is not (0x10 or 0x11 or 0x12)) held.Add(key);
        Recorder.InitializeHeld(held);
        Gesture.InitializeHeld(held);
    }

    public void BeginRecording()
    {
        Gesture.Reset();
        Recorder.Start(held);
    }

    public void CancelRecording()
    {
        Recorder.Cancel();
        Gesture.InitializeHeld(held);
    }

    public void SetBindings(KeyboardBindings bindings)
    {
        CancelRecording();
        Gesture.SetBindings(bindings);
        Gesture.InitializeHeld(held);
    }

    public KeyDecision Key(int key, bool down, bool injected = false)
    {
        if (injected) return default;
        if (down) held.Add(key); else held.Remove(key);
        bool recording = Recorder.Recording;
        bool consumed = Recorder.Key(key, down);
        if (recording || consumed) Gesture.Reset();
        var decision = Gesture.Key(key, down);
        if (recording && !Recorder.Recording) Gesture.InitializeHeld(held);
        return new(consumed || decision.Suppress, decision.Action);
    }
}
