namespace RadioPassthrough.Core.Dsp;

// The guide's Voicemeeter bus B1, exactly: mic + game summed at unity in stereo, the game only while a
// radio key is held (the macro's Strip[4].B1 = 1). All audio is interleaved stereo. The only thing added
// is a 10 ms fade when the radio opens or closes, so it never clicks.
public sealed class Mixer
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
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
        if (n % Channels != 0)
            throw new ArgumentException("Audio must be whole stereo frames.");

        float target = gateOpen ? 1f : 0f;
        float micPeak = 0, gamePeak = 0, outPeak = 0;
        for (int i = 0; i < n; i += Channels)
        {
            if (_gate < target) _gate = MathF.Min(target, _gate + GateStep);
            else if (_gate > target) _gate = MathF.Max(target, _gate - GateStep);

            for (int c = 0; c < Channels; c++)
            {
                float m = mic[i + c], g = game[i + c];
                float y = m + g * _gate;
                output[i + c] = y;

                micPeak = MathF.Max(micPeak, MathF.Abs(m));
                gamePeak = MathF.Max(gamePeak, MathF.Abs(g));
                outPeak = MathF.Max(outPeak, MathF.Abs(y));
            }
        }
        MicPeak = micPeak;
        GamePeak = gamePeak;
        OutputPeak = outPeak;
    }
}
