using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Mix.Core;
using Mix.Platform;
using Windows.Graphics;
using WinRT.Interop;

namespace Mix.UI;

public sealed class MixerWindow : Window
{
    readonly AppWindow appWindow;
    readonly TitleBar titleBar = new() { Height = 32 };
    readonly Grid mixerView = new();
    readonly Grid settingsView = new();
    readonly StackPanel sessionPanel = new() { Spacing = 12 };
    readonly TextBlock errorText = new() { TextWrapping = TextWrapping.Wrap, MaxLines = 3 };
    readonly Button settingsButton = new();
    readonly InfoBar shortcutCard = new() {
        IsOpen = true, IsClosable = false, Severity = InfoBarSeverity.Informational,
        Title = "Try the quick mixer",
        Message = "Hold Left Ctrl + Left Alt. Hover or use ← → to select, scroll or ↑ ↓ to adjust, and M to mute."
    };
    readonly Dictionary<string, AudioControls> channels = new();
    readonly Dictionary<string, AudioControls> sessions = new();
    readonly Dictionary<string, ComboBox> devicesByChannel = new();
    ToggleSwitch startupToggle = null!;
    DeviceChoice[] devices = [];
    AudioState? state;
    string sessionFingerprint = "";
    string? preferenceError;
    bool rendering, showingSettings, destroying;

    public event Action<string, float>? LevelChanged;
    public event Action<string>? MuteRequested;
    public event Action<string, float>? SessionLevelChanged;
    public event Action<string, bool>? SessionMuteChanged;
    public event Action<string, string?>? DeviceChanged;
    public event Action<bool>? StartupChanged;

    public MixerWindow()
    {
        Title = "Win Mix";
        Content = BuildUi();
        var hwnd = WindowNative.GetWindowHandle(this);
        appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico"));
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(titleBar);
        titleBar.Loaded += (_, _) => UpdateCaptionColors();
        titleBar.ActualThemeChanged += (_, _) => UpdateCaptionColors();
        appWindow.Closing += (_, args) =>
        {
            if (destroying) return;
            args.Cancel = true;
            appWindow.Hide();
        };
    }

    void UpdateCaptionColors()
    {
        var caption = appWindow.TitleBar;
        caption.ButtonBackgroundColor = Colors.Transparent;
        caption.ButtonInactiveBackgroundColor = Colors.Transparent;
        if (titleBar.Foreground is SolidColorBrush foreground)
        {
            caption.ButtonForegroundColor = foreground.Color;
            caption.ButtonInactiveForegroundColor = foreground.Color;
        }
    }

