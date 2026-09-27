using Microsoft.Data.Sqlite;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough.Tests;

public sealed class TeamSpeakSettingsTests : IDisposable
{
    private const string CableId = "{0.0.1.00000000}.{11111111-2222-3333-4444-555555555555}";
    private const string MicId = "{0.0.1.00000000}.{2b517207-7c94-4d3a-ad08-62de24c1c7c7}";
    private const string PreProcessing = "echo_reduction=false\nagc=true\ndenoise=true\ndenoiser_level=1\nvad=true";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rp-test-{Guid.NewGuid():N}");

    public TeamSpeakSettingsTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "plugins"));
        File.WriteAllBytes(Path.Combine(_dir, "plugins", "acre2_win64.dll"), []);
        using var c = new SqliteConnection($"Data Source={Path.Combine(_dir, "settings.db")};Pooling=False");
        c.Open();
        Exec(c, "CREATE TABLE Profiles (timestamp INTEGER UNSIGNED NOT NULL, key VARCHAR NOT NULL UNIQUE, value VARCHAR)");
        Insert(c, "Capture/", null);
        Insert(c, "DefaultCaptureProfile", "Default");
        Insert(c, "DefaultPlaybackProfile", "Default");
        Insert(c, "Capture/Default", $"DeviceDisplayName=In 1-2 (MOTU M Series)\nDevice={MicId}\nMode=");
        Insert(c, "Capture/Default/PreProcessing", PreProcessing);
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private static void Insert(SqliteConnection c, string key, string? value)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO Profiles VALUES (1, $k, $v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", (object?)value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private string? Value(string key)
    {
        using var c = new SqliteConnection($"Data Source={Path.Combine(_dir, "settings.db")};Mode=ReadOnly;Pooling=False");
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM Profiles WHERE key = $k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    [Fact]
    public void Reads_current_profile()
    {
        var state = new TeamSpeakSettings(_dir, () => false).Read();
        Assert.True(state.Installed);
        Assert.True(state.AcrePluginInstalled);
        Assert.Equal("Default", state.DefaultCaptureProfile);
        Assert.Equal(MicId, state.ActiveCapture?.DeviceId);
    }

    [Fact]
    public void Apply_adds_profile_with_unchanged_processing_and_makes_it_default()
    {
        var ts = new TeamSpeakSettings(_dir, () => false);
        string previous = ts.Apply(CableId, "CABLE Output (VB-Audio Virtual Cable)");

        Assert.Equal("Default", previous);
        Assert.Equal(TeamSpeakSettings.ProfileName, Value("DefaultCaptureProfile"));
        Assert.Equal($"DeviceDisplayName=CABLE Output (VB-Audio Virtual Cable)\nDevice={CableId}\nMode=", Value("Capture/Radio Passthrough"));
        Assert.Equal(PreProcessing, Value("Capture/Radio Passthrough/PreProcessing"));
        Assert.Equal($"DeviceDisplayName=In 1-2 (MOTU M Series)\nDevice={MicId}\nMode=", Value("Capture/Default")); // untouched
        Assert.True(File.Exists(ts.LastBackupPath));

        var state = ts.Read();
        Assert.Equal(CableId, state.ActiveCapture?.DeviceId);
    }

    [Fact]
    public void Restore_switches_back()
    {
        var ts = new TeamSpeakSettings(_dir, () => false);
        string previous = ts.Apply(CableId, "CABLE Output");
        ts.Restore(previous);
        Assert.Equal("Default", Value("DefaultCaptureProfile"));
    }

    [Fact]
    public void Refuses_while_teamspeak_is_running()
    {
        var ts = new TeamSpeakSettings(_dir, () => true);
        Assert.Throws<InvalidOperationException>(() => ts.Apply(CableId, "CABLE Output"));
        Assert.Equal("Default", Value("DefaultCaptureProfile"));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch { }
    }
}
