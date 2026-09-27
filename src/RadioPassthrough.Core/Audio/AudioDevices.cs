using NAudio.CoreAudioApi;

namespace RadioPassthrough.Core.Audio;

public sealed record DeviceInfo(string Id, string Name);

public static class AudioDevices
{
    public const string CableInputName = "CABLE Input";
    public const string CableOutputName = "CABLE Output";

    public static IReadOnlyList<DeviceInfo> Microphones()
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Select(d => { using (d) return new DeviceInfo(d.ID, d.FriendlyName); })
            .Where(d => !IsCable(d.Name))
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static DeviceInfo? CableInput() => Find(DataFlow.Render, CableInputName);

    public static DeviceInfo? CableOutput() => Find(DataFlow.Capture, CableOutputName);

    public static DeviceInfo? DefaultMicrophone() => Default(DataFlow.Capture, Role.Communications) ?? Default(DataFlow.Capture, Role.Console);

    public static DeviceInfo? DefaultPlayback() => Default(DataFlow.Render, Role.Multimedia);

    public static DeviceInfo? DefaultRecording() => Default(DataFlow.Capture, Role.Console);

    public static DeviceInfo? DefaultCommunicationsPlayback() => Default(DataFlow.Render, Role.Communications);

    public static DeviceInfo? DefaultCommunicationsRecording() => Default(DataFlow.Capture, Role.Communications);

    public static bool Exists(string id)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(id);
            return device.State == DeviceState.Active;
        }
        catch
        {
            return false;
        }
    }

    public static int? SampleRate(string id)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDevice(id);
            using var client = device.CreateAudioClient();
            return client.MixFormat.SampleRate;
        }
        catch
        {
            return null;
        }
    }

    public static int Channels(MMDevice device)
    {
        try
        {
            using var client = device.CreateAudioClient();
            return Math.Max(1, client.MixFormat.Channels);
        }
        catch
        {
            return 2;
        }
    }

    public static bool IsCable(string name) =>
        name.StartsWith(CableInputName, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith(CableOutputName, StringComparison.OrdinalIgnoreCase)
        || name.Contains("VB-Audio Virtual Cable", StringComparison.OrdinalIgnoreCase);

    private static DeviceInfo? Find(DataFlow flow, string prefix)
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
        {
            using (device)
            {
                if (device.FriendlyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return new DeviceInfo(device.ID, device.FriendlyName);
            }
        }
        return null;
    }

    private static DeviceInfo? Default(DataFlow flow, Role role)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.TryGetDefaultAudioEndpoint(flow, role, out var device) || device is null) return null;
        using (device) return new DeviceInfo(device.ID, device.FriendlyName);
    }
}
