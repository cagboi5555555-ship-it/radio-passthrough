using System.IO;
using RadioPassthrough.Core.Audio;
using RadioPassthrough.Core.Game;
using RadioPassthrough.Core.Ptt;
using RadioPassthrough.Core.Settings;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough;

// Owns the long-lived services: audio engine, radio key hook, Arma watcher, settings.
public sealed class AppController : IAsyncDisposable
{
    private readonly Timer _saveTimer;
    private volatile bool _testMode;

    public AppController()
    {
        Store = new SettingsStore();
        Settings = Store.Load();
        _saveTimer = new Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
        Hook = new InputHook(Settings.Bindings, IsRadioKeyAllowed);
    }

    public SettingsStore Store { get; }
    public AppSettings Settings { get; }
    public AudioEngine Engine { get; } = new();
    public ArmaWatcher Arma { get; } = new();
    public TeamSpeakSettings TeamSpeak { get; } = new();
    public InputHook Hook { get; }

    // While testing, the radio key works in any window so you can hold it over the browser.
    public bool TestMode
    {
        get => _testMode;
        set => _testMode = value;
    }

    private bool IsRadioKeyAllowed()
    {
        if (_testMode) return true;
        int arma = Arma.CurrentPid;
        return arma != 0 && ProcessInfo.ForegroundProcessId() == arma;
    }

    public async Task StartAsync()
    {
        Engine.Mixer.Settings = Settings.ActiveMix;
        Engine.MicMode = Settings.MicChannels;

        if (Settings.MicDeviceId is null)
        {
            // First run: use the mic TeamSpeak uses today.
            try
            {
                var active = TeamSpeak.Read().CaptureProfiles.FirstOrDefault(p => p.Name == "Default");
                if (active?.DeviceId is { } id && !AudioDevices.IsCable(active.DeviceName ?? "") && AudioDevices.Exists(id))
                    Settings.MicDeviceId = id;
            }
            catch
            {
                // No TeamSpeak settings: Windows' default mic is used instead.
            }
            ScheduleSave();
        }

        Hook.RadioKeyChanged += Engine.SetRadioKey;
        Hook.Start();
        Arma.Changed += g => _ = Engine.SetArmaAsync(g);
        Arma.Start();
        await Engine.SetArmaAsync(Arma.Current);
        await Engine.SetMicAsync(Settings.MicDeviceId);
    }

    public void ApplyMix() => Engine.Mixer.Settings = Settings.ActiveMix;

    public void ScheduleSave() => _saveTimer.Change(TimeSpan.FromMilliseconds(600), Timeout.InfiniteTimeSpan);

    public void SaveNow()
    {
        try
        {
            Store.Save(Settings);
        }
        catch (IOException)
        {
            // Try again on the next change.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _saveTimer.DisposeAsync();
        SaveNow();
        Hook.Dispose();
        Arma.Dispose();
        await Engine.DisposeAsync();
    }
}
