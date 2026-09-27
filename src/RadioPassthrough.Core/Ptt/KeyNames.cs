using System.Runtime.InteropServices;
using System.Text;

namespace RadioPassthrough.Core.Ptt;

public static partial class KeyNames
{
    public static string Describe(PttBinding binding)
    {
        var parts = new List<string>();
        if (binding.Modifiers.HasFlag(Modifiers.Ctrl)) parts.Add("Ctrl");
        if (binding.Modifiers.HasFlag(Modifiers.Shift)) parts.Add("Shift");
        if (binding.Modifiers.HasFlag(Modifiers.Alt)) parts.Add("Alt");
        parts.Add(Trigger(binding.Kind, binding.Code));
        return string.Join(" + ", parts);
    }

    public static string Trigger(TriggerKind kind, int code)
    {
        if (kind == TriggerKind.Mouse)
        {
            return code switch
            {
                MouseButtons.Middle => "Middle mouse",
                MouseButtons.X1 => "Mouse 4",
                MouseButtons.X2 => "Mouse 5",
                _ => $"Mouse {code}",
            };
        }

        if (code == PttBinding.CapsLock) return "Caps Lock";

        uint scan = MapVirtualKeyW((uint)code, 0);
        bool extended = code is >= 0x21 and <= 0x2E or 0x5B or 0x5C or 0x6F or 0x90;
        int lParam = (int)(scan << 16) | (extended ? 1 << 24 : 0);
        var sb = new StringBuilder(64);
        int len = GetKeyNameTextW(lParam, sb, sb.Capacity);
        if (len > 0) return Title(sb.ToString());
        return $"Key {code:X2}";
    }

    private static string Title(string s) =>
        s.Length <= 1 ? s : string.Join(' ', s.Split(' ').Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint uCode, uint uMapType);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetKeyNameTextW(int lParam, StringBuilder lpString, int cchSize);
}
