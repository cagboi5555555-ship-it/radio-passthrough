using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RadioPassthrough.Core;
using RadioPassthrough.Core.Diagnostics;
using RadioPassthrough.Core.Settings;
using RadioPassthrough.Core.Setup;
using RadioPassthrough.Dialogs;
using RadioPassthrough.Theme;
using RadioPassthrough.ViewModels;

namespace RadioPassthrough;

// Start-up modes:
//   (no args) / --tray       normal app (--tray starts hidden, used at Windows sign-in)
//   run from outside the install folder → installer
//   --installed              first start right after install/update
//   --install [--desktop-shortcut]  install/update silently (scripted roll-out), then start in the tray
//   --uninstall [--quiet]    remove (from Settings → Apps)
//   --portable               run in place without installing
//   --snapshot <dir>         render every screen to PNG off-screen (design review)
//   --selftest <file>        check the parts that need real Windows, without showing or changing anything
public partial class App : Application
{
    private const int MaxCrashRestarts = 3;

    private Mutex? _instance;
    private AppController? _controller;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private MenuItem? _gameAudioItem;
    private bool _quitting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandled;
        AppDomain.CurrentDomain.UnhandledException += OnFatal;
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error("Background task failed", args.Exception);
            args.SetObserved();
        };

        string[] args = e.Args;
        string? forcedTheme = ArgValue(args, "--theme");
        ThemeManager.Initialize(forcedTheme is null ? null : forcedTheme == "dark");

        if (ArgValue(args, "--snapshot") is { } snapshotDir)
        {
            await RunSnapshotAsync(snapshotDir);
            return;
        }

        if (ArgValue(args, "--selftest") is { } resultFile)
        {
            bool passed = await SelfTest.RunAsync(resultFile, BuildTrayMenu);
            Shutdown(passed ? 0 : 1);
            return;
        }

        if (args.Contains("--uninstall"))
        {
            await UninstallAsync(quiet: args.Contains("--quiet"));
            Shutdown();
            return;
        }

        var installation = new Installation();
        if (args.Contains("--install"))
        {
            Shutdown(await InstallSilentlyAsync(installation, args.Contains("--desktop-shortcut")) ? 0 : 1);
            return;
        }

        if (!IsBuildOutput && !args.Contains("--portable") && !installation.IsThisCopy(Environment.ProcessPath))
        {
            new InstallerWindow(installation).ShowDialog();
            Shutdown();
            return;
        }

        await RunAppAsync(args);
    }

    private async Task RunAppAsync(string[] args)
    {
        _instance = new Mutex(true, Instances.MutexName, out bool first);
        if (!first && (args.Contains("--restarted") || args.Contains("--installed")))
        {
            try { first = _instance.WaitOne(TimeSpan.FromSeconds(8)); }
            catch (AbandonedMutexException) { first = true; }
        }
        if (!first)
        {
            Instances.SignalShow();
            _instance = null;
            Shutdown();
            return;
        }

        ListenForSignal(Instances.ShowEventName, ShowWindow);
        ListenForSignal(Instances.QuitEventName, () => Quit(confirm: false));

        _controller = new AppController();
        _viewModel = new MainViewModel(_controller);
        _window = new MainWindow(_viewModel);
        _tray = new TrayIcon(ShowWindow, BuildTrayMenu());
        _viewModel.PropertyChanged += (_, p) =>
        {
            if (p.PropertyName == nameof(MainViewModel.StatusText)) _tray?.SetTooltip($"Radio Passthrough · {_viewModel.StatusText}");
        };
        _controller.DeviceGuard.Restored += message => Dispatcher.BeginInvoke(() => _tray?.ShowMessage("Sound devices put back", message));
        _window.HiddenToTray += () =>
        {
            if (_controller.Settings.TrayHintShown) return;
            _controller.Settings.TrayHintShown = true;
            _controller.ScheduleSave();
            _tray?.ShowMessage("Still running", "Radio Passthrough keeps TeamSpeak's mic working from the tray. Right-click the icon to quit.");
        };

        await _controller.StartAsync();
        _viewModel.Start();

        if (!_controller.Settings.FirstRunDone)
        {
            _controller.Settings.FirstRunDone = true;
            _controller.SaveNow();
            _viewModel.SelectedTab = 2;
            ShowWindow();
            return;
        }

        if (!args.Contains("--tray")) ShowWindow();
    }

    private ContextMenu BuildTrayMenu()
    {
        var menu = new ContextMenu();
        menu.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenu");

        MenuItem Item(string header, Action onClick)
        {
            var item = new MenuItem { Header = header };
            item.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuItem");
            item.Click += (_, _) => onClick();
            return item;
        }

        Separator Line()
        {
            var s = new Separator();
            s.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuSeparator");
            return s;
        }

        _gameAudioItem = Item("Send game audio over radio", () => _viewModel?.Live.ToggleGameAudio());
        menu.Opened += (_, _) => _gameAudioItem.IsChecked = _controller?.Settings.GameAudioEnabled == true;

        menu.Items.Add(Item("Open Radio Passthrough", ShowWindow));
        menu.Items.Add(Line());
        menu.Items.Add(_gameAudioItem);
        menu.Items.Add(Line());
        menu.Items.Add(Item("Quit…", () => Quit(confirm: true)));
        return menu;
    }

    private void ListenForSignal(string name, Action action)
    {
        var signal = new EventWaitHandle(false, EventResetMode.AutoReset, name);
        new Thread(() =>
        {
            while (signal.WaitOne())
                Dispatcher.BeginInvoke(action);
        }) { IsBackground = true, Name = name }.Start();
    }

    private static bool IsBuildOutput =>
        Environment.ProcessPath?.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase) == true;

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public static void RestartElevated()
    {
        if (Current is not App app || Environment.ProcessPath is not { } exe) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe, "--restarted") { UseShellExecute = true, Verb = "runas" })?.Dispose();
        }
        catch (Win32Exception)
        {
            return; // UAC prompt declined.
        }
        app.Quit(confirm: false);
    }

    public async void Quit(bool confirm)
    {
        if (_quitting) return;
        if (confirm && !SheetDialog.Ask(_window, "Quit Radio Passthrough?",
                "TeamSpeak uses this app as your microphone, so nobody hears you until it runs again. It starts by itself the next time you sign in.",
                "Quit", "Keep running"))
            return;

        _quitting = true;
        try
        {
            _tray?.Dispose();
            if (_window is not null)
            {
                _window.AllowClose = true;
                _window.Close();
            }
            if (_controller is not null) await _controller.DisposeAsync();
        }
        catch (Exception e)
        {
            Log.Error("Error while quitting", e); // still exit below
        }
        finally
        {
            try { _instance?.ReleaseMutex(); } catch (ApplicationException) { }
            Shutdown();
        }
    }

    private async Task UninstallAsync(bool quiet)
    {
        if (!quiet && !SheetDialog.Ask(null, "Remove Radio Passthrough?",
                "TeamSpeak goes back to your normal microphone and the app is removed from this PC. VB-CABLE stays installed.",
                "Remove", "Keep"))
            return;

        string note = await Task.Run(async () =>
        {
            Instances.StopOthers(TimeSpan.FromSeconds(6));
            var settings = new SettingsStore().Load();
            string result = "";
            try
            {
                var teamSpeak = new TeamSpeakSettings();
                var state = teamSpeak.Read();
                if (state.Installed && state.CaptureProfiles.Any(p => p.Name == TeamSpeakSettings.ProfileName))
                {
                    string? relaunch = null;
                    if (TeamSpeakClient.IsRunning()) relaunch = await TeamSpeakClient.CloseAsync(TimeSpan.FromSeconds(15));
                    teamSpeak.RemoveProfile(settings.PreviousTeamSpeakProfile);
                    if (relaunch is not null) TeamSpeakClient.Start(relaunch);
                }
            }
            catch (Exception e)
            {
                Log.Error("Couldn't restore TeamSpeak during uninstall", e);
                result = "TeamSpeak couldn't be switched back automatically. In TeamSpeak, open Options → Capture and pick your usual profile. ";
            }

            new Installation().RemoveIntegration();
            return result;
        });

        if (!quiet)
            SheetDialog.Tell(null, "Radio Passthrough is removed",
                note + "VB-CABLE is still installed. If you don't need it anymore, remove it in Settings → Apps.");

        // Files go last. Nothing may write a log line after this, or the log folder comes back.
        Installation.DeleteDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadioPassthrough"));
        Installation.DeleteDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadioPassthrough"));
        Installation.RemoveFiles(new Installation().InstallDirectory, Environment.ProcessPath);
    }

    // For scripted roll-outs: install or update with no window, then start the installed copy in the tray.
    private static async Task<bool> InstallSilentlyAsync(Installation installation, bool desktopShortcut)
    {
        try
        {
            string source = Environment.ProcessPath ?? throw new InvalidOperationException("Can't tell where this file is.");
            await Task.Run(() =>
            {
                Instances.StopOthers(TimeSpan.FromSeconds(6));
                installation.Install(source, AppInfo.Version, desktopShortcut, startWithWindows: true);
            });
            Process.Start(new ProcessStartInfo(installation.ExePath, "--tray --installed") { UseShellExecute = true, WorkingDirectory = installation.InstallDirectory })?.Dispose();
            return true;
        }
        catch (Exception e)
        {
            Log.Error("Silent install failed", e);
            return false;
        }
    }

    private async Task RunSnapshotAsync(string directory)
    {
        _controller = new AppController();
        _viewModel = new MainViewModel(_controller);
        _window = new MainWindow(_viewModel);
        await _controller.StartAsync(audio: false);
        _viewModel.Start();
        await Snapshot.CaptureAllAsync(_window, _viewModel, directory);
        _quitting = true;
        _window.AllowClose = true;
        _window.Close();
        await _controller.DisposeAsync();
        Shutdown();
    }

    private void OnDispatcherUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error("Unexpected error in the window", e.Exception);
        e.Handled = true;
    }

    // Last resort: the process is going down. Log it and start a fresh copy in the tray so TeamSpeak
    // doesn't lose your mic, unless it keeps crashing.
    private void OnFatal(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Error("Fatal error", e.ExceptionObject as Exception);
        if (_controller is null || _quitting || Environment.ProcessPath is not { } exe) return;
        try
        {
            string file = Path.Combine(Log.Directory, "crashes.txt");
            var recent = File.Exists(file)
                ? File.ReadAllLines(file).Select(l => DateTime.TryParse(l, out var t) ? t : DateTime.MinValue).Where(t => t > DateTime.Now.AddMinutes(-10)).ToList()
                : [];
            recent.Add(DateTime.Now);
            File.WriteAllLines(file, recent.Select(t => t.ToString("O")));
            if (recent.Count > MaxCrashRestarts) return;
            try { _instance?.ReleaseMutex(); } catch (ApplicationException) { }
            Process.Start(new ProcessStartInfo(exe, "--tray --restarted") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception)
        {
            // Nothing more can be done from here.
        }
    }
}
