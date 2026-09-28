namespace RadioPassthrough.Core.Dsp;

// The guide's Voicemeeter routing, exactly: mic + game summed at unity, game only while a radio key is
// held. The only thing added is a 10 ms fade when the radio opens or closes, so it never clicks.
public sealed class Mixer
{
    public const int SampleRate = 48000;
    public const float VoiceThresholdDb = -45f;

    private const float GateStep = 1f / (0.010f * SampleRate);

    private float _gate;

    public float GateLevel => _gate;
    public float MicPeak { get; private set; }
    public float GamePeak { get; private set; }
    public float OutputPeak { get; private set; }

    public void Process(ReadOnlySpan<float> mic, ReadOnlySpan<float> game, bool gateOpen, Span<float> output)
    {
        int n = output.Length;
        if (mic.Length < n || game.Length < n)
            throw new ArgumentException("Inputs must be at least as long as the output.");

        float target = gateOpen ? 1f : 0f;
        float micPeak = 0, gamePeak = 0, outPeak = 0;
        for (int i = 0; i < n; i++)
        {
            if (_gate < target) _gate = MathF.Min(target, _gate + GateStep);
            else if (_gate > target) _gate = MathF.Max(target, _gate - GateStep);

            float m = mic[i], g = game[i];
            float y = m + g * _gate;
            output[i] = y;

            micPeak = MathF.Max(micPeak, MathF.Abs(m));
            gamePeak = MathF.Max(gamePeak, MathF.Abs(g));
            outPeak = MathF.Max(outPeak, MathF.Abs(y));
        }
        MicPeak = micPeak;
        GamePeak = gamePeak;
        OutputPeak = outPeak;
    }
}
