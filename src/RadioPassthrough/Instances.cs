using System.Diagnostics;
using RadioPassthrough.Core.Diagnostics;

namespace RadioPassthrough;

// Talks to other running copies of the app: bring the window forward, or ask them to quit (for an
// update or uninstall).
public static class Instances
{
    public const string MutexName = "RadioPassthrough.SingleInstance";
    public const string ShowEventName = "RadioPassthrough.Show";
    public const string QuitEventName = "RadioPassthrough.Quit";

    public static void SignalShow()
    {
        try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch (WaitHandleCannotBeOpenedException) { }
    }

    public static void StopOthers(TimeSpan wait)
    {
        try { EventWaitHandle.OpenExisting(QuitEventName).Set(); } catch (WaitHandleCannotBeOpenedException) { }

        var others = Process.GetProcessesByName("RadioPassthrough").Where(p => p.Id != Environment.ProcessId).ToList();
        try
        {
            var deadline = DateTime.UtcNow + wait;
            while (DateTime.UtcNow < deadline && others.Any(p => { p.Refresh(); return !p.HasExited; }))
                Thread.Sleep(150);

            foreach (var p in others.Where(p => !p.HasExited))
            {
                // Older versions don't listen for the quit signal.
                Log.Info($"Stopping running copy (pid {p.Id}).");
                try { p.Kill(); p.WaitForExit(3000); } catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            }
        }
        finally
        {
            foreach (var p in others) p.Dispose();
        }
    }
}
