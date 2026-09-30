using RadioPassthrough.Core.Dsp;

namespace RadioPassthrough.Tests;

public class DriftBufferTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(-200)]
    [InlineData(0)]
    public void Holds_fill_steady_across_clock_skew_without_gaps(int ppm)
    {
        const int block = 480;
        const int target = 960;
        var buffer = new DriftBuffer(target);
        var write = Enumerable.Repeat(0.25f, 441).ToArray(); // capture packets not aligned with output blocks
        var read = new float[block];

        double writerRate = 48000 * (1 + ppm / 1e6);
        double writerAcc = 0;
        int underrunsAfterStart = 0;
        double minFill = double.MaxValue, maxFill = 0;

        // 10 simulated minutes of 10 ms output blocks.
        for (int step = 0; step < 60_000; step++)
        {
            writerAcc += writerRate / 100;
            while (writerAcc >= write.Length)
            {
                buffer.Write(write);
                writerAcc -= write.Length;
            }
            int produced = buffer.Read(read);
            if (step > 100)
            {
                if (produced < block) underrunsAfterStart++;
                minFill = Math.Min(minFill, buffer.Fill);
                maxFill = Math.Max(maxFill, buffer.Fill);
            }
        }

        Assert.Equal(0, underrunsAfterStart);
        Assert.InRange(minFill, 0, target * 3);
        Assert.InRange(maxFill, target / 4.0, target * 3);
    }

    [Fact]
    public void Stereo_channels_stay_separate_and_aligned()
    {
        var buffer = new DriftBuffer(960, channels: 2);
        var chunk = new float[441 * 2];
        var block = new float[480 * 2];
        long n = 0;
        int due = 0;
        var outL = new List<float>();
        var outR = new List<float>();
        for (int step = 0; step < 400; step++)
        {
            for (int f = 0; f < 441; f++, n++)
            {
                chunk[f * 2] = 0.5f * MathF.Sin(2 * MathF.PI * 440 * n / 48000);
                chunk[f * 2 + 1] = -chunk[f * 2]; // right is the exact inverse of left
            }
            buffer.Write(chunk);
            for (due += 441; due >= 480; due -= 480)
                if (buffer.Read(block) == block.Length)
                    for (int f = 0; f < 480; f++) { outL.Add(block[f * 2]); outR.Add(block[f * 2 + 1]); }
        }

        Assert.True(outL.Count > 150_000, $"only {outL.Count} frames");
        for (int i = 0; i < outL.Count; i++)
            Assert.Equal(-outL[i], outR[i], 5); // same timing on both sides, nothing crossed over
    }

    [Fact]
    public void Tone_keeps_its_level_and_stays_smooth()
    {
        var buffer = new DriftBuffer(960);
        var input = Enumerable.Range(0, 48000 * 5).Select(i => 0.5f * MathF.Sin(2 * MathF.PI * 1000 * i / 48000)).ToArray();
        var output = new List<float>();
        var block = new float[480];
        int due = 0;
        for (int i = 0; i < input.Length; i += 441)
        {
            int n = Math.Min(441, input.Length - i);
            buffer.Write(input.AsSpan(i, n));
            due += n;
            while (due >= 480)
            {
                due -= 480;
                if (buffer.Read(block) == block.Length) output.AddRange(block);
            }
        }

        var steady = output.Skip(4800).ToArray();
        float rms = MathF.Sqrt(steady.Select(v => v * v).Average());
        Assert.InRange(rms, 0.5f / MathF.Sqrt(2) * 0.99f, 0.5f / MathF.Sqrt(2) * 1.01f);

        // A 1 kHz tone at 0.5 never moves more than ~0.066 per sample; a dropout or jump would.
        float maxStep = 0;
        for (int i = 1; i < steady.Length; i++) maxStep = MathF.Max(maxStep, MathF.Abs(steady[i] - steady[i - 1]));
        Assert.True(maxStep < 0.07f, $"step {maxStep}");
    }

    [Fact]
    public void A_short_gap_is_a_dropout_but_a_silent_pause_is_not()
    {
        long now = 0;
        var buffer = new DriftBuffer(960, clockMs: () => now);
        var packet = new float[480];
        var read = new float[480];

        for (int i = 0; i < 4; i++) buffer.Write(packet);
        while (buffer.Read(read) == read.Length) { } // runs dry
        Assert.Equal(1, buffer.Underruns);

        now += 40; // audio comes back almost at once: a real gap
        buffer.Write(packet);
        Assert.Equal(1, buffer.Dropouts);

        for (int i = 0; i < 4; i++) buffer.Write(packet);
        while (buffer.Read(read) == read.Length) { }
        now += 5000; // the app was silent for a while (per-app capture delivers nothing then)
        buffer.Write(packet);
        Assert.Equal(2, buffer.Underruns);
        Assert.Equal(1, buffer.Dropouts);
    }
}
