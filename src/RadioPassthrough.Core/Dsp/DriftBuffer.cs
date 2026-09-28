namespace RadioPassthrough.Core.Dsp;

// Carries audio from a capture clock to the output clock. The two devices never run at exactly the
// same rate, so instead of dropping or repeating samples (audible clicks) the read side resamples by
// a tiny ratio (at most ±0.4 %) that keeps the fill level near its target. Audio is interleaved; all
// channels share one read position, so left and right always stay exactly aligned.
public sealed class DriftBuffer
{
    private const int Capacity = 1 << 16; // frames
    private const int Mask = Capacity - 1;
    private const double MaxRatioOffset = 0.004;
    private const double ControlGain = 0.004;
    private const double ErrorSmoothing = 0.02;

    private readonly int _channels;
    private readonly float[] _ring;
    private readonly object _lock = new();
    private readonly int _target;
    private long _written;
    private long _readIndex;
    private double _frac;
    private double _smoothedError;
    private bool _primed;

    public DriftBuffer(int targetFrames, int channels = 1)
    {
        if (targetFrames <= 0 || targetFrames > Capacity / 8)
            throw new ArgumentOutOfRangeException(nameof(targetFrames));
        if (channels < 1) throw new ArgumentOutOfRangeException(nameof(channels));
        _target = targetFrames;
        _channels = channels;
        _ring = new float[Capacity * channels];
    }

    public int Channels => _channels;

    public double Ratio { get; private set; } = 1.0;

    public int Underruns { get; private set; }

    // In frames.
    public double Fill
    {
        get { lock (_lock) return _written - _readIndex - _frac; }
    }

    public void Write(ReadOnlySpan<float> interleaved)
    {
        int frames = interleaved.Length / _channels;
        lock (_lock)
        {
            for (int f = 0; f < frames; f++)
            {
                int slot = (int)(_written & Mask) * _channels;
                for (int c = 0; c < _channels; c++)
                    _ring[slot + c] = interleaved[f * _channels + c];
                _written++;
            }

            if (_written - _readIndex > Capacity - 8)
                JumpToTarget();
        }
    }

    // Always fills the whole destination; returns how many samples came from real data.
    public int Read(Span<float> destination)
    {
        int frames = destination.Length / _channels;
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

            if (fill > _target * 4 + frames)
            {
                JumpToTarget();
                fill = _written - _readIndex - _frac;
            }

            double error = (fill - _target) / _target;
            _smoothedError += ErrorSmoothing * (error - _smoothedError);
            Ratio = 1.0 + Math.Clamp(_smoothedError * ControlGain, -MaxRatioOffset, MaxRatioOffset);

            for (int i = 0; i < frames; i++)
            {
                if (_readIndex + 2 >= _written)
                {
                    destination[(i * _channels)..].Clear();
                    _primed = false;
                    Underruns++;
                    return i * _channels;
                }

                float t = (float)_frac;
                for (int c = 0; c < _channels; c++)
                    destination[i * _channels + c] = Hermite(At(_readIndex - 1, c), At(_readIndex, c), At(_readIndex + 1, c), At(_readIndex + 2, c), t);
                _frac += Ratio;
                while (_frac >= 1.0)
                {
                    _frac -= 1.0;
                    _readIndex++;
                }
            }
            return frames * _channels;
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

    private float At(long index, int channel) => index < 0 ? 0f : _ring[(int)(index & Mask) * _channels + channel];

    private static float Hermite(float xm1, float x0, float x1, float x2, float t)
    {
        float c1 = 0.5f * (x1 - xm1);
        float c2 = xm1 - 2.5f * x0 + 2f * x1 - 0.5f * x2;
        float c3 = 0.5f * (x2 - xm1) + 1.5f * (x0 - x1);
        return ((c3 * t + c2) * t + c1) * t + x0;
    }
}
