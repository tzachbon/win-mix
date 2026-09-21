using Mix.Core;

internal static class KeyboardEditorTests
{
    internal static void Run()
    {
        var editor = new KeyboardEditor(KeyboardBindings.Default);
        Is(false, editor.HasChanges, "new draft matches active bindings");
        Is(false, editor.CanApply, "unchanged draft cannot be applied");

        editor.SetOpening([KeyboardBindings.Default.Mute]);
        Is(true, editor.ValidationError is not null, "overlapping draft is invalid");
        Is(false, editor.CanApply, "invalid draft cannot be applied");

        editor.TrySetMute([0x4E]);
        Is(null, editor.ValidationError, "second edit resolves intermediate overlap");
        Is(true, editor.WarnsAboutSingleOpeningKey, "single-key opening warning is exposed");
        Is(true, editor.CanApply, "complete valid draft can be applied");

        var validDraft = editor.Draft;
        Is(false, editor.TrySetMute([0x4D, 0x4E]), "multi-key mute draft is rejected");
        Is(true, editor.Draft.SameAs(validDraft), "rejected mute recording preserves the draft");
        var submitted = editor.Draft;
        editor.SetActive(KeyboardBindings.Default);
        editor.Accept(submitted);
        Is(true, editor.Active.SameAs(submitted), "accept uses the submitted snapshot");
        Is(false, editor.HasChanges, "accept updates the active draft");
        editor.TrySetMute([0x4D]);
        editor.Cancel();
        Is(false, editor.HasChanges, "cancel restores the accepted bindings");

        editor.TrySetMute([0x4D]);
        editor.RestoreDefaults();
        Is(true, editor.HasChanges, "restore defaults stages the default bindings");
        editor.Cancel();
        Is(false, editor.HasChanges, "cancel restores accepted bindings after defaults");

        int recording = editor.BeginOperation();
        editor.CancelPendingOperation();
        int next = editor.BeginOperation();
        Is(false, editor.IsCurrentOperation(recording), "canceled recording completion is stale");
        Is(true, editor.IsCurrentOperation(next), "latest operation remains current");
    }

    static void Is<T>(T expected, T actual, string message)
    {
        if (!Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }
}
