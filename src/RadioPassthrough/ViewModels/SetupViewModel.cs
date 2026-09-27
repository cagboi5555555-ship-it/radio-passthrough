using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using RadioPassthrough.Core.Audio;
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
    public bool HasAction => Action is not null;
    public bool HasPrimaryAction => HasAction && IsPrimary;
    public bool HasSecondaryAction => HasAction && !IsPrimary;
}

public sealed class SetupViewModel : ObservableObject
{
    private readonly AppController _app;
    private readonly SemaphoreSlim _refreshing = new(1, 1);
    private DeviceInfo? _selectedMic;
    private string? _message;
    private CheckLevel _messageLevel;
    private bool _startWithWindows;
    private bool _suppressMicChange;

    public SetupViewModel(AppController app)
    {
        _app = app;
        _startWithWindows = AutoStart.IsEnabled;
    }

    public ObservableCollection<CheckItem> Checks { get; } = new();

    public IReadOnlyList<Check> LatestChecks { get; private set; } = [];

    public ObservableCollection<DeviceInfo> Microphones { get; } = new();

    public DeviceInfo? SelectedMic
    {
        get => _selectedMic;
        set
        {
            if (!Set(ref _selectedMic, value) || _suppressMicChange || value is null) return;
            _app.Settings.MicDeviceId = value.Id;
            _app.ScheduleSave();
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
        get => _startWithWindows;
        set
        {
            if (!Set(ref _startWithWindows, value)) return;
            _app.Settings.StartWithWindows = value;
            _app.ScheduleSave();
            if (Environment.ProcessPath is { } exe) AutoStart.Set(value, exe);
        }
    }

    public string? Message { get => _message; private set => Set(ref _message, value); }

    public CheckLevel MessageLevel { get => _messageLevel; private set => Set(ref _messageLevel, value); }

    public string Version => $"Version {typeof(SetupViewModel).Assembly.GetName().Version?.ToString(3)}";

    public event Action? Refreshed;

    public async Task RefreshAsync()
    {
        if (!await _refreshing.WaitAsync(0)) return;
        try
        {
            var arma = _app.Arma.Current;
            var (checks, mics) = await Task.Run(() =>
            {
                var ts = _app.TeamSpeak.Read();
                return (SystemChecks.Run(ts, arma), AudioDevices.Microphones());
            });

            LatestChecks = checks;
            bool primaryGiven = false;
            Checks.Clear();
            foreach (var c in checks)
            {
                ICommand? action = c.Action switch
                {
                    CheckAction.None => null,
                    _ => new AsyncCommand(() => RunAsync(c.Action)),
                };
                bool primary = action is not null && !primaryGiven && c.Level == CheckLevel.Blocking;
                primaryGiven |= primary;
                Checks.Add(new CheckItem
                {
                    Title = c.Title,
                    Detail = c.Detail,
                    Level = c.Level,
                    ActionLabel = c.ActionLabel,
                    Action = action,
                    IsPrimary = primary,
                });
            }

            _suppressMicChange = true;
            Microphones.Clear();
            foreach (var m in mics) Microphones.Add(m);
            string? current = _app.Settings.MicDeviceId ?? AudioDevices.DefaultMicrophone()?.Id;
            SelectedMic = Microphones.FirstOrDefault(m => m.Id == current);
            _suppressMicChange = false;
        }
        finally
        {
            _refreshing.Release();
        }
        Refreshed?.Invoke();
    }

    private async Task RunAsync(CheckAction action)
    {
        Message = null;
        try
        {
            switch (action)
            {
                case CheckAction.GetCable:
                    Open("https://vb-audio.com/Cable/");
                    Say("Download the VB-CABLE Driver Pack, unzip it, run VBCABLE_Setup_x64.exe as administrator and press Install Driver. Then restart your PC.", CheckLevel.Info);
                    break;
                case CheckAction.OpenSoundSettings:
                    Open("ms-settings:sound");
                    break;
                case CheckAction.OpenCableFormat:
                    Process.Start(new ProcessStartInfo("control.exe", "mmsys.cpl,,1") { UseShellExecute = true });
                    Say("In Recording, open CABLE Output → Advanced and pick 48000 Hz. Do the same for CABLE Input under Playback.", CheckLevel.Info);
                    break;
                case CheckAction.SetUpTeamSpeak:
                    SetUpTeamSpeak();
                    break;
                case CheckAction.RestoreTeamSpeak:
                    _app.TeamSpeak.Restore(_app.Settings.PreviousTeamSpeakProfile);
                    Say("TeamSpeak is back on your normal microphone.", CheckLevel.Ok);
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            Say($"Couldn't change TeamSpeak's settings: {ex.Message}", CheckLevel.Blocking);
        }
        await RefreshAsync();
    }

    private void SetUpTeamSpeak()
    {
        var cable = AudioDevices.CableOutput() ?? throw new InvalidOperationException("Install VB-CABLE first.");
        string previous = _app.TeamSpeak.Apply(cable.Id, cable.Name);
        _app.Settings.PreviousTeamSpeakProfile = previous;
        _app.SaveNow();
        Say("Done. TeamSpeak now uses Radio Passthrough on all servers, with the same processing as your old profile. A backup of its settings sits next to the original.", CheckLevel.Ok);
    }

    private void Say(string text, CheckLevel level)
    {
        MessageLevel = level;
        Message = text;
    }

    private static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
}
