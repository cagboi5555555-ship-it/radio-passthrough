using RadioPassthrough.Core.Dsp;

namespace RadioPassthrough.Tests;

public class MixerTests
{
    private static float[] Sine(int length, float freq, float amp, int phaseOffset = 0) =>
        Enumerable.Range(phaseOffset, length).Select(i => amp * MathF.Sin(2 * MathF.PI * freq * i / Mixer.SampleRate)).ToArray();

    private static Take MakeTake(bool[] gate, int blockSize = 480)
    {
        int length = gate.Length * blockSize;
        return new Take(Sine(length, 220, 0.3f), Sine(length, 1300, 0.6f, 17), gate, blockSize);
    }

    [Fact]
    public void DocOneToOne_is_exactly_mic_plus_gated_game()
    {
        var gate = Enumerable.Range(0, 200).Select(b => b % 50 is >= 10 and < 35).ToArray();
        var take = MakeTake(gate);

        var rendered = take.Render(MixSettings.DocOneToOne);
        var sum = take.PlainSum();

        Assert.Equal(sum.Length, rendered.Length);
        for (int i = 0; i < sum.Length; i++)
            Assert.Equal(sum[i], rendered[i]);
    }

    [Fact]
    public void Recorded_take_replays_exactly_what_was_sent_live()
    {
        // The audio device asks for uneven chunks; the recording must keep them so a re-render with the
        // live mix matches the live output sample for sample, even with every extra switched on.
        var settings = new MixSettings { GameDb = -3, MicDb = 2, DuckEnabled = true, DuckDb = 9, LevelerEnabled = true, LimiterEnabled = true };
        var live = new Mixer { Settings = settings };
        var recorder = new TakeRecorder(TimeSpan.FromSeconds(2));
        int[] sizes = [480, 480, 96, 480, 384, 441, 480, 17, 480];
        var mic = Sine(96000, 250, 0.5f);
        var game = Sine(96000, 1700, 0.9f, 5);
        var sent = new List<float>();

        int offset = 0;
        for (int c = 0; offset < mic.Length; c++)
        {
            int n = Math.Min(sizes[c % sizes.Length], mic.Length - offset);
            bool radio = c % 23 is >= 4 and < 15;
            var output = new float[n];
            live.Process(mic.AsSpan(offset, n), game.AsSpan(offset, n), radio, output);
            recorder.Append(mic.AsSpan(offset, n), game.AsSpan(offset, n), radio);
            sent.AddRange(output);
            offset += n;
        }

        var replay = recorder.ToTake().Render(settings);
        Assert.Equal(sent.Count, replay.Length);
        for (int i = 0; i < replay.Length; i++)
            Assert.Equal(sent[i], replay[i]);
    }

    [Fact]
    public void Game_is_silent_while_radio_key_is_up()
    {
        var take = MakeTake(new bool[20]);
        var rendered = take.Render(MixSettings.DocOneToOne);
        for (int i = 0; i < rendered.Length; i++)
            Assert.Equal(take.Mic[i], rendered[i]);
    }

    [Fact]
    public void Gate_ramps_without_jumps()
    {
        var mixer = new Mixer();
        var silence = new float[480];
        var ones = Enumerable.Repeat(1f, 480).ToArray();
        var output = new float[480];
        var all = new List<float>();
        for (int b = 0; b < 6; b++)
        {
            mixer.Process(silence, ones, gateOpen: b is >= 1 and < 4, output);
            all.AddRange(output);
        }

        float maxStep = 0;
        for (int i = 1; i < all.Count; i++)
            maxStep = MathF.Max(maxStep, MathF.Abs(all[i] - all[i - 1]));

        Assert.True(maxStep <= 1f / 470, $"step {maxStep}");
        Assert.Equal(1f, all[480 * 3 - 1], 3);
        Assert.Equal(0f, all[^1], 3);
    }

    [Fact]
    public void Limiter_keeps_peaks_under_ceiling()
    {
        var gate = Enumerable.Repeat(true, 100).ToArray();
        int length = 100 * 480;
        var take = new Take(Sine(length, 200, 0.9f), Sine(length, 90, 0.95f), gate, 480);

        var rendered = take.Render(MixSettings.DocOneToOne with { LimiterEnabled = true });

        Assert.True(rendered.Max(MathF.Abs) <= Mixer.LimiterCeiling + 1e-5f);
    }

    [Fact]
    public void Ducking_lowers_game_only_while_speaking()
    {
        int blocks = 400;
        var gate = Enumerable.Repeat(true, blocks).ToArray();
        int length = blocks * 480;
        var mic = new float[length];
        Sine(48000, 300, 0.3f).CopyTo(mic, 0); // one second of voice, then quiet
        var game = Sine(length, 1000, 0.2f);
        var take = new Take(mic, game, gate, 480);

        var settings = MixSettings.DocOneToOne with { DuckEnabled = true, DuckDb = 6 };
        var ducked = take.Render(settings);

        float Rms(float[] x, int from, int to) => MathF.Sqrt(x[from..to].Select(v => v * v).Average());
        // Middle of the talking part: game is 6 dB down, so total is quieter than the plain sum.
        var plain = take.PlainSum();
        float gameOnlyDucked = Rms(ducked, length - 9600, length);
        float gameOnlyPlain = Rms(plain, length - 9600, length);
        Assert.Equal(gameOnlyPlain, gameOnlyDucked, 3); // released after voice stops

        var diff = ducked.Zip(plain, (a, b) => a - b).ToArray();
        Assert.True(Rms(diff, 9600, 24000) > 0.05f);
    }
}
