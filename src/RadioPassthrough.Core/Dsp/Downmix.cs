namespace RadioPassthrough.Core.Dsp;

public enum MicChannelMode
{
    // Average of all channels: what Windows and TeamSpeak do with a multi-channel mic today.
    Both,
    First,
    Second,
}

public static class Downmix
{
    public static void ToMono(ReadOnlySpan<float> interleaved, int channels, MicChannelMode mode, Span<float> mono)
    {
        int frames = interleaved.Length / channels;
        if (channels == 1)
        {
            interleaved[..frames].CopyTo(mono);
            return;
        }

        int pick = mode switch
        {
            MicChannelMode.First => 0,
            MicChannelMode.Second => Math.Min(1, channels - 1),
            _ => -1,
        };

        if (pick >= 0)
        {
            for (int f = 0; f < frames; f++)
                mono[f] = interleaved[f * channels + pick];
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
