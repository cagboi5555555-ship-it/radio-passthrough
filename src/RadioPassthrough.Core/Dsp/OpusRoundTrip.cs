using Concentus;
using Concentus.Enums;

namespace RadioPassthrough.Core.Dsp;

public enum TeamSpeakCodec
{
    OpusVoice,
    OpusMusic,
}

// Encodes and decodes through Opus the way TeamSpeak 3 sends voice, at the bitrate TeamSpeak uses
// for the channel's codec quality (TeamSpeak Client SDK codec table).
public static class OpusRoundTrip
{
    public const int SampleRate = 48000;
    public const int FrameSize = 960; // 20 ms

    public static int Bitrate(TeamSpeakCodec codec, int quality)
    {
        int q = Math.Clamp(quality, 0, 10);
        return codec switch
        {
            TeamSpeakCodec.OpusVoice => 4096 * (q + 1),
            TeamSpeakCodec.OpusMusic => 7200 * (q + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(codec)),
        };
    }

    public static float[] Process(ReadOnlySpan<float> input, TeamSpeakCodec codec, int quality)
    {
        var application = codec == TeamSpeakCodec.OpusVoice
            ? OpusApplication.OPUS_APPLICATION_VOIP
            : OpusApplication.OPUS_APPLICATION_AUDIO;

        using var encoder = OpusCodecFactory.CreateEncoder(SampleRate, 1, application, null);
        using var decoder = OpusCodecFactory.CreateDecoder(SampleRate, 1, null);
        encoder.Bitrate = Bitrate(codec, quality);
        encoder.SignalType = codec == TeamSpeakCodec.OpusVoice ? OpusSignal.OPUS_SIGNAL_VOICE : OpusSignal.OPUS_SIGNAL_MUSIC;

        var output = new float[input.Length];
        var frame = new float[FrameSize];
        var decoded = new float[FrameSize];
        var packet = new byte[1275];

        for (int offset = 0; offset < input.Length; offset += FrameSize)
        {
            int n = Math.Min(FrameSize, input.Length - offset);
            Array.Clear(frame);
            for (int i = 0; i < n; i++)
                frame[i] = Math.Clamp(input[offset + i], -1f, 1f);

            int bytes = encoder.Encode((ReadOnlySpan<float>)frame, FrameSize, (Span<byte>)packet, packet.Length);
            int samples = decoder.Decode((ReadOnlySpan<byte>)packet.AsSpan(0, bytes), (Span<float>)decoded, FrameSize, false);
            decoded.AsSpan(0, Math.Min(n, samples)).CopyTo(output.AsSpan(offset));
        }
        return output;
    }
}
