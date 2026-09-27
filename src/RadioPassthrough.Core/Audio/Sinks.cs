using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RadioPassthrough.Core.Audio;

public interface IMixSink : IAsyncDisposable
{
    string Name { get; }
    bool IsCable { get; }
    event Action<Exception?>? Faulted;
}

// Plays the mix into VB-CABLE's input, which TeamSpeak records from as "CABLE Output".
public sealed class CableSink : IMixSink
{
    private readonly WasapiPlayer _player;
    private int _stopping;

    private CableSink(WasapiPlayer player, string name)
    {
        _player = player;
        Name = name;
        _player.PlaybackStopped += (_, e) =>
        {
            if (Volatile.Read(ref _stopping) == 0) Faulted?.Invoke(e.Exception);
        };
    }

    public string Name { get; }
    public bool IsCable => true;
    public event Action<Exception?>? Faulted;

    public static Task<CableSink> OpenAsync(string deviceId, IWaveProvider provider) => Task.Run(() =>
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(deviceId);
        var player = new WasapiPlayerBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithEventSync()
            .WithLatency(30)
            .WithMmcssThreadPriority("Pro Audio")
            .Build();
        player.Init(provider);
        player.Play();
        return new CableSink(player, device.FriendlyName);
    });

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 1) return;
        try
        {
            _player.Stop();
        }
        catch
        {
            // Device already gone.
        }
        await _player.DisposeAsync().ConfigureAwait(false);
    }
}

// Used while VB-CABLE is missing: keeps the mixer running on the system clock so meters and test
// recordings still work, but sends the audio nowhere.
public sealed class ClockSink : IMixSink
{
    private readonly IWaveProvider _provider;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;

    public ClockSink(IWaveProvider provider)
    {
        _provider = provider;
        _thread = new Thread(Run) { IsBackground = true, Name = "Mix clock", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public string Name => "No output";
    public bool IsCable => false;
    public event Action<Exception?>? Faulted { add { } remove { } }

    private void Run()
    {
        const int frames = 480;
        var buffer = new byte[frames * _provider.WaveFormat.BlockAlign];
        var clock = Stopwatch.StartNew();
        long produced = 0;
        while (!_cts.IsCancellationRequested)
        {
            long due = clock.ElapsedTicks * CaptureSource.SampleRate / Stopwatch.Frequency;
            while (produced + frames <= due)
            {
                _provider.Read(buffer);
                produced += frames;
            }
            Thread.Sleep(5);
        }
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _thread.Join(TimeSpan.FromSeconds(1));
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
