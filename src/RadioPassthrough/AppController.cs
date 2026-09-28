using System.IO;
using Microsoft.Win32;
using RadioPassthrough.Core.Audio;
using RadioPassthrough.Core.Diagnostics;
using RadioPassthrough.Core.Game;
using RadioPassthrough.Core.Ptt;
using RadioPassthrough.Core.Settings;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough;

// Owns the long-lived services: audio engine, radio keys, Arma watcher, default-device guard, settings.
public sealed class AppController : IAsyncDisposable
{
    private readonly Timer _saveTimer;
    private readonly object _saveLock = new();
    private int _saveFailures;
    private volatile bool _testMode;
    private volatile bool _disposed;
    private bool _started;

    public AppController()
    {
        Store = new SettingsStore();
        Settings = Store.Load();
        _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
        Keys = new KeyPoller(Settings.Bindings, IsRadioKeyAllowed);
        DeviceGuard = new DefaultDeviceGuard(new WindowsDefaultDevices(), Settings.RememberedDefaults, () => Settings.MicDeviceId)
        {
            Enabled = Settings.KeepRealDefaults,
        };
        DeviceGuard.RememberedChanged += remembered =>
        {
            Settings.RememberedDefaults = new Dictionary<string, string>(remembered);
            ScheduleSave();
        };
    }

    public SettingsStore Store { get; }
    public AppSettings Settings { get; }
    public AudioEngine Engine { get; } = new();
    public ArmaWatcher Arma { get; } = new();
    public TeamSpeakSettings TeamSpeak { get; } = new();
    public KeyPoller Keys { get; }
    public DefaultDeviceGuard DeviceGuard { get; }
    public Installation Installation { get; } = new();

    // While testing, the radio key works in any window so you can hold it over the browser.
    public bool TestMode
    {
        get => _testMode;
        set
        {
            _testMode = value;
            Engine.TestActive = value;
        }
    }

    private bool IsRadioKeyAllowed()
    {
        if (_testMode) return true;
        int arma = Arma.CurrentPid;
        return arma != 0 && ProcessInfo.ForegroundProcessId() == arma;
    }

    // Preview renders (--snapshot) pass audio: false so they never touch devices or the cable.
    public async Task StartAsync(bool audio = true)
    {
        if (!audio)
        {
            Arma.Start();
            return;
        }

        _started = true;
        Log.Info($"Starting {Core.AppInfo.Name} {Core.AppInfo.Version.ToString(3)} from {Environment.ProcessPath}");
        Engine.MicMode = Settings.MicChannels;
        Engine.NoiseReductionDb = Settings.NoiseReductionDb;
        Engine.GameAudioEnabled = Settings.GameAudioEnabled;

        if (Settings.MicDeviceId is null)
        {
            // First run: use the mic TeamSpeak uses today.
            try
            {
                var profile = TeamSpeak.Read().CaptureProfiles.FirstOrDefault(p => p.Name == "Default");
                if (profile?.DeviceId is { } id && !AudioDevices.IsCable(profile.DeviceName ?? "") && AudioDevices.Exists(id))
                    Settings.MicDeviceId = id;
            }
            catch (Exception e)
            {
                Log.Warn($"Couldn't read TeamSpeak's mic: {e.Message}");
            }
            ScheduleSave();
        }

        Keys.RadioKeyChanged += Engine.SetRadioKey;
        Keys.Start();
        Arma.Changed += g =>
        {
            Log.Info(g is null ? "Arma 3 closed." : $"Arma 3 found (pid {g.Pid}{(g.Elevated ? ", admin" : "")}).");
            _ = Engine.SetArmaAsync(g);
        };
        Arma.Start();

        await Task.Run(() =>
        {
            try
            {
                if (Settings.HideUnusedCableDevices) CableHousekeeping.HideUnusedEndpoints();
                DeviceGuard.Start();
            }
            catch (Exception e)
            {
                Log.Error("Device guard failed to start", e);
            }
        });

        // When VB-CABLE gets installed while the app runs, tidy up straight away: hide its unused
        // devices and undo any default-device switch its installer made.
        bool cableSeen = AudioDevices.CableInput() is not null;
        Engine.StatusChanged += status =>
        {
            if (!status.CableFound || cableSeen) return;
            cableSeen = true;
            Log.Info("VB-CABLE appeared.");
            _ = Task.Run(() =>
            {
                try
                {
                    if (Settings.HideUnusedCableDevices) CableHousekeeping.HideUnusedEndpoints();
                    DeviceGuard.Check();
                }
                catch (Exception e)
                {
                    Log.Error("Tidying up after VB-CABLE install failed", e);
                }
            });
        };

        await Engine.SetArmaAsync(Arma.Current);
        await Engine.SetMicAsync(Settings.MicDeviceId);

        // Audio streams don't survive sleep reliably; reopen everything on wake.
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        Log.Info("Resumed from sleep; reopening audio.");
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => Engine.RestartAsync());
    }

    public void ScheduleSave() => ScheduleSave(TimeSpan.FromMilliseconds(600));

    private void ScheduleSave(TimeSpan delay)
    {
        try
        {
            _saveTimer.Change(delay, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Quitting; DisposeAsync saves one last time.
        }
    }

    // Called from the UI, the save timer and background work, so saves are serialized. A failed save
    // (file locked, or settings changing mid-write) is retried shortly instead of ever taking the app down.
    public void SaveNow()
    {
        lock (_saveLock)
        {
            try
            {
                Store.Save(Settings);
                _saveFailures = 0;
            }
            catch (Exception e)
            {
                if (_saveFailures++ == 0) Log.Warn($"Couldn't save settings: {e.Message}");
                if (_saveFailures <= 3 && !_disposed) ScheduleSave(TimeSpan.FromSeconds(5));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _disposed = true;
        await _saveTimer.DisposeAsync();
        SaveNow();
        Keys.Dispose();
        DeviceGuard.Dispose();
        Arma.Dispose();
        await Engine.DisposeAsync();
        if (_started) Log.Info("Stopped.");
    }
}
