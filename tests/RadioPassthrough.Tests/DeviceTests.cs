using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using RadioPassthrough.Core.Audio;

namespace RadioPassthrough.Tests;

// Real-device checks that stay silent: tones only ever go into VB-CABLE, never to speakers.
// They pass trivially on machines without VB-CABLE.
[Collection("Devices")]
public class DeviceTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private sealed class Tone(float frequency, float amplitude) : IWaveProvider
    {
        private long _n;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 2);

        public int Read(Span<byte> buffer)
        {
            var f = MemoryMarshal.Cast<byte, float>(buffer);
            for (int i = 0; i < f.Length / 2; i++, _n++)
                f[i * 2] = f[i * 2 + 1] = amplitude * MathF.Sin(2 * MathF.PI * frequency * _n / 48000);
            return buffer.Length;
        }
    }

    private static float Rms(ReadOnlySpan<float> x)
    {
        double s = 0;
        foreach (float v in x) s += v * v;
        return (float)Math.Sqrt(s / Math.Max(1, x.Length));
    }

    private static double Frequency(ReadOnlySpan<float> x)
    {
        int crossings = 0;
        for (int i = 1; i < x.Length; i++)
            if (x[i - 1] < 0 && x[i] >= 0) crossings++;
        return crossings / (x.Length / 48000.0);
    }

    [Fact]
    public async Task Tone_sent_into_the_cable_arrives_unchanged_at_TeamSpeaks_side()
    {
        var cableIn = AudioDevices.CableInput();
        var cableOut = AudioDevices.CableOutput();
        if (cableIn is null || cableOut is null) return;

        await using var sink = await CableSink.OpenAsync(cableIn.Id, new Tone(1000, 0.25f));
        await Task.Delay(300);
        var clip = await ClipRecorder.RecordDeviceAsync(cableOut.Id, TimeSpan.FromSeconds(1.5), null, CancellationToken.None);

        var steady = clip.AsSpan(4800);
        output.WriteLine($"cable: rms {Rms(steady):0.0000} freq {Frequency(steady):0.0} samples {clip.Length}");
        float expected = 0.25f / MathF.Sqrt(2);
        Assert.InRange(Rms(steady), expected * 0.89f, expected * 1.12f); // within ±1 dB
        Assert.InRange(Frequency(steady), 990, 1010);
    }

    [Fact]
    public void Setting_the_current_default_again_is_accepted_by_windows()
    {
        // A no-op on purpose: proves the Windows interface works without changing anything.
        var system = new WindowsDefaultDevices();
        var current = system.GetDefault(DataFlow.Render, DeviceRole.Console);
        if (current is null) return;
        PolicyConfig.SetDefault(current.Id, DeviceRole.Console);
        Assert.Equal(current.Id, system.GetDefault(DataFlow.Render, DeviceRole.Console)!.Id);
    }

    [Fact]
    public void Signature_check_trusts_windows_files_but_only_vb_audio_for_the_cable()
    {
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Assert.False(RadioPassthrough.Core.Setup.CableInstaller.IsSignedByVbAudio(explorer, out string signer));
        Assert.Contains("Microsoft", signer); // valid signature, wrong publisher

        string unsigned = Path.Combine(Path.GetTempPath(), $"rp-unsigned-{Guid.NewGuid():N}.exe");
        File.WriteAllBytes(unsigned, new byte[1024]);
        try
        {
            Assert.False(RadioPassthrough.Core.Setup.CableInstaller.IsSignedByVbAudio(unsigned, out _));
        }
        finally
        {
            File.Delete(unsigned);
        }
    }

    [Fact]
    public async Task Process_capture_hears_only_that_process()
    {
        var cableIn = AudioDevices.CableInput();
        if (cableIn is null) return;

        await using var sink = await CableSink.OpenAsync(cableIn.Id, new Tone(440, 0.3f));
        await Task.Delay(300);
        var clip = await ClipRecorder.RecordProcessAsync(Environment.ProcessId, TimeSpan.FromSeconds(1.5), null, CancellationToken.None);

        var steady = clip.AsSpan(Math.Min(clip.Length, 9600));
        output.WriteLine($"process: rms {Rms(steady):0.0000} freq {Frequency(steady):0.0} samples {clip.Length}");
        Assert.True(steady.Length > 24000, $"only {steady.Length} samples captured");
        Assert.InRange(Frequency(steady), 430, 450);
        Assert.True(Rms(steady) > 0.1f, $"rms {Rms(steady)}");
    }
}
