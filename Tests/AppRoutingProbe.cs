using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Mix.Core;
using Mix.UI;

namespace Mix;

// Real Settings controls, with isolated fixtures and no tray, hooks, or audio writes.
static class AppRoutingProbe
{
    [STAThread]
    static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(parameters =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            _ = new ProbeApp();
        });
    }

    sealed class ProbeApp : App
    {
        protected override void OnLaunched(LaunchActivatedEventArgs args) => DispatcherQueue.GetForCurrentThread().TryEnqueue(async () =>
        {
            MixerWindow? window = null;
            try
            {
                const string running = @"C:\Apps\Player.exe", offline = @"C:\Apps\Music.exe";
                Level[] channels = [new("Game", "Game", "game", .5f, false, true), new("Chat", "Chat", "chat", .5f, false, true),
                    new("Media", "Media", "media", .5f, false, false), new("Master", "Master", "master", .5f, false, true)];
                AudioState state = new([new("game", "Game output"), new("chat", "Chat output")], channels,
                    [new("session", "game", "Player", .5f, false, running, 123, 1, true), new("system", "game", "Renamed system", .5f, false, null, 0, 0, false, true)], null);
                window = new MixerWindow();
                window.Update(new([], [], [], null));
                int requests = 0;
                string? requestedPath = null, requestedChannel = null;
                window.AppChannelChanged += (path, channel) => { requests++; requestedPath = path; requestedChannel = channel; };
                window.SetAppChannels(new Dictionary<string,string> { [offline] = "Media" });
                window.Update(state);
                window.ShowSettings(true);
                await Task.Delay(200);
                var root = (DependencyObject)window.Content;
                var routeView = Descendants(root).OfType<AppRoutingView>().Single();
                var expansion = Descendants(routeView).OfType<Expander>().Single();
                Require(expansion.Header.ToString() == "Not running (1)" && !expansion.IsExpanded, "Inactive apps must start collapsed.");
                var player = Choices(root, "Player channel");
                Require(player.SelectedIndex == -1 && player.IsEnabled, "Live app must not get an implicit assignment.");
                Require(requests == 0, "Rendering settings must not route apps.");
                player.SelectedIndex = 1;
                Require(requests == 1 && requestedPath == running && requestedChannel == "Chat", "Native selector must emit the selected app and channel exactly once.");
                window.SetAppChannels(new Dictionary<string,string> { [offline] = "Media", [running] = "Chat" });
                window.Update(state with { Sessions = state.Sessions.Select(x => x with { Volume = .6f }).ToArray() });
                Require(ReferenceEquals(player, Choices(root, "Player channel")), "Level-only updates must preserve controls and focus.");
                Require(Descendants(routeView).OfType<TextBlock>().Any(x => x.Text == "Playing on Game."), "Saved choice must not pretend the observed output moved.");
                expansion.IsExpanded = true;
                await Task.Delay(150);
                var music = Choices(root, "Music channel");
                Require(music.SelectedIndex == 2, "Inactive assignment must be visible.");
                music.SelectedIndex = 0;
                Require(requests == 2 && requestedPath == offline && requestedChannel == "Game", "Inactive selector must emit an assignment.");
                routeView.Add(Environment.ProcessPath!);
                window.Update(state);
                await Task.Delay(100);
                Require(requests == 2, "Adding an executable must not choose a channel.");
                Require(Descendants(routeView).OfType<ComboBox>().Count() == 3, "Added inactive executable must get a selector; system sounds must be excluded.");
                var scrolling = Descendants(root).OfType<ScrollViewer>().First(x => Descendants(x).Contains(routeView));
                scrolling.ChangeView(null, 300, null);
                await Task.Delay(2500);
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "routing-ui-result.txt"),
                    "PASS: real Settings selectors, active/inactive assignments, collapsed inactive section, add-without-assignment, observed output, stable controls, and system-sounds exclusion");
                window.Destroy();
                Environment.Exit(0);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "routing-ui-result.txt"), error.ToString());
                window?.Destroy();
                Environment.Exit(1);
            }
        });
    }

    static ComboBox Choices(DependencyObject root, string name) => Descendants(root).OfType<ComboBox>().Single(x => AutomationProperties.GetName(x) == name);
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}
