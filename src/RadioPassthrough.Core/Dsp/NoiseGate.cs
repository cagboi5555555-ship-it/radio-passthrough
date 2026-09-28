namespace RadioPassthrough.Core.Dsp;

// Noise gate for the mic only. Between words it turns the mic down, so steady background noise (PC fans,
// room hum) isn't sent: ACRE boosts a radio transmission x3, which would turn that noise into hiss on top of
// ACRE's own static. The threshold follows the mic's own noise floor, so quiet and noisy mics both work.
// Game audio never passes through it.
//
// Everything is measured per sample or per fixed 10 ms block, never per chunk: the audio device hands
// over chunks of any size, and the gate must behave the same for all of them.
public sealed class NoiseGate
{
    public const float DefaultReductionDb = 30f; // how far the mic is turned down between words
    public const float MaxReductionDb = 40f;
    public const float OpenAboveFloorDb = 9f;    // voice has to be this far above the noise to open
    public const float CloseAboveFloorDb = 5f;   // and falls back under this before it closes
    public const float LowestThresholdDb = -62f; // never opens on near-silence from a very quiet mic
    private const float FloorRiseDbPerSecond = 2f;
    private const float HoldSeconds = 0.30f;     // keeps word endings and short pauses intact
    private const int FloorBlock = Mixer.SampleRate / 100;

    private static readonly float Attack = Coefficient(0.001f);
    private static readonly float Release = Coefficient(0.120f);
    private static readonly float Level = Coefficient(0.005f);  // loudness follower: reacts within a few ms
    private static readonly int Hold = (int)(HoldSeconds * Mixer.SampleRate);

    private float _floorDb = float.NaN;
    private float _energy;
    private double _blockEnergy;
    private int _blockCount;
    private float _openAt = LowestThresholdDb, _closeAt = LowestThresholdDb - 4f;
    private float _gain = 1f;
    private int _hold;
    private bool _open = true;
    private float _closed = Db.ToGain(-DefaultReductionDb);

    public bool IsOpen => _open;

    // 0 = off. Set from any thread; takes effect on the next chunk.
    public float ReductionDb
    {
        get => -Db.FromGain(Volatile.Read(ref _closed));
        set => Volatile.Write(ref _closed, value <= 0 ? 1f : Db.ToGain(-Math.Min(value, MaxReductionDb)));
    }

    public float NoiseFloorDb => float.IsNaN(_floorDb) ? Db.Floor : _floorDb;

    private static float Coefficient(float seconds) => 1f - MathF.Exp(-1f / (seconds * Mixer.SampleRate));

    public void Reset()
    {
        _floorDb = float.NaN;
        _energy = 0;
        _blockEnergy = 0;
        _blockCount = 0;
        _openAt = LowestThresholdDb;
        _closeAt = LowestThresholdDb - 4f;
        _gain = 1f;
        _hold = 0;
        _open = true;
    }

    public void Process(Span<float> mic)
    {
        // Thresholds in energy terms, so the per-sample loop needs no logarithms.
        float openAt = DbToEnergy(_openAt), closeAt = DbToEnergy(_closeAt);
        float closed = Volatile.Read(ref _closed);

        for (int i = 0; i < mic.Length; i++)
        {
            float x = mic[i];
            float e = x * x;
            _energy += (e - _energy) * Level;

            if (_energy >= openAt)
            {
                _open = true;
                _hold = Hold;
            }
            else if (_open && _energy < closeAt && --_hold <= 0)
            {
                _open = false;
            }

            float target = _open ? 1f : closed;
            _gain += (target - _gain) * (target > _gain ? Attack : Release);
            mic[i] = x * _gain;

            _blockEnergy += e;
            if (++_blockCount == FloorBlock)
            {
                UpdateFloor(Db.FromGain((float)Math.Sqrt(_blockEnergy / FloorBlock)));
                openAt = DbToEnergy(_openAt);
                closeAt = DbToEnergy(_closeAt);
                _blockEnergy = 0;
                _blockCount = 0;
            }
        }
    }

    // Follows the quietest 10 ms blocks: drops quickly to a quieter block, creeps up slowly otherwise.
    private void UpdateFloor(float blockDb)
    {
        if (float.IsNaN(_floorDb)) _floorDb = blockDb;
        else if (blockDb < _floorDb) _floorDb += (blockDb - _floorDb) * 0.3f;
        else _floorDb += MathF.Min(blockDb - _floorDb, FloorRiseDbPerSecond * FloorBlock / Mixer.SampleRate);
        _floorDb = Math.Clamp(_floorDb, Db.Floor, -20f);

        _openAt = MathF.Max(_floorDb + OpenAboveFloorDb, LowestThresholdDb);
        _closeAt = MathF.Max(_floorDb + CloseAboveFloorDb, LowestThresholdDb - 4f);
    }

    private static float DbToEnergy(float db) => MathF.Pow(10f, db / 10f);
}
