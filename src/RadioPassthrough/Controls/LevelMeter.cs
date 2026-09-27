using System.Windows;
using System.Windows.Media;

namespace RadioPassthrough.Controls;

// Thin rounded level bar with a peak tick.
public sealed class LevelMeter : FrameworkElement
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PeakProperty = DependencyProperty.Register(
        nameof(Peak), typeof(double), typeof(LevelMeter), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(LevelMeter), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackProperty = DependencyProperty.Register(
        nameof(Track), typeof(Brush), typeof(LevelMeter), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public double Peak { get => (double)GetValue(PeakProperty); set => SetValue(PeakProperty, value); }
    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush Track { get => (Brush)GetValue(TrackProperty); set => SetValue(TrackProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(0, 6);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = Math.Min(6, ActualHeight);
        double y = (ActualHeight - h) / 2;
        double r = h / 2;
        dc.DrawRoundedRectangle(Track, null, new Rect(0, y, w, h), r, r);

        double level = Math.Clamp(Level, 0, 1);
        if (level > 0.001)
        {
            double fw = Math.Max(h, w * level);
            dc.DrawRoundedRectangle(Fill, null, new Rect(0, y, fw, h), r, r);
        }

        double peak = Math.Clamp(Peak, 0, 1);
        if (peak > level + 0.01)
            dc.DrawRoundedRectangle(Fill, null, new Rect(Math.Max(0, w * peak - 2), y, 2, h), 1, 1);
    }
}
