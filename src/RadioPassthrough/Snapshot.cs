using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RadioPassthrough.Core.Setup;
using RadioPassthrough.Dialogs;
using RadioPassthrough.ViewModels;

namespace RadioPassthrough;

// Development aid: `RadioPassthrough.exe --snapshot <dir> [--theme light|dark]` renders every screen to
// PNG so the design can be reviewed without driving the UI by hand. Windows are placed far off-screen,
// never activated and kept out of the taskbar, so nothing pops up.
internal static class Snapshot
{
    public static async Task CaptureAllAsync(MainWindow window, MainViewModel vm, string directory)
    {
        Directory.CreateDirectory(directory);
        window.Height = 1600;
        Hide(window);
        window.Show();
        await Task.Delay(1500);

        string[] names = ["live", "test", "setup"];
        for (int tab = 0; tab < names.Length; tab++)
        {
            vm.SelectedTab = tab;
            await Task.Delay(tab == 2 ? 2000 : 600);
            await SaveAsync(window, Path.Combine(directory, $"{names[tab]}.png"));
        }

        var installer = new InstallerWindow(new Installation(Path.Combine(Path.GetTempPath(), "rp-snapshot-none")));
        Hide(installer);
        installer.Show();
        await Task.Delay(500);
        await SaveAsync(installer, Path.Combine(directory, "installer.png"));
        installer.Close();
    }

    private static void Hide(Window window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000;
        window.Top = -30000;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
    }

    private static async Task SaveAsync(Window window, string path)
    {
        window.UpdateLayout();
        if (window.Content is not FrameworkElement root) return;
        const double scale = 2;
        // Floating sheets are rendered whole (shadow margin included) on a sunken backdrop.
        bool sheet = window.AllowsTransparency;
        double w = sheet ? window.ActualWidth : root.ActualWidth;
        double h = sheet ? window.ActualHeight : root.ActualHeight;
        var bitmap = new RenderTargetBitmap((int)(w * scale), (int)(h * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
            dc.DrawRectangle(sheet ? window.FindResource("Sunken") as Brush : window.Background, null, new Rect(0, 0, w, h));
        bitmap.Render(background);
        bitmap.Render(sheet ? window : root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var file = File.Create(path);
        encoder.Save(file);
    }
}
