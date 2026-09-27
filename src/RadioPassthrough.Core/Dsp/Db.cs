namespace RadioPassthrough.Core.Dsp;

public static class Db
{
    public const float Floor = -96f;

    public static float ToGain(float db) => MathF.Pow(10f, db / 20f);

    public static float FromGain(float gain) => gain <= 1.585e-5f ? Floor : 20f * MathF.Log10(gain);
}
