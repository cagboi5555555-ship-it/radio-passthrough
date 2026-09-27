namespace RadioPassthrough.Core.Dsp;

public sealed record TakeAnalysis(float PeakDb, float LoudnessDb, int ClippedSamples)
{
    public static TakeAnalysis Of(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty) return new TakeAnalysis(Db.Floor, Db.Floor, 0);

        float peak = 0;
        int clipped = 0;
        double activeSumSq = 0;
        long activeCount = 0;
        const int window = 480;

        for (int offset = 0; offset < samples.Length; offset += window)
        {
            int n = Math.Min(window, samples.Length - offset);
            double sumSq = 0;
            for (int i = 0; i < n; i++)
            {
                float a = MathF.Abs(samples[offset + i]);
                peak = MathF.Max(peak, a);
                if (a >= 1f) clipped++;
                sumSq += a * a;
            }
            if (Db.FromGain((float)Math.Sqrt(sumSq / n)) > Mixer.VoiceThresholdDb)
            {
                activeSumSq += sumSq;
                activeCount += n;
            }
        }

        float loudness = activeCount == 0 ? Db.Floor : Db.FromGain((float)Math.Sqrt(activeSumSq / activeCount));
        return new TakeAnalysis(Db.FromGain(peak), loudness, clipped);
    }
}
