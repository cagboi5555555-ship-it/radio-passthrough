using System.Text.Json;
using System.Text.Json.Serialization;
using RadioPassthrough.Core.Dsp;
using RadioPassthrough.Core.Ptt;

namespace RadioPassthrough.Core.Settings;

public sealed class AppSettings
{
    public const int CurrentSchema = 2;

    public int SchemaVersion { get; set; } = CurrentSchema;
    public bool GameAudioEnabled { get; set; } = true;
    public bool KeepRealDefaults { get; set; } = true;
    public bool HideUnusedCableDevices { get; set; } = true;
    public Dictionary<string, string> RememberedDefaults { get; set; } = new();
    public bool TrayHintShown { get; set; }
    public string? MicDeviceId { get; set; }
    public List<PttBinding> Bindings { get; set; } = PttBinding.AcreDefaults();
    public bool StartWithWindows { get; set; } = true;
    public string? PreviousTeamSpeakProfile { get; set; }
    public RadioPreviewSettings Preview { get; set; } = new();
    public bool FirstRunDone { get; set; }
}

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SettingsStore(string? directory = null)
    {
        Directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RadioPassthrough");
    }

    public string Directory { get; }

    public string FilePath => Path.Combine(Directory, "settings.json");

    // Settings from older versions load with defaults for anything new. A damaged file is kept aside as
    // settings.json.bad and the app starts with defaults rather than refusing to run.
    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
            settings.Bindings ??= PttBinding.AcreDefaults();
            settings.RememberedDefaults ??= new();
            settings.Preview ??= new RadioPreviewSettings();
            settings.SchemaVersion = AppSettings.CurrentSchema;
            return settings;
        }
        catch (Exception e) when (e is JsonException or NotSupportedException)
        {
            Diagnostics.Log.Warn($"Settings file was damaged ({e.Message}); starting with defaults.");
            try { File.Copy(FilePath, FilePath + ".bad", overwrite: true); } catch (IOException) { }
        }
        catch (IOException e)
        {
            Diagnostics.Log.Warn($"Couldn't read settings ({e.Message}); starting with defaults.");
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        System.IO.Directory.CreateDirectory(Directory);
        string temp = FilePath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, FilePath, overwrite: true);
    }
}
