using System.Diagnostics;
using RadioPassthrough.Core.Dsp;

namespace RadioPassthrough.Core.Audio;

// Records a fixed length of mono audio from a capture device or from one process.
public static class ClipRecorder
{
    public static Task<float[]> RecordDeviceAsync(string deviceId, TimeSpan duration, IProgress<double>? progress, CancellationToken ct) =>
        RecordAsync(h => CaptureSource.OpenDeviceAsync(deviceId, h), duration, progress, ct);

    public static Task<float[]> RecordProcessAsync(int pid, TimeSpan duration, IProgress<double>? progress, CancellationToken ct) =>
        RecordAsync(h => CaptureSource.OpenProcessAsync(pid, "process", h), duration, progress, ct);

    private static async Task<float[]> RecordAsync(Func<CaptureHandler, Task<CaptureSource>> open, TimeSpan duration, IProgress<double>? progress, CancellationToken ct)
    {
        int length = (int)(duration.TotalSeconds * CaptureSource.SampleRate);
        var clip = new float[length];
        int written = 0;
        var gate = new object();
        var clock = Stopwatch.StartNew();
        float[] mono = new float[4096];

        void OnData(ReadOnlySpan<float> interleaved, int channels, bool silent)
        {
            int frames = interleaved.Length / channels;
            if (mono.Length < frames) mono = new float[frames * 2];
            Downmix.ToMono(interleaved, channels, MicChannelMode.Both, mono);
            lock (gate)
            {
                // Process captures stop delivering while the app is silent; keep the timeline honest.
                int due = (int)(clock.Elapsed.TotalSeconds * CaptureSource.SampleRate) - frames;
                if (due - written > 4800) written = Math.Min(length, due);
                int n = Math.Min(frames, length - written);
                if (n > 0) mono.AsSpan(0, n).CopyTo(clip.AsSpan(written));
                written += Math.Max(0, n);
            }
        }

        await using var source = await open(OnData).ConfigureAwait(false);
        clock.Restart();
        while (!ct.IsCancellationRequested && clock.Elapsed < duration)
        {
            progress?.Report(clock.Elapsed / duration);
            try
            {
                await Task.Delay(50, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
        progress?.Report(1);
        int elapsed = (int)(Math.Min(clock.Elapsed.TotalSeconds, duration.TotalSeconds) * CaptureSource.SampleRate);
        lock (gate) return clip[..Math.Min(length, Math.Max(written, elapsed))];
    }
}
