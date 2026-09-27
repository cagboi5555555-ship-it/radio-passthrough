using System.Text.Json;
using System.Text.Json.Serialization;
using RadioPassthrough.Core.Dsp;
using RadioPassthrough.Core.Ptt;

namespace RadioPassthrough.Core.Settings;

public sealed class AppSettings
{
    public string? MicDeviceId { get; set; }
    public MicChannelMode MicChannels { get; set; } = MicChannelMode.Both;
    public MixPreset Preset { get; set; } = MixPreset.DocOneToOne;
    public MixSettings Custom { get; set; } = MixSettings.CustomDefault;
    public List<PttBinding> Bindings { get; set; } = PttBinding.AcreDefaults();
    public bool StartWithWindows { get; set; } = true;
    public string? PreviousTeamSpeakProfile { get; set; }
    public RadioPreviewSettings Preview { get; set; } = new();
    public bool FirstRunDone { get; set; }

    [JsonIgnore]
    public MixSettings ActiveMix => Preset == MixPreset.DocOneToOne ? MixSettings.DocOneToOne : Custom;
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

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // A damaged file falls back to defaults rather than stopping the app.
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
