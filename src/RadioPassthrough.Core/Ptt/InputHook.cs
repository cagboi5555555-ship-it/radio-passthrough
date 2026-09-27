using System.Runtime.InteropServices;

namespace RadioPassthrough.Core.Ptt;

// Passive global keyboard and mouse hooks on their own thread. Events are only observed, never
// blocked or changed, so the game and every other app still get every key.
public sealed partial class InputHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;
    private const int WmMButtonDown = 0x0207, WmMButtonUp = 0x0208, WmXButtonDown = 0x020B, WmXButtonUp = 0x020C;
    private const int WmQuit = 0x0012;
    private const int VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12, VkEscape = 0x1B;

    private readonly PttState _state;
    private readonly Func<bool> _gameFocused;
    private readonly object _lock = new();
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _keyboardHook, _mouseHook;
    private Action<PttBinding?>? _capture;

    public InputHook(IReadOnlyList<PttBinding> bindings, Func<bool> gameFocused)
    {
        _state = new PttState(bindings);
        _gameFocused = gameFocused;
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    // Raised on the hook thread; keep handlers cheap.
    public event Action<bool>? RadioKeyChanged;

    public bool IsRadioKeyHeld
    {
        get { lock (_lock) return _state.IsOpen; }
    }

    public void Start()
    {
        if (_thread is not null) return;
        using var ready = new ManualResetEventSlim();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Radio key hook", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    public void SetBindings(IReadOnlyList<PttBinding> bindings)
    {
        bool changed;
        lock (_lock)
        {
            changed = _state.IsOpen;
            _state.SetBindings(bindings);
        }
        if (changed) RadioKeyChanged?.Invoke(false);
    }

    // The next key or extra mouse button pressed (with its modifiers) is reported instead of being
    // treated as a radio key. Escape cancels and reports null.
    public void CaptureNext(Action<PttBinding?> onCaptured)
    {
        lock (_lock) _capture = onCaptured;
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

    private void Run(ManualResetEventSlim ready)
    {
        _threadId = GetCurrentThreadId();
        IntPtr module = GetModuleHandleW(null);
        _keyboardHook = SetWindowsHookExW(WhKeyboardLl, _keyboardProc, module, 0);
        _mouseHook = SetWindowsHookExW(WhMouseLl, _mouseProc, module, 0);
        ready.Set();

        while (GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessageW(ref msg);
        }

        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
    }

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            bool down = msg is WmKeyDown or WmSysKeyDown;
            bool up = msg is WmKeyUp or WmSysKeyUp;
            if ((down || up) && !IsModifier((int)data.VkCode))
                Handle(TriggerKind.Key, (int)data.VkCode, down);
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WmMButtonDown or WmMButtonUp or WmXButtonDown or WmXButtonUp)
            {
                var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
                int button = msg is WmMButtonDown or WmMButtonUp
                    ? MouseButtons.Middle
                    : ((data.MouseData >> 16) & 0xFFFF) == 1 ? MouseButtons.X1 : MouseButtons.X2;
                Handle(TriggerKind.Mouse, button, msg is WmMButtonDown or WmXButtonDown);
            }
        }
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private void Handle(TriggerKind kind, int code, bool down)
    {
        Action<PttBinding?>? captured = null;
        PttBinding? binding = null;
        bool changed;
        bool open;

        lock (_lock)
        {
            if (_capture is not null)
            {
                if (!down) return;
                captured = _capture;
                _capture = null;
                binding = kind == TriggerKind.Key && code == VkEscape ? null : new PttBinding(kind, code, HeldModifiers());
                changed = false;
                open = _state.IsOpen;
            }
            else
            {
                bool focused = !down || _gameFocused();
                changed = _state.OnTrigger(kind, code, down, down ? HeldModifiers() : Modifiers.None, focused);
                open = _state.IsOpen;
            }
        }

        if (captured is not null)
            ThreadPool.QueueUserWorkItem(_ => captured(binding));
        else if (changed)
            RadioKeyChanged?.Invoke(open);
    }

    private static bool IsModifier(int vk) => vk is >= 0xA0 and <= 0xA5 or VkShift or VkControl or VkMenu or 0x5B or 0x5C;

    private static Modifiers HeldModifiers()
    {
        var m = Modifiers.None;
        if ((GetAsyncKeyState(VkShift) & 0x8000) != 0) m |= Modifiers.Shift;
        if ((GetAsyncKeyState(VkControl) & 0x8000) != 0) m |= Modifiers.Ctrl;
        if ((GetAsyncKeyState(VkMenu) & 0x8000) != 0) m |= Modifiers.Alt;
        return m;
    }

    public void Dispose()
    {
        if (_thread is null) return;
        PostThreadMessageW(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookExW(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UnhookWindowsHookEx(IntPtr hhk);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr GetModuleHandleW(string? lpModuleName);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessageW(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessageW(ref Msg lpMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostThreadMessageW(uint idThread, int msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial short GetAsyncKeyState(int vKey);
}
