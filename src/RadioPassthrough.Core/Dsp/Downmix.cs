using System.Text.Json.Serialization;

namespace RadioPassthrough.Core.Dsp;

public enum MicChannelMode
{
    // The inputs your mic is actually on: a mic on one input of a two-input interface comes through at full
    // level, a stereo mic is averaged. (Saved as "Both", its name before 1.4.)
    [JsonStringEnumMemberName("Both")] Auto,
    First,
    Second,
}

public static class Downmix
{
    // Average of all channels (game audio, recordings).
    public static void ToMono(ReadOnlySpan<float> interleaved, int channels, Span<float> mono)
    {
        int frames = interleaved.Length / channels;
        if (channels == 1)
        {
            interleaved[..frames].CopyTo(mono);
            return;
        }

        float scale = 1f / channels;
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
                sum += interleaved[f * channels + c];
            mono[f] = sum * scale;
        }
    }
}

// Turns a multi-input mic device into one voice channel. In Auto it watches how much each input carries
// over the last second or so: inputs more than 30 dB below the loudest (nothing plugged in) are left out
// instead of halving your voice, and changes glide over 50 ms so they never click.
public sealed class MicDownmix
{
    private const float ActiveRatio = 0.001f; // -30 dB in energy
    private static readonly float Tracking = 1f - MathF.Exp(-1f / (1.0f * Mixer.SampleRate));
    private static readonly float Glide = 1f - MathF.Exp(-1f / (0.050f * Mixer.SampleRate));

    private float[] _energy = [];
    private float[] _weight = [];
    private volatile int _mode;

    public MicChannelMode Mode
    {
        get => (MicChannelMode)_mode;
        set => _mode = (int)value;
    }

    public void Reset()
    {
        _energy = [];
        _weight = [];
    }

    public void Process(ReadOnlySpan<float> interleaved, int channels, Span<float> mono)
    {
        int frames = interleaved.Length / channels;
        if (channels == 1)
        {
            interleaved[..frames].CopyTo(mono);
            return;
        }
        if (_energy.Length != channels)
        {
            _energy = new float[channels];
            _weight = new float[channels];
            Array.Fill(_weight, 1f / channels);
        }

        // Per-input energy, followed over about a second.
        for (int c = 0; c < channels; c++)
        {
            double sum = 0;
            for (int f = 0; f < frames; f++)
            {
                float x = interleaved[f * channels + c];
                sum += x * x;
            }
            float blockEnergy = (float)(sum / Math.Max(1, frames));
            float k = 1f - MathF.Pow(1f - Tracking, frames);
            _energy[c] += (blockEnergy - _energy[c]) * k;
        }

        Span<float> target = stackalloc float[channels];
        var mode = Mode;
        if (mode == MicChannelMode.First || mode == MicChannelMode.Second)
        {
            target[mode == MicChannelMode.First ? 0 : Math.Min(1, channels - 1)] = 1f;
        }
        else
        {
            float loudest = 0;
            foreach (float e in _energy) loudest = MathF.Max(loudest, e);
            int active = 0;
            for (int c = 0; c < channels; c++)
                if (loudest <= 1e-12f || _energy[c] >= loudest * ActiveRatio) active++;
            for (int c = 0; c < channels; c++)
                target[c] = loudest <= 1e-12f || _energy[c] >= loudest * ActiveRatio ? 1f / active : 0f;
        }

        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
            {
                _weight[c] += (target[c] - _weight[c]) * Glide;
                sum += interleaved[f * channels + c] * _weight[c];
            }
            mono[f] = sum;
        }
    }
}
