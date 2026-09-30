using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Microsoft.Win32;
using RadioPassthrough.Core.Diagnostics;

namespace RadioPassthrough.Core.Setup;

// Per-user install: no administrator rights, no installer framework. The app copies itself to
// %LOCALAPPDATA%\Programs\Radio Passthrough, adds a Start menu entry, an entry in Settings → Apps
// (with a working Uninstall), and starts with Windows.
public sealed class Installation
{
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RadioPassthrough";
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "RadioPassthrough";

    private readonly RegistryKey _root;

    public Installation(string? installDirectory = null, RegistryKey? registryRoot = null, string? startMenuDirectory = null, string? desktopDirectory = null)
    {
        InstallDirectory = installDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppInfo.Name);
        _root = registryRoot ?? Registry.CurrentUser;
        StartMenuDirectory = startMenuDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        DesktopDirectory = desktopDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    public string InstallDirectory { get; }
    public string StartMenuDirectory { get; }
    public string DesktopDirectory { get; }
    public string ExePath => Path.Combine(InstallDirectory, AppInfo.ExeName);
    public string StartMenuShortcut => Path.Combine(StartMenuDirectory, AppInfo.Name + ".lnk");
    public string DesktopShortcut => Path.Combine(DesktopDirectory, AppInfo.Name + ".lnk");

    public bool IsInstalled => File.Exists(ExePath);

    public Version? InstalledVersion
    {
        get
        {
            using var key = _root.OpenSubKey(UninstallKeyPath);
            return key?.GetValue("DisplayVersion") is string s && Version.TryParse(s, out var v) ? v : null;
        }
    }

    public bool HasDesktopShortcut => File.Exists(DesktopShortcut);

    public bool IsThisCopy(string? exePath) =>
        exePath is not null && string.Equals(Path.GetFullPath(exePath), Path.GetFullPath(ExePath), StringComparison.OrdinalIgnoreCase);

    public void Install(string sourceExe, Version version, bool desktopShortcut, bool startWithWindows)
    {
        Directory.CreateDirectory(InstallDirectory);
        if (!IsThisCopy(sourceExe)) CopyWithRetry(sourceExe, ExePath);

        CreateShortcut(StartMenuShortcut, ExePath);
        if (desktopShortcut) CreateShortcut(DesktopShortcut, ExePath);
        else if (File.Exists(DesktopShortcut)) File.Delete(DesktopShortcut);

        using (var key = _root.CreateSubKey(UninstallKeyPath))
        {
            key.SetValue("DisplayName", AppInfo.Name);
            key.SetValue("DisplayVersion", version.ToString(3));
            key.SetValue("Publisher", AppInfo.Name);
            key.SetValue("DisplayIcon", $"{ExePath},0");
            key.SetValue("InstallLocation", InstallDirectory);
            key.SetValue("UninstallString", $"\"{ExePath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{ExePath}\" --uninstall --quiet");
            key.SetValue("URLInfoAbout", AppInfo.ReleasesPage);
            key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
            key.SetValue("EstimatedSize", (int)Math.Max(1, new FileInfo(ExePath).Length / 1024), RegistryValueKind.DWord);
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        }

        SetStartWithWindows(startWithWindows);
        Log.Info($"Installed {version.ToString(3)} to {InstallDirectory}.");
    }

    public void SetStartWithWindows(bool enabled)
    {
        using var key = _root.CreateSubKey(RunKeyPath);
        if (enabled) key.SetValue(RunValueName, $"\"{ExePath}\" --tray");
        else key.DeleteValue(RunValueName, throwOnMissingValue: false);
    }

    // Updates keep the earlier choice: autostart stays off if it was switched off. First installs start with Windows.
    public bool StartWithWindowsAfterInstall => !IsInstalled || StartsWithWindows;

    public bool StartsWithWindows
    {
        get
        {
            using var key = _root.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string s && s.Contains(ExePath, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Removes shortcuts, the Apps entry and autostart. Files are removed by RemoveFiles.
    public void RemoveIntegration()
    {
        foreach (string link in new[] { StartMenuShortcut, DesktopShortcut })
            if (File.Exists(link)) File.Delete(link);
        _root.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        using (var run = _root.OpenSubKey(RunKeyPath, writable: true))
        {
            if (run?.GetValue(RunValueName) is string s && s.Contains(AppInfo.ExeName, StringComparison.OrdinalIgnoreCase))
                run.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
        Log.Info("Removed shortcuts, Apps entry and autostart.");
    }

    // Deletes the install folder, including the exe that's running right now: Windows won't delete a
    // running program but will let it be moved, so it's parked in the Temp folder (same drive) where
    // Windows' own clean-up removes it later. No helper process or script is involved.
    public static void RemoveFiles(string installDirectory, string? runningExe)
    {
        if (runningExe is not null && File.Exists(runningExe) && IsInside(runningExe, installDirectory))
        {
            string parked = Path.Combine(Path.GetTempPath(), $"RadioPassthrough-removed-{Guid.NewGuid():N}.tmp");
            if (string.Equals(Path.GetPathRoot(Path.GetFullPath(parked)), Path.GetPathRoot(Path.GetFullPath(runningExe)), StringComparison.OrdinalIgnoreCase))
            {
                try { File.Move(runningExe, parked); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        DeleteDirectory(installDirectory);
    }

    // The downloaded setup file isn't needed once it's installed. It is the running program, so it can't be
    // deleted, but it can be moved on the same drive: it's parked in the Temp folder, where Windows' own
    // clean-up removes it. Its checksum file goes too. Returns false (and leaves everything) when the file is
    // on another drive, such as a USB stick, or can't be moved.
    public static bool DiscardSetupFile(string setupPath)
    {
        try
        {
            string full = Path.GetFullPath(setupPath);
            if (!full.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(full)) return false;
            string parked = Path.Combine(Path.GetTempPath(), $"RadioPassthrough-setup-{Guid.NewGuid():N}.tmp");
            if (!string.Equals(Path.GetPathRoot(full), Path.GetPathRoot(Path.GetFullPath(parked)), StringComparison.OrdinalIgnoreCase))
                return false;
            File.Move(full, parked);
            if (File.Exists(full + ".sha256")) File.Delete(full + ".sha256");
            Log.Info("Moved the setup file to Temp.");
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void DeleteDirectory(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Something still open in there; the folder stays, harmless.
        }
    }

    private static bool IsInside(string file, string directory) =>
        Path.GetFullPath(file).StartsWith(Path.GetFullPath(directory).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static void CopyWithRetry(string source, string target)
    {
        string staging = target + ".new";
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Copy(source, staging, overwrite: true);
                File.Move(staging, target, overwrite: true);
                return;
            }
            catch (Exception e) when ((e is IOException or UnauthorizedAccessException) && attempt < 40)
            {
                Thread.Sleep(250); // the previous version may still be exiting
            }
        }
    }

    public static void CreateShortcut(string linkPath, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(linkPath)!);
        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(target);
            link.SetWorkingDirectory(Path.GetDirectoryName(target)!);
            link.SetDescription("Game audio over your ACRE2 radio");
            link.SetIconLocation(target, 0);
            ((IPersistFile)link).Save(linkPath, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int icon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }
}
