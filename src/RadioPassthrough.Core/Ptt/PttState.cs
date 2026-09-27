namespace RadioPassthrough.Core.Ptt;

// Decides whether a radio key is held. A binding matches only when the held modifiers are exactly its
// modifiers (as CBA keybinds do), and the key stays active until its trigger is released.
public sealed class PttState
{
    private readonly HashSet<(TriggerKind, int)> _active = new();
    private IReadOnlyList<PttBinding> _bindings;

    public PttState(IReadOnlyList<PttBinding> bindings) => _bindings = bindings;

    public bool IsOpen => _active.Count > 0;

    public void SetBindings(IReadOnlyList<PttBinding> bindings)
    {
        _bindings = bindings;
        _active.Clear();
    }

    public void Clear() => _active.Clear();

    // Returns true when the open/closed state changed.
    public bool OnTrigger(TriggerKind kind, int code, bool down, Modifiers held, bool gameFocused)
    {
        bool before = IsOpen;
        var key = (kind, code);
        if (down)
        {
            if (_active.Contains(key)) return false; // key repeat
            if (!gameFocused) return false;
            foreach (var b in _bindings)
            {
                if (b.Kind == kind && b.Code == code && b.Modifiers == held)
                {
                    _active.Add(key);
                    break;
                }
            }
        }
        else
        {
            _active.Remove(key);
        }
        return before != IsOpen;
    }
}
