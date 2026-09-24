using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Mix.Core;
using Mix.Platform;

internal static class GestureTests
{
    private const int LControl = 0xA2;
    private const int LAlt = 0xA4;
    private const int RAlt = 0xA5;
    private const int Left = 0x25;
    private const int Up = 0x26;
    private const int Right = 0x27;
    private const int Down = 0x28;
    private const int M = 0x4D;
    private const int A = 0x41;

    private static async Task<int> Main()
    {
        ModifierOrderActivates();
        AltGrDoesNotActivate();
        InitializeHeldWaitsForNeutral();
        UnrelatedKeyPreventsActivation();
        GestureKeysOwnTheirKeyUps();
        RepeatsAndMuteToggleOnce();
        CancelPassesThroughAndHides();
        EscapeConsumesAndHides();
        ResetPreservesOwnedKeyUpsAndWaitsForNeutral();
        WheelAccumulatesAndResetsOnDisarm();
        KeyboardBindingsAreImmutableAndValidated();
        GestureUsesEditableBindings();
        ShortcutRecorderCapturesAndDrains();
        KeyboardBindingsSerialize();
        await KeyboardPreferencesPersistSafely();
        MixRulesRemainDeterministic();
        StartupApprovalIsConservative();
        KeyboardInputTests.Run();
        KeyboardEditorTests.Run();
        Console.WriteLine("Gesture state machine checks passed.");
        return 0;
    }

    private static void ModifierOrderActivates()
    {
        Gesture gesture = Armed();
        Is(default(KeyDecision), gesture.Key(LControl, true), "left Ctrl passes through");
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(LAlt, true), "Ctrl then Alt shows and consumes completing key");
        Is(true, gesture.Active, "gesture active after Ctrl then Alt");

