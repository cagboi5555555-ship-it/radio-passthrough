using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RadioPassthrough.ViewModels;

namespace RadioPassthrough;

// Development aid: `RadioPassthrough.exe --snapshot <dir> [--theme light|dark]` renders each tab to a
// PNG so the design can be reviewed without driving the UI by hand.
internal static class Snapshot
{
    public static async Task CaptureTabsAsync(Window window, MainViewModel vm, string directory)
    {
        Directory.CreateDirectory(directory);
        // Rendered far off-screen, never activated and not in the taskbar, so nothing pops up.
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Height = 1500;
        window.Show();
        await Task.Delay(1500);
        string[] names = ["live", "test", "setup"];
        for (int tab = 0; tab < names.Length; tab++)
        {
            vm.SelectedTab = tab;
            await Task.Delay(tab == 2 ? 1500 : 600);
            window.UpdateLayout();
            if (window.Content is not FrameworkElement root) continue;
            const double scale = 2;
            var bitmap = new RenderTargetBitmap((int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen())
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
            bitmap.Render(background);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var file = File.Create(Path.Combine(directory, $"{names[tab]}.png"));
            encoder.Save(file);
        }
    }
}
