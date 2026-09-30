using NAudio.CoreAudioApi;
using RadioPassthrough.Core.Diagnostics;

namespace RadioPassthrough.Core.Audio;

// What the guard sees and changes; the real one talks to Windows, tests use a fake.
public interface IDefaultDeviceSystem
{
    DeviceInfo? GetDefault(DataFlow flow, DeviceRole role);
    IReadOnlyList<DeviceInfo> Active(DataFlow flow);
    void SetDefault(string deviceId, DeviceRole role);
}

public sealed class WindowsDefaultDevices : IDefaultDeviceSystem
{
    public DeviceInfo? GetDefault(DataFlow flow, DeviceRole role)
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.TryGetDefaultAudioEndpoint(flow, (Role)(int)role, out var device) || device is null) return null;
        using (device) return new DeviceInfo(device.ID, device.FriendlyName);
    }

    public IReadOnlyList<DeviceInfo> Active(DataFlow flow)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(d => { using (d) return new DeviceInfo(d.ID, d.FriendlyName); })
            .ToList();
    }

    public void SetDefault(string deviceId, DeviceRole role) => PolicyConfig.SetDefault(deviceId, role);
}

// Keeps VB-CABLE from becoming a Windows default device. Remembers your real speakers and mic for every
// role and puts them back whenever Windows (or the VB-CABLE installer) switches a default to the cable.
public sealed class DefaultDeviceGuard : IDisposable
{
    private static readonly DataFlow[] Flows = [DataFlow.Render, DataFlow.Capture];

    private readonly IDefaultDeviceSystem _system;
    private readonly Dictionary<string, string> _remembered;
    private readonly Func<string?> _preferredMic;
    private readonly object _lock = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDeviceNotificationClient? _notifications;
    private Timer? _debounce;

    public DefaultDeviceGuard(IDefaultDeviceSystem system, IReadOnlyDictionary<string, string>? remembered, Func<string?> preferredMic)
    {
        _system = system;
        _remembered = remembered is null ? new() : new(remembered);
        _preferredMic = preferredMic;
    }

    public bool Enabled { get; set; } = true;

    // Raised with the full remembered set whenever it changes, for saving.
    public event Action<IReadOnlyDictionary<string, string>>? RememberedChanged;

    // Raised with a sentence describing each device that was put back.
    public event Action<string>? Restored;

    public static string Key(DataFlow flow, DeviceRole role) => $"{flow}:{role}";

    public void Start()
    {
        _enumerator = new MMDeviceEnumerator();
        _notifications = _enumerator.CreateNotificationClient(false);
        _debounce = new Timer(_ => SafeCheck(), null, Timeout.Infinite, Timeout.Infinite);
        // Windows fires one notification per role; wait for the burst to finish.
        _notifications.DefaultDeviceChanged += (_, _) => Debounce(400);
        _notifications.DeviceStateChanged += (_, _) => Debounce(1000);
        SafeCheck();
    }

    // Windows can still deliver a notification while the app shuts down.
    private void Debounce(int milliseconds)
    {
        try
        {
            _debounce?.Change(milliseconds, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void SafeCheck()
    {
        try
        {
            Check();
        }
        catch (Exception e)
        {
            Log.Error("Default device check failed", e);
        }
    }

    // Returns how many defaults were put back.
    public int Check()
    {
        lock (_lock)
        {
            int fixedCount = 0;
            bool rememberedChanged = false;
            foreach (var flow in Flows)
            {
                var active = _system.Active(flow);
                foreach (var role in Enum.GetValues<DeviceRole>())
                {
                    string key = Key(flow, role);
                    var current = _system.GetDefault(flow, role);
                    if (current is null) continue;

                    if (!AudioDevices.IsCable(current.Name))
                    {
                        if (!_remembered.TryGetValue(key, out var id) || id != current.Id)
                        {
                            _remembered[key] = current.Id;
                            rememberedChanged = true;
                        }
                        continue;
                    }

                    if (!Enabled) continue;
                    var target = Choose(flow, role, active);
                    if (target is null)
                    {
                        Log.Warn($"{Describe(flow, role)} is the cable and there's no other device to switch to.");
                        continue;
                    }

                    _system.SetDefault(target.Id, role);
                    fixedCount++;
                    string message = $"Windows switched your {Describe(flow, role)} to the cable. Put back {target.Name}.";
                    Log.Info(message);
                    Restored?.Invoke(message);
                }
            }

            if (rememberedChanged) RememberedChanged?.Invoke(new Dictionary<string, string>(_remembered));
            return fixedCount;
        }
    }

    private DeviceInfo? Choose(DataFlow flow, DeviceRole role, IReadOnlyList<DeviceInfo> active)
    {
        var real = active.Where(d => !AudioDevices.IsCable(d.Name)).ToList();
        if (real.Count == 0) return null;

        DeviceInfo? Find(string? id) => id is null ? null : real.FirstOrDefault(d => d.Id == id);

        // 1. What this role used before. 2. What another role of the same kind uses.
        // 3. For mics, the one this app records from. 4. Any real device.
        return Find(_remembered.GetValueOrDefault(Key(flow, role)))
               ?? Enum.GetValues<DeviceRole>().Select(r => Find(_remembered.GetValueOrDefault(Key(flow, r)))).FirstOrDefault(d => d is not null)
               ?? (flow == DataFlow.Capture ? Find(_preferredMic()) : null)
               ?? real[0];
    }

    private static string Describe(DataFlow flow, DeviceRole role) => (flow, role) switch
    {
        (DataFlow.Render, DeviceRole.Communications) => "default communication speakers",
        (DataFlow.Render, _) => "default speakers",
        (DataFlow.Capture, DeviceRole.Communications) => "default communication microphone",
        _ => "default microphone",
    };

    public void Dispose()
    {
        _notifications?.Dispose();
        _debounce?.Dispose();
        _enumerator?.Dispose();
    }
}

public static class CableHousekeeping
{
    // VB-CABLE adds devices this app never uses (for example "CABLE In 16ch"). Hiding them keeps
    // Windows and games from picking them. Returns the names that were hidden.
    public static IReadOnlyList<string> HideUnusedEndpoints()
    {
        var hidden = new List<string>();
        using var enumerator = new MMDeviceEnumerator();
        foreach (var flow in new[] { DataFlow.Render, DataFlow.Capture })
        {
            foreach (var device in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            {
                using (device)
                {
                    string name = device.FriendlyName;
                    if (!AudioDevices.IsCable(name)) continue;
                    bool used = name.StartsWith(flow == DataFlow.Render ? AudioDevices.CableInputName : AudioDevices.CableOutputName, StringComparison.OrdinalIgnoreCase);
                    if (used) continue;
                    try
                    {
                        PolicyConfig.SetVisibility(device.ID, false);
                        hidden.Add(name);
                        Log.Info($"Hid unused cable device \"{name}\".");
                    }
                    catch (Exception e) when (e is System.Runtime.InteropServices.COMException or UnauthorizedAccessException)
                    {
                        Log.Warn($"Couldn't hide \"{name}\": {e.Message}");
                    }
                }
            }
        }
        return hidden;
    }
}