    Grid BuildUi()
    {
        var root = MixVisuals.Root();
        root.Padding = new Thickness(32, 12, 32, 32);
        root.RowSpacing = 16;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { ColumnSpacing = 12 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel { Spacing = 4 };
        heading.Children.Add(new TextBlock { Text = "Win Mix", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        heading.Children.Add(MixVisuals.Caption("Your sound, in balance"));
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        brand.Children.Add(MixVisuals.BrandIcon(44));
        brand.Children.Add(heading);
        header.Children.Add(brand);
        settingsButton.Content = new SymbolIcon(Symbol.Setting);
        AutomationProperties.SetName(settingsButton, "Settings");
        ToolTipService.SetToolTip(settingsButton, "Settings");
        settingsButton.Click += (_, _) => ShowSettings(!showingSettings);
        Grid.SetColumn(settingsButton, 1);
        header.Children.Add(settingsButton);
        root.Children.Add(header);

        errorText.Visibility = Visibility.Collapsed;
        root.Children.Add(errorText);
        Grid.SetRow(errorText, 1);

        var mixerContent = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 14, 8) };
        mixerContent.Children.Add(shortcutCard);
        foreach (var key in MixRules.Channels)
        {
            if (key == "Master") mixerContent.Children.Add(MixVisuals.Caption("Headset", new Thickness(0, 12, 0, 0)));
            var control = CreateControls(key, key == "Master" ? "Master volume" : key, false, MixVisuals.ChannelIcon(key));
            channels.Add(key, control);
            mixerContent.Children.Add(control.Container);
        }
        mixerContent.Children.Add(new TextBlock { Text = "Apps", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 4) });
        mixerContent.Children.Add(sessionPanel);
        mixerView.Children.Add(new ScrollViewer { Content = mixerContent, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        settingsView.Visibility = Visibility.Collapsed;
        settingsView.Children.Add(BuildSettings());
        root.Children.Add(mixerView);
        root.Children.Add(settingsView);
        Grid.SetRow(mixerView, 2);
        Grid.SetRow(settingsView, 2);
        var shell = MixVisuals.Root();
        shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        shell.Children.Add(titleBar);
        Grid.SetRow(root, 1);
        shell.Children.Add(root);
        return shell;
    }

    UIElement BuildSettings()
    {
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(new TextBlock { Text = "Output devices", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var channel in MixRules.Channels)
        {
            var combo = new ComboBox { Tag = channel, Header = $"{channel} device", HorizontalAlignment = HorizontalAlignment.Stretch };
            AutomationProperties.SetName(combo, $"{channel} output device");
            combo.Items.Add(new ComboBoxItem { Content = "Not assigned" });
            combo.SelectedIndex = 0;
            combo.SelectionChanged += (_, _) =>
            {
                if (!rendering && combo.SelectedItem is ComboBoxItem item)
                    DeviceChanged?.Invoke(channel, item.Tag as string);
            };
            devicesByChannel.Add(channel, combo);
            content.Children.Add(combo);
        }
        startupToggle = new ToggleSwitch { Header = "Start with Windows", OffContent = "Off", OnContent = "On" };
        AutomationProperties.SetName(startupToggle, "Start Win Mix with Windows");
        startupToggle.Toggled += (_, _) => { if (!rendering) StartupChanged?.Invoke(startupToggle.IsOn); };
        content.Children.Add(startupToggle);
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
        content.Children.Add(MixVisuals.Caption($"Win Mix · Version {version}", new Thickness(0, 12, 0, 0)));
        return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    }

    AudioControls CreateControls(string key, string label, bool session, FrameworkElement icon)
    {
        var header = new Grid { ColumnSpacing = 12, RowSpacing = 4 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        header.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var name = new TextBlock { Text = label, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ToolTipService.SetToolTip(name, label);
        var percent = new TextBlock { Text = "Unavailable", FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
        var mute = new ToggleButton { Content = new SymbolIcon(Symbol.Volume), Width = 40, Height = 36, Padding = new Thickness(0), IsEnabled = false };
        var slider = new Slider { Minimum = 0, Maximum = 100, StepFrequency = 1, IsEnabled = false };
        AutomationProperties.SetName(slider, $"{label} volume");
        AutomationProperties.SetHelpText(slider, "Volume from 0 to 100 percent");
        AutomationProperties.SetName(mute, $"{label} mute");
        Grid.SetColumn(name, 1);
        Grid.SetColumn(percent, 2);
        Grid.SetColumn(mute, 3);
        Grid.SetRow(slider, 1);
        Grid.SetColumn(slider, 1);
        Grid.SetColumnSpan(slider, 3);
        Grid.SetRowSpan(icon, 2);
        header.Children.Add(icon);
        header.Children.Add(name);
        header.Children.Add(percent);
        header.Children.Add(mute);
        header.Children.Add(slider);

        var container = MixVisuals.Card();
        container.Padding = new Thickness(14, 10, 14, 6);
        container.Child = header;
        AutomationProperties.SetName(container, label);
        var controls = new AudioControls(container, name, percent, slider, mute);
        slider.ValueChanged += (_, args) =>
        {
            if (rendering) return;
            if (session) SessionLevelChanged?.Invoke(key, (float)(args.NewValue / 100));
            else LevelChanged?.Invoke(key, (float)(args.NewValue / 100));
        };
        mute.Click += (_, _) =>
        {
            if (rendering) return;
            if (session) SessionMuteChanged?.Invoke(key, mute.IsChecked == true);
            else MuteRequested?.Invoke(key);
        };
        slider.LostFocus += (_, _) => ReapplyLatest();
        mute.LostFocus += (_, _) => ReapplyLatest();
        return controls;
    }

    public void Update(AudioState value) => OnUi(() => Render(value));
    public void SetShortcutUsed(bool used) => shortcutCard.Visibility = used ? Visibility.Collapsed : Visibility.Visible;

    void Render(AudioState value)
    {
        state = value;
        rendering = true;
        try
        {
            if (!value.Devices.SequenceEqual(devices))
            {
                devices = value.Devices;
                foreach (var combo in devicesByChannel.Values)
                {
                    combo.Items.Clear();
                    combo.Items.Add(new ComboBoxItem { Content = "Not assigned" });
                    foreach (var device in devices) combo.Items.Add(new ComboBoxItem { Content = device.Name, Tag = device.Id });
                }
            }

            foreach (var key in MixRules.Channels)
            {
                var level = value.Channels.FirstOrDefault(x => x.Key == key);
                var control = channels[key];
                Apply(control, level?.Volume ?? 0, level?.Muted ?? false, level?.Available ?? false);
                if (level != null)
                {
                    var selected = devicesByChannel[key].Items.OfType<ComboBoxItem>().FirstOrDefault(item => StringComparer.Ordinal.Equals(item.Tag as string, level.DeviceId));
                    if (selected == null && level.DeviceId != null)
                    {
                        selected = new ComboBoxItem { Content = "Unavailable (saved device)", Tag = level.DeviceId };
                        devicesByChannel[key].Items.Add(selected);
                    }
                    devicesByChannel[key].SelectedItem = selected ?? devicesByChannel[key].Items[0];
                }
            }

            var fingerprint = string.Join("\u001f", value.Sessions.Select(x => $"{x.Key}\u001e{x.DeviceId}\u001e{x.Name}"));
            if (fingerprint != sessionFingerprint)
            {
                sessionFingerprint = fingerprint;
                RebuildSessions(value);
            }
            foreach (var item in value.Sessions)
                if (sessions.TryGetValue(item.Key, out var control)) Apply(control, item.Volume, item.Muted, true);
            RenderErrors();
        }
        finally { rendering = false; }
    }

    void RebuildSessions(AudioState value)
    {
        sessions.Clear();
        sessionPanel.Children.Clear();
        foreach (var group in value.Sessions.GroupBy(x => x.DeviceId).OrderBy(x => DeviceName(value, x.Key), StringComparer.OrdinalIgnoreCase))
        {
            var rowsPanel = new StackPanel { Spacing = 6 };
            var expander = new Expander { Header = $"{DeviceName(value, group.Key)} ({group.Count()})", Content = rowsPanel, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch };
            sessionPanel.Children.Add(expander);
            var rows = group.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Key, StringComparer.Ordinal).ToArray();
            var parts = rows.Select(x => SplitSessionName(x.Name)).ToArray();
            var counts = parts.GroupBy(x => x.App, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
            var ordinals = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rows.Length; i++)
            {
                var part = parts[i];
                ordinals[part.App] = ordinals.GetValueOrDefault(part.App) + 1;
                var label = counts[part.App] > 1 ? $"{part.App} #{ordinals[part.App]}" : part.App;
                var control = CreateControls(rows[i].Key, label, true, AppIcons.Create(rows[i].ExecutablePath, part.App == "System sounds"));
                ToolTipService.SetToolTip(control.Name, rows[i].Name);
                sessions.Add(rows[i].Key, control);
                rowsPanel.Children.Add(control.Container);
            }
        }
        if (value.Sessions.Length == 0) sessionPanel.Children.Add(new TextBlock { Text = "No active app sessions" });
    }

    static (string App, string Process) SplitSessionName(string name)
    {
        int i = name.LastIndexOf(" · PID ", StringComparison.OrdinalIgnoreCase);
        return i < 0 ? (name, "") : (name[..i], name[i..]);
    }

    static string DeviceName(AudioState value, string id) => value.Devices.FirstOrDefault(x => x.Id == id)?.Name ?? "Unavailable endpoint";

    void Apply(AudioControls control, float volume, bool muted, bool available)
    {
        control.Slider.IsEnabled = available;
        control.Mute.IsEnabled = available;
        control.Slider.Value = Math.Clamp(float.IsFinite(volume) ? volume : 0, 0, 1) * 100;
        control.Mute.IsChecked = muted;
        ((SymbolIcon)control.Mute.Content).Symbol = muted ? Symbol.Mute : Symbol.Volume;
        ToolTipService.SetToolTip(control.Mute, muted ? "Unmute" : "Mute");
        control.Percent.Text = available ? $"{Math.Round(control.Slider.Value):0}%" : "Unavailable";
    }

    void ReapplyLatest()
    {
        if (state is { } latest) Render(latest);
    }

    public void SetStartupEnabled(bool enabled) => OnUi(() =>
    {
        rendering = true;
        try { startupToggle.IsOn = enabled; }
        finally { rendering = false; }
    });

    public void SetError(string message) => OnUi(() => { preferenceError = string.IsNullOrWhiteSpace(message) ? null : message; RenderErrors(); });

    void RenderErrors()
    {
        var errors = new[] { state?.Error, preferenceError }.Where(x => !string.IsNullOrWhiteSpace(x));
        errorText.Text = string.Join(Environment.NewLine, errors);
        errorText.Visibility = errorText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    public void ShowSettings(bool show) => OnUi(() =>
    {
        showingSettings = show;
        mixerView.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        settingsView.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        settingsButton.Content = new SymbolIcon(show ? Symbol.Back : Symbol.Setting);
        AutomationProperties.SetName(settingsButton, show ? "Back to mixer" : "Settings");
        ToolTipService.SetToolTip(settingsButton, show ? "Back to mixer" : "Settings");
        Present();
    });

    public void Show() => OnUi(Present);

    void Present()
    {
        if (!appWindow.IsVisible)
        {
            var bounds = WindowPlacement.Fit(560, 740, true);
            appWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
        }
        appWindow.Show(true);
        Activate();
    }

    public void Destroy() => OnUi(() => { destroying = true; Close(); });

    void OnUi(Action action)
    {
        if (DispatcherQueue.HasThreadAccess) action();
        else DispatcherQueue.TryEnqueue(() => action());
    }

    sealed record AudioControls(Border Container, TextBlock Name, TextBlock Percent, Slider Slider, ToggleButton Mute);
}

internal static class MixVisuals
{
    internal static Image BrandIcon(double size)
    {
        var icon = new Image {
            Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center,
            Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/app-icon.png"))
        };
        AutomationProperties.SetName(icon, "Win Mix");
        return icon;
    }
    const string Xmlns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    internal static Grid Root() => (Grid)XamlReader.Load($"<Grid xmlns='{Xmlns}' Background='{{ThemeResource ApplicationPageBackgroundThemeBrush}}' />");
    internal static Border Card() => (Border)XamlReader.Load($"<Border xmlns='{Xmlns}' Background='{{ThemeResource CardBackgroundFillColorDefaultBrush}}' BorderBrush='{{ThemeResource CardStrokeColorDefaultBrush}}' BorderThickness='1' CornerRadius='8' />");
    internal static TextBlock Caption(string text, Thickness margin = default)
    {
        var label = (TextBlock)XamlReader.Load($"<TextBlock xmlns='{Xmlns}' Foreground='{{ThemeResource TextFillColorSecondaryBrush}}' FontSize='12' TextWrapping='Wrap' />");
        label.Text = text;
        label.Margin = margin;
        return label;
    }
    internal static FrameworkElement ChannelIcon(string key) => key switch
    {
        "Game" => new FontIcon { Glyph = "\uE7FC", FontSize = 22 },
        "Chat" => new SymbolIcon(Symbol.Message),
        "Media" => new SymbolIcon(Symbol.MusicInfo),
        _ => new FontIcon { Glyph = "\uE7F6", FontSize = 22 }
    };
}

internal static class WindowPlacement
{
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);

    internal static Native.Rect Fit(int widthDip, int heightDip, bool centerVertically, int verticalOffsetDip = 18)
    {
        Native.GetCursorPos(out var point);
        var monitor = Native.MonitorFromPoint(point, 2);
        var info = new Native.MonitorInfo { Size = (uint)Marshal.SizeOf<Native.MonitorInfo>() };
        var width = GetSystemMetrics(0);
        var height = GetSystemMetrics(1);
        var work = new Native.Rect { Right = width > 0 ? width : 1280, Bottom = height > 0 ? height : 800 };
        if (monitor != 0 && Native.GetMonitorInfoW(monitor, ref info) && info.Work.Right > info.Work.Left && info.Work.Bottom > info.Work.Top)
            work = info.Work;

        uint dpi = 96;
        if (monitor != 0 && Native.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0) dpi = dpiX;
        int pixels(int dip) => Math.Max(1, (int)Math.Round(dip * dpi / 96d));
        int availableWidth = Math.Max(1, work.Right - work.Left);
        int availableHeight = Math.Max(1, work.Bottom - work.Top);
        int w = Math.Min(availableWidth, pixels(widthDip));
        int h = Math.Min(availableHeight, pixels(heightDip));
        int x = centerVertically ? Math.Clamp(point.X - w / 2, work.Left, work.Right - w) : work.Left + (availableWidth - w) / 2;
        int offset = pixels(verticalOffsetDip);
        int y = centerVertically ? point.Y - h / 2 : work.Bottom - h - offset;
        y = Math.Clamp(y, work.Top, work.Bottom - h);
        return new Native.Rect { Left = x, Top = y, Right = x + w, Bottom = y + h };
    }
}
