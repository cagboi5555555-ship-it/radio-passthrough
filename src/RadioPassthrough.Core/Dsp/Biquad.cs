namespace RadioPassthrough.Core.Dsp;

// RBJ Audio EQ Cookbook biquads, the same designs ACRE2 uses via DspFilters' RBJ filters.
public sealed class Biquad
{
    private double _b0, _b1, _b2, _a1, _a2;
    private double _z1, _z2;

    private Biquad() { }

    public static Biquad LowPass(double sampleRate, double cutoff, double q)
    {
        var (cos, alpha) = Prewarp(sampleRate, cutoff, q);
        return Normalized((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static Biquad HighPass(double sampleRate, double cutoff, double q)
    {
        var (cos, alpha) = Prewarp(sampleRate, cutoff, q);
        return Normalized((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static Biquad LowShelf(double sampleRate, double cutoff, double gainDb, double shelfSlope)
    {
        double a = Math.Pow(10, gainDb / 40);
        double w0 = 2 * Math.PI * cutoff / sampleRate;
        double cos = Math.Cos(w0);
        double alpha = Math.Sin(w0) / 2 * Math.Sqrt((a + 1 / a) * (1 / shelfSlope - 1) + 2);
        double sq = 2 * Math.Sqrt(a) * alpha;
        return Normalized(
            a * ((a + 1) - (a - 1) * cos + sq),
            2 * a * ((a - 1) - (a + 1) * cos),
            a * ((a + 1) - (a - 1) * cos - sq),
            (a + 1) + (a - 1) * cos + sq,
            -2 * ((a - 1) + (a + 1) * cos),
            (a + 1) + (a - 1) * cos - sq);
    }

    private static (double cos, double alpha) Prewarp(double sampleRate, double cutoff, double q)
    {
        double w0 = 2 * Math.PI * cutoff / sampleRate;
        return (Math.Cos(w0), Math.Sin(w0) / (2 * q));
    }

    private static Biquad Normalized(double b0, double b1, double b2, double a0, double a1, double a2) => new()
    {
        _b0 = b0 / a0, _b1 = b1 / a0, _b2 = b2 / a0, _a1 = a1 / a0, _a2 = a2 / a0,
    };

    public void Reset() => _z1 = _z2 = 0;

    public void Process(Span<float> samples)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            double x = samples[i];
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            samples[i] = (float)y;
        }
    }

    public double MagnitudeAt(double sampleRate, double frequency)
    {
        double w = 2 * Math.PI * frequency / sampleRate;
        var z1 = System.Numerics.Complex.FromPolarCoordinates(1, -w);
        var z2 = z1 * z1;
        var num = _b0 + _b1 * z1 + _b2 * z2;
        var den = 1 + _a1 * z1 + _a2 * z2;
        return (num / den).Magnitude;
    }
}
