namespace Mix.Core;

internal sealed class KeyboardEditor
{
    public KeyboardBindings Active { get; private set; } = KeyboardBindings.Default;
    public KeyboardBindings Draft { get; private set; } = KeyboardBindings.Default;
    public string? ValidationError => Draft.Validate();
    public bool HasChanges => !Draft.SameAs(Active);
    public bool CanApply => HasChanges && ValidationError is null;
    public bool WarnsAboutSingleOpeningKey => Draft.Opening.Count == 1;
    int generation;

    public KeyboardEditor(KeyboardBindings active) => SetActive(active);

    public void SetActive(KeyboardBindings bindings)
    {
        ArgumentNullException.ThrowIfNull(bindings);
        Active = Draft = bindings;
    }
    public void SetOpening(IReadOnlyList<int> keys) => Draft = new(keys, Draft.Mute);
    public bool TrySetMute(IReadOnlyList<int> keys)
    {
        if (keys.Count != 1) return false;
        Draft = new(Draft.Opening, keys[0]);
        return true;
    }
    public void RestoreDefaults() => Draft = KeyboardBindings.Default;
    public void Cancel() => Draft = Active;
    public void Accept(KeyboardBindings applied) => Active = Draft = applied;
    public int BeginOperation() => ++generation;
    public bool IsCurrentOperation(int id) => id == generation;
    public void CancelPendingOperation() => generation++;
}
