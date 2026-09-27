using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using RadioPassthrough.Core.Game;

namespace RadioPassthrough.Core.Audio;

public sealed record AudioApp(int Pid, string Name, bool Playing);

public static class AudioApps
{
    private static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "ts3client_win64", "ts3client_win32", "RadioPassthrough", "audiodg",
    };

    // Apps with an audio session on any playback device, playing ones first.
    public static IReadOnlyList<AudioApp> List()
    {
        var apps = new Dictionary<int, AudioApp>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                try
                {
                    var manager = device.AudioSessionManager;
                    manager.RefreshSessions();
                    var sessions = manager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        using var session = sessions[i];
                        if (session.IsSystemSoundsSession) continue;
                        int pid = (int)session.GetProcessID;
                        if (pid == 0 || pid == Environment.ProcessId) continue;
                        bool playing = session.State == AudioSessionState.AudioSessionStateActive;
                        if (apps.TryGetValue(pid, out var existing) && existing.Playing) continue;
                        string? name = NameFor(pid);
                        if (name is null) continue;
                        apps[pid] = new AudioApp(pid, name, playing);
                    }
                }
                catch
                {
                    // A device that disappears mid-enumeration is simply skipped.
                }
            }
        }

        return apps.Values
            .OrderByDescending(a => a.Playing)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NameFor(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (Excluded.Contains(process.ProcessName)) return null;
            if (process.ProcessName.StartsWith("arma3", StringComparison.OrdinalIgnoreCase)) return "Arma 3";
            return ProcessInfo.FriendlyName(process);
        }
        catch
        {
            return null;
        }
    }
}
