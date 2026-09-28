using RadioPassthrough.Core.Dsp;

namespace RadioPassthrough.Tests;

public class NoiseGateTests
{
    private const int Rate = Mixer.SampleRate;

    // Steady fan-like noise at the given level, plus optional "speech" bursts (tone) at another level.
    private static float[] Signal(double seconds, float noiseDb, float voiceDb = float.NaN, Func<double, bool>? talking = null, int seed = 3)
    {
        var random = new Random(seed);
        float noise = Db.ToGain(noiseDb) * MathF.Sqrt(3); // uniform noise with that RMS
        float voice = float.IsNaN(voiceDb) ? 0 : Db.ToGain(voiceDb) * MathF.Sqrt(2);
        var x = new float[(int)(seconds * Rate)];
        for (int i = 0; i < x.Length; i++)
        {
            x[i] = noise * (float)(random.NextDouble() * 2 - 1);
            if (talking?.Invoke((double)i / Rate) == true) x[i] += voice * MathF.Sin(2 * MathF.PI * 220 * i / Rate);
        }
        return x;
    }

    private static float[] Gate(float[] input, int chunk = 480)
    {
        var gate = new NoiseGate();
        var output = input.ToArray();
        for (int offset = 0; offset < output.Length; offset += chunk)
            gate.Process(output.AsSpan(offset, Math.Min(chunk, output.Length - offset)));
        return output;
    }

    private static float RmsDb(float[] x, double from, double to)
    {
        int a = (int)(from * Rate), b = (int)(to * Rate);
        double s = 0;
        for (int i = a; i < b; i++) s += x[i] * x[i];
        return Db.FromGain((float)Math.Sqrt(s / (b - a)));
    }

    [Fact]
    public void Steady_background_noise_is_turned_down_about_30_dB()
    {
        var input = Signal(4, noiseDb: -50);
        var output = Gate(input);
        Assert.InRange(RmsDb(output, 2, 4) - RmsDb(input, 2, 4), -31, -28);
    }

    [Fact]
    public void Speech_passes_untouched_and_the_noise_between_words_is_gated()
    {
        // Talking at -25 dBFS for 1 s every 2 s, over -50 dBFS noise.
        bool Talking(double t) => t % 2.0 is >= 1.0 and < 2.0;
        var input = Signal(8, noiseDb: -50, voiceDb: -25, talking: Talking);
        var output = Gate(input);

        for (double start = 3; start < 8; start += 2)
        {
            Assert.InRange(RmsDb(output, start + 0.05, start + 0.95) - RmsDb(input, start + 0.05, start + 0.95), -0.2, 0.1); // voice
            Assert.InRange(RmsDb(output, start - 0.4, start - 0.05) - RmsDb(input, start - 0.4, start - 0.05), -31, -20);    // pause
        }
    }

    [Fact]
    public void Word_onsets_are_not_clipped()
    {
        bool Talking(double t) => t is >= 2.0 and < 2.5;
        var input = Signal(3, noiseDb: -50, voiceDb: -30, talking: Talking);
        var output = Gate(input);
        // The first 10 ms of the word already come through at full level (within 1 dB).
        Assert.InRange(RmsDb(output, 2.0, 2.01) - RmsDb(input, 2.0, 2.01), -1, 0.1);
    }

    [Fact]
    public void Soft_speech_on_a_very_clean_mic_still_gets_through()
    {
        // -80 dBFS floor, speech at only -55 dBFS.
        bool Talking(double t) => t is >= 2.0 and < 3.0;
        var input = Signal(3.5, noiseDb: -80, voiceDb: -55, talking: Talking);
        var output = Gate(input);
        Assert.InRange(RmsDb(output, 2.05, 2.95) - RmsDb(input, 2.05, 2.95), -0.2, 0.1);
        Assert.All(output, s => Assert.True(float.IsFinite(s)));
    }

    [Fact]
    public void Gain_changes_never_click()
    {
        bool Talking(double t) => t % 1.0 < 0.3;
        var output = Gate(Signal(5, noiseDb: -45, voiceDb: -20, talking: Talking, seed: 9), chunk: 441);
        var input = Signal(5, noiseDb: -45, voiceDb: -20, talking: Talking, seed: 9);
        // The gate's gain, sample to sample, where it can be read reliably: it may only glide (a 1 ms
        // opening ramp at most), never jump.
        float largest = 0;
        for (int i = 1; i < input.Length; i++)
        {
            if (MathF.Abs(input[i]) < 1e-3f || MathF.Abs(input[i - 1]) < 1e-3f) continue;
            largest = MathF.Max(largest, MathF.Abs(output[i] / input[i] - output[i - 1] / input[i - 1]));
        }
        Assert.True(largest < 0.025f, $"largest gain step {largest}");
    }
}
