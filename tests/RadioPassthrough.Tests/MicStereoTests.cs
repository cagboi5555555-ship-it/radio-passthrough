using RadioPassthrough.Core.Dsp;

namespace RadioPassthrough.Tests;

public class MicStereoTests
{
    private static float[] Voice(int frames, float amp) =>
        Enumerable.Range(0, frames).Select(i => amp * MathF.Sin(2 * MathF.PI * 200 * i / 48000f)).ToArray();

    private static float[] Interleave(float[] a, float[] b)
    {
        var x = new float[a.Length * 2];
        for (int i = 0; i < a.Length; i++) { x[i * 2] = a[i]; x[i * 2 + 1] = b[i]; }
        return x;
    }

    private static (float[] L, float[] R) Run(float[] interleaved, int channels = 2, int chunk = 480)
    {
        var sides = new MicStereo();
        int frames = interleaved.Length / channels;
        var stereo = new float[frames * 2];
        for (int f = 0; f < frames; f += chunk)
        {
            int n = Math.Min(chunk, frames - f);
            sides.Process(interleaved.AsSpan(f * channels, n * channels), channels, stereo.AsSpan(f * 2, n * 2));
        }
        var l = new float[frames]; var r = new float[frames];
        for (int f = 0; f < frames; f++) { l[f] = stereo[f * 2]; r[f] = stereo[f * 2 + 1]; }
        return (l, r);
    }

    private static float Rms(float[] x) => MathF.Sqrt(x[24000..].Select(v => v * v).Average());

    [Fact]
    public void A_mic_on_input_1_goes_on_both_sides_at_full_level()
    {
        var voice = Voice(48000, 0.2f);
        var (l, r) = Run(Interleave(voice, new float[48000]));
        Assert.Equal(Rms(voice), Rms(l), 4);
        Assert.Equal(Rms(voice), Rms(r), 4); // TeamSpeak's average is then your full voice, not half
    }

    [Fact]
    public void A_mic_on_input_2_goes_on_both_sides_too()
    {
        var voice = Voice(48000, 0.2f);
        var (l, r) = Run(Interleave(new float[48000], voice));
        Assert.Equal(Rms(voice), Rms(l), 4);
        Assert.Equal(Rms(voice), Rms(r), 4);
    }

    [Fact]
    public void Two_active_inputs_stay_as_they_are()
    {
        var a = Voice(48000, 0.2f);
        var b = Voice(48000, 0.1f);
        var (l, r) = Run(Interleave(a, b));
        for (int i = 24000; i < 48000; i++)
        {
            Assert.Equal(a[i], l[i], 5);
            Assert.Equal(b[i], r[i], 5);
        }
    }

    [Fact]
    public void A_one_channel_mic_is_on_both_sides()
    {
        var voice = Voice(48000, 0.2f);
        var (l, r) = Run(voice, channels: 1);
        Assert.Equal(voice, l);
        Assert.Equal(voice, r);
    }
}
