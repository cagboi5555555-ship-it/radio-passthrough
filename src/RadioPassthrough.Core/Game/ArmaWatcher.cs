namespace RadioPassthrough.Core.Game;

public sealed record GameProcess(int Pid, string Name, bool Elevated);

// Polls for Arma 3 and reports when it starts or exits.
public sealed class ArmaWatcher : IDisposable
{
    private static readonly string[] ExeNames = ["arma3_x64.exe", "arma3.exe"];
    private readonly Timer _timer;
    private GameProcess? _current;

    public ArmaWatcher() => _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);

    public event Action<GameProcess?>? Changed;

    public GameProcess? Current => Volatile.Read(ref _current);

    public int CurrentPid => Current?.Pid ?? 0;

    public void Start()
    {
        Poll();
        _timer.Change(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    // Only process names are listed here. The game itself is looked at once per launch (to see whether it
    // runs as administrator), never on every poll.
    private void Poll()
    {
        int? pid = ProcessInfo.FindProcess(ExeNames);
        var previous = Current;
        if (previous?.Pid == pid) return;
        var found = pid is { } id ? new GameProcess(id, "Arma 3", ProcessInfo.IsElevated(id)) : null;
        Volatile.Write(ref _current, found);
        Changed?.Invoke(found);
    }

    public void Dispose() => _timer.Dispose();
}
