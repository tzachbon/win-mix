using System;
using Mix.Core;

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

    private static int Main()
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
        MixRulesRemainDeterministic();
        Console.WriteLine("Gesture state machine checks passed.");
        return 0;
    }

    private static void ModifierOrderActivates()
    {
        Gesture gesture = Armed();
        Is(default(KeyDecision), gesture.Key(LControl, true), "left Ctrl passes through");
        Is(new KeyDecision(false, GestureAction.Show), gesture.Key(LAlt, true), "Ctrl then Alt shows");
        Is(true, gesture.Active, "gesture active after Ctrl then Alt");

        gesture = Armed();
        Is(default(KeyDecision), gesture.Key(LAlt, true), "left Alt passes through");
        Is(new KeyDecision(false, GestureAction.Show), gesture.Key(LControl, true), "Alt then Ctrl shows");
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
        Is(new KeyDecision(false, GestureAction.Show), gesture.Key(LAlt, true), "physical left chord activates");

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
        Is(new KeyDecision(false, GestureAction.Show), gesture.Key(LAlt, true), "chord works after neutral");
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
        Is(new KeyDecision(false, GestureAction.Show), gesture.Key(LAlt, true), "chord works after full neutral");
    }

    private static void GestureKeysOwnTheirKeyUps()
    {
        Gesture gesture = ActiveGesture();
        Is(new KeyDecision(true, GestureAction.Left), gesture.Key(Left, true), "left is consumed");
        Is(new KeyDecision(false, GestureAction.Hide), gesture.Key(LAlt, false), "modifier release hides");
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
        Is(default(KeyDecision), gesture.Key(LAlt, false), "modifier release after cancel stays hidden");
        Is(default(KeyDecision), gesture.Key(LAlt, true), "remaining held Ctrl cannot reactivate after cancel");
        Is(false, gesture.Active, "cancel requires full neutral before rearming");
        gesture.Key(LAlt, false);
        gesture.Key(LControl, false);
        gesture.Key(LControl, true);
        Is(new KeyDecision(false, GestureAction.Show), gesture.Key(LAlt, true), "cancel rearms after full neutral");
    }

    private static void EscapeConsumesAndHides()
    {
        Gesture gesture = ActiveGesture();
        Is(new KeyDecision(true, GestureAction.Hide), gesture.Key(0x1B, true), "Escape is consumed and hides");
        Is(false, gesture.Active, "Escape disarms gesture");
        Is(new KeyDecision(true, null), gesture.Key(0x1B, false), "Escape keyup is consumed");
        Is(default(KeyDecision), gesture.Key(LControl, true), "held Ctrl remains disarmed after Escape");
        Is(default(KeyDecision), gesture.Key(LAlt, true), "held Alt remains disarmed after Escape");

        gesture.Key(LControl, false);
        gesture.Key(LAlt, false);
        gesture.Key(LControl, true);
        Is(new KeyDecision(false, GestureAction.Show), gesture.Key(LAlt, true), "gesture rearms after Escape neutral");
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
        Is(default(KeyDecision), gesture.Key(LAlt, true), "held Alt repeat remains unarmed");
        Is(false, gesture.Active, "held chord cannot rearm before neutral");

        gesture.Key(LAlt, false);
        gesture.Key(LControl, false);
        Is(default(KeyDecision), gesture.Key(LControl, true), "Ctrl after neutral passes through");
        Is(new KeyDecision(false, GestureAction.Show), gesture.Key(LAlt, true), "gesture rearms after neutral");
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
        Is(new KeyDecision(false, GestureAction.Show), gesture.Key(LAlt, true), "gesture can activate again");
        Is(0, gesture.Wheel(60), "disarm clears partial wheel remainder");
        Is(1, gesture.Wheel(60), "fresh partials make a step");
    }

    private static void MixRulesRemainDeterministic()
    {
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
