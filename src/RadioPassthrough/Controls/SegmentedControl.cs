using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace RadioPassthrough.Controls;

// Apple-style segmented control: a sunken track with the selected segment raised.
public sealed class SegmentedControl : Border
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(
        nameof(Items), typeof(IEnumerable), typeof(SegmentedControl), new PropertyMetadata(null, (d, _) => ((SegmentedControl)d).Rebuild()));

    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(
        nameof(SelectedIndex), typeof(int), typeof(SegmentedControl),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((SegmentedControl)d).SyncChecked()));

    public static readonly DependencyProperty SegmentHeightProperty = DependencyProperty.Register(
        nameof(SegmentHeight), typeof(double), typeof(SegmentedControl), new PropertyMetadata(28.0, (d, _) => ((SegmentedControl)d).Rebuild()));

    private readonly UniformGrid _grid = new() { Rows = 1 };
    private readonly string _group = Guid.NewGuid().ToString("N");

    public SegmentedControl()
    {
        SetResourceReference(BackgroundProperty, "Sunken");
        CornerRadius = new CornerRadius(9);
        Padding = new Thickness(2);
        SnapsToDevicePixels = true;
        Child = _grid;
    }

    public IEnumerable? Items
    {
        get => (IEnumerable?)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public double SegmentHeight
    {
        get => (double)GetValue(SegmentHeightProperty);
        set => SetValue(SegmentHeightProperty, value);
    }

    private void Rebuild()
    {
        _grid.Children.Clear();
        if (Items is null) return;
        int index = 0;
        foreach (object item in Items)
        {
            int i = index++;
            var button = new RadioButton
            {
                Content = item,
                GroupName = _group,
                Height = SegmentHeight,
                IsChecked = i == SelectedIndex,
            };
            button.SetResourceReference(StyleProperty, "SegmentButton");
            button.Checked += (_, _) => SelectedIndex = i;
            _grid.Children.Add(button);
        }
        _grid.Columns = Math.Max(1, index);
    }

    private void SyncChecked()
    {
        for (int i = 0; i < _grid.Children.Count; i++)
            if (_grid.Children[i] is RadioButton b) b.IsChecked = i == SelectedIndex;
    }
}
