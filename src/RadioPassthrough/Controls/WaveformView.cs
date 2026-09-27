using System.Windows;
using System.Windows.Media;

namespace RadioPassthrough.Controls;

// Voice-memo style waveform: rounded bars, a soft band where the radio key was held, and a playhead.
public sealed class WaveformView : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(float[]), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpansProperty = DependencyProperty.Register(
        nameof(Spans), typeof(IReadOnlyList<(double Start, double End)>), typeof(WaveformView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayheadProperty = DependencyProperty.Register(
        nameof(Playhead), typeof(double), typeof(WaveformView), new FrameworkPropertyMetadata(-1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BarBrushProperty = DependencyProperty.Register(
        nameof(BarBrush), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty BandBrushProperty = DependencyProperty.Register(
        nameof(BandBrush), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PlayheadBrushProperty = DependencyProperty.Register(
        nameof(PlayheadBrush), typeof(Brush), typeof(WaveformView), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public float[]? Samples { get => (float[]?)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }
    public IReadOnlyList<(double Start, double End)>? Spans { get => (IReadOnlyList<(double, double)>?)GetValue(SpansProperty); set => SetValue(SpansProperty, value); }
    public double Playhead { get => (double)GetValue(PlayheadProperty); set => SetValue(PlayheadProperty, value); }
    public Brush BarBrush { get => (Brush)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public Brush BandBrush { get => (Brush)GetValue(BandBrushProperty); set => SetValue(BandBrushProperty, value); }
    public Brush PlayheadBrush { get => (Brush)GetValue(PlayheadBrushProperty); set => SetValue(PlayheadBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        if (Spans is { } spans)
            foreach (var (start, end) in spans)
                dc.DrawRoundedRectangle(BandBrush, null, new Rect(start * w, 0, Math.Max(2, (end - start) * w), h), 6, 6);

        var samples = Samples;
        if (samples is { Length: > 0 })
        {
            const double barWidth = 2.5, gap = 2;
            int bars = Math.Max(1, (int)((w - 8) / (barWidth + gap)));
            int per = Math.Max(1, samples.Length / bars);
            double mid = h / 2;
            for (int b = 0; b < bars; b++)
            {
                int from = b * per, to = Math.Min(samples.Length, from + per);
                double sum = 0;
                for (int i = from; i < to; i++) sum += samples[i] * samples[i];
                double rms = to > from ? Math.Sqrt(sum / (to - from)) : 0;
                // Perceptual scale so quiet speech is still visible next to gunfire.
                double scaled = Math.Clamp((20 * Math.Log10(Math.Max(rms, 1e-5)) + 54) / 54, 0.04, 1);
                double barH = Math.Max(2.5, scaled * (h - 10));
                double x = 4 + b * (barWidth + gap);
                dc.DrawRoundedRectangle(BarBrush, null, new Rect(x, mid - barH / 2, barWidth, barH), 1.25, 1.25);
            }
        }

        if (Playhead is >= 0 and <= 1)
            dc.DrawRoundedRectangle(PlayheadBrush, null, new Rect(Math.Clamp(Playhead * w - 1, 0, w - 2), 2, 2, h - 4), 1, 1);
    }
}
