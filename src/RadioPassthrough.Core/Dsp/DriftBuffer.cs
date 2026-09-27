namespace RadioPassthrough.Core.Dsp;

// Carries audio from a capture clock to the output clock. The two devices never run at exactly the
// same rate, so instead of dropping or repeating samples (audible clicks) the read side resamples by
// a tiny ratio (at most ±0.4 %) that keeps the fill level near its target.
public sealed class DriftBuffer
{
    private const int Capacity = 1 << 16;
    private const int Mask = Capacity - 1;
    private const double MaxRatioOffset = 0.004;
    private const double ControlGain = 0.004;
    private const double ErrorSmoothing = 0.02;

    private readonly float[] _ring = new float[Capacity];
    private readonly object _lock = new();
    private readonly int _target;
    private long _written;
    private long _readIndex;
    private double _frac;
    private double _smoothedError;
    private bool _primed;

    public DriftBuffer(int targetSamples)
    {
        if (targetSamples <= 0 || targetSamples > Capacity / 8)
            throw new ArgumentOutOfRangeException(nameof(targetSamples));
        _target = targetSamples;
    }

    public int TargetSamples => _target;

    public double Ratio { get; private set; } = 1.0;

    public int Underruns { get; private set; }

    public double Fill
    {
        get { lock (_lock) return _written - _readIndex - _frac; }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (float s in samples)
                _ring[(_written++) & Mask] = s;

            if (_written - _readIndex > Capacity - 8)
                JumpToTarget();
        }
    }

    public void WriteSilence(int count)
    {
        lock (_lock)
        {
            for (int i = 0; i < count; i++)
                _ring[(_written++) & Mask] = 0f;

            if (_written - _readIndex > Capacity - 8)
                JumpToTarget();
        }
    }

    // Always fills the whole destination; returns how many samples came from real data.
    public int Read(Span<float> destination)
    {
        lock (_lock)
        {
            double fill = _written - _readIndex - _frac;
            if (!_primed)
            {
                if (fill < _target + 2)
                {
                    destination.Clear();
                    return 0;
                }
                _primed = true;
            }

            if (fill > _target * 4 + destination.Length)
            {
                JumpToTarget();
                fill = _written - _readIndex - _frac;
            }

            double error = (fill - _target) / _target;
            _smoothedError += ErrorSmoothing * (error - _smoothedError);
            Ratio = 1.0 + Math.Clamp(_smoothedError * ControlGain, -MaxRatioOffset, MaxRatioOffset);

            for (int i = 0; i < destination.Length; i++)
            {
                if (_readIndex + 2 >= _written)
                {
                    destination[i..].Clear();
                    _primed = false;
                    Underruns++;
                    return i;
                }

                destination[i] = Hermite(At(_readIndex - 1), At(_readIndex), At(_readIndex + 1), At(_readIndex + 2), (float)_frac);
                _frac += Ratio;
                while (_frac >= 1.0)
                {
                    _frac -= 1.0;
                    _readIndex++;
                }
            }
            return destination.Length;
        }
    }

    public void Reset()
    {
        lock (_lock)
        {
            _written = _readIndex = 0;
            _frac = _smoothedError = 0;
            _primed = false;
            Ratio = 1.0;
            Array.Clear(_ring);
        }
    }

    private void JumpToTarget()
    {
        _readIndex = _written - _target - 2;
        _frac = 0;
        _smoothedError = 0;
    }

    private float At(long index) => index < 0 ? 0f : _ring[index & Mask];

    private static float Hermite(float xm1, float x0, float x1, float x2, float t)
    {
        float c1 = 0.5f * (x1 - xm1);
        float c2 = xm1 - 2.5f * x0 + 2f * x1 - 0.5f * x2;
        float c3 = 0.5f * (x2 - xm1) + 1.5f * (x0 - x1);
        return ((c3 * t + c2) * t + c1) * t + x0;
    }
}
