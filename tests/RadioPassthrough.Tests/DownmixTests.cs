using RadioPassthrough.Core.Dsp;

namespace RadioPassthrough.Tests;

public class DownmixTests
{
    private static float[] Interleave(float[] left, float[] right)
    {
        var x = new float[left.Length * 2];
        for (int i = 0; i < left.Length; i++) { x[i * 2] = left[i]; x[i * 2 + 1] = right[i]; }
        return x;
    }

    private static float[] Voice(int n, float amp) => Enumerable.Range(0, n).Select(i => amp * MathF.Sin(2 * MathF.PI * 200 * i / 48000f)).ToArray();

    private static float[] Run(MicDownmix downmix, float[] interleaved, int chunk = 480)
    {
        var mono = new float[interleaved.Length / 2];
        for (int f = 0; f < mono.Length; f += chunk)
        {
            int n = Math.Min(chunk, mono.Length - f);
            downmix.Process(interleaved.AsSpan(f * 2, n * 2), 2, mono.AsSpan(f, n));
        }
        return mono;
    }

    private static float Rms(float[] x, int from) => MathF.Sqrt(x[from..].Select(v => v * v).Average());

    [Fact]
    public void Auto_uses_the_one_input_a_mic_is_plugged_into_at_full_level()
    {
        var voice = Voice(48000, 0.2f);
        var mono = Run(new MicDownmix(), Interleave(voice, new float[48000]));
        Assert.Equal(Rms(voice, 24000), Rms(mono, 24000), 3);  // not halved

        var onSecond = Run(new MicDownmix(), Interleave(new float[48000], voice));
        Assert.Equal(Rms(voice, 24000), Rms(onSecond, 24000), 3);
    }

    [Fact]
    public void Auto_averages_a_stereo_mic()
    {
        var left = Voice(48000, 0.2f);
        var right = Voice(48000, 0.1f);
        var mono = Run(new MicDownmix(), Interleave(left, right));
        Assert.Equal(0.15f / MathF.Sqrt(2), Rms(mono, 24000), 3);
    }

    [Fact]
    public void Chosen_inputs_are_used_as_asked()
    {
        var left = Voice(48000, 0.2f);
        var right = Voice(48000, 0.05f);
        var first = Run(new MicDownmix { Mode = MicChannelMode.First }, Interleave(left, right));
        var second = Run(new MicDownmix { Mode = MicChannelMode.Second }, Interleave(left, right));
        Assert.Equal(Rms(left, 24000), Rms(first, 24000), 3);
        Assert.Equal(Rms(right, 24000), Rms(second, 24000), 3);
    }

    [Fact]
    public void Old_settings_value_Both_loads_as_Auto()
    {
        var mode = System.Text.Json.JsonSerializer.Deserialize<MicChannelMode>("\"Both\"",
            new System.Text.Json.JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
        Assert.Equal(MicChannelMode.Auto, mode);
    }
}
