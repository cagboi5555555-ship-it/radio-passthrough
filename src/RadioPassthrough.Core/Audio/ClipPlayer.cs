using System.Runtime.InteropServices;
using NAudio.Wave;

namespace RadioPassthrough.Core.Audio;

// Plays a 48 kHz clip (mono, or interleaved stereo) on the default playback device.
public sealed class ClipPlayer : IAsyncDisposable
{
    private WasapiPlayer? _player;
    private ClipProvider? _provider;

    public event Action? Finished;

    public bool IsPlaying => _player?.PlaybackState == PlaybackState.Playing;

    public double Position => _provider?.Progress ?? 0;

    public async Task PlayAsync(float[] clip, int channels = 1)
    {
        await StopAsync().ConfigureAwait(false);
        var provider = new ClipProvider(clip, channels);
        var player = await Task.Run(() =>
        {
            var p = new WasapiPlayerBuilder().WithSharedMode().WithLatency(60).Build();
            p.Init(provider);
            return p;
        }).ConfigureAwait(false);
        player.PlaybackStopped += (_, _) =>
        {
            if (ReferenceEquals(_player, player)) Finished?.Invoke();
        };
        _provider = provider;
        _player = player;
        player.Play();
    }

    public async Task StopAsync()
    {
        var player = _player;
        _player = null;
        _provider = null;
        if (player is null) return;
        try
        {
            player.Stop();
        }
        catch
        {
            // Device already gone.
        }
        await player.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(StopAsync());

    private sealed class ClipProvider(float[] clip, int channels) : IWaveProvider
    {
        private readonly int _frames = clip.Length / channels;
        private int _position; // frames

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(CaptureSource.SampleRate, 2);

        public double Progress => _frames == 0 ? 1 : (double)Volatile.Read(ref _position) / _frames;

        public int Read(Span<byte> buffer)
        {
            var output = MemoryMarshal.Cast<byte, float>(buffer);
            int frames = Math.Min(output.Length / 2, _frames - _position);
            if (frames <= 0) return 0;
            for (int i = 0; i < frames; i++)
            {
                int f = _position + i;
                output[i * 2] = clip[f * channels];
                output[i * 2 + 1] = clip[f * channels + (channels > 1 ? 1 : 0)];
            }
            Volatile.Write(ref _position, _position + frames);
            return frames * 2 * sizeof(float);
        }
    }
}
