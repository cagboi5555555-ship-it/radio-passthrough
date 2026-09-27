using System.IO;
using System.Windows;
using RadioPassthrough.Core;
using RadioPassthrough.Core.Audio;
using RadioPassthrough.Core.Diagnostics;
using RadioPassthrough.Core.Ptt;
using RadioPassthrough.Core.Settings;
using RadioPassthrough.Core.Setup;
using RadioPassthrough.Dialogs;

namespace RadioPassthrough;

// `RadioPassthrough.exe --selftest <result file>`: exercises the parts that only work against the real
// Windows shell and audio stack (tray icon, SQLite, key polling, device guard, windows) without opening
// anything visible, touching audio devices or changing settings. Exit code 0 means everything passed.
internal static class SelfTest
{
    public static async Task<bool> RunAsync(string resultFile, Func<System.Windows.Controls.ContextMenu> trayMenu)
    {
        var lines = new List<string> { $"Radio Passthrough {AppInfo.Version.ToString(3)} self-test {DateTime.Now:u}" };
        bool ok = true;

        await File.WriteAllLinesAsync(resultFile, lines);

        // Each result is written as soon as it's known, so a hard crash still shows where it happened.
        async Task Check(string name, Func<Task<string?>> body)
        {
            await File.AppendAllTextAsync(resultFile, $"....  {name}{Environment.NewLine}");
            string line;
            try
            {
                string? detail = await body();
                line = $"PASS  {name}{(detail is null ? "" : $"  ({detail})")}";
            }
            catch (Exception e)
            {
                ok = false;
                line = $"FAIL  {name}: {e.GetType().Name}: {e.Message}";
            }
            lines.Add(line);
            await File.AppendAllTextAsync(resultFile, line + Environment.NewLine);
        }

        TeamSpeakState? ts = null;
        await Check("TeamSpeak settings via Windows SQLite", async () =>
        {
            ts = await Task.Run(() => new TeamSpeakSettings().Read());
            return $"installed={ts.Installed}, profile={ts.DefaultCaptureProfile}, config={TeamSpeakClient.ConfigDirectory()}";
        });

        IReadOnlyList<Check> checks = [];
        await Check("System checks", async () =>
        {
            checks = await Task.Run(() => SystemChecks.Run(ts ?? new TeamSpeakState(), null));
            return string.Join("; ", checks.Select(c => $"{c.Title}={c.Level}"));
        });

        await Check("Tray icon", () =>
        {
            using var tray = new TrayIcon(() => { }, trayMenu());
            tray.SetTooltip("Radio Passthrough self-test");
            if (!tray.IsAdded) throw new InvalidOperationException("Shell_NotifyIcon refused the icon");
            return Task.FromResult<string?>(null);
        });

        await Check("Radio key poller", async () =>
        {
            using var keys = new KeyPoller(PttBinding.AcreDefaults(), () => false);
            keys.Start();
            await Task.Delay(100);
            return keys.IsRadioKeyHeld ? "held" : "idle";
        });

        await Check("Default device guard (read-only)", async () =>
        {
            var guard = new DefaultDeviceGuard(new WindowsDefaultDevices(), null, () => null) { Enabled = false };
            IReadOnlyDictionary<string, string>? seen = null;
            guard.RememberedChanged += r => seen = r;
            await Task.Run(guard.Check);
            guard.Dispose();
            return $"{seen?.Count ?? 0} defaults seen";
        });

        await Check("Audio device list", () => Task.FromResult<string?>($"{AudioDevices.Microphones().Count} mics, cable={(AudioDevices.CableInput() is not null)}"));

        await Check("Installer window", async () =>
        {
            var window = new InstallerWindow(new Installation(Path.Combine(Path.GetTempPath(), "rp-selftest-none")))
            {
                WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false, ShowInTaskbar = false,
            };
            window.Show();
            await Task.Delay(200);
            window.Close();
            return null;
        });

        await Check("Diagnostics report", async () =>
        {
            string report = await Task.Run(() => DiagnosticsReport.Build(new AppSettings(), new EngineStatus(), ts, checks, null));
            return $"{report.Length} chars";
        });

        await Check("Update check", async () =>
        {
            var update = await UpdateChecker.CheckAsync();
            return update is null ? "no newer release visible" : $"newer: {update.Version}";
        });

        lines.Add(ok ? "RESULT PASS" : "RESULT FAIL");
        await File.WriteAllLinesAsync(resultFile, lines);
        Log.Info($"Self-test {(ok ? "passed" : "failed")}.");
        return ok;
    }
}
