using System.Windows;
using System.Windows.Input;

namespace RadioPassthrough.Dialogs;

// A small floating sheet: title, one paragraph, one or two buttons.
public partial class SheetDialog : Window
{
    private SheetDialog(string title, string body, string primary, string? secondary)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;
        PrimaryButton.Content = primary;
        if (secondary is null) SecondaryButton.Visibility = Visibility.Collapsed;
        else SecondaryButton.Content = secondary;
    }

    public static bool Ask(Window? owner, string title, string body, string primary, string? secondary)
    {
        var dialog = new SheetDialog(title, body, primary, secondary);
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dialog.ShowInTaskbar = false;
        }
        return dialog.ShowDialog() == true;
    }

    public static void Tell(Window? owner, string title, string body, string button = "OK") =>
        Ask(owner, title, body, button, null);

    private void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Secondary_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
