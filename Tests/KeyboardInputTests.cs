using Mix.Core;

internal static class KeyboardInputTests
{
    public static void Run()
    {
        var input = new KeyboardInput(KeyboardBindings.Default);
        input.InitializeHeld([]);
        input.BeginRecording();
        Check(input.Key(0xA2, true).Suppress, "record Ctrl");
        Check(input.Key(0x20, true).Suppress, "record Space");
        Check(!input.Gesture.Active, "recording cannot open overlay");
        Check(input.Key(0x20, false).Suppress, "record Space up");
        Check(input.Recorder.TakeCompleted() == null, "wait for every release");
        Check(input.Key(0xA2, false).Suppress, "record Ctrl up");
        Check(input.Recorder.TakeCompleted()!.Order().SequenceEqual(new[] { 0x20, 0xA2 }), "record chord");
        input.Key(0xA2, true);
        Check(input.Key(0xA4, true).Action == GestureAction.Show, "recording returns to neutral");

        input.SetBindings(new KeyboardBindings(new[] { 0x77 }, 0x4B));
        Check(!input.Gesture.Active, "rebind hides");
        Check(input.Key(0xA4, false).Suppress, "rebind drains old completion key");
        input.Key(0xA2, false);
        Check(input.Key(0x77, true) == new KeyDecision(true, GestureAction.Show), "F8 after rebind");
        Check(input.Key(0x4B, true).Action == GestureAction.Mute, "new mute key");
        input.Key(0x4B, false);
        Check(input.Key(0x77, false) == new KeyDecision(true, GestureAction.Hide), "F8 release hides and consumes");

        input.BeginRecording();
        input.Key(0x41, true);
        input.CancelRecording();
        Check(input.Key(0x41, false).Suppress, "focus cancellation owns release");
        Check(input.Key(0x77, true).Action == GestureAction.Show, "focus cancellation rearms at neutral");
        input.Key(0x77, false);

        input.BeginRecording();
        Check(!input.Key(0x42, true, true).Suppress, "injected key passes through");
        input.Key(0x1B, true);
        Check(input.Recorder.TakeCancelled(), "Escape cancels");
        Check(input.Key(0x1B, false).Suppress, "Escape release drains");
        Check(input.Key(0x77, true).Action == GestureAction.Show, "Escape returns to neutral");
        input.Key(0x77, false);

        input.Key(0x41, true);
        input.BeginRecording();
        Check(!input.Key(0x41, false).Suppress, "preheld key release passes through");
        input.Key(0x42, true);
        input.Key(0x42, false);
        Check(input.Recorder.TakeCompleted()!.SequenceEqual(new[] { 0x42 }), "preheld key excluded");

        input.BeginRecording();
        input.Key(0x43, true);
        input.CancelRecording();
        input.BeginRecording();
        Check(input.Key(0x43, false).Suppress, "new recording preserves old owned release");
        input.Key(0x44, true);
        input.Key(0x44, false);
        Check(input.Recorder.TakeCompleted()!.SequenceEqual(new[] { 0x44 }), "new recording waits for old keys");

        input.SetBindings(new KeyboardBindings(new[] { 0xA2, 0x20 }, 0x4B));
        Check(!input.Key(0xA2, true).Suppress, "Ctrl prefix passes through");
        Check(input.Key(0x20, true) == new KeyDecision(true, GestureAction.Show), "Ctrl Space activates");
        Check(input.Key(0x20, false) == new KeyDecision(true, GestureAction.Hide), "Space release hides");
        input.Key(0xA2, false);
        input.BeginRecording();
        input.Key(0x41, true);
        input.InitializeHeld([]); // A release was lost while the session was locked.
        input.BeginRecording();
        input.Key(0x42, true);
        input.Key(0x42, false);
        Check(input.Recorder.TakeCompleted()!.SequenceEqual(new[] { 0x42 }), "session reset clears stale recording ownership");
        Console.WriteLine("Keyboard input integration checks passed.");
    }

    static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