        gesture = Armed();
        Is(default(KeyDecision), gesture.Key(LAlt, true), "left Alt passes through");
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(LControl, true), "Alt then Ctrl shows and consumes completing key");
        Is(true, gesture.Active, "gesture active after Alt then Ctrl");
    }

    private static void AltGrDoesNotActivate()
    {
        Gesture gesture = Armed();
        Is(default(KeyDecision), gesture.Key(LAlt, true), "left Alt passes through");
        Is(default(KeyDecision), gesture.Key(LControl, true, injected: true), "injected Ctrl is ignored");
        Is(default(KeyDecision), gesture.Key(RAlt, true), "right Alt passes through");
        Is(false, gesture.Active, "AltGr does not activate gesture");

        gesture.Key(RAlt, false);
        gesture.Key(LAlt, false);
        Is(default(KeyDecision), gesture.Key(LControl, true), "physical Ctrl passes through");
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(LAlt, true), "physical left chord activates");

        gesture = Armed();
        gesture.Key(RAlt, true);
        gesture.Key(LControl, true);
        Is(default(KeyDecision), gesture.Key(LAlt, true), "held right Alt prevents the left chord");
        Is(false, gesture.Active, "right Alt cannot complete the chord");
        gesture.Key(RAlt, false);
        Is(false, gesture.Active, "releasing right Alt does not synthesize activation");
    }

    private static void InitializeHeldWaitsForNeutral()
    {
        var gesture = new Gesture();
        gesture.InitializeHeld([LControl, LAlt, RAlt, 0x10, 0x11, 0x12, 1, 2, 4]);
        Is(false, gesture.Active, "seeded chord does not activate");
        Is(default(KeyDecision), gesture.Key(LAlt, true), "seeded modifier repeat stays inactive");
        Is(default(KeyDecision), gesture.Key(RAlt, false), "right Alt release stays inactive");
        Is(default(KeyDecision), gesture.Key(LControl, false), "Ctrl release stays inactive");
        Is(default(KeyDecision), gesture.Key(LAlt, false), "neutral release stays inactive");
        Is(default(KeyDecision), gesture.Key(LControl, true), "Ctrl after neutral passes through");
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(LAlt, true), "chord works after neutral");
    }

    private static void UnrelatedKeyPreventsActivation()
    {
        Gesture gesture = Armed();
        Is(default(KeyDecision), gesture.Key(A, true), "unrelated key passes through");
        Is(default(KeyDecision), gesture.Key(LControl, true), "Ctrl with unrelated key passes through");
        Is(default(KeyDecision), gesture.Key(LAlt, true), "unrelated held key prevents activation");
        Is(false, gesture.Active, "unrelated held key blocks chord");

        gesture.Key(A, false);
        gesture.Key(LAlt, false);
        gesture.Key(LControl, false);
        gesture.Key(LControl, true);
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(LAlt, true), "chord works after full neutral");
    }

    private static void GestureKeysOwnTheirKeyUps()
    {
        Gesture gesture = ActiveGesture();
        Is(new KeyDecision(true, GestureAction.Left), gesture.Key(Left, true), "left is consumed");
        Is(new KeyDecision(true, GestureAction.Hide), gesture.Key(LAlt, false), "consumed opening-key release hides");
        Is(false, gesture.Active, "modifier release disarms");
        Is(new KeyDecision(true, null), gesture.Key(Left, false), "left release stays consumed after hide");
        Is(default(KeyDecision), gesture.Key(Left, false), "ownership ends after matching keyup");
    }

    private static void RepeatsAndMuteToggleOnce()
    {
        Gesture gesture = ActiveGesture();
        Is(new KeyDecision(true, GestureAction.Right), gesture.Key(Right, true), "first arrow press");
        Is(new KeyDecision(true, GestureAction.Right), gesture.Key(Right, true), "arrow repeat repeats action");
        Is(new KeyDecision(true, null), gesture.Key(Right, false), "arrow keyup consumed");

        Is(new KeyDecision(true, GestureAction.Mute), gesture.Key(M, true), "first M press toggles");
        Is(new KeyDecision(true, null), gesture.Key(M, true), "M repeat does not toggle");
        Is(new KeyDecision(true, null), gesture.Key(M, false), "M keyup consumed");
        Is(new KeyDecision(true, GestureAction.Mute), gesture.Key(M, true), "new M press toggles again");

        Is(new KeyDecision(true, GestureAction.Up), gesture.Key(Up, true), "up action");
        Is(new KeyDecision(true, GestureAction.Down), gesture.Key(Down, true), "down action");
    }

    private static void CancelPassesThroughAndHides()
    {
        Gesture gesture = ActiveGesture();
        Is(new KeyDecision(true, GestureAction.Up), gesture.Key(Up, true), "owned key starts");
        Is(new KeyDecision(false, GestureAction.Hide), gesture.Key(A, true), "unrelated key cancels and passes through");
        Is(false, gesture.Active, "unrelated key cancels gesture");
        Is(new KeyDecision(true, null), gesture.Key(Up, false), "owned keyup survives cancel");
        Is(default(KeyDecision), gesture.Key(A, false), "unrelated keyup passes through");
        Is(new KeyDecision(true, null), gesture.Key(LAlt, false), "owned opening release stays consumed after cancel");
        Is(default(KeyDecision), gesture.Key(LAlt, true), "remaining held Ctrl cannot reactivate after cancel");
        Is(false, gesture.Active, "cancel requires full neutral before rearming");
        gesture.Key(LAlt, false);
        gesture.Key(LControl, false);
        gesture.Key(LControl, true);
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(LAlt, true), "cancel rearms after full neutral");
    }

    private static void EscapeConsumesAndHides()
    {
        Gesture gesture = ActiveGesture();
        Is(new KeyDecision(true, GestureAction.Hide), gesture.Key(0x1B, true), "Escape is consumed and hides");
        Is(false, gesture.Active, "Escape disarms gesture");
        Is(new KeyDecision(true, null), gesture.Key(0x1B, false), "Escape keyup is consumed");
        Is(default(KeyDecision), gesture.Key(LControl, true), "held Ctrl remains disarmed after Escape");
        Is(new KeyDecision(true, null), gesture.Key(LAlt, true), "owned opening-key repeat stays consumed after Escape");

        gesture.Key(LControl, false);
        gesture.Key(LAlt, false);
        gesture.Key(LControl, true);
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(LAlt, true), "gesture rearms after Escape neutral");
    }

    private static void ResetPreservesOwnedKeyUpsAndWaitsForNeutral()
    {
        Gesture gesture = ActiveGesture();
        Is(new KeyDecision(true, GestureAction.Left), gesture.Key(Left, true), "left key starts");
        gesture.Reset();
        Is(false, gesture.Active, "reset disarms gesture");
        Is(new KeyDecision(true, null), gesture.Key(Left, false), "reset preserves the owned keyup");
        Is(default(KeyDecision), gesture.Key(Left, false), "reset does not leave keyup suppression stuck");
        Is(default(KeyDecision), gesture.Key(LControl, true), "held Ctrl repeat remains unarmed");
        Is(new KeyDecision(true, null), gesture.Key(LAlt, true), "owned opening-key repeat remains consumed while unarmed");
        Is(false, gesture.Active, "held chord cannot rearm before neutral");

        gesture.Key(LAlt, false);
        gesture.Key(LControl, false);
        Is(default(KeyDecision), gesture.Key(LControl, true), "Ctrl after neutral passes through");
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(LAlt, true), "gesture rearms after neutral");
    }

    private static void WheelAccumulatesAndResetsOnDisarm()
    {
        Gesture gesture = ActiveGesture();
        Is(0, gesture.Wheel(60), "partial wheel step");
        Is(1, gesture.Wheel(60), "positive wheel step");
        Is(-1, gesture.Wheel(-120), "negative wheel step");
        Is(0, gesture.Wheel(60), "new partial wheel step");
        Is(new KeyDecision(false, GestureAction.Hide), gesture.Key(LControl, false), "modifier release hides");
        Is(0, gesture.Wheel(120), "inactive wheel is ignored");

        gesture.Key(LAlt, false);
        Is(default(KeyDecision), gesture.Key(LControl, true), "Ctrl after neutral passes through");
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(LAlt, true), "gesture can activate again");
        Is(0, gesture.Wheel(60), "disarm clears partial wheel remainder");
        Is(1, gesture.Wheel(60), "fresh partials make a step");
    }

    private static void KeyboardBindingsAreImmutableAndValidated()
    {
        int[] source = [LControl, LAlt];
        var bindings = new KeyboardBindings(source, M);
        source[0] = A;
        Is(LControl, bindings.Opening[0], "binding clones its opening keys");
        if (bindings.Opening is IList<int> opening)
            Throws<NotSupportedException>(() => opening[0] = A, "binding snapshot is read-only");

        Is<string?>(null, KeyboardBindings.Default.Validate(), "default bindings are valid");
        Is("Left Ctrl + Left Alt", bindings.OpeningLabel, "opening chord label");
        Is("M", bindings.MuteLabel, "mute label");
        Is(true, bindings.SameAs(new KeyboardBindings([LAlt, LControl], M)), "chord equality ignores press order");
        Is<string?>(null, new KeyboardBindings([0xA3, 0xA5], 0x4B).Validate(), "right-side modifier chord is valid");
        Is<string?>(null, new KeyboardBindings([0xA2], 0x4B).Validate(), "modifier-only shortcut is valid");
        Is<string?>(null, new KeyboardBindings([A, 0x42], 0x4B).Validate(), "arbitrary multi-key chord is valid");

        Is(true, new KeyboardBindings([], M).Validate() is not null, "empty opening chord is invalid");
        Is(true, new KeyboardBindings([A, A], M).Validate()?.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true,
            "duplicate opening keys are invalid");
        Is(true, new KeyboardBindings([1], M).Validate() is not null, "mouse VK is invalid");
        Is(true, new KeyboardBindings([0x11], 0x4B).Validate()?.Contains("invalid", StringComparison.OrdinalIgnoreCase) == true,
            "generic modifier aliases are invalid");
        Is(true, new KeyboardBindings([A], 0xFF).Validate() is not null, "out-of-range VK is invalid");
        Is(true, new KeyboardBindings([Left], M).Validate() is not null, "opening arrows are reserved");
        Is(true, new KeyboardBindings([0x1B], M).Validate() is not null, "opening Escape is reserved");
        Is(true, new KeyboardBindings([A], Up).Validate() is not null, "mute arrows are reserved");
        Is(true, new KeyboardBindings([A], 0x1B).Validate() is not null, "mute Escape is reserved");
        Is(true, new KeyboardBindings([A, M], M).Validate()?.Contains("share", StringComparison.OrdinalIgnoreCase) == true,
            "opening and mute keys cannot overlap");

        Is(true, new KeyboardBindings([LControl, RAlt, 0x2E], M).Validate()?.Contains("Ctrl+Alt+Delete", StringComparison.Ordinal) == true,
            "Ctrl+Alt+Delete is rejected across modifier sides");
        Is(true, new KeyboardBindings([LControl, LAlt], 0x2E).Validate()?.Contains("Ctrl+Alt+Delete", StringComparison.Ordinal) == true,
            "mute cannot complete Ctrl+Alt+Delete");
        Is(true, new KeyboardBindings([0x5C], 0x4C).Validate()?.Contains("Win+L", StringComparison.Ordinal) == true,
            "mute cannot complete Win+L");
        Is("VK 0x92", KeyboardBindings.KeyLabel(0x92), "uncommon VK has a stable fallback label");
    }

    private static void GestureUsesEditableBindings()
    {
        var gesture = new Gesture(new KeyboardBindings([A, 0x42], 0x4B));
        gesture.InitializeHeld(Array.Empty<int>());
        Is(default(KeyDecision), gesture.Key(A, true), "first chord key passes through");
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(0x42, true), "completing chord key shows overlay");
        Is(new KeyDecision(true, null), gesture.Key(0x42, true), "completing key repeats are consumed");
        Is(new KeyDecision(true, GestureAction.Mute), gesture.Key(0x4B, true), "configured mute key toggles");
        Is(new KeyDecision(true, null), gesture.Key(0x4B, true), "configured mute repeats only once");
        Is(new KeyDecision(true, null), gesture.Key(0x4B, false), "configured mute release is consumed");
        Is(new KeyDecision(true, GestureAction.Left), gesture.Key(Left, true), "navigation remains available");
        Is(new KeyDecision(true, null), gesture.Key(Left, false), "navigation release is consumed");
        Is(new KeyDecision(false, GestureAction.Hide), gesture.Key(A, false), "any opening release hides");
        Is(false, gesture.Active, "opening release hides active overlay");
        Is(new KeyDecision(true, null), gesture.Key(0x42, false), "completing key release stays consumed after hide");

        gesture = new Gesture(new KeyboardBindings([0x77], LControl));
        gesture.InitializeHeld(Array.Empty<int>());
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(0x77, true), "function key opens a custom binding");
        Is(new KeyDecision(true, GestureAction.Mute), gesture.Key(LControl, true), "modifier can be the mute key");
        Is(new KeyDecision(true, null), gesture.Key(LControl, false), "mute modifier release stays consumed");
        Is(true, gesture.Active, "mute modifier release does not hide the overlay");
        Is(new KeyDecision(true, GestureAction.Hide), gesture.Key(0x77, false), "opening function-key release hides");

        gesture = ActiveGesture();
        gesture.SetBindings(new KeyboardBindings([0x77], 0x4B));
        Is(false, gesture.Active, "rebinding hides overlay");
        Is(new KeyDecision(true, null), gesture.Key(LAlt, false), "old completing key release stays owned");
        gesture.Key(LControl, false);
        Is(new KeyDecision(true, GestureAction.Show), gesture.Key(0x77, true), "new single-key shortcut activates after neutral");
        Is(new KeyDecision(true, GestureAction.Hide), gesture.Key(0x77, false), "single-key release hides and is consumed");
    }

    private static void ShortcutRecorderCapturesAndDrains()
    {
        var recorder = new ShortcutRecorder();
        recorder.Start([A]);
        Is(true, recorder.Recording, "recorder starts");
        Is(false, recorder.Key(A, false), "pre-held key release passes through");
        Is(true, recorder.Key(LControl, true), "first captured key is consumed");
        Is(true, recorder.Key(0x20, true), "simultaneous captured key is consumed");
        Is(true, recorder.Key(0x20, false), "first captured release is consumed");
        Is<int[]?>(null, recorder.TakeCompleted(), "completion waits for all captured keys to release");
        Is(true, recorder.Key(LControl, false), "last captured release is consumed");
        Is(true, recorder.TakeCompleted()!.SequenceEqual(new[] { LControl, 0x20 }), "recorder captures the simultaneous set");
        Is<int[]?>(null, recorder.TakeCompleted(), "completion is taken once");
        Is(false, recorder.HasOwnedKeys, "all captured releases are drained");

        recorder.Start([]);
        Is(false, recorder.Key(0x42, true, injected: true), "injected keys are ignored");
        Is(false, recorder.Key(0x42, false, injected: true), "injected releases are ignored");
        Is(true, recorder.Key(0x42, true), "recording resumes after ignored injection");
        recorder.Cancel();
        Is(true, recorder.TakeCancelled(), "cancel state is consumable");
        Is(true, recorder.HasOwnedKeys, "cancel preserves a consumed release");
        recorder.Start([0x42]);
        Is(true, recorder.Key(0x42, false), "owned release survives a new recording");
        Is(true, recorder.Key(0x1B, true), "Escape cancels recording");
        Is(false, recorder.Recording, "Escape stops recording");
        Is(true, recorder.TakeCancelled(), "Escape cancellation is consumable");
        Is(true, recorder.Key(0x1B, false), "Escape release stays owned");

        recorder.Start([]);
        recorder.Key(A, true);
        recorder.InitializeHeld([]);
        Is(false, recorder.HasOwnedKeys, "held-state resync clears stale owned releases");
    }

    private static void KeyboardBindingsSerialize()
    {
        var bindings = new KeyboardBindings([LControl, LAlt], 0x4B);
        string json = JsonSerializer.Serialize(bindings);
        KeyboardBindings restored = JsonSerializer.Deserialize<KeyboardBindings>(json)!;
        Is(true, bindings.SameAs(restored), "keyboard bindings round-trip through JSON");
        Is(false, json.Contains(nameof(KeyboardBindings.OpeningLabel), StringComparison.Ordinal), "computed labels are not serialized");
        Is(false, json.Contains(nameof(KeyboardBindings.MuteLabel), StringComparison.Ordinal), "computed labels are not serialized");
    }

    private static async Task KeyboardPreferencesPersistSafely()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"mix-bindings-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var preferences = new Preferences { Selected = 3, HasUsedShortcut = true };
            preferences.Bindings["Game"] = "audio-device";
            var candidate = new KeyboardBindings([0x77], 0x4B);
            bool savedBeforeActivation = false;
            string? error = await preferences.ApplyKeyboardAsync(candidate, value =>
            {
                savedBeforeActivation = Preferences.Load(path).Keyboard.SameAs(value);
                return Task.CompletedTask;
            }, path);
            Is<string?>(null, error, "valid bindings save and activate");
            Is(true, savedBeforeActivation, "candidate is durable before host activation");

            Preferences loaded = Preferences.Load(path);
            Is("audio-device", loaded.Bindings["Game"], "keyboard save preserves audio bindings");
            Is(3, loaded.Selected, "keyboard save preserves selection");
            Is(true, loaded.HasUsedShortcut, "keyboard save preserves tutorial state");
            Is(true, candidate.SameAs(loaded.Keyboard), "keyboard bindings persist");

            File.WriteAllText(path, """{"Bindings":{"Game":"audio-device"},"Selected":1,"HasUsedShortcut":true,"Keyboard":"wrong shape"}""");
            loaded = Preferences.Load(path);
            Is("audio-device", loaded.Bindings["Game"], "wrong keyboard shape keeps valid audio bindings");
            Is(true, KeyboardBindings.Default.SameAs(loaded.Keyboard), "wrong keyboard shape falls back to default");
            File.WriteAllText(path, """{"Bindings":{"Game":"audio-device"},"Keyboard":null}""");
            loaded = Preferences.Load(path);
            Is("audio-device", loaded.Bindings["Game"], "null keyboard settings keep valid audio bindings");
            Is(true, KeyboardBindings.Default.SameAs(loaded.Keyboard), "null keyboard settings fall back to default");
            File.WriteAllText(path, """{"Bindings":{"Game":"audio-device"}}""");
            loaded = Preferences.Load(path);
            Is("audio-device", loaded.Bindings["Game"], "old settings keep valid audio bindings");
            Is(true, KeyboardBindings.Default.SameAs(loaded.Keyboard), "old settings default the keyboard bindings");
            File.WriteAllText(path, """{"Bindings":{"Game":"audio-device"},"Keyboard":{"Opening":[37],"Mute":77}}""");
            loaded = Preferences.Load(path);
            Is("audio-device", loaded.Bindings["Game"], "invalid keyboard values keep valid audio bindings");
            Is(true, KeyboardBindings.Default.SameAs(loaded.Keyboard), "invalid keyboard values fall back to default");

            var pendingCandidate = new KeyboardBindings([0x78], 0x4C);
            var acknowledgement = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<string?> pendingApply = preferences.ApplyKeyboardAsync(pendingCandidate, _ => acknowledgement.Task, path);
            Is(false, pendingApply.IsCompleted, "apply waits for the asynchronous host acknowledgement");
            Is(true, pendingCandidate.SameAs(Preferences.Load(path).Keyboard), "candidate is saved before host acknowledgement");
            acknowledgement.SetResult(true);
            Is<string?>(null, await pendingApply, "host acknowledgement completes apply");

            var next = new KeyboardBindings([0x79], 0x4D);
            error = await preferences.ApplyKeyboardAsync(next, _ => Task.FromException(new InvalidOperationException("host failure")), path);
            Is(true, error?.Contains("Saved, but could not activate", StringComparison.Ordinal) == true,
                "activation error reports that bindings were saved");
            Is(true, next.SameAs(preferences.Keyboard), "activation failure retains the candidate in memory");
            Is(true, next.SameAs(Preferences.Load(path).Keyboard), "activation failure retains the candidate on disk");

            KeyboardBindings previous = preferences.Keyboard;
            int activationCalls = 0;
            error = await preferences.ApplyKeyboardAsync(new KeyboardBindings([0x79], 0x4D), _ =>
            {
                activationCalls++;
                return Task.CompletedTask;
            }, directory);
            Is(true, error?.StartsWith("Could not save keyboard bindings:", StringComparison.Ordinal) == true,
                "save failure is reported");
            Is(0, activationCalls, "save failure prevents host activation");
            Is(true, previous.SameAs(preferences.Keyboard), "save failure restores previous in-memory bindings");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
            if (File.Exists(directory + ".tmp")) File.Delete(directory + ".tmp");
            Directory.Delete(directory);
        }
    }

    private static void MixRulesRemainDeterministic()
    {
        Is(3, MixRules.MoveSelection(0, -1), "Game left selects Main");
        Is(0, MixRules.MoveSelection(3, 1), "Main right selects Game");
        Is(1, MixRules.MoveSelection(0, 1), "Game right selects Chat");
        Is(2, MixRules.MoveSelection(1, 1), "Chat right selects Media");
        Is(3, MixRules.MoveSelection(3, -1), "Main left clamps");
        Is(2, MixRules.MoveSelection(2, 1), "Media right clamps");
        DeviceChoice[] devices =
        [
            new("game", "SteelSeries Sonar - Gaming (USB)"),
            new("master", "Headphones (Arctis Nova Pro Wireless)"),
            new("other", "Headphones (Other Device)"),
            new("sonar", "SteelSeries Sonar - Gaming (Arctis Nova Pro Wireless)")
        ];
        Is("game", MixRules.Discover("Game", [devices[0]]), "unique Sonar match");
        Is<string?>(null, MixRules.Discover("Chat", devices), "missing Sonar match");
        Is("master", MixRules.Discover("Master", devices), "Master uses the exact headphone match");
        Is<string?>(null, MixRules.Discover("Game", [devices[0], new("second", "SteelSeries Sonar - Gaming (USB headset)")]), "ambiguous Sonar match");
        Is(0f, MixRules.Clamp(0), "clamp accepts zero");
        Is(1f, MixRules.Clamp(1), "clamp accepts one");
        Is(0f, MixRules.Clamp(-1), "clamp floors at zero");
        Is(1f, MixRules.Clamp(2), "clamp caps at one");
        Throws<ArgumentOutOfRangeException>(() => MixRules.Clamp(float.NaN), "clamp rejects NaN");
    }

    private static void StartupApprovalIsConservative()
    {
        byte[] State(uint value) { var bytes = new byte[12]; System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, value); return bytes; }
        Is(true, Mix.Platform.Preferences.IsStartupApproved(null), "absent approval defaults enabled");
        foreach (uint value in new uint[] { 2, 6 })
            Is(true, Mix.Platform.Preferences.IsStartupApproved(State(value)), "known enabled approval");
        foreach (uint value in new uint[] { 0, 1, 3, 7, 255 })
            Is(false, Mix.Platform.Preferences.IsStartupApproved(State(value)), "disabled or unknown approval");
        Is(false, Mix.Platform.Preferences.IsStartupApproved(new byte[] { 2 }), "truncated approval");
        Is(false, Mix.Platform.Preferences.IsStartupApproved("2"), "wrong approval type");
    }

    private static Gesture Armed()
    {
        var gesture = new Gesture();
        gesture.InitializeHeld(Array.Empty<int>());
        return gesture;
    }

    private static Gesture ActiveGesture()
    {
        Gesture gesture = Armed();
        gesture.Key(LControl, true);
        gesture.Key(LAlt, true);
        Is(true, gesture.Active, "setup activates gesture");
        return gesture;
    }

    private static void Is<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void Throws<TException>(Action action, string message) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"{message}: expected {typeof(TException).Name}");
    }
}
