using System.Diagnostics;

namespace RadioPassthrough.Core.Game;

public sealed record GameProcess(int Pid, string Name, bool Elevated);

// Polls for Arma 3 and reports when it starts or exits.
public sealed class ArmaWatcher : IDisposable
{
    private static readonly string[] ProcessNames = ["arma3_x64", "arma3"];
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

    private void Poll()
    {
        GameProcess? found = null;
        foreach (string name in ProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var p in processes)
            {
                if (found is null)
                    found = new GameProcess(p.Id, "Arma 3", ProcessInfo.IsElevated(p.Id));
                p.Dispose();
            }
            if (found is not null) break;
        }

        var previous = Current;
        if (previous?.Pid == found?.Pid) return;
        Volatile.Write(ref _current, found);
        Changed?.Invoke(found);
    }

    public void Dispose() => _timer.Dispose();
}
