using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using RadioPassthrough.Core;
using RadioPassthrough.Core.Diagnostics;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough.Dialogs;

// Shown when the app is started from anywhere other than its install folder (e.g. Downloads).
public partial class InstallerWindow : Window
{
    private readonly Installation _installation;

    public InstallerWindow(Installation installation)
    {
        InitializeComponent();
        _installation = installation;

        var installed = installation.IsInstalled ? installation.InstalledVersion : null;
        var version = AppInfo.Version;
        VersionText.Text = installed is null ? $"Version {version.ToString(3)}" : $"Installed {installed.ToString(3)}  →  {version.ToString(3)}";
        InstallButton.Content = installed is null ? "Install"
            : installed < version ? "Update"
            : installed == version ? "Reinstall"
            : "Install this version";
        if (installed is not null) DesktopShortcut.IsChecked = installation.HasDesktopShortcut;
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = CancelButton.IsEnabled = CloseButton.IsEnabled = false;
        bool desktop = DesktopShortcut.IsChecked == true;
        bool autostart = _installation.StartWithWindowsAfterInstall;
        Say("Installing…", "Ink2");
        try
        {
            string source = Environment.ProcessPath ?? throw new InvalidOperationException("Can't tell where this file is.");
            await Task.Run(() =>
            {
                Instances.StopOthers(TimeSpan.FromSeconds(6));
                _installation.Install(source, AppInfo.Version, desktop, autostart);
            });
            Process.Start(new ProcessStartInfo(_installation.ExePath, "--installed") { UseShellExecute = true, WorkingDirectory = _installation.InstallDirectory })?.Dispose();
            Close();
        }
        catch (Exception ex)
        {
            Log.Error("Install failed", ex);
            Say($"Couldn't install: {ex.Message}", "Critical");
            InstallButton.IsEnabled = CancelButton.IsEnabled = CloseButton.IsEnabled = true;
        }
    }

    private void Say(string text, string brushKey)
    {
        StatusText.Text = text;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty, brushKey);
        StatusText.Visibility = Visibility.Visible;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
