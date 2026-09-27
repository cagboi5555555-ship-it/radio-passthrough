using System.Windows;
using System.Windows.Input;

namespace RadioPassthrough.Dialogs;

// A small floating sheet: title, one paragraph, one or two buttons.
public partial class SheetDialog : Window
{
    private SheetDialog(string title, string body, string primary, string? secondary, bool destructive)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;
        PrimaryButton.Content = primary;
        if (secondary is null) SecondaryButton.Visibility = Visibility.Collapsed;
        else SecondaryButton.Content = secondary;

        // Enter never confirms something that stops the app or undoes setup; it picks the safe choice.
        if (destructive && secondary is not null)
        {
            PrimaryButton.IsDefault = false;
            SecondaryButton.IsDefault = true;
            Loaded += (_, _) => SecondaryButton.Focus();
        }
    }

    public static bool Ask(Window? owner, string title, string body, string primary, string? secondary, bool destructive = false)
    {
        var dialog = new SheetDialog(title, body, primary, secondary, destructive);
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

    public static bool Confirm(Window? owner, string title, string body, string action, string keep) =>
        Ask(owner, title, body, action, keep, destructive: true);

    private void Primary_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    private void Secondary_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }
}
