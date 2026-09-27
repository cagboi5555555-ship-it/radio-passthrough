using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace RadioPassthrough.Theme;

// Follows the Windows light/dark app setting and colours the title bar to match the page.
public static partial class ThemeManager
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaTextColor = 36;

    public static bool IsDark { get; private set; } = true;

    public static event Action? Changed;

    public static void Initialize(bool? forceDark = null)
    {
        Apply(forceDark ?? ReadSystemIsDark());
        if (forceDark is not null) return;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                bool dark = ReadSystemIsDark();
                if (dark != IsDark) Apply(dark);
            });
        };
    }

    private static bool ReadSystemIsDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is not int light || light == 0;
    }

    private static void Apply(bool dark)
    {
        IsDark = dark;
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = new Uri($"Theme/Palette.{(dark ? "Dark" : "Light")}.xaml", UriKind.Relative) };
        if (dictionaries.Count > 0) dictionaries[0] = palette;
        else dictionaries.Add(palette);

        foreach (Window window in Application.Current.Windows)
            StyleTitleBar(window);
        Changed?.Invoke();
    }

    public static void StyleTitleBar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        int dark = IsDark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));
        SetColor(hwnd, DwmwaCaptionColor, "BaseColor");
        SetColor(hwnd, DwmwaBorderColor, "BorderColor");
        SetColor(hwnd, DwmwaTextColor, "InkColor");
    }

    private static void SetColor(IntPtr hwnd, int attribute, string key)
    {
        if (Application.Current.TryFindResource(key) is not Color c) return;
        int colorRef = c.R | (c.G << 8) | (c.B << 16);
        DwmSetWindowAttribute(hwnd, attribute, ref colorRef, sizeof(int));
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
