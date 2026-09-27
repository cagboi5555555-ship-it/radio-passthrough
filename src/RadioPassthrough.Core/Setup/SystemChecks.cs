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
    GetCable,
    OpenSoundSettings,
    OpenCableFormat,
    SetUpTeamSpeak,
    RestoreTeamSpeak,
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

        if (cableIn is null || cableOut is null)
        {
            checks.Add(new Check("VB-CABLE", "Not installed. The app needs it to hand your mixed mic to TeamSpeak.",
                CheckLevel.Blocking, CheckAction.GetCable, "Get VB-CABLE"));
        }
        else
        {
            checks.Add(new Check("VB-CABLE", "Installed.", CheckLevel.Ok));

            var hijacked = new[]
            {
                AudioDevices.DefaultPlayback(), AudioDevices.DefaultRecording(),
                AudioDevices.DefaultCommunicationsPlayback(), AudioDevices.DefaultCommunicationsRecording(),
            }.Where(d => d is not null && AudioDevices.IsCable(d.Name)).ToList();
            checks.Add(hijacked.Count > 0
                ? new Check("Windows sound devices", "Windows is using the cable as a default device. Set your headset back as default.",
                    CheckLevel.Attention, CheckAction.OpenSoundSettings, "Open Sound")
                : new Check("Windows sound devices", "Your normal devices are still the defaults.", CheckLevel.Ok));

            int? rate = AudioDevices.SampleRate(cableIn.Id);
            int? rateOut = AudioDevices.SampleRate(cableOut.Id);
            checks.Add(rate == 48000 && rateOut == 48000
                ? new Check("Cable format", "48 kHz, same as TeamSpeak.", CheckLevel.Ok)
                : new Check("Cable format", $"Running at {Khz(rate)} / {Khz(rateOut)}. Set CABLE Input and CABLE Output to 48000 Hz so nothing is resampled.",
                    CheckLevel.Attention, CheckAction.OpenCableFormat, "Open settings"));
        }

        if (!ts.Installed)
        {
            checks.Add(new Check("TeamSpeak", "TeamSpeak 3 settings not found.", CheckLevel.Blocking));
        }
        else
        {
            var active = ts.ActiveCapture;
            bool usesCable = active is not null && cableOut is not null && active.DeviceId == cableOut.Id;
            if (usesCable)
            {
                checks.Add(new Check("TeamSpeak microphone", $"Uses \"{active!.Name}\" on all servers.", CheckLevel.Ok,
                    CheckAction.RestoreTeamSpeak, "Restore normal mic"));
            }
            else
            {
                string now = active?.DeviceName is { } n ? $"Currently {n}." : "Currently your normal mic.";
                checks.Add(new Check("TeamSpeak microphone", $"{now} Switch it to the passthrough profile{(ts.Running ? " (close TeamSpeak first)" : "")}.",
                    cableOut is null ? CheckLevel.Info : CheckLevel.Blocking,
                    cableOut is null ? CheckAction.None : CheckAction.SetUpTeamSpeak, "Set up"));
            }

            checks.Add(ts.AcrePluginInstalled
                ? new Check("ACRE2 plugin", "Installed in TeamSpeak.", CheckLevel.Ok)
                : new Check("ACRE2 plugin", "Not found in TeamSpeak's plugins. Start Arma with ACRE2 once to install it.", CheckLevel.Attention));
        }

        if (arma is null)
        {
            checks.Add(new Check("Arma 3", "Not running. The app attaches automatically when it starts.", CheckLevel.Info));
        }
        else if (arma.Elevated && !ProcessInfo.IsCurrentProcessElevated)
        {
            checks.Add(new Check("Arma 3", "Running as administrator, so Windows hides your radio keys from this app.",
                CheckLevel.Blocking, CheckAction.RestartAsAdmin, "Restart as admin"));
        }
        else
        {
            checks.Add(new Check("Arma 3", "Running. Game audio is attached.", CheckLevel.Ok));
        }

        return checks;
    }

    private static string Khz(int? rate) => rate is { } r ? $"{r / 1000.0:0.#} kHz" : "unknown";
}
