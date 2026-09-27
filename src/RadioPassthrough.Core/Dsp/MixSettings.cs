namespace RadioPassthrough.Core.Dsp;

public enum MixPreset
{
    DocOneToOne,
    Custom,
}

public sealed record MixSettings
{
    public float GameDb { get; init; }
    public float MicDb { get; init; }
    public bool DuckEnabled { get; init; }
    public float DuckDb { get; init; } = 6f;
    public bool LevelerEnabled { get; init; }
    public bool LimiterEnabled { get; init; }

    // The guide's Voicemeeter routing: mic + game summed at unity, nothing else.
    public static MixSettings DocOneToOne { get; } = new();

    public static MixSettings CustomDefault { get; } = new()
    {
        DuckEnabled = true,
        DuckDb = 6f,
        LimiterEnabled = true,
    };

    public bool IsPlainSum => GameDb == 0 && MicDb == 0 && !DuckEnabled && !LevelerEnabled && !LimiterEnabled;
}
