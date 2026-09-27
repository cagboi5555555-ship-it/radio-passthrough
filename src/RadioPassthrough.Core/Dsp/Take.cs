namespace RadioPassthrough.Core.Dsp;

// One test recording kept as separate stems (raw mic, raw game) plus the exact chunks the live mixer
// processed and whether the radio key was held for each, so the same moment can be re-rendered with any
// mix. Rendering with the mix that was live reproduces what was sent, sample for sample.
public sealed class Take
{
    // Evenly sized blocks: `gate` has one entry per block of `blockSize` samples (the last may be shorter).
    public Take(float[] mic, float[] game, bool[] gate, int blockSize)
        : this(mic, game, EvenChunks(mic.Length, blockSize), gate)
    {
    }

    public Take(float[] mic, float[] game, int[] chunkLengths, bool[] gate)
    {
        if (game.Length != mic.Length) throw new ArgumentException("Mic and game must be the same length.");
        if (chunkLengths.Length != gate.Length) throw new ArgumentException("One radio key state is needed per chunk.");
        if (chunkLengths.Sum() != mic.Length) throw new ArgumentException("Chunks must cover the whole take.");
        Mic = mic;
        Game = game;
        ChunkLengths = chunkLengths;
        Gate = gate;
    }

    public float[] Mic { get; }
    public float[] Game { get; }
    public int[] ChunkLengths { get; }
    public bool[] Gate { get; } // one entry per chunk
    public int Length => Mic.Length;

    public IEnumerable<(int Offset, int Length, bool Radio)> Chunks()
    {
        for (int c = 0, offset = 0; c < ChunkLengths.Length; offset += ChunkLengths[c], c++)
            yield return (offset, ChunkLengths[c], Gate[c]);
    }

    public float[] Render(MixSettings settings)
    {
        var mixer = new Mixer { Settings = settings };
        var output = new float[Length];
        foreach (var (offset, n, radio) in Chunks())
            mixer.Process(Mic.AsSpan(offset, n), Game.AsSpan(offset, n), radio, output.AsSpan(offset, n));
        return output;
    }

    public float[] PlainSum()
    {
        var output = new float[Length];
        float gate = 0;
        const float step = 1f / (0.010f * Mixer.SampleRate);
        foreach (var (offset, n, radio) in Chunks())
        {
            float target = radio ? 1f : 0f;
            for (int i = offset; i < offset + n; i++)
            {
                gate = gate < target ? MathF.Min(target, gate + step) : MathF.Max(target, gate - step);
                output[i] = Mic[i] + Game[i] * gate;
            }
        }
        return output;
    }

    // Where the radio key was held, as fractions of the take.
    public IReadOnlyList<(double Start, double End)> RadioSpans()
    {
        var spans = new List<(double, double)>();
        int? start = null;
        foreach (var (offset, _, radio) in Chunks().Append((Length, 0, false)))
        {
            if (radio && start is null) start = offset;
            if (!radio && start is { } s)
            {
                spans.Add(((double)s / Length, (double)offset / Length));
                start = null;
            }
        }
        return spans;
    }

    private static int[] EvenChunks(int length, int blockSize)
    {
        if (blockSize <= 0) throw new ArgumentOutOfRangeException(nameof(blockSize));
        var chunks = new int[(length + blockSize - 1) / blockSize];
        for (int c = 0; c < chunks.Length; c++) chunks[c] = Math.Min(blockSize, length - c * blockSize);
        return chunks;
    }
}

public sealed class TakeRecorder
{
    private readonly float[] _mic;
    private readonly float[] _game;
    private readonly List<int> _chunks = new();
    private readonly List<bool> _gate = new();
    private readonly object _lock = new();
    private int _position;

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

    // Called from the audio thread with each chunk exactly as the mixer processed it.
    public void Append(ReadOnlySpan<float> mic, ReadOnlySpan<float> game, bool gateOpen)
    {
        lock (_lock)
        {
            int n = Math.Min(mic.Length, _mic.Length - _position);
            if (n <= 0) return;
            mic[..n].CopyTo(_mic.AsSpan(_position));
            game[..n].CopyTo(_game.AsSpan(_position));
            _position += n;
            _chunks.Add(n);
            _gate.Add(gateOpen);
        }
    }

    public Take ToTake()
    {
        lock (_lock)
        {
            int length = _position;
            return new Take(_mic[..length], _game[..length], _chunks.ToArray(), _gate.ToArray());
        }
    }
}
