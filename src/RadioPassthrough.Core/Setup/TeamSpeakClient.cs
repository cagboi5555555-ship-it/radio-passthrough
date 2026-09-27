using System.Diagnostics;
using Microsoft.Win32;
using RadioPassthrough.Core.Diagnostics;

namespace RadioPassthrough.Core.Setup;

// Finds TeamSpeak 3 (installed for everyone, for one user, or with settings kept in its own folder)
// and closes or starts it when its settings need changing.
public static class TeamSpeakClient
{
    private static readonly string[] ProcessNames = ["ts3client_win64", "ts3client_win32"];

    public static string AppDataConfig { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TS3Client");

    public static string? InstallDirectory()
    {
        foreach (var (hive, view) in new[] { (RegistryHive.CurrentUser, RegistryView.Default), (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32) })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var key = root.OpenSubKey(@"SOFTWARE\TeamSpeak 3 Client");
                if (key?.GetValue(null) is string dir && System.IO.Directory.Exists(dir)) return dir;
            }
            catch (Exception e) when (e is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
            }
        }

        string standard = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "TeamSpeak 3 Client");
        return System.IO.Directory.Exists(standard) ? standard : null;
    }

    private static bool ConfigInInstallFolder()
    {
        foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"SOFTWARE\TeamSpeak 3 Client");
            if (key?.GetValue("ConfigLocation") is { } v && v.ToString() == "1") return true;
        }
        return false;
    }

    // Where TeamSpeak keeps settings.db on this PC. Falls back to the usual AppData folder.
    public static string ConfigDirectory()
    {
        var candidates = new List<string>();
        string? install = InstallDirectory();
        string? local = install is null ? null : Path.Combine(install, "config");
        if (local is not null && ConfigInInstallFolder()) candidates.Add(local);
        candidates.Add(AppDataConfig);
        if (local is not null) candidates.Add(local);

        return candidates.FirstOrDefault(c => File.Exists(Path.Combine(c, "settings.db"))) ?? candidates[0];
    }

    public static string? Executable()
    {
        string? install = InstallDirectory();
        if (install is null) return null;
        foreach (string name in new[] { "ts3client_win64.exe", "ts3client_win32.exe" })
        {
            string path = Path.Combine(install, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    public static bool IsRunning()
    {
        foreach (string name in ProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var p in processes) p.Dispose();
            if (processes.Length > 0) return true;
        }
        return false;
    }

    public static int? ProcessId()
    {
        foreach (string name in ProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length > 0) return processes[0].Id;
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }
        return null;
    }

    // Asks TeamSpeak to close like clicking X would. Returns the executable to start it again, or null if
    // it wasn't running. Throws if it doesn't close in time (for example a dialog is open).
    public static async Task<string?> CloseAsync(TimeSpan timeout)
    {
        var processes = ProcessNames.SelectMany(Process.GetProcessesByName).ToList();
        if (processes.Count == 0) return null;

        string? exe = null;
        try
        {
            foreach (var p in processes)
            {
                try { exe ??= p.MainModule?.FileName; } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
                p.CloseMainWindow();
            }

            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (processes.All(p => { p.Refresh(); return p.HasExited; })) break;
                await Task.Delay(200).ConfigureAwait(false);
            }

            if (!processes.All(p => p.HasExited))
                throw new InvalidOperationException("TeamSpeak didn't close. Close it yourself (right-click its tray icon → Quit) and try again.");
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }

        // TeamSpeak writes its settings on the way out; give the file a moment to settle.
        await Task.Delay(800).ConfigureAwait(false);
        Log.Info("TeamSpeak closed for a settings change.");
        return exe ?? Executable();
    }

    public static void Start(string? executable)
    {
        string? exe = executable ?? Executable();
        if (exe is null || !File.Exists(exe)) return;
        try
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) })?.Dispose();
            Log.Info("TeamSpeak started again.");
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log.Warn($"Couldn't start TeamSpeak again: {e.Message}");
        }
    }
}
