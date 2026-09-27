using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using RadioPassthrough.Core.Setup;
using RadioPassthrough.Theme;
using RadioPassthrough.ViewModels;

namespace RadioPassthrough;

public partial class App : Application
{
    private const string InstanceName = "RadioPassthrough.SingleInstance";
    private const string ShowEventName = "RadioPassthrough.Show";

    private Mutex? _instance;
    private EventWaitHandle? _showEvent;
    private AppController? _controller;
    private MainViewModel? _viewModel;
    private MainWindow? _window;
    private TrayIcon? _tray;
    private bool _quitting;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        string? snapshotDir = ArgValue(e.Args, "--snapshot");

        if (snapshotDir is null)
        {
            _instance = new Mutex(true, InstanceName, out bool first);
            if (!first && e.Args.Contains("--restarted"))
            {
                try { first = _instance.WaitOne(TimeSpan.FromSeconds(8)); }
                catch (AbandonedMutexException) { first = true; }
            }
            if (!first)
            {
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (WaitHandleCannotBeOpenedException) { }
                _instance = null;
                Shutdown();
                return;
            }

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            new Thread(() =>
            {
                while (_showEvent.WaitOne())
                    Dispatcher.BeginInvoke(ShowWindow);
            }) { IsBackground = true, Name = "Show signal" }.Start();
        }

        string? forcedTheme = ArgValue(e.Args, "--theme");
        ThemeManager.Initialize(forcedTheme is null ? null : forcedTheme == "dark");

        _controller = new AppController();
        _viewModel = new MainViewModel(_controller);
        _window = new MainWindow(_viewModel);

        if (snapshotDir is not null)
        {
            await _controller.StartAsync();
            _viewModel.Start();
            await Snapshot.CaptureTabsAsync(_window, _viewModel, snapshotDir);
            Quit();
            return;
        }

        _tray = new TrayIcon(ShowWindow, Quit);
        _controller.Engine.StatusChanged += s => Dispatcher.BeginInvoke(() => _tray?.SetStatus(s.CableFound ? "Running" : "VB-CABLE missing"));

        await _controller.StartAsync();
        _viewModel.Start();

        // Only the installed copy registers itself to start with Windows, never a build folder.
        if (!_controller.Settings.FirstRunDone && !IsBuildOutput)
        {
            _controller.Settings.FirstRunDone = true;
            if (_controller.Settings.StartWithWindows && Environment.ProcessPath is { } exe)
                AutoStart.Set(true, exe);
            _controller.SaveNow();
            _viewModel.SelectedTab = 2;
        }

        if (!e.Args.Contains("--tray"))
            ShowWindow();
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
            Process.Start(new ProcessStartInfo(exe, "--restarted") { UseShellExecute = true, Verb = "runas" });
        }
        catch (Win32Exception)
        {
            return; // UAC prompt declined.
        }
        app.Quit();
    }

    public async void Quit()
    {
        if (_quitting) return;
        _quitting = true;
        _tray?.Dispose();
        if (_window is not null)
        {
            _window.AllowClose = true;
            _window.Close();
        }
        if (_controller is not null) await _controller.DisposeAsync();
        _instance?.ReleaseMutex();
        Shutdown();
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadioPassthrough");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTime.Now:u}  {e.Exception}\n\n");
        }
        catch (IOException)
        {
        }
        e.Handled = true;
    }
}
