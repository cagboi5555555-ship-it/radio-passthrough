namespace RadioPassthrough.Core.Dsp;

// One test recording kept as separate stems (raw mic, raw game, radio key state per block) so the
// same moment can be re-rendered with any mix and compared.
public sealed class Take
{
    public Take(float[] mic, float[] game, bool[] gate, int blockSize)
    {
        Mic = mic;
        Game = game;
        Gate = gate;
        BlockSize = blockSize;
    }

    public float[] Mic { get; }
    public float[] Game { get; }
    public bool[] Gate { get; } // one entry per block
    public int BlockSize { get; }
    public int Length => Mic.Length;
    public TimeSpan Duration => TimeSpan.FromSeconds((double)Length / Mixer.SampleRate);

    public float[] Render(MixSettings settings)
    {
        var mixer = new Mixer { Settings = settings };
        var output = new float[Length];
        for (int b = 0, offset = 0; offset < Length; b++, offset += BlockSize)
        {
            int n = Math.Min(BlockSize, Length - offset);
            mixer.Process(Mic.AsSpan(offset, n), Game.AsSpan(offset, n), Gate[b], output.AsSpan(offset, n));
        }
        return output;
    }

    public float[] PlainSum()
    {
        var output = new float[Length];
        float gate = 0;
        const float step = 1f / (0.010f * Mixer.SampleRate);
        for (int b = 0, offset = 0; offset < Length; b++, offset += BlockSize)
        {
            int n = Math.Min(BlockSize, Length - offset);
            float target = Gate[b] ? 1f : 0f;
            for (int i = offset; i < offset + n; i++)
            {
                gate = gate < target ? MathF.Min(target, gate + step) : MathF.Max(target, gate - step);
                output[i] = Mic[i] + Game[i] * gate;
            }
        }
        return output;
    }

    public IReadOnlyList<(double Start, double End)> RadioSpans()
    {
        var spans = new List<(double, double)>();
        int? start = null;
        for (int b = 0; b <= Gate.Length; b++)
        {
            bool on = b < Gate.Length && Gate[b];
            if (on && start is null) start = b;
            if (!on && start is { } s)
            {
                spans.Add(((double)s / Gate.Length, (double)b / Gate.Length));
                start = null;
            }
        }
        return spans;
    }
}

public sealed class TakeRecorder
{
    private readonly float[] _mic;
    private readonly float[] _game;
    private readonly List<bool> _gate = new();
    private readonly object _lock = new();
    private int _position;
    private int _blockSize;

    public TakeRecorder(TimeSpan duration)
    {
        int length = (int)(duration.TotalSeconds * Mixer.SampleRate);
        _mic = new float[length];
        _game = new float[length];
    }

    public bool IsFull
    {
        get { lock (_lock) return _position >= _mic.Length; }
    }

    public double Progress
    {
        get { lock (_lock) return (double)_position / _mic.Length; }
    }

    public void Append(ReadOnlySpan<float> mic, ReadOnlySpan<float> game, bool gateOpen)
    {
        lock (_lock)
        {
            int n = Math.Min(mic.Length, _mic.Length - _position);
            if (n <= 0) return;
            if (_blockSize == 0) _blockSize = mic.Length;
            // Blocks are fixed size so gate state maps 1:1 onto render blocks.
            for (int offset = 0; offset < n; offset += _blockSize)
            {
                int m = Math.Min(_blockSize, n - offset);
                mic.Slice(offset, m).CopyTo(_mic.AsSpan(_position));
                game.Slice(offset, m).CopyTo(_game.AsSpan(_position));
                _position += m;
                _gate.Add(gateOpen);
            }
        }
    }

    public Take ToTake()
    {
        lock (_lock)
        {
            int length = _position;
            return new Take(_mic[..length], _game[..length], _gate.ToArray(), Math.Max(1, _blockSize));
        }
    }
}
