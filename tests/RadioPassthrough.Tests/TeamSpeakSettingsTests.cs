using RadioPassthrough.Core.Native;
using RadioPassthrough.Core.Setup;

namespace RadioPassthrough.Tests;

public sealed class TeamSpeakSettingsTests : IDisposable
{
    private const string CableId = "{0.0.1.00000000}.{11111111-2222-3333-4444-555555555555}";
    private const string MicId = "{0.0.1.00000000}.{aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee}";
    private const string PreProcessing = "echo_reduction=false\nagc=true\ndenoise=true\ndenoiser_level=1\nvad=true";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"rp-test-{Guid.NewGuid():N}");

    public TeamSpeakSettingsTests()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "plugins"));
        File.WriteAllBytes(Path.Combine(_dir, "plugins", "acre2_win64.dll"), []);
        using var db = SqliteDatabase.Create(Path.Combine(_dir, "settings.db"));
        db.Execute("CREATE TABLE Profiles (timestamp INTEGER UNSIGNED NOT NULL, key VARCHAR NOT NULL UNIQUE, value VARCHAR)");
        foreach (var (key, value) in new (string, string?)[]
                 {
                     ("Capture/", null),
                     ("DefaultCaptureProfile", "Default"),
                     ("DefaultPlaybackProfile", "Default"),
                     ("Capture/Default", $"DeviceDisplayName=In 1-2 (USB Audio Interface)\nDevice={MicId}\nMode="),
                     ("Capture/Default/PreProcessing", PreProcessing),
                 })
            db.Execute("INSERT INTO Profiles VALUES (1, ?, ?)", key, value);
    }

    private string? Value(string key)
    {
        using var db = SqliteDatabase.Open(Path.Combine(_dir, "settings.db"), readOnly: true);
        return db.Query("SELECT value FROM Profiles WHERE key = ?", key).FirstOrDefault()?[0];
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
        Assert.Equal($"DeviceDisplayName=In 1-2 (USB Audio Interface)\nDevice={MicId}\nMode=", Value("Capture/Default"));
        Assert.True(File.Exists(ts.LastBackupPath));
        Assert.Equal(CableId, ts.Read().ActiveCapture?.DeviceId);
    }

    [Fact]
    public void Applying_twice_keeps_the_real_previous_profile()
    {
        var ts = new TeamSpeakSettings(_dir, () => false);
        ts.Apply(CableId, "CABLE Output");
        Assert.Equal("Default", ts.Apply(CableId, "CABLE Output"));
    }

    [Fact]
    public void Restore_switches_back_and_remove_deletes_the_profile()
    {
        var ts = new TeamSpeakSettings(_dir, () => false);
        string previous = ts.Apply(CableId, "CABLE Output");
        ts.Restore(previous);
        Assert.Equal("Default", Value("DefaultCaptureProfile"));

        ts.Apply(CableId, "CABLE Output");
        ts.RemoveProfile(previous);
        Assert.Equal("Default", Value("DefaultCaptureProfile"));
        Assert.Null(Value("Capture/Radio Passthrough"));
        Assert.Null(Value("Capture/Radio Passthrough/PreProcessing"));
    }

    [Fact]
    public void Refuses_while_teamspeak_is_running()
    {
        var ts = new TeamSpeakSettings(_dir, () => true);
        Assert.Throws<InvalidOperationException>(() => ts.Apply(CableId, "CABLE Output"));
        Assert.Equal("Default", Value("DefaultCaptureProfile"));
    }

    [Fact]
    public void Keeps_only_the_newest_backups()
    {
        var ts = new TeamSpeakSettings(_dir, () => false);
        for (int i = 0; i < 8; i++)
        {
            File.WriteAllText(Path.Combine(_dir, $"settings.db.radiopassthrough-2020010{i}-000000.bak"), "old");
        }
        ts.Apply(CableId, "CABLE Output");
        Assert.Equal(5, Directory.EnumerateFiles(_dir, "settings.db.radiopassthrough-*.bak").Count());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }
}
