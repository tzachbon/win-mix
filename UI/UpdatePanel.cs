using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Mix.Platform;

namespace Mix.UI;

internal sealed class UpdatePanel : StackPanel
{
    readonly Button actionButton = new();
    readonly Button cancelButton = new() { Content = "Cancel" };
    readonly ProgressBar progressBar = new() { Minimum = 0, Maximum = 100 };
    readonly InfoBar statusBar = new() { IsClosable = false, IsOpen = false };
    UpdateState state = new(UpdatePhase.Idle, "");

    internal event Action? UpdateRequested;
    internal event Action? UpdateCanceled;

    internal UpdatePanel(string version)
    {
        Spacing = 8;
        var header = new Grid { ColumnSpacing = 8 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var versionLabel = MixVisuals.Caption($"Win Mix · Version {version}", new Thickness(0, 12, 0, 0));
        versionLabel.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(versionLabel);
        actionButton.Content = state.ActionLabel;
        AutomationProperties.SetName(actionButton, state.ActionLabel);
        ToolTipService.SetToolTip(actionButton, state.ActionLabel);
        actionButton.Click += (_, _) => { if (!state.Busy) UpdateRequested?.Invoke(); };
        Grid.SetColumn(actionButton, 1);
        header.Children.Add(actionButton);
        Children.Add(header);

        statusBar.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(statusBar, "Update status");
        Children.Add(statusBar);

        var progressRow = new Grid { ColumnSpacing = 8 };
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progressBar.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(progressBar, "Update progress");
        AutomationProperties.SetHelpText(progressBar, "Update progress from 0 to 100 percent");
        cancelButton.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(cancelButton, "Cancel update");
        ToolTipService.SetToolTip(cancelButton, "Cancel update");
        cancelButton.Click += (_, _) => { if (state.CanCancel) UpdateCanceled?.Invoke(); };
        Grid.SetColumn(cancelButton, 1);
        progressRow.Children.Add(progressBar);
        progressRow.Children.Add(cancelButton);
        Children.Add(progressRow);

    }

    internal void SetState(UpdateState value)
    {
        state = value;
        actionButton.Content = value.ActionLabel;
        actionButton.IsEnabled = !value.Busy;
        AutomationProperties.SetName(actionButton, value.ActionLabel);
        ToolTipService.SetToolTip(actionButton, value.ActionLabel);
        statusBar.Message = value.Message;
        statusBar.Severity = value.Phase switch
        {
            UpdatePhase.Error => InfoBarSeverity.Error,
            UpdatePhase.Current or UpdatePhase.Updated => InfoBarSeverity.Success,
            _ => InfoBarSeverity.Informational
        };
        statusBar.IsOpen = !string.IsNullOrWhiteSpace(value.Message);
        statusBar.Visibility = statusBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;

        bool showProgress = value.Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Verifying;
        progressBar.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        progressBar.IsIndeterminate = showProgress && value.Progress is null;
        progressBar.Value = value.Progress is double progress && double.IsFinite(progress)
            ? Math.Clamp(progress, 0, 100)
            : 0;
        cancelButton.Visibility = value.CanCancel ? Visibility.Visible : Visibility.Collapsed;
        cancelButton.IsEnabled = value.CanCancel;
    }
}
