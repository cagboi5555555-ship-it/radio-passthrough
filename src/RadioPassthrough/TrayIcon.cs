using WinForms = System.Windows.Forms;

namespace RadioPassthrough;

public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;

    public TrayIcon(Action open, Action quit)
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open Radio Passthrough", null, (_, _) => open());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => quit());

        _icon = new WinForms.NotifyIcon
        {
            Icon = Environment.ProcessPath is { } exe ? System.Drawing.Icon.ExtractAssociatedIcon(exe) : System.Drawing.SystemIcons.Application,
            Text = "Radio Passthrough",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) open();
        };
    }

    public void SetStatus(string status)
    {
        string text = $"Radio Passthrough · {status}";
        _icon.Text = text.Length > 63 ? text[..63] : text;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
