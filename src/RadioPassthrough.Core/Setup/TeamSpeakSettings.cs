using RadioPassthrough.Core.Diagnostics;
using RadioPassthrough.Core.Native;

namespace RadioPassthrough.Core.Setup;

public sealed record TeamSpeakProfile(string Name, string? DeviceId, string? DeviceName);

public sealed record TeamSpeakState
{
    public bool Installed { get; init; }
    public bool Running { get; init; }
    public bool AcrePluginInstalled { get; init; }
    public string DefaultCaptureProfile { get; init; } = "Default";
    public IReadOnlyList<TeamSpeakProfile> CaptureProfiles { get; init; } = [];

    public TeamSpeakProfile? ActiveCapture => CaptureProfiles.FirstOrDefault(p => p.Name == DefaultCaptureProfile);
}

// Reads and edits TeamSpeak 3's capture profiles in settings.db. Edits are only made while TeamSpeak
// is closed (it rewrites the file on exit) and always after a backup.
public sealed class TeamSpeakSettings
{
    public const string ProfileName = "Radio Passthrough";
    private const int BackupsKept = 5;
    private const string BackupPrefix = "settings.db.radiopassthrough-";

    private readonly Func<bool> _isRunning;
    private readonly string? _fixedDirectory;

    public TeamSpeakSettings(string? configDirectory = null, Func<bool>? isRunning = null)
    {
        _fixedDirectory = configDirectory;
        _isRunning = isRunning ?? TeamSpeakClient.IsRunning;
    }

    // Looked up each time: TeamSpeak may be installed or moved while the app runs.
    public string ConfigDirectory => _fixedDirectory ?? TeamSpeakClient.ConfigDirectory();

    public string DatabasePath => Path.Combine(ConfigDirectory, "settings.db");

    public string? LastBackupPath { get; private set; }

    public bool IsRunning => _isRunning();

