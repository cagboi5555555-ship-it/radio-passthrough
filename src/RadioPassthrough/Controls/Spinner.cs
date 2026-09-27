using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace RadioPassthrough.Controls;

// Small activity ring: a faint track with a rotating arc.
public sealed class Spinner : FrameworkElement
{
    private readonly RotateTransform _rotation = new();

    public Spinner()
    {
        RenderTransform = _rotation;
        RenderTransformOrigin = new Point(0.5, 0.5);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
                _rotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(0.9)) { RepeatBehavior = RepeatBehavior.Forever });
            else
                _rotation.BeginAnimation(RotateTransform.AngleProperty, null);
        };
    }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        double stroke = Math.Max(1.5, size / 9);
        double r = (size - stroke) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);

        var track = TryFindResource("Sunken") as Brush ?? Brushes.Gray;
        var arc = TryFindResource("Ink2") as Brush ?? Brushes.White;
        dc.DrawEllipse(null, new Pen(track, stroke), center, r, r);

        var figure = new PathFigure { StartPoint = new Point(center.X, center.Y - r) };
        figure.Segments.Add(new ArcSegment(new Point(center.X + r, center.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        dc.DrawGeometry(null, new Pen(arc, stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new PathGeometry([figure]));
    }
}
