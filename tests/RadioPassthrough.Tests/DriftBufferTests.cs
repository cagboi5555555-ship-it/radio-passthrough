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
}
