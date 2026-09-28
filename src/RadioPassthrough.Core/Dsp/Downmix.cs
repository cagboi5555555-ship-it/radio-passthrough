namespace RadioPassthrough.Core.Dsp;

public static class Downmix
{
    // Average of all channels (test recordings).
    public static void ToMono(ReadOnlySpan<float> interleaved, int channels, Span<float> mono)
    {
        int frames = interleaved.Length / channels;
        if (channels == 1)
        {
            interleaved[..frames].CopyTo(mono);
            return;
        }

        float scale = 1f / channels;
        for (int f = 0; f < frames; f++)
        {
            float sum = 0;
            for (int c = 0; c < channels; c++)
                sum += interleaved[f * channels + c];
            mono[f] = sum * scale;
        }
    }
}
