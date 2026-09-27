using System.Windows.Controls;
using System.Windows.Input;
using RadioPassthrough.ViewModels;

namespace RadioPassthrough.Views;

public partial class LivePage : UserControl
{
    public LivePage() => InitializeComponent();
}

public partial class SetupPage : UserControl
{
    public SetupPage() => InitializeComponent();
}

public partial class TestPage : UserControl
{
    public TestPage() => InitializeComponent();

    private TestViewModel? Vm => DataContext as TestViewModel;

    private void Hold_Down(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        Vm.Holding = true;
        HoldButton.CaptureMouse();
        e.Handled = true;
    }

    private void Hold_Up(object sender, MouseButtonEventArgs e)
    {
        if (Vm is null) return;
        Vm.Holding = false;
        HoldButton.ReleaseMouseCapture();
        e.Handled = true;
    }

    private void Hold_Leave(object sender, MouseEventArgs e)
    {
        if (Vm is { Holding: true } && !HoldButton.IsMouseCaptured) Vm.Holding = false;
    }
}
