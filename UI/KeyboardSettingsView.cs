using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Mix.Core;

namespace Mix.UI;

internal sealed class KeyboardSettingsView : UserControl
{
    readonly KeyboardEditor editor = new(KeyboardBindings.Default);
    readonly TextBlock openingValue = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock muteValue = new() { TextWrapping = TextWrapping.Wrap };
    readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    readonly Button openingButton = new() { Content = "Record", MinWidth = 120 };
    readonly Button muteButton = new() { Content = "Record", MinWidth = 120 };
    readonly Button applyButton = new() { Content = "Apply" };
    readonly Button cancelButton = new() { Content = "Cancel" };
    readonly Button defaultsButton = new() { Content = "Restore defaults" };
    Func<nint, Task<int[]?>>? record;
    Action? cancelRecording;
    Func<KeyboardBindings, Task<string?>>? apply;
    nint owner;
    bool configured, busy, recordingOpening, recordingMute;
    string? operationMessage;

    public KeyboardSettingsView()
    {
        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock { Text = "Keyboard shortcuts", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        content.Children.Add(MixVisuals.Caption("Record a shortcut, then release its keys. Press Escape to cancel recording."));

        AutomationProperties.SetName(openingValue, "Active quick mixer shortcut");
        AutomationProperties.SetHelpText(openingButton, "Record one or more keys. Release all keys to finish. Press Escape to cancel.");
        AutomationProperties.SetName(openingButton, "Record quick mixer opening shortcut");
        openingButton.Click += async (_, _) => await RecordOpening();
        openingButton.LostFocus += (_, _) => { if (recordingOpening) CancelRecording(); };
        content.Children.Add(RecordRow("Open the quick mixer", openingValue, openingButton));

        AutomationProperties.SetName(muteValue, "Active mute shortcut");
        AutomationProperties.SetHelpText(muteButton, "Record one key. Multiple keys are not allowed. Release the key to finish.");
        AutomationProperties.SetName(muteButton, "Record mute shortcut");
        muteButton.Click += async (_, _) => await RecordMute();
        muteButton.LostFocus += (_, _) => { if (recordingMute) CancelRecording(); };
        content.Children.Add(RecordRow("Mute the selected channel", muteValue, muteButton));

        AutomationProperties.SetName(status, "Keyboard shortcut settings status");
        AutomationProperties.SetHelpText(status, "Validation and recording status for keyboard shortcuts.");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        content.Children.Add(status);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        AutomationProperties.SetName(applyButton, "Apply keyboard shortcuts");
        applyButton.Click += async (_, _) => await ApplyDraft();
        actions.Children.Add(applyButton);
        AutomationProperties.SetName(cancelButton, "Cancel keyboard shortcut changes");
        cancelButton.Click += (_, _) => { editor.Cancel(); operationMessage = null; Render(); };
        actions.Children.Add(cancelButton);
        AutomationProperties.SetName(defaultsButton, "Restore default keyboard shortcuts");
        defaultsButton.Click += (_, _) => { editor.RestoreDefaults(); operationMessage = null; Render(); };
        actions.Children.Add(defaultsButton);
        content.Children.Add(actions);
        Content = content;
        Render();
    }

    internal void Configure(KeyboardBindings active, nint owner, Func<nint, Task<int[]?>> record,
        Action cancelRecording, Func<KeyboardBindings, Task<string?>> apply)
    {
        if (configured) throw new InvalidOperationException("Keyboard settings are already configured.");
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(cancelRecording);
        ArgumentNullException.ThrowIfNull(apply);
        this.owner = owner;
        this.record = record;
        this.cancelRecording = cancelRecording;
        this.apply = apply;
        editor.SetActive(active);
        configured = true;
        Render();
    }

    internal void SetKeyboardBindings(KeyboardBindings bindings)
    {
        editor.SetActive(bindings);
        operationMessage = null;
        Render();
    }

    internal void CancelRecording()
    {
        if (!recordingOpening && !recordingMute) return;
        editor.CancelPendingOperation();
        recordingOpening = recordingMute = busy = false;
        operationMessage = "Recording canceled.";
        try { cancelRecording?.Invoke(); }
        catch (Exception ex) { operationMessage = "Could not cancel recording: " + ex.Message; }
        Render();
    }

    internal void LeaveSettings()
    {
        if (recordingOpening || recordingMute) CancelRecording();
    }

    Task RecordOpening() => Record(opening: true);
    Task RecordMute() => Record(opening: false);

    async Task Record(bool opening)
    {
        if (!configured || busy) return;
        Button button = opening ? openingButton : muteButton;
        if (!button.Focus(FocusState.Programmatic))
        {
            operationMessage = $"Could not focus the {(opening ? "opening" : "mute")} shortcut button.";
            Render();
            return;
        }
        int current = editor.BeginOperation();
        busy = true;
        recordingOpening = opening;
        recordingMute = !opening;
        operationMessage = null;
        Render();
        try
        {
            int[]? keys = await record!(owner);
            if (!editor.IsCurrentOperation(current)) return;
            if (keys is null) operationMessage = "Recording canceled.";
            else if (opening && keys.Length == 0) operationMessage = "Record at least one key for the opening shortcut.";
            else if (opening) { editor.SetOpening(keys); operationMessage = null; }
            else if (!editor.TrySetMute(keys)) operationMessage = "The mute shortcut must be exactly one key. Try recording one key.";
            else operationMessage = null;
        }
        catch (Exception ex)
        {
            if (editor.IsCurrentOperation(current))
                operationMessage = $"Could not record the {(opening ? "opening" : "mute")} shortcut: " + ex.Message;
        }
        finally
        {
            if (editor.IsCurrentOperation(current))
            {
                busy = recordingOpening = recordingMute = false;
                Render();
            }
        }
    }

    async Task ApplyDraft()
    {
        if (!configured || busy || !editor.CanApply) return;
        KeyboardBindings draft = editor.Draft;
        int current = editor.BeginOperation();
        busy = true;
        operationMessage = null;
        Render();
        try
        {
            string? error = await apply!(draft);
            if (!editor.IsCurrentOperation(current)) return;
            if (error is null)
            {
                editor.Accept(draft);
                operationMessage = "Keyboard shortcuts applied.";
            }
            else operationMessage = error;
        }
        catch (Exception ex)
        {
            if (editor.IsCurrentOperation(current)) operationMessage = "Could not apply keyboard shortcuts: " + ex.Message;
        }
        finally
        {
            if (editor.IsCurrentOperation(current))
            {
                busy = false;
                Render();
            }
        }
    }

    void Render()
    {
        openingValue.Text = $"Active: {editor.Active.OpeningLabel}";
        muteValue.Text = $"Active: {editor.Active.MuteLabel}";
        AutomationProperties.SetName(openingValue, openingValue.Text);
        AutomationProperties.SetName(muteValue, muteValue.Text);
        SetButtonContent(openingButton, recordingOpening ? "Listening…" : $"Record\n{editor.Draft.OpeningLabel}");
        SetButtonContent(muteButton, recordingMute ? "Listening…" : $"Record\n{editor.Draft.MuteLabel}");
        AutomationProperties.SetName(openingButton, recordingOpening ? "Recording quick mixer opening shortcut" : $"Record quick mixer opening shortcut. Draft: {editor.Draft.OpeningLabel}");
        AutomationProperties.SetName(muteButton, recordingMute ? "Recording mute shortcut" : $"Record mute shortcut. Draft: {editor.Draft.MuteLabel}");
        openingButton.IsEnabled = configured && (!busy || recordingOpening);
        muteButton.IsEnabled = configured && (!busy || recordingMute);
        applyButton.IsEnabled = configured && !busy && editor.CanApply;
        cancelButton.IsEnabled = configured && !busy && editor.HasChanges;
        defaultsButton.IsEnabled = configured && !busy && !editor.Draft.SameAs(KeyboardBindings.Default);
        if (busy)
            status.Text = recordingOpening || recordingMute
                ? $"Recording {(recordingOpening ? "opening" : "mute")} shortcut. Press the key or keys, then release to finish. Press Escape to cancel."
                : "Applying keyboard shortcuts…";
        else
        {
            string? issue = operationMessage ?? editor.ValidationError;
            string? warning = editor.WarnsAboutSingleOpeningKey
                ? "A single-key opening shortcut may trigger while you type. Consider using a combination."
                : null;
            status.Text = string.Join(Environment.NewLine, new[] { issue, warning }.Where(x => !string.IsNullOrWhiteSpace(x)));
            if (status.Text.Length == 0)
                status.Text = editor.HasChanges ? "The draft is ready to apply." : "Keyboard shortcuts match the active settings.";
        }
    }

    static void SetButtonContent(Button button, string text) => button.Content = new TextBlock
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center
    };

    static UIElement RecordRow(string label, TextBlock value, Button button)
    {
        var panel = new StackPanel { Spacing = 4 };
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(button, 1);
        row.Children.Add(title);
        row.Children.Add(button);
        panel.Children.Add(row);
        panel.Children.Add(value);
        return panel;
    }
}
