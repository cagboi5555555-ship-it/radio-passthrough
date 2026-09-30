using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RadioPassthrough.Core.Diagnostics;
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

// Mic + game audio → mixer → VB-CABLE in stereo, like the guide's Voicemeeter bus B1, with everything
// re-opened automatically when devices or the game come and go.
public sealed class AudioEngine : IAsyncDisposable
{
    public const int BlockSize = 480; // frames

    private readonly SemaphoreSlim _ops = new(1, 1);
    private readonly Mixer _mixer = new();
    private readonly DriftBuffer _micBuffer = new(960, Mixer.Channels);
    private readonly DriftBuffer _gameBuffer = new(1440, Mixer.Channels);
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
    private float[] _micStereo = new float[8192];
    private float[] _gameStereo = new float[8192];

    private string? _wantedMicId;
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

    // Master switch: off means mic only, as if the app weren't adding anything.
    public bool GameAudioEnabled { get; set; } = true;

    // While a test runs the radio key always works, even with game audio switched off.
    public bool TestActive { get; set; }

    public bool GateOpen => _latch || (_radioKey && (GameAudioEnabled || TestActive));

    public bool RadioKeyHeld => _radioKey;

    // How often the mic or game buffer ran dry (each one is an audible gap). For diagnostics.
    public (int Mic, int Game) Dropouts => (_micBuffer.Underruns, _gameBuffer.Underruns);

    public EngineStatus Status => Volatile.Read(ref _status);

    public event Action<EngineStatus>? StatusChanged;

    public TakeRecorder? Recorder
    {
        get => Volatile.Read(ref _recorder);
        set => Volatile.Write(ref _recorder, value);
    }

    public void SetRadioKey(bool held) => _radioKey = held;

    public void SetLatch(bool on) => _latch = on;

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

            // The first problem wins: the cable matters more than the mic, and the mic more than the game.
            string? sinkProblem = await ReconcileSinkAsync().ConfigureAwait(false);
            string? micProblem = await ReconcileMicAsync().ConfigureAwait(false);
            string? gameProblem = await ReconcileGameAsync().ConfigureAwait(false);
            problem = sinkProblem ?? micProblem ?? gameProblem;
        }
        catch (Exception e)
        {
            // Anything unexpected from the audio stack: report it, and the retry below tries again.
            Log.Error("Audio setup failed", e);
            problem = $"Audio error: {e.Message}";
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
        string? wanted = _wantedMicId is { } id && AudioDevices.Exists(id) && id != AudioDevices.CableOutput()?.Id ? id : FallbackMicrophone();

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

    // Windows' default mic, unless that's the cable: recording our own output would feed it straight
    // back into TeamSpeak as an echo loop.
    public static string? FallbackMicrophone()
    {
        var preferred = AudioDevices.DefaultMicrophone();
        if (preferred is not null && !AudioDevices.IsCable(preferred.Name)) return preferred.Id;
        return AudioDevices.Microphones().FirstOrDefault()?.Id; // this list never contains the cable
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
        catch (Exception ex) when (ex is COMException or InvalidOperationException or ArgumentException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return $"Couldn't capture {target.Value.Name}'s audio: {ex.Message}";
        }
    }

    private enum Part { Sink, Mic, Game }

    private void OnFault(Part part, object faulted)
    {
        Log.Warn($"{part} stream stopped unexpectedly; reopening.");
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
        var previous = Volatile.Read(ref _status);
        Volatile.Write(ref _status, status);
        if (status != previous)
        {
            Log.Info($"Engine: cable={(status.CableFound ? "yes" : "no")}, mic={status.MicName ?? "none"}, " +
                     $"game={(status.GameAttached ? status.GameName : "none")}{(status.GameIsTestSource ? " (test)" : "")}" +
                     (status.Problem is null ? "" : $", problem: {status.Problem}"));
        }
        StatusChanged?.Invoke(status);
    }

    // Voicemeeter's stereo input strip: the device's first two channels as left and right (a one-channel
    // device on both). Nothing is mixed down here; TeamSpeak does that itself, as it did with B1.
    private void OnMicData(ReadOnlySpan<float> interleaved, int channels, bool silent) =>
        ToStereo(interleaved, channels, ref _micStereo, _micBuffer);

    private void OnGameData(ReadOnlySpan<float> interleaved, int channels, bool silent) =>
        ToStereo(interleaved, channels, ref _gameStereo, _gameBuffer);

    private static void ToStereo(ReadOnlySpan<float> interleaved, int channels, ref float[] scratch, DriftBuffer buffer)
    {
        int frames = interleaved.Length / channels;
        if (channels == 2)
        {
            buffer.Write(interleaved[..(frames * 2)]);
            return;
        }
        if (scratch.Length < frames * 2) scratch = new float[frames * 4];
        for (int f = 0; f < frames; f++)
        {
            float left = interleaved[f * channels];
            scratch[f * 2] = left;
            scratch[f * 2 + 1] = channels > 1 ? interleaved[f * channels + 1] : left;
        }
        buffer.Write(scratch.AsSpan(0, frames * 2));
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
        private const int BlockSamples = BlockSize * Mixer.Channels;
        private readonly float[] _mic = new float[BlockSamples];
        private readonly float[] _game = new float[BlockSamples];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(CaptureSource.SampleRate, Mixer.Channels);

        private bool _loggedFailure;

        // Runs on the audio thread: it must never throw, or Windows stops the stream.
        public int Read(Span<byte> buffer)
        {
            try
            {
                return Mix(buffer);
            }
            catch (Exception e)
            {
                if (!_loggedFailure) Log.Error("Mixing failed; sending silence for this block", e);
                _loggedFailure = true;
                buffer.Clear();
                return buffer.Length;
            }
        }

        // Interleaved stereo straight through: output = mic + game (while a radio key is held).
        private int Mix(Span<byte> buffer)
        {
            var output = MemoryMarshal.Cast<byte, float>(buffer);
            int samples = output.Length - output.Length % Mixer.Channels;
            for (int done = 0; done < samples;)
            {
                int n = Math.Min(BlockSamples, samples - done);
                var mic = _mic.AsSpan(0, n);
                var game = _game.AsSpan(0, n);

                engine._micBuffer.Read(mic);
                if (engine._gameActive) engine._gameBuffer.Read(game);
                else game.Clear();

                bool gate = engine.GateOpen;
                engine._mixer.Process(mic, game, gate, output.Slice(done, n));
                engine.Recorder?.Append(mic, game, gate);
                done += n;
            }
            output[samples..].Clear();
            return buffer.Length;
        }
    }
}
