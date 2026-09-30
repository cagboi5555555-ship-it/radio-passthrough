using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using RadioPassthrough.Core.Dsp;
using RadioPassthrough.Core.Ptt;

namespace RadioPassthrough.ViewModels;

public sealed class KeyBindingItem
{
    public required PttBinding Binding { get; init; }
    public required IReadOnlyList<string> Keys { get; init; }
    public required ICommand Remove { get; init; }
}

public sealed class MeterState : ObservableObject
{
    private const double FloorDb = -60;
    private double _level, _peak, _peakHoldUntil;
    private string _text = "–∞";

    public double Level { get => _level; private set => Set(ref _level, value); }
    public double Peak { get => _peak; private set => Set(ref _peak, value); }
    public string Text { get => _text; private set => Set(ref _text, value); }

    // Fast rise, ~30 dB/s fall, peak tick held for a second: standard meter ballistics.
    public void Update(float linearPeak, double now, double dt)
    {
        double db = Math.Max(FloorDb, Db.FromGain(linearPeak));
        double target = (db - FloorDb) / -FloorDb;
        Level = target >= Level ? target : Math.Max(target, Level - dt * 30 / -FloorDb);
        if (target >= Peak || now > _peakHoldUntil)
        {
            Peak = Math.Max(target, Level);
            if (target >= Peak) _peakHoldUntil = now + 1.0;
        }
        double shownDb = Level * -FloorDb + FloorDb;
        Text = shownDb <= FloorDb + 0.5 ? "–∞" : $"{shownDb:0} dB";
    }
}

public sealed class LiveViewModel : ObservableObject
{
    private readonly AppController _app;
    private bool _onAir;
    private bool _capturing;
    private bool _armaRunning;

    public LiveViewModel(AppController app)
    {
        _app = app;
        AddKeyCommand = new RelayCommand(BeginCapture, () => !_capturing);
        ResetKeysCommand = new RelayCommand(() => SetBindings(PttBinding.AcreDefaults()));
        RebuildBindings();
    }

    public MeterState Voice { get; } = new();
    public MeterState Game { get; } = new();
    public MeterState Output { get; } = new();

    public bool OnAir
    {
        get => _onAir;
        private set
        {
            if (Set(ref _onAir, value))
            {
                OnPropertyChanged(nameof(HeroTitle));
                OnPropertyChanged(nameof(HeroDetail));
            }
        }
    }

    public string HeroTitle => OnAir ? "On the radio" : GameAudioEnabled ? "Standing by" : "Game audio off";

    public string HeroDetail => OnAir
        ? "Game audio is going out with your voice."
        : !GameAudioEnabled
            ? "Your radio carries your voice only. Switch game audio back on below."
            : _app.Arma.Current is null
                ? "Start Arma 3. Game audio joins your voice while you hold a radio key."
                : "Hold a radio key in Arma to send game audio with your voice.";

    public bool GameAudioEnabled
    {
        get => _app.Settings.GameAudioEnabled;
        set
        {
            if (_app.Settings.GameAudioEnabled == value) return;
            _app.Settings.GameAudioEnabled = value;
            _app.Engine.GameAudioEnabled = value;
            _app.ScheduleSave();
            Core.Diagnostics.Log.Info($"Game audio over radio turned {(value ? "on" : "off")}.");
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeroTitle));
            OnPropertyChanged(nameof(HeroDetail));
        }
    }

    public void ToggleGameAudio() => GameAudioEnabled = !GameAudioEnabled;

    public void Tick(double now, double dt)
    {
        var mixer = _app.Engine.Mixer;
        Voice.Update(mixer.MicPeak, now, dt);
        Game.Update(mixer.GamePeak, now, dt);
        Output.Update(mixer.OutputPeak, now, dt);
        OnAir = _app.Engine.GateOpen;
        bool armaRunning = _app.Arma.Current is not null;
        if (armaRunning != _armaRunning)
        {
            _armaRunning = armaRunning;
            OnPropertyChanged(nameof(HeroDetail));
        }
    }

    // Radio keys

    public ObservableCollection<KeyBindingItem> Bindings { get; } = new();

    public ICommand AddKeyCommand { get; }

    public ICommand ResetKeysCommand { get; }

    public bool Capturing
    {
        get => _capturing;
        private set => Set(ref _capturing, value);
    }

    private void BeginCapture()
    {
        Capturing = true;
        _app.Keys.CaptureNext(binding => Application.Current.Dispatcher.BeginInvoke(() =>
        {
            Capturing = false;
            if (binding is null) return;
            if (_app.Settings.Bindings.Contains(binding)) return;
            SetBindings([.. _app.Settings.Bindings, binding]);
        }));
    }

    public void CancelCapture()
    {
        if (_capturing) _app.Keys.CancelCapture();
    }

    private void SetBindings(List<PttBinding> bindings)
    {
        _app.Settings.Bindings = bindings;
        _app.Keys.SetBindings(bindings);
        _app.ScheduleSave();
        RebuildBindings();
    }

    private void RebuildBindings()
    {
        Bindings.Clear();
        foreach (var b in _app.Settings.Bindings)
        {
            var keys = KeyNames.Describe(b).Split(" + ");
            var binding = b;
            Bindings.Add(new KeyBindingItem
            {
                Binding = b,
                Keys = keys,
                // The last key stays: without one, game audio could never go out.
                Remove = new RelayCommand(() => SetBindings(_app.Settings.Bindings.Where(x => x != binding).ToList()), () => _app.Settings.Bindings.Count > 1),
            });
        }
    }
}
