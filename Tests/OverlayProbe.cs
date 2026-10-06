using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Mix.Platform;
using Mix.UI;

namespace Mix;

// Runs the real WinUI overlay without starting audio, input hooks, or the tray host.
static class OverlayProbe
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
        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            DispatcherQueue.GetForCurrentThread().TryEnqueue(async () =>
            {
                try
                {
                    var foreground = Native.GetForegroundWindow();
                    var overlay = new OverlayWindow();
                    var loadedBounds = new TaskCompletionSource<Native.Rect[]>(TaskCreationOptions.RunContinuationsAsynchronously);
                    overlay.BoundsChanged += bounds => { if (bounds.Length == 4) loadedBounds.TrySetResult(bounds); };
                    overlay.Update(new([], [], [], null), 2);
                    overlay.ShowAtBottom();
                    var initialBounds = await loadedBounds.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(overlay);
                    for (int i = 0; i < 20; i++)
                    {
                        var rects = i == 0 ? initialBounds : overlay.ShowAtBottom();
                        if ((Native.GetWindowLongPtrW(hwnd, -16).ToInt64() & 0x00CF0000) != 0)
                            throw new Exception("Overlay has caption or resizable frame");
                        long exStyle = Native.GetWindowLongPtrW(hwnd, -20).ToInt64();
                        if ((exStyle & 0x08000088) != 0x08000088)
                            throw new Exception($"Overlay lost topmost, tool-window, or no-activate styles: 0x{exStyle:X}");
                        if (Native.GetForegroundWindow() != foreground) throw new Exception("Overlay stole focus");
                        if (rects.Length != 4 || rects.Any(r => r.Right <= r.Left || r.Bottom <= r.Top))
                            throw new Exception("Invalid overlay tile bounds");
                        await Task.Delay(25);
                        overlay.Hide();
                        if (overlay.AppWindow.IsVisible) throw new Exception("Overlay did not hide");
                    }
                    overlay.Destroy();
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe-result.txt"),
                        "PASS: first-show bounds, 20 show/hide cycles, window styles, focus, and shutdown");
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "probe-result.txt"), ex.ToString());
                    Environment.Exit(1);
                }
            });
        }
    }
}
