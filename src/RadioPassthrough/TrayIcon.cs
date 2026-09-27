using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using RadioPassthrough.Core.Diagnostics;

namespace RadioPassthrough;

// Notification-area icon built on Shell_NotifyIcon directly, with a WPF menu styled like the rest of
// the app. Comes back by itself if Explorer restarts.
public sealed partial class TrayIcon : IDisposable
{
    private const int CallbackMessage = 0x8000 + 1; // WM_APP + 1
    private const int NimAdd = 0, NimModify = 1, NimDelete = 2, NimSetVersion = 4;
    private const int NifMessage = 0x1, NifIcon = 0x2, NifTip = 0x4, NifInfo = 0x10, NifShowTip = 0x80;
    private const int NiifNoSound = 0x10, NiifUser = 0x4;
    private const int NotifyIconVersion4 = 4;
    private const int WmLButtonUp = 0x0202, WmRButtonUp = 0x0205, WmContextMenu = 0x007B, NinSelect = 0x0400, NinKeySelect = 0x0401;

    private readonly HwndSource _window;
    private readonly ContextMenu _menu;
    private readonly Action _open;
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private string _tooltip = "Radio Passthrough";
    private bool _added;

    public TrayIcon(Action open, ContextMenu menu)
    {
        _open = open;
        _menu = menu;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        var parameters = new HwndSourceParameters("RadioPassthroughTray")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, never shown
            ExtendedWindowStyle = 0x00000080,         // WS_EX_TOOLWINDOW: no taskbar button
        };
        _window = new HwndSource(parameters);
        _window.AddHook(WndProc);
        _icon = LoadTrayIcon();
        Add();
    }

    public bool IsAdded => _added;

    public void SetTooltip(string text)
    {
        _tooltip = text.Length > 127 ? text[..127] : text;
        var data = NewData(NifTip | NifShowTip);
        Notify(NimModify, ref data);
    }

    public void ShowMessage(string title, string text)
    {
        var data = NewData(NifInfo);
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = text.Length > 255 ? text[..255] : text;
        data.dwInfoFlags = NiifUser | NiifNoSound;
        data.hBalloonIcon = _icon;
        Notify(NimModify, ref data);
    }

    private void Add()
    {
        var data = NewData(NifMessage | NifIcon | NifTip | NifShowTip);
        _added = Notify(NimAdd, ref data);
        data.uVersion = NotifyIconVersion4;
        Notify(NimSetVersion, ref data);
        if (!_added) Log.Warn("Couldn't add the tray icon.");
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == CallbackMessage)
        {
            int ev = (int)(lParam.ToInt64() & 0xFFFF);
            switch (ev)
            {
                case WmLButtonUp or NinSelect or NinKeySelect:
                    _open();
                    handled = true;
                    break;
                case WmContextMenu or WmRButtonUp:
                    ShowMenu();
                    handled = true;
                    break;
            }
        }
        else if (msg == _taskbarCreated)
        {
            Add(); // Explorer restarted and dropped every icon.
        }
        return IntPtr.Zero;
    }

    private void ShowMenu()
    {
        SetForegroundWindow(_window.Handle); // lets the menu close when you click elsewhere
        _menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        _menu.IsOpen = true;
    }

    private NotifyIconData NewData(int flags) => new()
    {
        cbSize = Marshal.SizeOf<NotifyIconData>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = flags,
        uCallbackMessage = CallbackMessage,
        hIcon = _icon,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = "",
    };

    private static bool Notify(int message, ref NotifyIconData data) => Shell_NotifyIconW(message, ref data);

    // Picks the .ico frame that matches the tray's size at this DPI, so the icon is sharp.
    private static IntPtr LoadTrayIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
            if (resource is null) return IntPtr.Zero;
            using var stream = resource.Stream;
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            byte[] ico = ms.ToArray();

            int size = GetSystemMetricsForDpi(49 /* SM_CXSMICON */, GetDpiForSystem());
            int count = BitConverter.ToUInt16(ico, 4);
            // Smallest frame at least as big as the tray wants; otherwise the biggest there is.
            int best = -1, bestWidth = 0;
            for (int i = 0; i < count; i++)
            {
                int w = ico[6 + i * 16];
                if (w == 0) w = 256;
                bool better = best < 0
                              || (bestWidth < size && w > bestWidth)
                              || (w >= size && w < bestWidth);
                if (better) { best = i; bestWidth = w; }
            }
            if (best < 0) return IntPtr.Zero;
            int bytes = BitConverter.ToInt32(ico, 6 + best * 16 + 8);
            int offset = BitConverter.ToInt32(ico, 6 + best * 16 + 12);

            IntPtr buffer = Marshal.AllocHGlobal(bytes);
            try
            {
                Marshal.Copy(ico, offset, buffer, bytes);
                return CreateIconFromResourceEx(buffer, (uint)bytes, true, 0x00030000, size, size, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception e)
        {
            Log.Warn($"Tray icon image failed to load: {e.Message}");
            return IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData(0);
            Notify(NimDelete, ref data);
            _added = false;
        }
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
        _window.Dispose();
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIconW(int message, ref NotifyIconData data);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterWindowMessageW(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr CreateIconFromResourceEx(IntPtr data, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int cx, int cy, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyIcon(IntPtr icon);

    [LibraryImport("user32.dll")]
    private static partial int GetSystemMetricsForDpi(int index, uint dpi);

    [LibraryImport("user32.dll")]
    private static partial uint GetDpiForSystem();
}
