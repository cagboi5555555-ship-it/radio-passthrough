using Microsoft.Win32;

namespace RadioPassthrough.Core.Setup;

// Windows' privacy switches for the microphone (Settings → Privacy & security → Microphone). When one is
// off, recording still "works" but only delivers silence, so teammates hear nothing and nothing errors.
public static class MicrophonePrivacy
{
    public const string ConsentPath = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone";
    public const string SettingsPage = "ms-settings:privacy-microphone";

    public static bool IsBlocked() => IsBlocked(Registry.LocalMachine, Registry.CurrentUser);

    // Blocked when the device-wide switch, the user's "Microphone access" or "Let desktop apps access your
    // microphone" is off. A missing value means Windows' default, which is allowed.
    public static bool IsBlocked(RegistryKey machine, RegistryKey user, string path = ConsentPath) =>
        Denied(machine, path) || Denied(user, path) || Denied(user, path + @"\NonPackaged");

    private static bool Denied(RegistryKey root, string path)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            return key?.GetValue("Value") is string value && value.Equals("Deny", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false; // can't tell; don't raise a false alarm
        }
    }
}
