using System.Runtime.InteropServices;
using RadioPassthrough.Core.Diagnostics;

namespace RadioPassthrough.Core.Ptt;

// Watches the radio keys by reading the keyboard/mouse state about 200 times a second. Unlike a global
// hook it can't be silently dropped by Windows, never delays anyone's input, and still sees keys while a
// game has focus. It only ever looks at the keys you bound (plus Shift/Ctrl/Alt).
public sealed partial class KeyPoller : IDisposable
{
    public const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkEscape = 0x1B;
    private const int VkMButton = 0x04, VkXButton1 = 0x05, VkXButton2 = 0x06;

    private readonly PttState _state;
    private readonly Func<bool> _gameFocused;
    private readonly Func<int, bool> _isDown;
    private readonly object _lock = new();
    private readonly Dictionary<(TriggerKind Kind, int Code), bool> _previous = new();
    private (TriggerKind Kind, int Code)[] _triggers;
    private Action<PttBinding?>? _capture;
    private bool[]? _captureBaseline;
    private Thread? _thread;
    private volatile bool _running;

    public KeyPoller(IReadOnlyList<PttBinding> bindings, Func<bool> gameFocused, Func<int, bool>? isDown = null)
    {
        _triggers = Triggers(bindings);
        _state = new PttState(bindings);
        _gameFocused = gameFocused;
        _isDown = isDown ?? (vk => (GetAsyncKeyState(vk) & 0x8000) != 0);
    }

    // Raised on the polling thread; keep handlers cheap.
    public event Action<bool>? RadioKeyChanged;

    public bool IsRadioKeyHeld
    {
        get { lock (_lock) return _state.IsOpen; }
    }

    private static (TriggerKind, int)[] Triggers(IReadOnlyList<PttBinding> bindings) =>
        bindings.Select(b => (b.Kind, b.Code)).Distinct().ToArray();

    public static int VirtualKey(TriggerKind kind, int code) => kind switch
    {
        TriggerKind.Mouse => code switch
        {
            MouseButtons.Middle => VkMButton,
            MouseButtons.X1 => VkXButton1,
            MouseButtons.X2 => VkXButton2,
            _ => 0,
        },
        _ => code,
    };

