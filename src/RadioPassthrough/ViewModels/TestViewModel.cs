using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using RadioPassthrough.Core.Audio;
using RadioPassthrough.Core.Dsp;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough.ViewModels;

public sealed record SourceItem(int? Pid, string Name, bool IsArma, bool Playing)
{
    public string Label => Playing && !IsArma ? $"{Name}  ·  playing" : Name;
}

public sealed record ResultCheck(string Text, CheckLevel Level);

public sealed class TestViewModel : ObservableObject
{
    private static readonly TimeSpan RecordLength = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan TeamSpeakTestLength = TimeSpan.FromSeconds(30);

    private readonly AppController _app;
    private readonly ClipPlayer _player = new();
    private SourceItem? _selectedSource;
    private CancellationTokenSource? _recordCts, _tsCts;
    private bool _isRecording, _holding, _hasTake, _isPlaying, _tsRunning, _hasTsClip, _playingTs;
    private double _recordProgress, _tsProgress, _playhead = -1;
    private string _recordLabel = "Record 10 seconds";
    private string _tsLabel = "Start 30-second TeamSpeak test";
    private string? _tsMessage;
    private int _listenIndex, _mixIndex, _tsListenIndex;
    private Take? _take;
    private float[]? _cableClip, _tsClip, _waveSamples;
    private MixSettings _mixDuringRecording = MixSettings.DocOneToOne;
    private IReadOnlyList<(double, double)>? _spans;
    private string _peakText = "", _loudText = "";

