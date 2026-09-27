namespace RadioPassthrough.Core.Dsp;

// Mono mix of mic and game audio. With MixSettings.DocOneToOne the output is exactly
// mic + game * gate, where the gate ramps over 10 ms so opening the radio never clicks.
public sealed class Mixer
{
    public const int SampleRate = 48000;
    public const float VoiceThresholdDb = -45f;
    public const float LevelerTargetDb = -20f;
    public const float LimiterCeiling = 0.8913f; // -1 dBFS

    private const float GateStep = 1f / (0.010f * SampleRate);
    private static readonly float DuckAttack = Coefficient(0.020f);
    private static readonly float DuckRelease = Coefficient(0.300f);
    private static readonly float LimiterRelease = Coefficient(0.060f);
    private const int VoiceHoldSamples = (int)(0.300f * SampleRate);

    private MixSettings _settings = MixSettings.DocOneToOne;
    private float _gate;
    private float _duck = 1f;
    private int _voiceHold;
    private float _levelerDb;
    private float _levelerGain = 1f;
    private float _limiterEnvelope;

    public MixSettings Settings
    {
        get => Volatile.Read(ref _settings);
        set => Volatile.Write(ref _settings, value);
    }

    public float GateLevel => _gate;
    public float MicPeak { get; private set; }
    public float GamePeak { get; private set; }
    public float OutputPeak { get; private set; }

    private static float Coefficient(float seconds) => 1f - MathF.Exp(-1f / (seconds * SampleRate));

    public void Process(ReadOnlySpan<float> mic, ReadOnlySpan<float> game, bool gateOpen, Span<float> output)
    {
        int n = output.Length;
        if (mic.Length < n || game.Length < n)
            throw new ArgumentException("Inputs must be at least as long as the output.");
        if (n == 0) return;

        var s = Settings;
        float micGain = Db.ToGain(s.MicDb);
        float gameGain = Db.ToGain(s.GameDb);

        float sumSq = 0;
        for (int i = 0; i < n; i++)
        {
            float m = mic[i] * micGain;
            sumSq += m * m;
        }
        float micRmsDb = Db.FromGain(MathF.Sqrt(sumSq / n));
        bool voiceNow = micRmsDb > VoiceThresholdDb;
        _voiceHold = voiceNow ? VoiceHoldSamples : Math.Max(0, _voiceHold - n);
        bool voiceActive = _voiceHold > 0;

        if (!s.LevelerEnabled)
            _levelerDb = 0;
        else if (voiceNow)
            _levelerDb += (Math.Clamp(LevelerTargetDb - micRmsDb, -6f, 12f) - _levelerDb) * 0.03f;
        float levelerStart = _levelerGain;
        float levelerEnd = Db.ToGain(_levelerDb);

        float duckTarget = s.DuckEnabled && voiceActive ? Db.ToGain(-s.DuckDb) : 1f;
        float gateTarget = gateOpen ? 1f : 0f;

        float micPeak = 0, gamePeak = 0, outPeak = 0;
        for (int i = 0; i < n; i++)
        {
            float leveler = levelerStart + (levelerEnd - levelerStart) * (i + 1) / n;
            float m = mic[i] * micGain * leveler;

            if (_gate < gateTarget) _gate = MathF.Min(gateTarget, _gate + GateStep);
            else if (_gate > gateTarget) _gate = MathF.Max(gateTarget, _gate - GateStep);

            _duck += (duckTarget - _duck) * (duckTarget < _duck ? DuckAttack : DuckRelease);

            float g = game[i] * gameGain;
            float y = m + g * _gate * _duck;

            if (s.LimiterEnabled)
            {
                float a = MathF.Abs(y);
                _limiterEnvelope = a >= _limiterEnvelope ? a : _limiterEnvelope + (a - _limiterEnvelope) * LimiterRelease;
                if (_limiterEnvelope > LimiterCeiling)
                    y *= LimiterCeiling / _limiterEnvelope;
            }

            output[i] = y;
            micPeak = MathF.Max(micPeak, MathF.Abs(m));
            gamePeak = MathF.Max(gamePeak, MathF.Abs(g));
            outPeak = MathF.Max(outPeak, MathF.Abs(y));
        }

        _levelerGain = levelerEnd;
        MicPeak = micPeak;
        GamePeak = gamePeak;
        OutputPeak = outPeak;
    }
}
