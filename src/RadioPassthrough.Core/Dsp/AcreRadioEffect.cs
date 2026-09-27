namespace RadioPassthrough.Core.Dsp;

// Receive-side radio effect as ACRE2 applies it to a transmission (CRadioEffect / CFilterRadio,
// IDI-Systems/acre2, GPL-3.0). Same order and constants: x3 boost, pink + white noise scaled by
// signal quality, 90 Hz ring modulation, sample-hold "foldback", 4 kHz low-pass, 750 Hz high-pass,
// optional speaker shelf, hard clamp.
public sealed class AcreRadioEffect
{
    public const int SampleRate = 48000;
    public const int BlockSize = 480;

    private static readonly float[] PinkA = [0.02109238f, 0.07113478f, 0.68873558f];
    private static readonly float[] PinkP = [0.3190f, 0.7756f, 0.9613f];
    private const float RandMax = 32767f;

    private readonly Biquad _lowPass = Biquad.LowPass(SampleRate, 4000, 2.0);
    private readonly Biquad _highPass = Biquad.HighPass(SampleRate, 750, 0.97);
    private readonly Biquad _lowShelf = Biquad.LowShelf(SampleRate, 1000, -10, 1);
    private readonly float[] _pinkState = new float[3];
    private readonly Random _random;
    private float _ringPhase;

    public AcreRadioEffect(int? seed = null) => _random = seed is { } s ? new Random(s) : new Random();

    public static int FoldbackDivisor(float value)
    {
        float div = 256.0f * MathF.Pow(value, 4) - 693.33f * MathF.Pow(value, 3)
                    + 648.0f * MathF.Pow(value, 2) - 250.67f * value + 40.0f;
        return Math.Max(5, (int)div);
    }

    public float[] ProcessAll(ReadOnlySpan<float> input, float signalQuality, bool noise = true, bool loudSpeaker = false)
    {
        var output = input.ToArray();
        for (int offset = 0; offset < output.Length; offset += BlockSize)
        {
            int n = Math.Min(BlockSize, output.Length - offset);
            ProcessBlock(output.AsSpan(offset, n), signalQuality, noise, loudSpeaker);
        }
        return output;
    }

    public void ProcessBlock(Span<float> x, float value, bool noise, bool loudSpeaker)
    {
        if (value <= 0f)
        {
            x.Clear();
            return;
        }

        for (int i = 0; i < x.Length; i++)
            x[i] *= 3.0f;

        if (noise)
        {
            float inverse = 1.25f - value;
            for (int i = 0; i < x.Length; i++)
            {
                float n = PinkTick() * (0.35f * inverse);
                x[i] = (x[i] + n) - (n * x[i]);
            }
            for (int i = 0; i < x.Length; i++)
                x[i] += WhiteNoise() * (0.001f * inverse);
        }

        float ringMix = (1.0f - value) * 0.20f;
        for (int i = 0; i < x.Length; i++)
        {
            float multiple = x[i] * MathF.Sin(_ringPhase * (MathF.PI / 2));
            _ringPhase += 90.0f / SampleRate;
            if (_ringPhase > 1.0f) _ringPhase = 0.0f;
            x[i] = x[i] * (1.0f - ringMix) + multiple * ringMix;
        }

        int divisor = FoldbackDivisor(value);
        for (int i = 0; i < x.Length; i += divisor)
            for (int k = 1; k < divisor && i + k < x.Length; k++)
                x[i + k] = x[i];

        _lowPass.Process(x);
        _highPass.Process(x);
        if (loudSpeaker) _lowShelf.Process(x);

        for (int i = 0; i < x.Length; i++)
            x[i] = Math.Clamp(x[i], -1f, 1f);
    }

    private float Rand() => _random.Next(0, 32768);

    private float PinkTick()
    {
        const float rmi2 = 2.0f / RandMax;
        float offset = PinkA[0] + PinkA[1] + PinkA[2];
        for (int s = 0; s < 3; s++)
        {
            float temp = Rand();
            _pinkState[s] = PinkP[s] * (_pinkState[s] - temp) + temp;
        }
        return (PinkA[0] * _pinkState[0] + PinkA[1] * _pinkState[1] + PinkA[2] * _pinkState[2]) * rmi2 - offset;
    }

    private float WhiteNoise()
    {
        const float c1 = (1 << 15) - 1;
        const float c2 = (int)(c1 / 3) + 1;
        const float c3 = 1f / c1;
        float r = Rand() / (RandMax + 1);
        return (2f * (r * c2 + r * c2 + r * c2) - 3f * (c2 - 1f)) * c3;
    }
}