    public void Start()
    {
        if (_thread is not null) return;
        _running = true;
        _thread = new Thread(Run) { IsBackground = true, Name = "Radio key poller", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void SetBindings(IReadOnlyList<PttBinding> bindings)
    {
        bool wasOpen;
        lock (_lock)
        {
            wasOpen = _state.IsOpen;
            _triggers = Triggers(bindings);
            _state.SetBindings(bindings);
            _previous.Clear();
        }
        if (wasOpen) RadioKeyChanged?.Invoke(false);
    }

    // The next key or extra mouse button pressed (with its modifiers) is reported instead of being used
    // as a radio key. Escape cancels and reports null.
    public void CaptureNext(Action<PttBinding?> onCaptured)
    {
        lock (_lock)
        {
            _capture = onCaptured;
            _captureBaseline = null;
        }
    }

    public void CancelCapture()
    {
        Action<PttBinding?>? capture;
        lock (_lock)
        {
            capture = _capture;
            _capture = null;
        }
        capture?.Invoke(null);
    }

    private void Run()
    {
        using var timer = HighResolutionTimer.TryCreate();
        while (_running)
        {
            try
            {
                Poll();
            }
            catch (Exception e)
            {
                Log.Error("Radio key poll failed", e);
            }
            if (timer is not null) timer.Wait(5);
            else Thread.Sleep(5);
        }
    }

    // One pass over the bound keys. Public for tests.
    public void Poll()
    {
        Action<PttBinding?>? captured = null;
        PttBinding? capturedBinding = null;
        bool changed = false, open = false;

        lock (_lock)
        {
            if (_capture is not null)
            {
                if (PollCapture(out capturedBinding))
                {
                    captured = _capture;
                    _capture = null;
                }
            }
            else
            {
                foreach (var trigger in _triggers)
                {
                    int vk = VirtualKey(trigger.Kind, trigger.Code);
                    if (vk == 0) continue;
                    bool down = _isDown(vk);
                    bool was = _previous.GetValueOrDefault(trigger);
                    if (down == was) continue;
                    _previous[trigger] = down;
                    changed |= down
                        ? _state.OnTrigger(trigger.Kind, trigger.Code, true, HeldModifiers(), _gameFocused())
                        : _state.OnTrigger(trigger.Kind, trigger.Code, false, Modifiers.None, true);
                }
                // Alt-tabbing (or Arma exiting) with a radio key still held must not leave the radio open.
                if (_state.IsOpen && !_gameFocused())
                {
                    _state.Clear();
                    changed = true;
                }
                open = _state.IsOpen;
            }
        }

        if (captured is not null) ThreadPool.QueueUserWorkItem(_ => captured(capturedBinding));
        else if (changed) RadioKeyChanged?.Invoke(open);
    }

    private bool PollCapture(out PttBinding? binding)
    {
        binding = null;
        var now = new bool[256];
        for (int vk = 3; vk < 255; vk++) now[vk] = _isDown(vk);

        if (_captureBaseline is null)
        {
            _captureBaseline = now; // keys already held when capture started don't count
            return false;
        }

        for (int vk = 3; vk < 255; vk++)
        {
            bool pressed = now[vk] && !_captureBaseline[vk];
            _captureBaseline[vk] = now[vk];
            if (!pressed || IsModifier(vk)) continue;

            if (vk == VkEscape) return true;
            binding = vk switch
            {
                VkMButton => new PttBinding(TriggerKind.Mouse, MouseButtons.Middle, HeldModifiers()),
                VkXButton1 => new PttBinding(TriggerKind.Mouse, MouseButtons.X1, HeldModifiers()),
                VkXButton2 => new PttBinding(TriggerKind.Mouse, MouseButtons.X2, HeldModifiers()),
                _ => new PttBinding(TriggerKind.Key, vk, HeldModifiers()),
            };
            return true;
        }
        return false;
    }

    private static bool IsModifier(int vk) => vk is VkShift or VkControl or VkMenu or >= 0xA0 and <= 0xA5 or 0x5B or 0x5C;

    private Modifiers HeldModifiers()
    {
        var m = Modifiers.None;
        if (_isDown(VkShift)) m |= Modifiers.Shift;
        if (_isDown(VkControl)) m |= Modifiers.Ctrl;
        if (_isDown(VkMenu)) m |= Modifiers.Alt;
        return m;
    }

    public void Dispose()
    {
        _running = false;
        _thread?.Join(TimeSpan.FromSeconds(1));
        _thread = null;
    }

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);

    // Waitable timer with 1 ms precision (Windows 10 1803+), so polling every 5 ms doesn't need the
    // system-wide timer resolution raised.
    private sealed partial class HighResolutionTimer : IDisposable
    {
        private const uint CreateWaitableTimerHighResolution = 0x2;
        private const uint TimerAllAccess = 0x1F0003;
        private readonly IntPtr _handle;

        private HighResolutionTimer(IntPtr handle) => _handle = handle;

        public static HighResolutionTimer? TryCreate()
        {
            IntPtr h = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
            return h == IntPtr.Zero ? null : new HighResolutionTimer(h);
        }

        public void Wait(int milliseconds)
        {
            long due = -milliseconds * 10_000L;
            if (SetWaitableTimer(_handle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                WaitForSingleObject(_handle, (uint)milliseconds + 50);
            else
                Thread.Sleep(milliseconds);
        }

        public void Dispose() => CloseHandle(_handle);

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool SetWaitableTimer(IntPtr timer, ref long dueTime, int period, IntPtr completion, IntPtr arg, [MarshalAs(UnmanagedType.Bool)] bool resume);

        [LibraryImport("kernel32.dll")]
        private static partial uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool CloseHandle(IntPtr handle);
    }
}
