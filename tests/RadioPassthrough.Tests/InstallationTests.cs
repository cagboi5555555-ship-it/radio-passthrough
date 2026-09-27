using Microsoft.Win32;
using RadioPassthrough.Core;
using RadioPassthrough.Core.Settings;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough.Tests;

public sealed class InstallationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"rp-install-{Guid.NewGuid():N}");
    private readonly string _registryPath = $@"Software\RadioPassthroughTests\{Guid.NewGuid():N}";
    private readonly RegistryKey _registry;

    public InstallationTests()
    {
        Directory.CreateDirectory(_root);
        _registry = Registry.CurrentUser.CreateSubKey(_registryPath);
    }

    private Installation Make() => new(
        Path.Combine(_root, "Programs", AppInfo.Name), _registry,
        Path.Combine(_root, "StartMenu"), Path.Combine(_root, "Desktop"));

    private string FakeExe()
    {
        string path = Path.Combine(_root, "Downloads", "RadioPassthrough-Setup.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[4096]);
        return path;
    }

    [Fact]
    public void Install_copies_exe_adds_shortcuts_apps_entry_and_autostart()
    {
        var install = Make();
        install.Install(FakeExe(), new Version(1, 2, 3), desktopShortcut: true, startWithWindows: true);

        Assert.True(File.Exists(install.ExePath));
        Assert.True(File.Exists(install.StartMenuShortcut));
        Assert.True(File.Exists(install.DesktopShortcut));
        Assert.Equal(new Version(1, 2, 3), install.InstalledVersion);
        Assert.True(install.StartsWithWindows);
        Assert.True(install.IsThisCopy(install.ExePath));

        using var key = _registry.OpenSubKey(Installation.UninstallKeyPath)!;
        Assert.Equal($"\"{install.ExePath}\" --uninstall", key.GetValue("UninstallString"));
        Assert.Equal(AppInfo.Name, key.GetValue("DisplayName"));
    }

    [Fact]
    public void Update_replaces_the_exe_and_can_drop_the_desktop_shortcut()
    {
        var install = Make();
        install.Install(FakeExe(), new Version(1, 0, 0), desktopShortcut: true, startWithWindows: true);
        install.Install(FakeExe(), new Version(1, 1, 0), desktopShortcut: false, startWithWindows: true);
        Assert.Equal(new Version(1, 1, 0), install.InstalledVersion);
        Assert.False(File.Exists(install.DesktopShortcut));
    }

    [Fact]
    public void Update_keeps_start_with_windows_switched_off()
    {
        var install = Make();
        Assert.True(install.StartWithWindowsAfterInstall); // first install: on

        install.Install(FakeExe(), new Version(1, 0, 0), desktopShortcut: false, startWithWindows: true);
        install.SetStartWithWindows(false);
        Assert.False(install.StartWithWindowsAfterInstall);

        install.Install(FakeExe(), new Version(1, 1, 0), desktopShortcut: false, install.StartWithWindowsAfterInstall);
        Assert.False(install.StartsWithWindows);
    }

    [Fact]
    public void Remove_takes_away_every_trace_but_the_files()
    {
        var install = Make();
        install.Install(FakeExe(), new Version(1, 0, 0), desktopShortcut: true, startWithWindows: true);
        install.RemoveIntegration();

        Assert.False(File.Exists(install.StartMenuShortcut));
        Assert.False(File.Exists(install.DesktopShortcut));
        Assert.Null(_registry.OpenSubKey(Installation.UninstallKeyPath));
        Assert.False(install.StartsWithWindows);
    }

    [Fact]
    public void Settings_survive_a_damaged_file()
    {
        var store = new SettingsStore(Path.Combine(_root, "settings"));
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.FilePath, "{ this is not json");
        var settings = store.Load();
        Assert.Equal(AppSettings.CurrentSchema, settings.SchemaVersion);
        Assert.True(File.Exists(store.FilePath + ".bad"));

        settings.GameAudioEnabled = false;
        store.Save(settings);
        Assert.False(store.Load().GameAudioEnabled);
    }

    [Fact]
    public void Settings_from_version_1_load_with_new_defaults()
    {
        var store = new SettingsStore(Path.Combine(_root, "settings1"));
        Directory.CreateDirectory(store.Directory);
        File.WriteAllText(store.FilePath, """{ "MicChannels": "First", "Preset": "Custom", "FirstRunDone": true }""");
        var settings = store.Load();
        Assert.Equal(Core.Dsp.MicChannelMode.First, settings.MicChannels);
        Assert.True(settings.GameAudioEnabled);
        Assert.True(settings.KeepRealDefaults);
        Assert.Equal(4, settings.Bindings.Count);
    }

    [Fact]
    public void Removing_files_leaves_no_install_folder_even_while_the_exe_runs()
    {
        var install = Make();
        install.Install(FakeExe(), new Version(1, 0, 0), desktopShortcut: false, startWithWindows: false);
        using (var locked = new FileStream(install.ExePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
        {
            // A running exe can be renamed but not deleted; FileShare.Delete models that here.
            Installation.RemoveFiles(install.InstallDirectory, install.ExePath);
        }
        Assert.False(Directory.Exists(install.InstallDirectory));
    }

    public void Dispose()
    {
        Registry.CurrentUser.DeleteSubKeyTree(_registryPath, throwOnMissingSubKey: false);
        _registry.Dispose();
        using (var parent = Registry.CurrentUser.OpenSubKey(@"Software\RadioPassthroughTests"))
        {
            if (parent is { SubKeyCount: 0, ValueCount: 0 })
                Registry.CurrentUser.DeleteSubKey(@"Software\RadioPassthroughTests", throwOnMissingSubKey: false);
        }
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }
}
