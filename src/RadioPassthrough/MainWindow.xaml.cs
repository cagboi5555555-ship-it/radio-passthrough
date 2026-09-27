using System.ComponentModel;
using System.Windows;
using RadioPassthrough.Theme;
using RadioPassthrough.ViewModels;

namespace RadioPassthrough;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) => ThemeManager.StyleTitleBar(this);
        IsVisibleChanged += (_, _) => viewModel.SetVisible(IsVisible);
    }

    public bool AllowClose { get; set; }

    public event Action? HiddenToTray;

    // Closing hides to the tray; the passthrough keeps running for TeamSpeak.
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
            HiddenToTray?.Invoke();
        }
        base.OnClosing(e);
    }
}
