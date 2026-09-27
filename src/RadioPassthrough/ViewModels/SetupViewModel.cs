using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using RadioPassthrough.Core;
using RadioPassthrough.Core.Audio;
using RadioPassthrough.Core.Diagnostics;
using RadioPassthrough.Core.Dsp;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough.ViewModels;

public sealed class CheckItem
{
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required CheckLevel Level { get; init; }
    public string? ActionLabel { get; init; }
    public ICommand? Action { get; init; }
    public bool IsPrimary { get; init; }
    public bool Busy { get; init; }
    public bool HasPrimaryAction => Action is not null && IsPrimary && !Busy;
    public bool HasSecondaryAction => Action is not null && !IsPrimary && !Busy;
}

public sealed class SetupViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private DeviceInfo? _selectedMic;
    private string? _message;
    private CheckLevel _messageLevel;
    private bool _suppressMicChange;
    private CheckAction _busyAction = CheckAction.None;
    private string? _busyText;
    private TeamSpeakState? _lastTeamSpeak;
    private bool _allReady;

    public SetupViewModel(AppController app)
    {
        _app = app;
        CopyDiagnosticsCommand = new AsyncCommand(CopyDiagnosticsAsync);
        OpenLogsCommand = new RelayCommand(() =>
        {
            Directory.CreateDirectory(Log.Directory);
            Open(Log.Directory);
        });
    }

    public ObservableCollection<CheckItem> Checks { get; } = new();

    public IReadOnlyList<Check> LatestChecks { get; private set; } = [];

    public ObservableCollection<DeviceInfo> Microphones { get; } = new();

    public ICommand CopyDiagnosticsCommand { get; }
    public ICommand OpenLogsCommand { get; }

    public bool AllReady { get => _allReady; private set => Set(ref _allReady, value); }

    public string Headline => AllReady ? "You're set" : "A few steps and you're set";

    public DeviceInfo? SelectedMic
    {
        get => _selectedMic;
        set
        {
            if (!Set(ref _selectedMic, value) || _suppressMicChange || value is null) return;
            _app.Settings.MicDeviceId = value.Id;
            _app.ScheduleSave();
            Log.Info($"Microphone set to {value.Name}.");
            _ = _app.Engine.SetMicAsync(value.Id);
        }
    }

    public int MicChannelIndex
    {
        get => (int)_app.Settings.MicChannels;
        set
        {
            _app.Settings.MicChannels = (MicChannelMode)value;
            _app.Engine.MicMode = (MicChannelMode)value;
            _app.ScheduleSave();
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => _app.Installation.IsThisCopy(Environment.ProcessPath) ? _app.Installation.StartsWithWindows : AutoStart.IsEnabled;
        set
        {
            _app.Settings.StartWithWindows = value;
            _app.ScheduleSave();
            if (_app.Installation.IsThisCopy(Environment.ProcessPath)) _app.Installation.SetStartWithWindows(value);
            else if (Environment.ProcessPath is { } exe) AutoStart.Set(value, exe);
            OnPropertyChanged();
        }
    }

    public bool KeepRealDefaults
    {
        get => _app.Settings.KeepRealDefaults;
        set
        {
            _app.Settings.KeepRealDefaults = value;
            _app.DeviceGuard.Enabled = value;
            _app.ScheduleSave();
            if (value) _ = Task.Run(_app.DeviceGuard.Check);
            OnPropertyChanged();
        }
    }

    public string? Message { get => _message; private set => Set(ref _message, value); }

    public CheckLevel MessageLevel { get => _messageLevel; private set => Set(ref _messageLevel, value); }

    public string Version => $"Version {AppInfo.Version.ToString(3)}";

    public event Action? Refreshed;

    public async Task RefreshAsync()
    {
        if (!await _refreshing.WaitAsync(0)) return;
        try
        {
            var arma = _app.Arma.Current;
            var (ts, checks, mics) = await Task.Run(() =>
            {
                TeamSpeakState ts;
                try
                {
                    ts = _app.TeamSpeak.Read();
                }
                catch (Exception e)
                {
                    Log.Warn($"Couldn't read TeamSpeak settings: {e.Message}");
                    ts = new TeamSpeakState { Installed = false };
                }
                return (ts, SystemChecks.Run(ts, arma), AudioDevices.Microphones());
            });

            _lastTeamSpeak = ts;
            LatestChecks = checks;
            RebuildChecks();

            _suppressMicChange = true;
            if (!Microphones.Select(m => m.Id).SequenceEqual(mics.Select(m => m.Id)))
            {
                Microphones.Clear();
                foreach (var m in mics) Microphones.Add(m);
            }
            string? current = _app.Settings.MicDeviceId ?? AudioEngine.FallbackMicrophone();
            SelectedMic = Microphones.FirstOrDefault(m => m.Id == current);
            _suppressMicChange = false;
            OnPropertyChanged(nameof(StartWithWindows));
        }
        finally
        {
            _refreshing.Release();
        }
        Refreshed?.Invoke();
    }

    private void RebuildChecks()
    {
        bool primaryGiven = false;
        Checks.Clear();
        foreach (var c in LatestChecks)
        {
            bool busy = c.Action != CheckAction.None && c.Action == _busyAction;
            ICommand? action = c.Action == CheckAction.None ? null : new AsyncCommand(() => RunAsync(c.Action), () => _busyAction == CheckAction.None);
            bool primary = action is not null && !primaryGiven && c.Level == CheckLevel.Blocking;
            primaryGiven |= primary;
            Checks.Add(new CheckItem
            {
                Title = c.Title,
                Detail = busy && _busyText is not null ? _busyText : c.Detail,
                Level = c.Level,
                ActionLabel = c.ActionLabel,
                Action = action,
                IsPrimary = primary,
                Busy = busy,
            });
        }
        AllReady = LatestChecks.All(c => c.Level is CheckLevel.Ok or CheckLevel.Info);
        OnPropertyChanged(nameof(Headline));
    }

    private void Busy(CheckAction action, string? text)
    {
        _busyAction = action;
        _busyText = text;
        Application.Current.Dispatcher.Invoke(RebuildChecks);
    }

    private async Task RunAsync(CheckAction action)
    {
        Message = null;
        try
        {
            switch (action)
            {
                case CheckAction.GetCable:
                    Open(VbCableWebsite);
                    Say("VB-Audio's page is open in your browser. Download the VB-CABLE Driver Pack, unzip it, right-click VBCABLE_Setup_x64.exe → Run as administrator → Install Driver. " +
                        "This app notices the moment it's installed and finishes the rest.", CheckLevel.Info);
                    break;
                case CheckAction.SetUpTeamSpeak:
                    await ChangeTeamSpeakAsync(apply: true);
                    break;
                case CheckAction.RestoreTeamSpeak:
                    if (SheetDialogAsk("Switch TeamSpeak back to your normal mic?",
                            "Radio passthrough stops working until you set it up again. TeamSpeak restarts for a moment if it's open.", "Switch back"))
                        await ChangeTeamSpeakAsync(apply: false);
                    break;
                case CheckAction.FixDevices:
                    await Task.Run(() =>
                    {
                        var hidden = CableHousekeeping.HideUnusedEndpoints();
                        int restored = _app.DeviceGuard.Check();
                        Application.Current.Dispatcher.Invoke(() => Say(
                            restored > 0 || hidden.Count > 0 ? "Done. Your own speakers and mic are the defaults, and unused cable devices are hidden." : "Nothing needed changing.",
                            CheckLevel.Ok));
                    });
                    break;
                case CheckAction.OpenCableFormat:
                    Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl,,1") { UseShellExecute = true })?.Dispose();
                    Say("In Recording, open CABLE Output → Advanced and choose 48000 Hz. Do the same for CABLE Input under Playback.", CheckLevel.Info);
                    break;
                case CheckAction.RestartAsAdmin:
                    App.RestartElevated();
                    return;
            }
        }
        catch (InvalidOperationException ex)
        {
            Say(ex.Message, CheckLevel.Attention);
        }
        catch (Exception ex)
        {
            Log.Error($"{action} failed", ex);
            Say($"That didn't work: {ex.Message}", CheckLevel.Blocking);
        }
        finally
        {
            Busy(CheckAction.None, null);
        }
        await RefreshAsync();
    }

    // VB-Audio's own page. The app never downloads anything itself.
    public const string VbCableWebsite = "https://vb-audio.com/Cable/";

    private async Task ChangeTeamSpeakAsync(bool apply)
    {
        string? relaunch = null;
        if (_app.TeamSpeak.IsRunning)
        {
            Busy(apply ? CheckAction.SetUpTeamSpeak : CheckAction.RestoreTeamSpeak, "Closing TeamSpeak for a moment…");
            relaunch = await TeamSpeakClient.CloseAsync(TimeSpan.FromSeconds(15));
        }

        try
        {
            await Task.Run(() =>
            {
                if (apply)
                {
                    var cable = AudioDevices.CableOutput() ?? throw new InvalidOperationException("Install VB-CABLE first.");
                    _app.Settings.PreviousTeamSpeakProfile = _app.TeamSpeak.Apply(cable.Id, cable.Name);
                    _app.SaveNow();
                }
                else
                {
                    _app.TeamSpeak.Restore(_app.Settings.PreviousTeamSpeakProfile);
                }
            });
        }
        finally
        {
            if (relaunch is not null) TeamSpeakClient.Start(relaunch);
        }

        Say(apply
                ? "Done. TeamSpeak uses Radio Passthrough on every server, with the same processing as before. A backup of its settings sits next to the original."
                : "TeamSpeak is back on your normal microphone.",
            CheckLevel.Ok);
    }

    private async Task CopyDiagnosticsAsync()
    {
        var arma = _app.Arma.Current;
        string report = await Task.Run(() => DiagnosticsReport.Build(_app.Settings, _app.Engine.Status, _lastTeamSpeak, LatestChecks, arma));
        try
        {
            Clipboard.SetText(report);
            Say("Diagnostics copied. Paste them where you're asking for help.", CheckLevel.Ok);
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            string file = Path.Combine(Log.Directory, "diagnostics.txt");
            await File.WriteAllTextAsync(file, report);
            Say($"Clipboard was busy, so diagnostics were saved to {file}.", CheckLevel.Info);
        }
    }

    private static bool SheetDialogAsk(string title, string body, string primary) =>
        Dialogs.SheetDialog.Ask(Application.Current.MainWindow, title, body, primary, "Cancel");

    private void Say(string text, CheckLevel level)
    {
        MessageLevel = level;
        Message = text;
    }

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Couldn't open {target}: {e.Message}");
        }
    }
}
