using System.Diagnostics;
using Microsoft.Data.Sqlite;

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

// Reads and edits TeamSpeak 3's capture profiles in %APPDATA%\TS3Client\settings.db. Edits are only
// made while TeamSpeak is closed (it rewrites the file on exit) and always after a backup.
public sealed class TeamSpeakSettings
{
    public const string ProfileName = "Radio Passthrough";

    public TeamSpeakSettings(string? configDirectory = null, Func<bool>? isRunning = null)
    {
        ConfigDirectory = configDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TS3Client");
        _isRunning = isRunning ?? IsTeamSpeakRunning;
    }

    private readonly Func<bool> _isRunning;

    public string ConfigDirectory { get; }

    public string DatabasePath => Path.Combine(ConfigDirectory, "settings.db");

    public static bool IsTeamSpeakRunning()
    {
        foreach (string name in new[] { "ts3client_win64", "ts3client_win32" })
        {
            var processes = Process.GetProcessesByName(name);
            foreach (var p in processes) p.Dispose();
            if (processes.Length > 0) return true;
        }
        return false;
    }

    public TeamSpeakState Read()
    {
        bool plugin = File.Exists(Path.Combine(ConfigDirectory, "plugins", "acre2_win64.dll"))
                      || File.Exists(Path.Combine(ConfigDirectory, "plugins", "acre2_win32.dll"));
        if (!File.Exists(DatabasePath))
            return new TeamSpeakState { Installed = false, Running = _isRunning(), AcrePluginInstalled = plugin };

        // Read a private copy so TeamSpeak's own locks and journal are never touched.
        string temp = Path.Combine(Path.GetTempPath(), $"rp-ts3-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temp);
        try
        {
            string copy = Path.Combine(temp, "settings.db");
            CopyShared(DatabasePath, copy);
            if (File.Exists(DatabasePath + "-journal")) CopyShared(DatabasePath + "-journal", copy + "-journal");

            using var connection = Open(copy, SqliteOpenMode.ReadWrite);
            var values = ReadProfiles(connection);
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
            SqliteConnection.ClearAllPools();
            try { Directory.Delete(temp, recursive: true); } catch { /* temp cleanup is best effort */ }
        }
    }

    // Adds (or refreshes) the Radio Passthrough capture profile using the cable as its device and a
    // verbatim copy of the current profile's processing, then makes it the default for all servers.
    // Returns the name of the profile that was the default before, for restoring later.
    public string Apply(string cableDeviceId, string cableDeviceName)
    {
        EnsureClosed();
        Backup();

        using var connection = Open(DatabasePath, SqliteOpenMode.ReadWrite);
        using var tx = connection.BeginTransaction();
        var values = ReadProfiles(connection, tx);

        string previous = values.GetValueOrDefault("DefaultCaptureProfile") ?? "Default";
        string source = previous == ProfileName || !values.ContainsKey($"Capture/{previous}") ? "Default" : previous;

        string mode = "";
        if (values.GetValueOrDefault($"Capture/{source}") is { } sourceProfile)
            mode = ParseLines(sourceProfile).GetValueOrDefault("Mode") ?? "";
        string preprocessing = values.GetValueOrDefault($"Capture/{source}/PreProcessing") ?? DefaultPreProcessing;

        Upsert(connection, tx, $"Capture/{ProfileName}", $"DeviceDisplayName={cableDeviceName}\nDevice={cableDeviceId}\nMode={mode}");
        Upsert(connection, tx, $"Capture/{ProfileName}/PreProcessing", preprocessing);
        Upsert(connection, tx, "DefaultCaptureProfile", ProfileName);
        tx.Commit();
        SqliteConnection.ClearPool(connection);
        return previous == ProfileName ? "Default" : previous;
    }

    public void Restore(string? previousProfile)
    {
        EnsureClosed();
        Backup();
        using var connection = Open(DatabasePath, SqliteOpenMode.ReadWrite);
        using var tx = connection.BeginTransaction();
        var values = ReadProfiles(connection, tx);
        string target = previousProfile is { } p && p != ProfileName && values.ContainsKey($"Capture/{p}") ? p : "Default";
        Upsert(connection, tx, "DefaultCaptureProfile", target);
        tx.Commit();
        SqliteConnection.ClearPool(connection);
    }

    public string? LastBackupPath { get; private set; }

    private void EnsureClosed()
    {
        if (!File.Exists(DatabasePath))
            throw new InvalidOperationException("TeamSpeak 3 settings weren't found. Start TeamSpeak once, close it, then try again.");
        if (_isRunning())
            throw new InvalidOperationException("Close TeamSpeak first. It rewrites its settings when it exits.");
    }

    private void Backup()
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string target = Path.Combine(ConfigDirectory, $"settings.db.radiopassthrough-{stamp}.bak");
        CopyShared(DatabasePath, target);
        if (File.Exists(DatabasePath + "-journal")) CopyShared(DatabasePath + "-journal", target + "-journal");
        LastBackupPath = target;
    }

    private static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static Dictionary<string, string?> ReadProfiles(SqliteConnection connection, SqliteTransaction? tx = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT key, value FROM Profiles";
        using var reader = command.ExecuteReader();
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        while (reader.Read())
            values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        return values;
    }

    private static void Upsert(SqliteConnection connection, SqliteTransaction tx, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO Profiles (timestamp, key, value) VALUES ($ts, $key, $value)
            ON CONFLICT(key) DO UPDATE SET timestamp = excluded.timestamp, value = excluded.value
            """;
        command.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

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
