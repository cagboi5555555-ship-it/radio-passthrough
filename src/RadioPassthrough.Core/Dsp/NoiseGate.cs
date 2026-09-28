namespace RadioPassthrough.Core.Dsp;

// Noise gate for the mic only. Between words it turns the mic down, so steady background noise (PC fans,
// room hum) isn't sent: ACRE boosts a radio transmission x3, which would turn that noise into hiss on top of
// ACRE's own static. The threshold follows the mic's own noise floor, so quiet and noisy mics both work.
// Game audio never passes through it.
public sealed class NoiseGate
{
    public const float ClosedDb = -30f;          // how far the mic is turned down between words
    public const float OpenAboveFloorDb = 9f;    // voice has to be this far above the noise to open
    public const float CloseAboveFloorDb = 5f;   // and falls back under this before it closes
    public const float LowestThresholdDb = -62f; // never opens on near-silence from a very quiet mic
    private const float FloorRiseDbPerSecond = 2f;
    private const float HoldSeconds = 0.30f;     // keeps word endings and short pauses intact

    private static readonly float Closed = Db.ToGain(ClosedDb);
    private static readonly float Attack = 1f - MathF.Exp(-1f / (0.001f * Mixer.SampleRate));
    private static readonly float Release = 1f - MathF.Exp(-1f / (0.120f * Mixer.SampleRate));

    private float _floorDb = float.NaN;
    private float _gain = 1f;
    private int _hold;
    private bool _open = true;

    public bool IsOpen => _open;

    public float NoiseFloorDb => float.IsNaN(_floorDb) ? Db.Floor : _floorDb;

    public void Reset()
    {
        _floorDb = float.NaN;
        _gain = 1f;
        _hold = 0;
        _open = true;
    }

    // Gates one chunk in place. The level of the chunk is measured before it is gated, so a word that
    // starts in this chunk opens the gate from this chunk's start: nothing of the first syllable is lost.
    public void Process(Span<float> mic)
    {
        int n = mic.Length;
        if (n == 0) return;

        double sumSq = 0;
        foreach (float s in mic) sumSq += s * s;
        float levelDb = Db.FromGain((float)Math.Sqrt(sumSq / n));

        if (float.IsNaN(_floorDb)) _floorDb = levelDb;
        else if (levelDb < _floorDb) _floorDb += (levelDb - _floorDb) * 0.3f;
        else _floorDb += MathF.Min(levelDb - _floorDb, FloorRiseDbPerSecond * n / Mixer.SampleRate);
        _floorDb = Math.Clamp(_floorDb, Db.Floor, -20f);

        float openAt = MathF.Max(_floorDb + OpenAboveFloorDb, LowestThresholdDb);
        float closeAt = MathF.Max(_floorDb + CloseAboveFloorDb, LowestThresholdDb - 4f);
        if (levelDb >= openAt)
        {
            _open = true;
            _hold = (int)(HoldSeconds * Mixer.SampleRate);
        }
        else if (_open && levelDb < closeAt)
        {
            _hold -= n;
            if (_hold <= 0) _open = false;
        }

        float target = _open ? 1f : Closed;
        for (int i = 0; i < n; i++)
        {
            _gain += (target - _gain) * (target > _gain ? Attack : Release);
            mic[i] *= _gain;
        }
    }
}
