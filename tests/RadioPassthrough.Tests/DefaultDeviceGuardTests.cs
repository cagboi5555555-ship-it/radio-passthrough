using NAudio.CoreAudioApi;
using RadioPassthrough.Core.Audio;

namespace RadioPassthrough.Tests;

public class DefaultDeviceGuardTests
{
    private sealed class FakeSystem : IDefaultDeviceSystem
    {
        public readonly Dictionary<(DataFlow, DeviceRole), DeviceInfo> Defaults = new();
        public readonly List<DeviceInfo> Render = [];
        public readonly List<DeviceInfo> Capture = [];
        public readonly List<(string Id, DeviceRole Role)> Changes = [];

        public DeviceInfo? GetDefault(DataFlow flow, DeviceRole role) => Defaults.GetValueOrDefault((flow, role));

        public IReadOnlyList<DeviceInfo> Active(DataFlow flow) => flow == DataFlow.Render ? Render : Capture;

        public void SetDefault(string deviceId, DeviceRole role)
        {
            Changes.Add((deviceId, role));
            var flow = Render.Any(d => d.Id == deviceId) ? DataFlow.Render : DataFlow.Capture;
            Defaults[(flow, role)] = (flow == DataFlow.Render ? Render : Capture).First(d => d.Id == deviceId);
        }

        public void SetAll(DataFlow flow, DeviceInfo device)
        {
            foreach (var role in Enum.GetValues<DeviceRole>()) Defaults[(flow, role)] = device;
        }
    }

    private static readonly DeviceInfo Headset = new("out-headset", "Speakers (Razer BlackShark V2 Pro)");
    private static readonly DeviceInfo Motu = new("out-motu", "Out 1-2 (MOTU M Series)");
    private static readonly DeviceInfo CableIn = new("out-cable", "CABLE Input (VB-Audio Virtual Cable)");
    private static readonly DeviceInfo Cable16 = new("out-cable16", "CABLE In 16ch (VB-Audio Virtual Cable)");
    private static readonly DeviceInfo Mic = new("in-motu", "In 1-2 (MOTU M Series)");
    private static readonly DeviceInfo CableOut = new("in-cable", "CABLE Output (VB-Audio Virtual Cable)");

    private static FakeSystem System()
    {
        var s = new FakeSystem();
        s.Render.AddRange([Headset, Motu, CableIn, Cable16]);
        s.Capture.AddRange([Mic, CableOut]);
        s.SetAll(DataFlow.Render, Motu);
        s.SetAll(DataFlow.Capture, Mic);
        return s;
    }

    [Fact]
    public void Puts_back_the_devices_you_had_when_the_cable_takes_over()
    {
        var system = System();
        var guard = new DefaultDeviceGuard(system, null, () => null);
        Assert.Equal(0, guard.Check()); // learns MOTU + mic

        system.SetAll(DataFlow.Render, Cable16);  // what the VB-CABLE installer tends to do
        system.SetAll(DataFlow.Capture, CableOut);
        Assert.Equal(6, guard.Check());

        Assert.All(Enum.GetValues<DeviceRole>(), r =>
        {
            Assert.Equal(Motu.Id, system.GetDefault(DataFlow.Render, r)!.Id);
            Assert.Equal(Mic.Id, system.GetDefault(DataFlow.Capture, r)!.Id);
        });
    }

    [Fact]
    public void Remembers_across_restarts()
    {
        var system = System();
        IReadOnlyDictionary<string, string>? saved = null;
        var first = new DefaultDeviceGuard(system, null, () => null);
        first.RememberedChanged += r => saved = r;
        first.Check();

        system.SetAll(DataFlow.Render, CableIn);
        var second = new DefaultDeviceGuard(system, saved, () => null);
        second.Check();
        Assert.Equal(Motu.Id, system.GetDefault(DataFlow.Render, DeviceRole.Console)!.Id);
    }

    [Fact]
    public void With_nothing_remembered_uses_the_apps_mic_then_any_real_device()
    {
        var system = System();
        system.SetAll(DataFlow.Render, CableIn);
        system.SetAll(DataFlow.Capture, CableOut);
        new DefaultDeviceGuard(system, null, () => Mic.Id).Check();
        Assert.Equal(Mic.Id, system.GetDefault(DataFlow.Capture, DeviceRole.Communications)!.Id);
        Assert.DoesNotContain("CABLE", system.GetDefault(DataFlow.Render, DeviceRole.Console)!.Name);
    }

    [Fact]
    public void Does_nothing_when_switched_off_or_when_no_real_device_exists()
    {
        var system = System();
        system.SetAll(DataFlow.Render, CableIn);
        new DefaultDeviceGuard(system, null, () => null) { Enabled = false }.Check();
        Assert.Empty(system.Changes);

        system.Render.RemoveAll(d => !d.Name.Contains("CABLE"));
        new DefaultDeviceGuard(system, null, () => null).Check();
        Assert.Empty(system.Changes);
    }
}