    public TestViewModel(AppController app)
    {
        _app = app;
        _mixIndex = app.Settings.Preset == MixPreset.DocOneToOne ? 0 : 1;
        RefreshSourcesCommand = new AsyncCommand(RefreshSourcesAsync);
        RecordCommand = new AsyncCommand(RecordAsync, () => !_tsRunning);
        PlayCommand = new AsyncCommand(() => PlayAsync(teamSpeakClip: false), () => _hasTake);
        TeamSpeakTestCommand = new AsyncCommand(TeamSpeakTestAsync, () => !_isRecording);
        PlayTeamSpeakCommand = new AsyncCommand(() => PlayAsync(teamSpeakClip: true), () => _hasTsClip);
        _player.Finished += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            IsPlaying = false;
            PlayingTs = false;
        });
    }

    public ObservableCollection<SourceItem> Sources { get; } = new();

    public SourceItem? SelectedSource
    {
        get => _selectedSource;
        set => Set(ref _selectedSource, value);
    }

    public ICommand RefreshSourcesCommand { get; }
    public ICommand RecordCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand TeamSpeakTestCommand { get; }
    public ICommand PlayTeamSpeakCommand { get; }

    public bool IsRecording { get => _isRecording; private set => Set(ref _isRecording, value); }
    public double RecordProgress { get => _recordProgress; private set => Set(ref _recordProgress, value); }
    public string RecordLabel { get => _recordLabel; private set => Set(ref _recordLabel, value); }

    public bool Holding
    {
        get => _holding;
        set
        {
            if (!Set(ref _holding, value)) return;
            _app.Engine.SetLatch(value);
        }
    }

    public bool HasTake { get => _hasTake; private set => Set(ref _hasTake, value); }
    public bool IsPlaying { get => _isPlaying; private set { if (Set(ref _isPlaying, value)) OnPropertyChanged(nameof(PlayLabel)); } }
    public string PlayLabel => IsPlaying && !PlayingTs ? "Stop" : "Play";
    public bool PlayingTs { get => _playingTs; private set { if (Set(ref _playingTs, value)) { OnPropertyChanged(nameof(PlayLabel)); OnPropertyChanged(nameof(PlayTsLabel)); } } }
    public string PlayTsLabel => IsPlaying && PlayingTs ? "Stop" : "Play";
    public double Playhead { get => _playhead; private set => Set(ref _playhead, value); }

    public float[]? WaveSamples { get => _waveSamples; private set => Set(ref _waveSamples, value); }
    public IReadOnlyList<(double Start, double End)>? Spans { get => _spans; private set => Set(ref _spans, value); }
    public string PeakText { get => _peakText; private set => Set(ref _peakText, value); }
    public string LoudText { get => _loudText; private set => Set(ref _loudText, value); }
    public ObservableCollection<ResultCheck> Checks { get; } = new();

    public int ListenIndex
    {
        get => _listenIndex;
        set { if (Set(ref _listenIndex, value)) OnPropertyChanged(nameof(IsTeammate)); }
    }

    public bool IsTeammate => _listenIndex == 1;

    public int MixIndex
    {
        get => _mixIndex;
        set { if (Set(ref _mixIndex, value)) Analyze(); }
    }

    public int SignalIndex
    {
        get => (int)_app.Settings.Preview.Signal;
        set { _app.Settings.Preview = _app.Settings.Preview with { Signal = (SignalStrength)value }; _app.ScheduleSave(); OnPropertyChanged(); }
    }

    public int CodecIndex
    {
        get => (int)_app.Settings.Preview.Codec;
        set { _app.Settings.Preview = _app.Settings.Preview with { Codec = (TeamSpeakCodec)value }; _app.ScheduleSave(); OnPropertyChanged(); OnPropertyChanged(nameof(QualityText)); }
    }

    public double Quality
    {
        get => _app.Settings.Preview.Quality;
        set { _app.Settings.Preview = _app.Settings.Preview with { Quality = (int)Math.Round(value) }; _app.ScheduleSave(); OnPropertyChanged(); OnPropertyChanged(nameof(QualityText)); }
    }

    public string QualityText => $"{_app.Settings.Preview.Quality}  ·  {OpusRoundTrip.Bitrate(_app.Settings.Preview.Codec, _app.Settings.Preview.Quality) / 1000.0:0.#} kbit/s";

    // TeamSpeak processing test

    public bool TsRunning { get => _tsRunning; private set => Set(ref _tsRunning, value); }
    public double TsProgress { get => _tsProgress; private set => Set(ref _tsProgress, value); }
    public string TsLabel { get => _tsLabel; private set => Set(ref _tsLabel, value); }
    public string? TsMessage { get => _tsMessage; private set => Set(ref _tsMessage, value); }
    public bool HasTsClip { get => _hasTsClip; private set => Set(ref _hasTsClip, value); }

    public int TsListenIndex
    {
        get => _tsListenIndex;
        set => Set(ref _tsListenIndex, value);
    }

    public void Tick() => Playhead = IsPlaying && !PlayingTs ? _player.Position : -1;

    public async Task RefreshSourcesAsync()
    {
        var arma = _app.Arma.Current;
        var apps = await Task.Run(AudioApps.List);
        var previous = SelectedSource;

        Sources.Clear();
        if (arma is not null) Sources.Add(new SourceItem(arma.Pid, "Arma 3", true, true));
        foreach (var a in apps.Where(a => a.Name != "Arma 3"))
            Sources.Add(new SourceItem(a.Pid, a.Name, false, a.Playing));
        if (Sources.Count == 0)
            Sources.Add(new SourceItem(null, "Nothing is playing sound", false, false));

        SelectedSource = Sources.FirstOrDefault(s => s.Pid == previous?.Pid && s.Pid is not null)
                         ?? Sources.FirstOrDefault(s => s.Playing && !s.IsArma)
                         ?? Sources[0];
    }

    private async Task EnterTestAsync()
    {
        await _player.StopAsync();
        IsPlaying = false;
        _app.TestMode = true;
        if (SelectedSource is { IsArma: false, Pid: int pid } source)
            await _app.Engine.SetTestSourceAsync(pid, source.Name);
    }

    private async Task LeaveTestAsync()
    {
        Holding = false;
        _app.Engine.SetLatch(false);
        await _app.Engine.SetTestSourceAsync(null, null);
        _app.TestMode = false;
    }

    private async Task RecordAsync()
    {
        if (IsRecording)
        {
            _recordCts?.Cancel();
            return;
        }

        _recordCts = new CancellationTokenSource();
        var ct = _recordCts.Token;
        IsRecording = true;
        float[]? cable = null;
        TakeRecorder recorder;
        try
        {
            await EnterTestAsync();
            _mixDuringRecording = _app.Settings.ActiveMix;
            recorder = new TakeRecorder(RecordLength);

            var cableOut = AudioDevices.CableOutput();
            Task<float[]>? cableTask = null;
            if (cableOut is not null && _app.Engine.Status.CableFound)
                cableTask = ClipRecorder.RecordDeviceAsync(cableOut.Id, RecordLength + TimeSpan.FromMilliseconds(300), null, ct);

            _app.Engine.Recorder = recorder;
            var clock = Stopwatch.StartNew();
            while (!ct.IsCancellationRequested && !recorder.IsFull && clock.Elapsed < RecordLength + TimeSpan.FromSeconds(2))
            {
                RecordProgress = recorder.Progress;
                int left = (int)Math.Ceiling((1 - recorder.Progress) * RecordLength.TotalSeconds);
                RecordLabel = $"Recording  ·  {left} s";
                await Task.Delay(50);
            }
            _app.Engine.Recorder = null;

            if (cableTask is not null)
            {
                try
                {
                    cable = await cableTask;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    cable = null;
                }
            }
        }
        finally
        {
            _app.Engine.Recorder = null;
            await LeaveTestAsync();
            IsRecording = false;
            RecordProgress = 0;
            RecordLabel = "Record 10 seconds";
        }

        var take = recorder.ToTake();
        if (take.Length < Mixer.SampleRate / 2) return;
        _take = take;
        _cableClip = cable;
        HasTake = true;
        Spans = take.RadioSpans();
        Analyze();
    }

    private MixSettings SelectedMix => _mixIndex == 0 ? MixSettings.DocOneToOne : _app.Settings.Custom;

    private void Analyze()
    {
        if (_take is not { } take) return;
        var rendered = take.Render(SelectedMix);
        WaveSamples = rendered;

        var analysis = TakeAnalysis.Of(rendered);
        PeakText = $"Peak {Db(analysis.PeakDb)}";
        LoudText = $"Speech {Db(analysis.LoudnessDb)}";

        Checks.Clear();

        var doc = take.Render(MixSettings.DocOneToOne);
        var sum = take.PlainSum();
        bool pure = doc.AsSpan().SequenceEqual(sum);
        Checks.Add(pure
            ? new ResultCheck("Doc 1:1 is exactly your voice plus the game, nothing added.", CheckLevel.Ok)
            : new ResultCheck("Doc 1:1 differs from a plain sum. Please report this.", CheckLevel.Attention));

        bool anyRadio = take.Gate.Any(g => g);
        if (!anyRadio)
        {
            Checks.Add(new ResultCheck("You didn't hold a radio key, so no game audio was mixed in.", CheckLevel.Info));
        }
        else
        {
            double sumSq = 0;
            long count = 0;
            foreach (var (offset, n, radio) in take.Chunks())
            {
                if (!radio) continue;
                for (int i = offset; i < offset + n; i++) sumSq += take.Game[i] * take.Game[i];
                count += n;
            }
            float gameDb = count == 0 ? Core.Dsp.Db.Floor : Core.Dsp.Db.FromGain((float)Math.Sqrt(sumSq / count));
            Checks.Add(gameDb > -60
                ? new ResultCheck("Game audio came through while you held the radio key.", CheckLevel.Ok)
                : new ResultCheck("No game audio was captured while you held the radio key. Is the source playing?", CheckLevel.Attention));
        }

        Checks.Add(analysis.ClippedSamples == 0
            ? new ResultCheck("Nothing clipped.", CheckLevel.Ok)
            : new ResultCheck($"{analysis.ClippedSamples:N0} samples went over full scale. Turn the game down a little, or try Catch loud peaks.", CheckLevel.Attention));

        if (_cableClip is null)
        {
            Checks.Add(new ResultCheck("VB-CABLE isn't set up yet, so this was a dry run inside the app.", CheckLevel.Info));
        }
        else
        {
            var sent = TakeAnalysis.Of(take.Render(_mixDuringRecording));
            var received = TakeAnalysis.Of(_cableClip);
            double diff = received.LoudnessDb - sent.LoudnessDb;
            Checks.Add(Math.Abs(diff) <= 1.5 || sent.LoudnessDb <= Core.Dsp.Db.Floor + 1
                ? new ResultCheck("TeamSpeak's input received the same audio through the cable.", CheckLevel.Ok)
                : new ResultCheck($"The cable changed the level by {diff:+0.0;−0.0} dB. Set CABLE Output's level to 100 in Windows Sound settings.", CheckLevel.Attention));
        }
    }

    private static string Db(float db) => db <= Core.Dsp.Db.Floor + 1 ? "–∞" : db < 0 ? $"−{-db:0.0} dB" : $"{db:0.0} dB";

    private async Task PlayAsync(bool teamSpeakClip)
    {
        if (IsPlaying && PlayingTs == teamSpeakClip)
        {
            await _player.StopAsync();
            IsPlaying = false;
            PlayingTs = false;
            return;
        }

        var preview = _app.Settings.Preview;
        float[]? clip;
        if (teamSpeakClip)
        {
            if (_tsClip is not { } ts) return;
            bool teammate = TsListenIndex == 1;
            clip = await Task.Run(() => teammate ? RadioPreview.AsTeammateHears(ts, preview) : ts);
        }
        else
        {
            if (_take is not { } take) return;
            var mix = SelectedMix;
            bool teammate = IsTeammate;
            clip = await Task.Run(() =>
            {
                var rendered = take.Render(mix);
                return teammate ? RadioPreview.AsTeammateHears(rendered, preview) : rendered;
            });
        }

        try
        {
            await _player.PlayAsync(clip);
            PlayMessage = null;
        }
        catch (Exception e)
        {
            // No default playback device, or it's in exclusive use by another app.
            Core.Diagnostics.Log.Warn($"Playback failed: {e.Message}");
            PlayMessage = "Couldn't play on your default speakers. Check Windows has a playback device selected.";
            return;
        }
        PlayingTs = teamSpeakClip;
        IsPlaying = true;
    }

    private string? _playMessage;

    public string? PlayMessage { get => _playMessage; private set => Set(ref _playMessage, value); }

    private async Task TeamSpeakTestAsync()
    {
        if (TsRunning)
        {
            _tsCts?.Cancel();
            return;
        }

        if (TeamSpeakClient.ProcessId() is not int pid)
        {
            TsMessage = "Open TeamSpeak first, then start the test.";
            return;
        }

        TsMessage = "Now click Begin Test in TeamSpeak's Options → Capture, and talk.";
        _tsCts = new CancellationTokenSource();
        TsRunning = true;
        try
        {
            await EnterTestAsync();
            _app.Engine.SetLatch(true);
            var progress = new Progress<double>(p =>
            {
                TsProgress = p;
                TsLabel = $"Listening to TeamSpeak  ·  {(int)Math.Ceiling((1 - p) * TeamSpeakTestLength.TotalSeconds)} s";
            });
            var clip = await ClipRecorder.RecordProcessAsync(pid, TeamSpeakTestLength, progress, _tsCts.Token);
            var analysis = TakeAnalysis.Of(clip);
            if (analysis.LoudnessDb <= Mixer.VoiceThresholdDb)
            {
                TsMessage = "Nothing came back from TeamSpeak. Make sure Begin Test was running while you talked.";
            }
            else
            {
                _tsClip = clip;
                HasTsClip = true;
                TsMessage = "Captured TeamSpeak's processed sound.";
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            TsMessage = $"Couldn't listen to TeamSpeak: {ex.Message}";
        }
        finally
        {
            await LeaveTestAsync();
            TsRunning = false;
            TsProgress = 0;
            TsLabel = "Start 30-second TeamSpeak test";
        }
    }
}
