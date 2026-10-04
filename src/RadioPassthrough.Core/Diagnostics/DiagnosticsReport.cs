using System.Text;
using NAudio.CoreAudioApi;
using RadioPassthrough.Core.Audio;
using RadioPassthrough.Core.Game;
using RadioPassthrough.Core.Ptt;
using RadioPassthrough.Core.Settings;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough.Core.Diagnostics;

// Plain-text summary for asking for help (paste it in Discord). Contains device names and settings,
// never audio or anything typed.
public static class DiagnosticsReport
{
    public static string Build(AppSettings settings, EngineStatus engine, TeamSpeakState? teamSpeak, IReadOnlyList<Check> checks, GameProcess? arma, (int Mic, int Game)? dropouts = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Radio Passthrough {AppInfo.Version.ToString(3)} diagnostics, {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"Windows {Environment.OSVersion.Version}, {System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}, admin: {ProcessInfo.IsCurrentProcessElevated}");
        sb.AppendLine($"Running from: {Environment.ProcessPath}");
        sb.AppendLine();

        sb.AppendLine("Checklist");
        foreach (var c in checks) sb.AppendLine($"  [{c.Level}] {c.Title}: {c.Detail}");
        sb.AppendLine();

        sb.AppendLine("Engine");
        sb.AppendLine($"  cable: {engine.CableFound}, mic: {engine.MicName ?? "none"}, game: {(engine.GameAttached ? engine.GameName : "not attached")}");
        if (engine.Problem is not null) sb.AppendLine($"  problem: {engine.Problem}");
        sb.AppendLine($"  Arma: {(arma is null ? "not running" : $"pid {arma.Pid}, admin: {arma.Elevated}")}");
        if (dropouts is { } d) sb.AppendLine($"  audio dropouts since start: mic {d.Mic}, game {d.Game}");
        sb.AppendLine();

        sb.AppendLine("Settings");
        sb.AppendLine($"  game audio on: {settings.GameAudioEnabled}");
        sb.AppendLine($"  radio keys: {string.Join(", ", settings.Bindings.Select(KeyNames.Describe))}");
        sb.AppendLine($"  keep real defaults: {settings.KeepRealDefaults}, hide unused cable devices: {settings.HideUnusedCableDevices}");
        sb.AppendLine();

        sb.AppendLine("TeamSpeak");
        if (teamSpeak is null) sb.AppendLine("  couldn't read settings");
        else
        {
            sb.AppendLine($"  installed: {teamSpeak.Installed}, running: {teamSpeak.Running}, ACRE2 plugin: {teamSpeak.AcrePluginInstalled}");
            sb.AppendLine($"  default capture profile: {teamSpeak.DefaultCaptureProfile} → {teamSpeak.ActiveCapture?.DeviceName ?? "?"}");
            if (teamSpeak.ActiveCapture is { MissesDirectSpeech: true })
                sb.AppendLine("  voice activation can't open: Automatic/Hybrid with Remove background noise off");
        }
        sb.AppendLine();

        sb.AppendLine("Audio devices");
        AppendDevices(sb, DataFlow.Render);
        AppendDevices(sb, DataFlow.Capture);
        sb.AppendLine();

        sb.AppendLine("Recent log");
        foreach (string line in Log.RecentEntries().TakeLast(60)) sb.AppendLine("  " + line);
        return Redact(sb.ToString());
    }

    // The report gets pasted in public channels: keep the Windows user name out of it.
    public static string Redact(string text)
    {
        string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (profile.Length > 3) text = text.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        string user = Environment.UserName;
        if (user.Length > 2) text = System.Text.RegularExpressions.Regex.Replace(text, $@"\b{System.Text.RegularExpressions.Regex.Escape(user)}\b", "<user>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return text;
    }

    private static void AppendDevices(StringBuilder sb, DataFlow flow)
    {
        try
        {
            var system = new WindowsDefaultDevices();
            string? console = system.GetDefault(flow, DeviceRole.Console)?.Id;
            string? comms = system.GetDefault(flow, DeviceRole.Communications)?.Id;
            foreach (var d in system.Active(flow))
            {
                string marks = (d.Id == console ? " [default]" : "") + (d.Id == comms ? " [communications]" : "");
                int? rate = AudioDevices.SampleRate(d.Id);
                sb.AppendLine($"  {(flow == DataFlow.Render ? "out" : "in ")} {d.Name}{marks}{(rate is { } r ? $" {r} Hz" : "")}");
            }
        }
        catch (Exception e)
        {
            sb.AppendLine($"  couldn't list {flow} devices: {e.Message}");
        }
    }
}
