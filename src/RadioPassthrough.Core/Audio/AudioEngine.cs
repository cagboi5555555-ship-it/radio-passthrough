using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RadioPassthrough.Core.Dsp;
using RadioPassthrough.Core.Game;

namespace RadioPassthrough.Core.Audio;

public sealed record EngineStatus
{
    public bool CableFound { get; init; }
    public string? MicName { get; init; }
    public bool MicRunning { get; init; }
    public string? GameName { get; init; }
    public bool GameAttached { get; init; }
    public bool GameIsTestSource { get; init; }
    public string? Problem { get; init; }
}

// Mic + game audio → mixer → VB-CABLE, with everything re-opened automatically when devices or
// the game come and go.
public sealed class AudioEngine : IAsyncDisposable
{
    public const int BlockSize = 480;

    private readonly SemaphoreSlim _ops = new(1, 1);
    private readonly Mixer _mixer = new();
    private readonly DriftBuffer _micBuffer = new(960);
    private readonly DriftBuffer _gameBuffer = new(1440);
    private readonly MixProvider _provider;
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient _notifications;
    private readonly Timer _retryTimer;

    private IMixSink? _sink;
    private string? _sinkDeviceId;
    private CaptureSource? _mic;
    private string? _micDeviceId;
    private CaptureSource? _game;
    private int _gamePid;
    private float[] _micMono = new float[4096];
    private float[] _gameMono = new float[4096];

    private string? _wantedMicId;
    private volatile int _micMode;
    private GameProcess? _arma;
    private (int Pid, string Name)? _testSource;
    private volatile bool _gameActive;
    private volatile bool _radioKey;
    private volatile bool _latch;
    private TakeRecorder? _recorder;
    private EngineStatus _status = new();
    private bool _disposed;

