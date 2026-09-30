namespace RadioPassthrough.Core.Dsp;

// Turns the mic device's inputs into the left/right pair TeamSpeak gets. A mic plugged into one input of a
// two-input interface is put on both sides at full level: with it on one side only, TeamSpeak averages it
// with the empty side, gets your voice at half level, and its voice activation no longer opens for normal
// speech (so direct speech isn't heard while radio still is). Two active inputs (a stereo mic) stay as they
// are. Inputs are judged by their level over about a second; changes glide over 50 ms, so they never click.
public sealed class MicStereo
{
    private const float ActiveRatio = 0.001f; // an input 30 dB below the other counts as empty
    private static readonly float Tracking = 1f - MathF.Exp(-1f / (1.0f * Mixer.SampleRate));
    private static readonly float Glide = 1f - MathF.Exp(-1f / (0.050f * Mixer.SampleRate));

    private float _energy1, _energy2;
    private float _leftFrom1 = 1, _leftFrom2, _rightFrom1, _rightFrom2 = 1;

    public void Reset()
    {
        _energy1 = _energy2 = 0;
        _leftFrom1 = _rightFrom2 = 1;
        _leftFrom2 = _rightFrom1 = 0;
    }

    // Writes interleaved stereo (2 × frames) into `stereo`.
    public void Process(ReadOnlySpan<float> interleaved, int channels, Span<float> stereo)
    {
        int frames = interleaved.Length / channels;
        if (channels == 1)
        {
            for (int f = 0; f < frames; f++) stereo[f * 2] = stereo[f * 2 + 1] = interleaved[f];
            return;
        }

        double sum1 = 0, sum2 = 0;
        for (int f = 0; f < frames; f++)
        {
            float a = interleaved[f * channels], b = interleaved[f * channels + 1];
            sum1 += a * a;
            sum2 += b * b;
        }
        float k = 1f - MathF.Pow(1f - Tracking, Math.Max(1, frames));
        _energy1 += ((float)(sum1 / Math.Max(1, frames)) - _energy1) * k;
        _energy2 += ((float)(sum2 / Math.Max(1, frames)) - _energy2) * k;

        // Targets: which input feeds each side.
        float l1 = 1, l2 = 0, r1 = 0, r2 = 1;               // both active (or both silent): as they are
        if (_energy2 < _energy1 * ActiveRatio) r1 = 1; else if (_energy1 < _energy2 * ActiveRatio) l2 = 1;
        if (r1 == 1) r2 = 0;
        if (l2 == 1) l1 = 0;

        for (int f = 0; f < frames; f++)
        {
            _leftFrom1 += (l1 - _leftFrom1) * Glide;
            _leftFrom2 += (l2 - _leftFrom2) * Glide;
            _rightFrom1 += (r1 - _rightFrom1) * Glide;
            _rightFrom2 += (r2 - _rightFrom2) * Glide;
            float a = interleaved[f * channels], b = interleaved[f * channels + 1];
            stereo[f * 2] = a * _leftFrom1 + b * _leftFrom2;
            stereo[f * 2 + 1] = a * _rightFrom1 + b * _rightFrom2;
        }
    }
}
