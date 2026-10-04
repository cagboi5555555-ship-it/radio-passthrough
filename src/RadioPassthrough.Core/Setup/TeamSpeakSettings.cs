using RadioPassthrough.Core.Diagnostics;
using RadioPassthrough.Core.Native;

namespace RadioPassthrough.Core.Setup;

// MissesDirectSpeech: the profile's voice activation never opens, so TeamSpeak only sends you on the radio.
public sealed record TeamSpeakProfile(string Name, string? DeviceId, string? DeviceName, bool MissesDirectSpeech = false);

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
                .Select(n => ParseProfile(n, values[$"Capture/{n}"], values.GetValueOrDefault($"Capture/{n}/PreProcessing")))
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

    // Adds (or refreshes) the Radio Passthrough capture profile with the cable as its device and a copy of
    // your normal profile's processing, then makes it the default for all servers. Returns the profile that
    // was the default before, for restoring later. normalProfile names it when the passthrough is already on.
    public string Apply(string cableDeviceId, string cableDeviceName, string? normalProfile = null)
    {
        EnsureClosed();
        Backup();

        string previous = "Default";
        using var connection = SqliteDatabase.Open(DatabasePath);
        connection.InTransaction(() =>
        {
            var values = ReadProfiles(connection);
            string current = values.GetValueOrDefault("DefaultCaptureProfile") ?? "Default";
            previous = current != ProfileName ? current
                : normalProfile is { } n && n != ProfileName && values.ContainsKey($"Capture/{n}") ? n : "Default";
            string source = values.ContainsKey($"Capture/{previous}") ? previous : "Default";

            string mode = "";
            if (values.GetValueOrDefault($"Capture/{source}") is { } sourceProfile)
                mode = ParseLines(sourceProfile).GetValueOrDefault("Mode") ?? "";
            string preprocessing = values.GetValueOrDefault($"Capture/{source}/PreProcessing") ?? DefaultPreProcessing;
            if (MissesDirectSpeech(preprocessing))
                preprocessing = WithValue(WithValue(preprocessing, "denoise", "true"), "denoiser_level", "1");

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

    private static TeamSpeakProfile ParseProfile(string name, string? value, string? preprocessing)
    {
        var lines = ParseLines(value);
        return new TeamSpeakProfile(name, lines.GetValueOrDefault("Device"), lines.GetValueOrDefault("DeviceDisplayName"), MissesDirectSpeech(preprocessing));
    }

    // TeamSpeak's Automatic and Hybrid voice activation (vad_mode 0 and 2; 1 is Volume Gate, as its options
    // dialog stores them) work from its "Remove background noise". With that off they never open: direct
    // speech isn't sent at all, and only the radio gets through, because ACRE switches transmission on itself.
    public static bool MissesDirectSpeech(string? preprocessing)
    {
        var p = ParseLines(preprocessing);
        return p.GetValueOrDefault("vad") == "true"
               && p.GetValueOrDefault("continous_transmission") != "true"
               && p.GetValueOrDefault("denoise") != "true"
               && p.GetValueOrDefault("vad_mode", "0") != "1";
    }

    private static string WithValue(string preprocessing, string key, string value)
    {
        var lines = preprocessing.Split('\n').ToList();
        int i = lines.FindIndex(l => l.StartsWith(key + "=", StringComparison.Ordinal));
        if (i >= 0) lines[i] = $"{key}={value}";
        else lines.Add($"{key}={value}");
        return string.Join('\n', lines);
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