    public TeamSpeakState Read()
    {
        string dir = ConfigDirectory;
        string db = Path.Combine(dir, "settings.db");
        bool plugin = File.Exists(Path.Combine(dir, "plugins", "acre2_win64.dll"))
                      || File.Exists(Path.Combine(dir, "plugins", "acre2_win32.dll"));
        if (!File.Exists(db))
            return new TeamSpeakState { Installed = false, Running = _isRunning(), AcrePluginInstalled = plugin };

        // Read a private copy so TeamSpeak's own locks and journal are never touched.
        string temp = Path.Combine(Path.GetTempPath(), $"rp-ts3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string copy = Path.Combine(temp, "settings.db");
            CopyShared(db, copy);
            if (File.Exists(db + "-journal")) CopyShared(db + "-journal", copy + "-journal");

            Dictionary<string, string?> values;
            using (var connection = SqliteDatabase.Open(copy))
                values = ReadProfiles(connection);

            var profiles = values.Keys
                .Where(k => k.StartsWith("Capture/", StringComparison.Ordinal) && !k.EndsWith("/PreProcessing", StringComparison.Ordinal))
                .Select(k => k["Capture/".Length..])
                .Where(n => n.Length > 0)
                .Select(n => ParseProfile(n, values[$"Capture/{n}"]))
                .ToList();

            return new TeamSpeakState
            {
                Installed = true,
                Running = _isRunning(),
                AcrePluginInstalled = plugin,
                DefaultCaptureProfile = values.GetValueOrDefault("DefaultCaptureProfile") ?? "Default",
                CaptureProfiles = profiles,
            };
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    // Adds (or refreshes) the Radio Passthrough capture profile with the cable as its device and a verbatim
    // copy of the current profile's processing, then makes it the default for all servers. Returns the
    // profile that was the default before, for restoring later.
    public string Apply(string cableDeviceId, string cableDeviceName)
    {
        EnsureClosed();
        Backup();

        string previous = "Default";
        using var connection = SqliteDatabase.Open(DatabasePath);
        connection.InTransaction(() =>
        {
            var values = ReadProfiles(connection);
            string current = values.GetValueOrDefault("DefaultCaptureProfile") ?? "Default";
            previous = current == ProfileName ? "Default" : current;
            string source = values.ContainsKey($"Capture/{previous}") ? previous : "Default";

            string mode = "";
            if (values.GetValueOrDefault($"Capture/{source}") is { } sourceProfile)
                mode = ParseLines(sourceProfile).GetValueOrDefault("Mode") ?? "";
            string preprocessing = values.GetValueOrDefault($"Capture/{source}/PreProcessing") ?? DefaultPreProcessing;

            Upsert(connection, $"Capture/{ProfileName}", $"DeviceDisplayName={cableDeviceName}\nDevice={cableDeviceId}\nMode={mode}");
            Upsert(connection, $"Capture/{ProfileName}/PreProcessing", preprocessing);
            Upsert(connection, "DefaultCaptureProfile", ProfileName);
        });
        Log.Info($"TeamSpeak capture profile set to \"{ProfileName}\" (was \"{previous}\").");
        return previous;
    }

    public void Restore(string? previousProfile)
    {
        EnsureClosed();
        Backup();
        using var connection = SqliteDatabase.Open(DatabasePath);
        connection.InTransaction(() =>
        {
            var values = ReadProfiles(connection);
            string target = previousProfile is { } p && p != ProfileName && values.ContainsKey($"Capture/{p}") ? p : "Default";
            Upsert(connection, "DefaultCaptureProfile", target);
            Log.Info($"TeamSpeak capture profile restored to \"{target}\".");
        });
    }

    // Used when uninstalling: switch back and delete the profile entirely.
    public void RemoveProfile(string? previousProfile)
    {
        Restore(previousProfile);
        using var connection = SqliteDatabase.Open(DatabasePath);
        connection.InTransaction(() =>
        {
            connection.Execute("DELETE FROM Profiles WHERE key = ?", $"Capture/{ProfileName}");
            connection.Execute("DELETE FROM Profiles WHERE key = ?", $"Capture/{ProfileName}/PreProcessing");
        });
    }

    private void EnsureClosed()
    {
        if (!File.Exists(DatabasePath))
            throw new InvalidOperationException("TeamSpeak 3 settings weren't found. Start TeamSpeak once, close it, then try again.");
        if (_isRunning())
            throw new InvalidOperationException("Close TeamSpeak first. It rewrites its settings when it exits.");
    }

    private void Backup()
    {
        string dir = ConfigDirectory;
        string target = Path.Combine(dir, $"{BackupPrefix}{DateTime.Now:yyyyMMdd-HHmmss}.bak");
        CopyShared(DatabasePath, target);
        if (File.Exists(DatabasePath + "-journal")) CopyShared(DatabasePath + "-journal", target + "-journal");
        LastBackupPath = target;

        foreach (string old in Directory.EnumerateFiles(dir, BackupPrefix + "*.bak").OrderByDescending(f => f).Skip(BackupsKept))
        {
            try
            {
                File.Delete(old);
                if (File.Exists(old + "-journal")) File.Delete(old + "-journal");
            }
            catch (IOException) { }
        }
    }

    private static Dictionary<string, string?> ReadProfiles(SqliteDatabase connection)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var row in connection.Query("SELECT key, value FROM Profiles"))
            if (row[0] is { } key) values[key] = row[1];
        return values;
    }

    private static void Upsert(SqliteDatabase connection, string key, string value) => connection.Execute(
        "INSERT INTO Profiles (timestamp, key, value) VALUES (?, ?, ?) " +
        "ON CONFLICT(key) DO UPDATE SET timestamp = excluded.timestamp, value = excluded.value",
        DateTimeOffset.UtcNow.ToUnixTimeSeconds(), key, value);

    private static TeamSpeakProfile ParseProfile(string name, string? value)
    {
        var lines = ParseLines(value);
        return new TeamSpeakProfile(name, lines.GetValueOrDefault("Device"), lines.GetValueOrDefault("DeviceDisplayName"));
    }

    private static Dictionary<string, string> ParseLines(string? value)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (value is null) return result;
        foreach (string line in value.Split('\n'))
        {
            int eq = line.IndexOf('=');
            if (eq > 0) result[line[..eq]] = line[(eq + 1)..].TrimEnd('\r');
        }
        return result;
    }

    private static void CopyShared(string from, string to)
    {
        using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = new FileStream(to, FileMode.Create, FileAccess.Write);
        source.CopyTo(target);
    }

    private const string DefaultPreProcessing =
        "echo_reduction=false\nagc=true\ndelay_ptt_msecs=300\ndelay_ptt=true\ndenoise=true\ndenoiser_level=1\n" +
        "continous_transmission=false\nvad_mode=0\nvad=true\necho_reduction_db=10\nvad_over_ptt=false\n" +
        "typing_suppression=false\nvoiceactivation_level=-40\necho_cancellation=false";
}
