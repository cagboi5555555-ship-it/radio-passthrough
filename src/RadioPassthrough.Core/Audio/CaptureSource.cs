using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RadioPassthrough.Core.Audio;

public delegate void CaptureHandler(ReadOnlySpan<float> interleaved, int channels, bool silent);

// A running capture from a device or from one process's audio, delivered as 48 kHz float.
public sealed class CaptureSource : IAsyncDisposable
{
    public const int SampleRate = 48000;

    private readonly WasapiRecorder _recorder;
    private readonly CaptureHandler _handler;
    private float[] _silence = [];
    private int _stopping;

    private CaptureSource(WasapiRecorder recorder, string name, CaptureHandler handler)
    {
        _recorder = recorder;
        _handler = handler;
        Name = name;
        _recorder.DataAvailable += OnData;
        _recorder.RecordingStopped += OnStopped;
    }

    public string Name { get; }

    // Raised when capture ends on its own (device unplugged, process gone, error).
    public event Action<Exception?>? Faulted;

    public static Task<CaptureSource> OpenDeviceAsync(string deviceId, CaptureHandler handler) => Task.Run(() =>
    {
        using var enumerator = new MMDeviceEnumerator();
        var device = enumerator.GetDevice(deviceId);
        int channels = AudioDevices.Channels(device);
        var recorder = new WasapiRecorderBuilder()
            .WithDevice(device)
            .WithSharedMode()
            .WithEventSync()
            .WithBufferLength(20)
            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, channels))
            .WithMmcssThreadPriority("Pro Audio")
            .Build();
        var source = new CaptureSource(recorder, device.FriendlyName, handler);
        recorder.StartRecording();
        return source;
    });

    public static Task<CaptureSource> OpenProcessAsync(int pid, string name, CaptureHandler handler) => Task.Run(async () =>
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            throw new PlatformNotSupportedException("Capturing one app's audio needs Windows 10 version 2004 or newer.");
        var recorder = await new WasapiRecorderBuilder()
            .WithProcessLoopback((uint)pid, ProcessLoopbackMode.IncludeTargetProcessTree)
            .WithFormat(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2))
            .WithBufferLength(20)
            .WithMmcssThreadPriority("Pro Audio")
            .BuildAsync()
            .ConfigureAwait(false);
        var source = new CaptureSource(recorder, name, handler);
        recorder.StartRecording();
        return source;
    });

    private bool _loggedFailure;

    // Runs on the Windows capture thread: nothing may escape from here.
    private void OnData(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        try
        {
            int channels = _recorder.WaveFormat.Channels;
            bool silent = (flags & AudioClientBufferFlags.Silent) != 0;
            if (silent)
            {
                int count = buffer.Length / sizeof(float);
                if (_silence.Length < count) _silence = new float[count];
                _handler(_silence.AsSpan(0, count), channels, true);
                return;
            }
            _handler(MemoryMarshal.Cast<byte, float>(buffer), channels, false);
        }
        catch (Exception e)
        {
            if (!_loggedFailure) Diagnostics.Log.Error($"Capture from {Name} failed", e);
            _loggedFailure = true;
        }
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (Volatile.Read(ref _stopping) == 0)
            Faulted?.Invoke(e.Exception);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _stopping, 1) == 1) return;
        _recorder.DataAvailable -= OnData;
        try
        {
            _recorder.StopRecording();
        }
        catch
        {
            // Already stopped because the device went away.
        }
        await _recorder.DisposeAsync().ConfigureAwait(false);
    }
}
