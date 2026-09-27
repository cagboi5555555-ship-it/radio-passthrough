using RadioPassthrough.Core.Dsp;

namespace RadioPassthrough.Tests;

public class RadioEffectTests
{
    private static float Rms(float[] x) => MathF.Sqrt(x.Select(v => v * v).Average());

    private static float ResponseAt(float frequency)
    {
        var tone = Enumerable.Range(0, 48000).Select(i => 0.05f * MathF.Sin(2 * MathF.PI * frequency * i / 48000)).ToArray();
        var output = new AcreRadioEffect(seed: 1).ProcessAll(tone, 0.95f, noise: false);
        return Rms(output[4800..]) / Rms(tone[4800..]);
    }

    [Fact]
    public void Voice_band_passes_with_acre_boost_and_lows_are_cut()
    {
        float low = ResponseAt(200);
        float mid = ResponseAt(1500);
        Assert.True(mid > 1.5f, $"mid {mid}"); // the x3 boost survives in the pass band
        Assert.True(low < mid / 4, $"low {low}");
    }

    [Fact]
    public void Filters_match_acre_corners()
    {
        var lp = Biquad.LowPass(48000, 4000, 2.0);
        var hp = Biquad.HighPass(48000, 750, 0.97);
        Assert.Equal(2.0, lp.MagnitudeAt(48000, 4000), 2);   // resonant peak = Q at the corner
        Assert.Equal(0.97, hp.MagnitudeAt(48000, 750), 2);
        Assert.True(lp.MagnitudeAt(48000, 10000) < 0.25);
        Assert.True(hp.MagnitudeAt(48000, 200) < 0.1);
        Assert.Equal(1.0, lp.MagnitudeAt(48000, 100), 2);
    }

    [Fact]
    public void Foldback_matches_acre_polynomial()
    {
        Assert.Equal(5, AcreRadioEffect.FoldbackDivisor(1.0f));
        Assert.Equal(10, AcreRadioEffect.FoldbackDivisor(0.2f));
        Assert.Equal(20, AcreRadioEffect.FoldbackDivisor(0.1f));
    }

    [Fact]
    public void Weak_signal_is_noisier()
    {
        var silence = new float[48000];
        float strong = Rms(new AcreRadioEffect(1).ProcessAll(silence, 0.95f));
        float weak = Rms(new AcreRadioEffect(1).ProcessAll(silence, 0.3f));
        Assert.True(weak > strong * 2, $"strong {strong} weak {weak}");
    }

    [Fact]
    public void Output_never_exceeds_full_scale()
    {
        var loud = Enumerable.Range(0, 48000).Select(i => MathF.Sin(i * 0.1f)).ToArray();
        var output = new AcreRadioEffect(1).ProcessAll(loud, 0.6f);
        Assert.True(output.Max(MathF.Abs) <= 1f);
    }

    [Fact]
    public void Opus_bitrates_follow_teamspeak_table()
    {
        Assert.Equal(28672, OpusRoundTrip.Bitrate(TeamSpeakCodec.OpusVoice, 6));
        Assert.Equal(45056, OpusRoundTrip.Bitrate(TeamSpeakCodec.OpusVoice, 10));
        Assert.Equal(79200, OpusRoundTrip.Bitrate(TeamSpeakCodec.OpusMusic, 10));
    }

    [Fact]
    public void Opus_round_trip_keeps_speech_band_energy()
    {
        var tone = Enumerable.Range(0, 48000).Select(i => 0.3f * MathF.Sin(2 * MathF.PI * 440 * i / 48000)).ToArray();
        var output = OpusRoundTrip.Process(tone, TeamSpeakCodec.OpusVoice, 6);
        Assert.Equal(tone.Length, output.Length);
        float ratio = Rms(output[4800..]) / Rms(tone[4800..]);
        Assert.InRange(ratio, 0.7f, 1.3f);
    }
}
