using System.Text;

namespace RadioPassthrough.Core.Diagnostics;

// Small rolling log: one file per day in %LOCALAPPDATA%\RadioPassthrough\logs, kept for 14 days,
// plus the most recent lines in memory for the diagnostics report.
public static class Log
{
    private const int KeepDays = 14;
    private const int RecentLines = 300;

    public static readonly string DefaultDirectory =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RadioPassthrough", "logs");

    private static readonly object Gate = new();
    private static readonly Queue<string> Recent = new();
    private static string _directory = DefaultDirectory;
    private static DateOnly _prunedOn;

    public static string Directory
    {
        get { lock (Gate) return _directory; }
        set { lock (Gate) _directory = value; }
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    public static IReadOnlyList<string> RecentEntries()
    {
        lock (Gate) return Recent.ToArray();
    }

    public static string CurrentFile => Path.Combine(Directory, $"app-{DateTime.Now:yyyyMMdd}.log");

    private static void Write(string level, string message)
    {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > RecentLines) Recent.Dequeue();
            try
            {
                System.IO.Directory.CreateDirectory(_directory);
                File.AppendAllText(Path.Combine(_directory, $"app-{DateTime.Now:yyyyMMdd}.log"), line + Environment.NewLine, Encoding.UTF8);
                PruneOnceADay();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down.
            }
        }
    }

    private static void PruneOnceADay()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        if (_prunedOn == today) return;
        _prunedOn = today;
        foreach (string file in System.IO.Directory.EnumerateFiles(_directory, "app-*.log"))
        {
            if (File.GetLastWriteTime(file) < DateTime.Now.AddDays(-KeepDays))
                File.Delete(file);
        }
    }
}
