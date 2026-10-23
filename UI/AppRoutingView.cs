using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Mix.Core;

namespace Mix.UI;

sealed class AppRoutingView : StackPanel
{
    readonly StackPanel active = new() { Spacing = 8 };
    readonly StackPanel inactive = new() { Spacing = 8 };
    readonly Expander expander;
    readonly HashSet<string> added = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Row> rows = new(StringComparer.OrdinalIgnoreCase);
    string? fingerprint;
    bool rendering;
    public event Action<string,string>? ChannelChanged;
    public event Action? AddRequested;

    public AppRoutingView()
    {
        Spacing = 10;
        Children.Add(new TextBlock { Text = "App channels", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        Children.Add(new TextBlock { Text = "Send an app's audio to Game, Chat, or Media. Apps with their own output setting may need Default output or playback restarted.", TextWrapping = TextWrapping.Wrap });
        var add = new Button { Content = "Add app..." };
        add.Click += (_, _) => AddRequested?.Invoke();
        Children.Add(add);
        Children.Add(active);
        expander = new Expander { Header = "Not running (0)", Content = inactive, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Visibility = Visibility.Collapsed };
        Children.Add(expander);
    }

    public void Add(string path)
    {
        if (AppRoutingRules.NormalizePath(path) is not { } executable || !File.Exists(executable))
            throw new InvalidOperationException("Choose an existing .exe application.");
        added.Add(executable);
        expander.IsExpanded = true;
    }

    public void Update(AudioState state, IReadOnlyDictionary<string,string> assignments)
    {
        var playing = state.Sessions.Where(x => !x.SystemSounds).GroupBy(x => x.ExecutablePath ?? x.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var offline = assignments.Keys.Concat(added).Distinct(StringComparer.OrdinalIgnoreCase).Where(x => !playing.ContainsKey(x)).Order(StringComparer.OrdinalIgnoreCase).ToArray();
        string next = string.Join('\n', playing.Keys.Order(StringComparer.OrdinalIgnoreCase).Select(x => "active:" + x).Concat(offline.Select(x => "inactive:" + x)));
        rendering = true;
        try
        {
            if (next != fingerprint)
            {
                fingerprint = next;
                rows.Clear(); active.Children.Clear(); inactive.Children.Clear();
                foreach (var pair in playing.OrderBy(x => Label(x.Key, x.Value[0]), StringComparer.OrdinalIgnoreCase))
                    Create(pair.Key, pair.Value[0].ExecutablePath, Label(pair.Key, pair.Value[0]), active);
                foreach (var path in offline) Create(path, path, Path.GetFileNameWithoutExtension(path), inactive);
                if (playing.Count == 0) active.Children.Add(new TextBlock { Text = "Play audio or add an app to choose a channel.", TextWrapping = TextWrapping.Wrap });
            }
            expander.Header = $"Not running ({offline.Length})";
            expander.Visibility = offline.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
            foreach (var (key, row) in rows)
            {
                playing.TryGetValue(key, out var sessions);
                bool routable = row.Path != null && (sessions == null || sessions.Any(x => x.Routable));
                row.Choice.IsEnabled = routable;
                assignments.TryGetValue(key, out var selected);
                row.Choice.SelectedItem = row.Choice.Items.OfType<ComboBoxItem>().FirstOrDefault(x => (string)x.Tag == selected);
                string? observed = sessions == null ? null : string.Join(", ", sessions.Select(x =>
                    state.Channels.FirstOrDefault(c => c.DeviceId == x.DeviceId)?.Name ?? state.Devices.FirstOrDefault(d => d.Id == x.DeviceId)?.Name ?? "Unavailable output").Distinct());
                string status = sessions == null ? selected == null ? "Choose a channel to save an assignment." : "Not running. Applies when it plays audio." : $"Playing on {observed}.";
                if (!routable) status += " Cannot route this app's audio session.";
                if (selected != null && state.Channels.FirstOrDefault(x => x.Key == selected)?.Available != true)
                    status += $" {selected} output unavailable. Applies when it returns.";
                if (state.RoutingErrors?.TryGetValue(key, out var error) == true) status += " Saved. Could not apply: " + error;
                row.Status.Text = status;
            }
        }
        finally { rendering = false; }
    }

    void Create(string key, string? path, string name, StackPanel parent)
    {
        var panel = new StackPanel { Spacing = 4 };
        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new() { Width = new GridLength(32) });
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(AppIcons.Create(path));
        var label = new TextBlock { Text = name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(label, path ?? name);
        Grid.SetColumn(label, 1); header.Children.Add(label);
        var choice = new ComboBox { PlaceholderText = "Choose channel", MinWidth = 130 };
        AutomationProperties.SetName(choice, name + " channel");
        foreach (var channel in MixRules.Channels.Where(AppRoutingRules.IsChannel)) choice.Items.Add(new ComboBoxItem { Content = channel, Tag = channel });
        choice.SelectionChanged += (_, _) =>
        {
            if (!rendering && path != null && choice.SelectedItem is ComboBoxItem item) ChannelChanged?.Invoke(path, (string)item.Tag);
        };
        Grid.SetColumn(choice, 2); header.Children.Add(choice);
        panel.Children.Add(header);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = .75 };
        panel.Children.Add(status);
        var card = MixVisuals.Card(); card.Padding = new Thickness(12); card.Child = panel;
        parent.Children.Add(card);
        rows.Add(key, new(path, choice, status));
    }

    static string Label(string path, SessionLevel session) => session.ExecutablePath != null ? Path.GetFileNameWithoutExtension(path) : session.Name.Split(" · PID ")[0];
    sealed record Row(string? Path, ComboBox Choice, TextBlock Status);
}
