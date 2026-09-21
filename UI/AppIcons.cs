using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Mix.UI;

internal static class AppIcons
{
    public static FrameworkElement Create(string? executablePath, bool systemSounds = false)
    {
        var grid = new Grid { Width = 32, Height = 32 };
        grid.Children.Add(new SymbolIcon(systemSounds ? Symbol.Volume : Symbol.AllApps));
        if (!string.IsNullOrWhiteSpace(executablePath)) _ = LoadThumbnailAsync(grid, executablePath);
        return grid;
    }

    static async Task LoadThumbnailAsync(Grid grid, string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, 32, ThumbnailOptions.UseCurrentScale);
            if (thumbnail is null) return;

            var bitmap = new BitmapImage();
            await bitmap.SetSourceAsync(thumbnail);
            grid.Children.Clear();
            grid.Children.Add(new Image { Source = bitmap, Stretch = Stretch.Uniform });
        }
        catch (Exception) { }
    }
}
