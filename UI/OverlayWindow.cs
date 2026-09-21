using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Mix.Core;
using Mix.Platform;
using Windows.Graphics;
using WinRT.Interop;

namespace Mix.UI;

public sealed class OverlayWindow : Window
{
    const int GwlStyle = -16, GwlExStyle = -20;
    const long CaptionAndFrame = 0x00CF0000, ExTopmost = 0x00000008, ExToolWindow = 0x00000080, ExNoActivate = 0x08000000;
    const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoActivate = 0x0010, SwpFrameChanged = 0x0020;
    static readonly nint HwndTopmost = new(-1);
    static readonly string[] ChannelKeys = ["Game", "Chat", "Media"];

    readonly AppWindow appWindow;
    readonly nint hwnd;
    readonly ToggleButton[] tiles = new ToggleButton[3];
    readonly TextBlock[] names = new TextBlock[3];
    readonly TextBlock[] values = new TextBlock[3];
    readonly TextBlock[] muteStates = new TextBlock[3];
    readonly ProgressBar[] meters = new ProgressBar[3];
    readonly TextBlock errorText = new() { TextWrapping = TextWrapping.Wrap, MaxLines = 2 };
    readonly Grid row = new() { ColumnSpacing = 6 };
    bool destroying;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetWindowPos(nint hwnd, nint insertAfter, int x, int y, int width, int height, uint flags);

    public OverlayWindow()
    {
        Title = "Win Mix overlay";
        Content = BuildUi();
        hwnd = WindowNative.GetWindowHandle(this);
        appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        appWindow.IsShownInSwitchers = false;
        if (appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }
        BorderlessNonActivatingTopmost();
        appWindow.Closing += (_, args) =>
        {
            if (destroying) return;
            args.Cancel = true;
            appWindow.Hide();
        };
    }

    Grid BuildUi()
    {
        var root = MixVisuals.Root();
        root.Padding = new Thickness(12);
        root.RowSpacing = 8;
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        errorText.Visibility = Visibility.Collapsed;
        root.Children.Add(errorText);

        for (int i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < 3; i++)
        {
            var content = new StackPanel { Spacing = 8 };
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            var icon = MixVisuals.ChannelIcon(ChannelKeys[i]);
            icon.Width = icon.Height = 20;
            names[i] = new TextBlock { Text = ChannelKeys[i], FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            heading.Children.Add(icon);
            heading.Children.Add(names[i]);
            values[i] = new TextBlock { Text = "Unavailable", FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
            muteStates[i] = new TextBlock { Text = "", FontSize = 12 };
            meters[i] = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, IsHitTestVisible = false };
            content.Children.Add(heading);
            content.Children.Add(values[i]);
            content.Children.Add(meters[i]);
            content.Children.Add(muteStates[i]);
            tiles[i] = new ToggleButton
            {
                Content = content,
                IsTabStop = false,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(12),
                CornerRadius = new CornerRadius(8)
            };
            AutomationProperties.SetName(tiles[i], ChannelKeys[i]);
            AutomationProperties.SetHelpText(tiles[i], "Audio channel status");
            Grid.SetColumn(tiles[i], i);
            row.Children.Add(tiles[i]);
        }
        Grid.SetRow(row, 1);
        root.Children.Add(row);
        var hint = MixVisuals.Caption("← → select   ·   Scroll / ↑ ↓ volume   ·   M mute");
        hint.HorizontalAlignment = HorizontalAlignment.Center;
        Grid.SetRow(hint, 2);
        root.Children.Add(hint);
        return root;
    }

    void BorderlessNonActivatingTopmost()
    {
        long style = Native.GetWindowLongPtrW(hwnd, GwlStyle).ToInt64();
        long exStyle = Native.GetWindowLongPtrW(hwnd, GwlExStyle).ToInt64();
        Native.SetWindowLongPtrW(hwnd, GwlStyle, new IntPtr(style & ~CaptionAndFrame));
        Native.SetWindowLongPtrW(hwnd, GwlExStyle, new IntPtr(exStyle | ExTopmost | ExToolWindow | ExNoActivate));
        if (!SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Update(AudioState state, int selected) => OnUi(() =>
    {
        for (int i = 0; i < tiles.Length; i++)
        {
            var level = state.Channels.FirstOrDefault(x => x.Key == ChannelKeys[i]);
            bool available = level?.Available ?? false;
            names[i].Text = level?.Name ?? ChannelKeys[i];
            values[i].Text = available ? $"{Math.Round(Math.Clamp(level!.Volume, 0, 1) * 100):0}%" : "Unavailable";
            values[i].FontSize = available ? 28 : 14;
            muteStates[i].Text = available ? level!.Muted ? "Muted" : "" : "Choose a device";
            meters[i].Value = available ? Math.Clamp(level!.Volume, 0, 1) * 100 : 0;
            meters[i].Opacity = level?.Muted == true ? .35 : 1;
            tiles[i].IsChecked = i == Math.Clamp(selected, 0, 2);
            AutomationProperties.SetName(tiles[i], $"{names[i].Text}, {values[i].Text}, {muteStates[i].Text}");
        }
        errorText.Text = state.Error ?? "";
        errorText.Visibility = string.IsNullOrWhiteSpace(state.Error) ? Visibility.Collapsed : Visibility.Visible;
        AutomationProperties.SetName(errorText, state.Error ?? "");
    });

    internal Native.Rect ShowNearPointer()
    {
        var bounds = WindowPlacement.Fit(444, errorText.Visibility == Visibility.Visible ? 232 : 192, false);
        ShowAt(bounds);
        ((Grid)Content).UpdateLayout();
        var point = row.TransformToVisual((UIElement)Content).TransformPoint(new Windows.Foundation.Point());
        double scale = row.XamlRoot.RasterizationScale;
        return new Native.Rect {
            Left = bounds.Left + (int)Math.Round(point.X * scale),
            Top = bounds.Top + (int)Math.Round(point.Y * scale),
            Right = bounds.Left + (int)Math.Round((point.X + row.ActualWidth) * scale),
            Bottom = bounds.Top + (int)Math.Round((point.Y + row.ActualHeight) * scale)
        };
    }

    void ShowAt(Native.Rect bounds)
    {
        appWindow.MoveAndResize(new RectInt32(bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top));
        appWindow.Show(false);
        if (!SetWindowPos(hwnd, HwndTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Hide() => OnUi(appWindow.Hide);

    public void Destroy() => OnUi(() => { destroying = true; Close(); });

    void OnUi(Action action)
    {
        if (DispatcherQueue.HasThreadAccess) action();
        else DispatcherQueue.TryEnqueue(() => action());
    }
}
