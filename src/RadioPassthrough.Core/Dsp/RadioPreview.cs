namespace RadioPassthrough.Core.Dsp;

public enum SignalStrength
{
    Strong,
    Fair,
    Weak,
}

public sealed record RadioPreviewSettings
{
    public TeamSpeakCodec Codec { get; init; } = TeamSpeakCodec.OpusVoice;
    public int Quality { get; init; } = 6;
    public SignalStrength Signal { get; init; } = SignalStrength.Strong;
}

// What a teammate hears on their radio: TeamSpeak's Opus at the channel quality, then ACRE2's
// receive effect at the chosen signal quality.
public static class RadioPreview
{
    public static float SignalQuality(SignalStrength strength) => strength switch
    {
        SignalStrength.Strong => 0.95f,
        SignalStrength.Fair => 0.6f,
        SignalStrength.Weak => 0.3f,
        _ => 0.95f,
    };

    public static float[] AsTeammateHears(ReadOnlySpan<float> teamSpeakInput, RadioPreviewSettings settings)
    {
        var decoded = OpusRoundTrip.Process(teamSpeakInput, settings.Codec, settings.Quality);
        return new AcreRadioEffect().ProcessAll(decoded, SignalQuality(settings.Signal));
    }

    public static float[] AsTeamSpeakSends(ReadOnlySpan<float> teamSpeakInput, RadioPreviewSettings settings) =>
        OpusRoundTrip.Process(teamSpeakInput, settings.Codec, settings.Quality);
}