    public AudioEngine()
    {
        _provider = new MixProvider(this);
        _notifications = _enumerator.CreateNotificationClient(false);
        _notifications.DeviceAdded += (_, _) => ScheduleReconcile(TimeSpan.FromMilliseconds(750));
        _notifications.DeviceRemoved += (_, _) => ScheduleReconcile(TimeSpan.FromMilliseconds(750));
        _notifications.DeviceStateChanged += (_, _) => ScheduleReconcile(TimeSpan.FromMilliseconds(750));
        _retryTimer = new Timer(_ => _ = ReconcileAsync(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public Mixer Mixer => _mixer;

    public bool GateOpen => _radioKey || _latch;

    public bool RadioKeyHeld => _radioKey;

    public bool Latched => _latch;

    public EngineStatus Status => Volatile.Read(ref _status);

    public event Action<EngineStatus>? StatusChanged;

    public TakeRecorder? Recorder
    {
        get => Volatile.Read(ref _recorder);
        set => Volatile.Write(ref _recorder, value);
    }

    public void SetRadioKey(bool held) => _radioKey = held;

    public void SetLatch(bool on) => _latch = on;

    public MicChannelMode MicMode
    {
        get => (MicChannelMode)_micMode;
        set => _micMode = (int)value;
    }

    public Task SetMicAsync(string? deviceId)
    {
        _wantedMicId = deviceId;
        return ReconcileAsync();
    }

    public Task SetArmaAsync(GameProcess? arma)
    {
        _arma = arma;
        return ReconcileAsync();
    }

    // Test mode: capture another app's audio in place of Arma until cleared.
    public Task SetTestSourceAsync(int? pid, string? name)
    {
        _testSource = pid is { } p ? (p, name ?? "App") : null;
        return ReconcileAsync();
    }

    public Task RestartAsync() => ReconcileAsync(forceRestart: true);

    private void ScheduleReconcile(TimeSpan delay)
    {
        if (!_disposed) _retryTimer.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private async Task ReconcileAsync(bool forceRestart = false)
    {
        if (_disposed) return;
        await _ops.WaitAsync().ConfigureAwait(false);
        string? problem = null;
        try
        {
            if (_disposed) return;
            if (forceRestart)
            {
                await CloseSinkAsync().ConfigureAwait(false);
                await CloseMicAsync().ConfigureAwait(false);
                await CloseGameAsync().ConfigureAwait(false);
            }

            problem = await ReconcileSinkAsync().ConfigureAwait(false) ?? problem;
            problem = await ReconcileMicAsync().ConfigureAwait(false) ?? problem;
            problem = await ReconcileGameAsync().ConfigureAwait(false) ?? problem;
        }
        finally
        {
            PublishStatus(problem);
            _ops.Release();
        }

        if (problem is not null || _sink is { IsCable: false })
            ScheduleReconcile(TimeSpan.FromSeconds(5));
    }

    private async Task<string?> ReconcileSinkAsync()
    {
        var cable = AudioDevices.CableInput();

        if (_sink is { IsCable: true } && (cable is null || cable.Id != _sinkDeviceId))
            await CloseSinkAsync().ConfigureAwait(false);
        if (_sink is { IsCable: false } && cable is not null)
            await CloseSinkAsync().ConfigureAwait(false);

        if (_sink is not null) return null;

        if (cable is null)
        {
            _sink = new ClockSink(_provider);
            _sinkDeviceId = null;
            return null;
        }

        try
        {
            var sink = await CableSink.OpenAsync(cable.Id, _provider).ConfigureAwait(false);
            sink.Faulted += _ => OnFault(Part.Sink, sink);
            _sink = sink;
            _sinkDeviceId = cable.Id;
            return null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            _sink = new ClockSink(_provider);
            _sinkDeviceId = null;
            return $"Couldn't open VB-CABLE: {ex.Message}";
        }
    }

    private async Task<string?> ReconcileMicAsync()
    {
        string? wanted = _wantedMicId is { } id && AudioDevices.Exists(id) ? id : AudioDevices.DefaultMicrophone()?.Id;

        if (_mic is not null && _micDeviceId != wanted)
            await CloseMicAsync().ConfigureAwait(false);
        if (_mic is not null) return null;
        if (wanted is null) return "No microphone found.";

        try
        {
            _micBuffer.Reset();
            var mic = await CaptureSource.OpenDeviceAsync(wanted, OnMicData).ConfigureAwait(false);
            mic.Faulted += _ => OnFault(Part.Mic, mic);
            _mic = mic;
            _micDeviceId = wanted;
            return null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException)
        {
            return $"Couldn't open the microphone: {ex.Message}";
        }
    }

    private async Task<string?> ReconcileGameAsync()
    {
        (int Pid, string Name)? target = _testSource ?? (_arma is { } a ? (a.Pid, a.Name) : null);

        if (_game is not null && _gamePid != target?.Pid)
            await CloseGameAsync().ConfigureAwait(false);
        if (_game is not null || target is null) return null;

        try
        {
            _gameBuffer.Reset();
            var game = await CaptureSource.OpenProcessAsync(target.Value.Pid, target.Value.Name, OnGameData).ConfigureAwait(false);
            game.Faulted += _ => OnFault(Part.Game, game);
            _game = game;
            _gamePid = target.Value.Pid;
            _gameActive = true;
            return null;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            return $"Couldn't capture {target.Value.Name}'s audio: {ex.Message}";
        }
    }

    private enum Part { Sink, Mic, Game }

    private void OnFault(Part part, object faulted)
    {
        _ = Task.Run(async () =>
        {
            await _ops.WaitAsync().ConfigureAwait(false);
            try
            {
                // Only close it if it is still the live instance; a newer one may already be open.
                if (part == Part.Sink && ReferenceEquals(_sink, faulted)) await CloseSinkAsync().ConfigureAwait(false);
                if (part == Part.Mic && ReferenceEquals(_mic, faulted)) await CloseMicAsync().ConfigureAwait(false);
                if (part == Part.Game && ReferenceEquals(_game, faulted)) await CloseGameAsync().ConfigureAwait(false);
            }
            finally
            {
                _ops.Release();
            }
            ScheduleReconcile(TimeSpan.FromSeconds(2));
        });
    }

    private async Task CloseSinkAsync()
    {
        var sink = _sink;
        _sink = null;
        _sinkDeviceId = null;
        if (sink is not null) await sink.DisposeAsync().ConfigureAwait(false);
    }

    private async Task CloseMicAsync()
    {
        var mic = _mic;
        _mic = null;
        _micDeviceId = null;
        if (mic is not null) await mic.DisposeAsync().ConfigureAwait(false);
        _micBuffer.Reset();
    }

    private async Task CloseGameAsync()
    {
        _gameActive = false;
        var game = _game;
        _game = null;
        _gamePid = 0;
        if (game is not null) await game.DisposeAsync().ConfigureAwait(false);
        _gameBuffer.Reset();
    }

    private void PublishStatus(string? problem)
    {
        var status = new EngineStatus
        {
            CableFound = _sink is { IsCable: true },
            MicName = _mic?.Name,
            MicRunning = _mic is not null,
            GameName = _game?.Name ?? _testSource?.Name ?? _arma?.Name,
            GameAttached = _game is not null,
            GameIsTestSource = _testSource is not null,
            Problem = problem,
        };
        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(status);
    }

    private void OnMicData(ReadOnlySpan<float> interleaved, int channels, bool silent)
    {
        int frames = interleaved.Length / channels;
        if (_micMono.Length < frames) _micMono = new float[frames * 2];
        Downmix.ToMono(interleaved, channels, MicMode, _micMono);
        _micBuffer.Write(_micMono.AsSpan(0, frames));
    }

    private void OnGameData(ReadOnlySpan<float> interleaved, int channels, bool silent)
    {
        int frames = interleaved.Length / channels;
        if (_gameMono.Length < frames) _gameMono = new float[frames * 2];
        Downmix.ToMono(interleaved, channels, MicChannelMode.Both, _gameMono);
        _gameBuffer.Write(_gameMono.AsSpan(0, frames));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _retryTimer.DisposeAsync().ConfigureAwait(false);
        _notifications.Dispose();
        await _ops.WaitAsync().ConfigureAwait(false);
        try
        {
            await CloseSinkAsync().ConfigureAwait(false);
            await CloseMicAsync().ConfigureAwait(false);
            await CloseGameAsync().ConfigureAwait(false);
        }
        finally
        {
            _ops.Release();
        }
        _enumerator.Dispose();
    }

    private sealed class MixProvider(AudioEngine engine) : IWaveProvider
    {
        private readonly float[] _mic = new float[BlockSize];
        private readonly float[] _game = new float[BlockSize];
        private readonly float[] _mono = new float[BlockSize];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(CaptureSource.SampleRate, 2);

        public int Read(Span<byte> buffer)
        {
            var output = MemoryMarshal.Cast<byte, float>(buffer);
            int frames = output.Length / 2;
            for (int done = 0; done < frames;)
            {
                int n = Math.Min(BlockSize, frames - done);
                var mic = _mic.AsSpan(0, n);
                var game = _game.AsSpan(0, n);
                var mono = _mono.AsSpan(0, n);

                engine._micBuffer.Read(mic);
                if (engine._gameActive) engine._gameBuffer.Read(game);
                else game.Clear();

                bool gate = engine.GateOpen;
                engine._mixer.Process(mic, game, gate, mono);
                engine.Recorder?.Append(mic, game, gate);

                for (int i = 0; i < n; i++)
                {
                    output[(done + i) * 2] = mono[i];
                    output[(done + i) * 2 + 1] = mono[i];
                }
                done += n;
            }
            return buffer.Length;
        }
    }
}
