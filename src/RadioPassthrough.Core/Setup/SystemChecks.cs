using NAudio.CoreAudioApi;
using RadioPassthrough.Core.Audio;
using RadioPassthrough.Core.Game;

namespace RadioPassthrough.Core.Setup;

public enum CheckLevel
{
    Ok,
    Info,
    Attention,
    Blocking,
}

public enum CheckAction
{
    None,
    InstallCable,
    SetUpTeamSpeak,
    RestoreTeamSpeak,
    FixDevices,
    OpenCableFormat,
    RestartAsAdmin,
}

public sealed record Check(string Title, string Detail, CheckLevel Level, CheckAction Action = CheckAction.None, string? ActionLabel = null);

public static class SystemChecks
{
    public static IReadOnlyList<Check> Run(TeamSpeakState ts, GameProcess? arma)
    {
        var checks = new List<Check>();
        var cableIn = AudioDevices.CableInput();
        var cableOut = AudioDevices.CableOutput();
        bool cable = cableIn is not null && cableOut is not null;

        checks.Add(cable
            ? new Check("VB-CABLE", "Installed. It carries your mixed mic to TeamSpeak.", CheckLevel.Ok)
            : new Check("VB-CABLE", "Needed to hand your mixed mic to TeamSpeak. Free, from VB-Audio. One Windows prompt, no restart usually.",
                CheckLevel.Blocking, CheckAction.InstallCable, "Install"));

        if (!ts.Installed)
        {
            checks.Add(new Check("TeamSpeak", "TeamSpeak 3 wasn't found. Install it, start it once, then come back.", CheckLevel.Blocking));
        }
        else
        {
            var active = ts.ActiveCapture;
            bool usesCable = active is not null && cableOut is not null && active.DeviceId == cableOut.Id;
            if (usesCable)
            {
                checks.Add(new Check("TeamSpeak microphone", $"Uses \"{active!.Name}\" on every server, with your usual processing.",
                    CheckLevel.Ok, CheckAction.RestoreTeamSpeak, "Undo"));
            }
            else
            {
                string now = active?.DeviceName is { } n ? $"Currently {n}." : "Currently your normal mic.";
                string restart = ts.Running ? " TeamSpeak restarts for a moment." : "";
                checks.Add(cable
                    ? new Check("TeamSpeak microphone", $"{now} Switch it to the passthrough.{restart}", CheckLevel.Blocking, CheckAction.SetUpTeamSpeak, "Set up")
                    : new Check("TeamSpeak microphone", $"{now} Set up after VB-CABLE.", CheckLevel.Info));
            }

            checks.Add(ts.AcrePluginInstalled
                ? new Check("ACRE2 plugin", "Installed in TeamSpeak.", CheckLevel.Ok)
                : new Check("ACRE2 plugin", "Not found in TeamSpeak. Start Arma with ACRE2 once and it installs itself.", CheckLevel.Attention));
        }

        if (cable)
        {
            var system = new WindowsDefaultDevices();
            bool hijacked = new[] { DataFlow.Render, DataFlow.Capture }
                .SelectMany(f => Enum.GetValues<DeviceRole>().Select(r => system.GetDefault(f, r)))
                .Any(d => d is not null && AudioDevices.IsCable(d.Name));
            var extras = UnusedCableEndpoints(system);

            if (hijacked)
                checks.Add(new Check("Windows sound devices", "Windows is using the cable as a default device. Put your own speakers and mic back.",
                    CheckLevel.Attention, CheckAction.FixDevices, "Fix"));
            else if (extras.Count > 0)
                checks.Add(new Check("Windows sound devices", $"VB-CABLE added {string.Join(", ", extras)}, which nothing uses. Hide it so nothing picks it by mistake.",
                    CheckLevel.Attention, CheckAction.FixDevices, "Hide"));
            else
                checks.Add(new Check("Windows sound devices", "Your own speakers and mic stay the defaults. The app keeps it that way.", CheckLevel.Ok));

            int? rate = AudioDevices.SampleRate(cableIn!.Id);
            int? rateOut = AudioDevices.SampleRate(cableOut!.Id);
            checks.Add(rate == 48000 && rateOut == 48000
                ? new Check("Cable format", "48 kHz, same as TeamSpeak.", CheckLevel.Ok)
                : new Check("Cable format", $"Running at {Khz(rate)} / {Khz(rateOut)}. 48 kHz avoids an extra conversion. It works either way.",
                    CheckLevel.Info, CheckAction.OpenCableFormat, "Open settings"));
        }

        if (arma is null)
            checks.Add(new Check("Arma 3", "Not running. The app attaches by itself when it starts.", CheckLevel.Info));
        else if (arma.Elevated && !ProcessInfo.IsCurrentProcessElevated)
            checks.Add(new Check("Arma 3", "Running as administrator, so Windows hides your radio keys from this app.",
                CheckLevel.Blocking, CheckAction.RestartAsAdmin, "Restart as admin"));
        else
            checks.Add(new Check("Arma 3", "Running. Game audio is attached.", CheckLevel.Ok));

        return checks;
    }

    private static List<string> UnusedCableEndpoints(WindowsDefaultDevices system)
    {
        var names = new List<string>();
        foreach (var d in system.Active(DataFlow.Render))
            if (AudioDevices.IsCable(d.Name) && !d.Name.StartsWith(AudioDevices.CableInputName, StringComparison.OrdinalIgnoreCase))
                names.Add(ShortName(d.Name));
        foreach (var d in system.Active(DataFlow.Capture))
            if (AudioDevices.IsCable(d.Name) && !d.Name.StartsWith(AudioDevices.CableOutputName, StringComparison.OrdinalIgnoreCase))
                names.Add(ShortName(d.Name));
        return names;
    }

    private static string ShortName(string name)
    {
        int paren = name.IndexOf(" (", StringComparison.Ordinal);
        return paren > 0 ? name[..paren] : name;
    }

    private static string Khz(int? rate) => rate is { } r ? $"{r / 1000.0:0.#} kHz" : "unknown";
}
