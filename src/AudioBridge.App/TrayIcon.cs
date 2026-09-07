using System.Drawing;
using System.Windows.Forms;

namespace AudioBridge.App;

/// <summary>
/// The system tray icon. AudioBridge is meant to sit in the tray for a whole gaming
/// session, so closing the window hides it here and the app is exited from the menu --
/// the same convention as the other bridge apps.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _shownHint;

    public TrayIcon(Action show, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show AudioBridge", null, (_, _) => show());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "AudioBridge",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => show();
    }

    /// <summary>Status shown when hovering the icon. Windows caps it at 63 characters.</summary>
    public void SetStatus(string text) =>
        _icon.Text = text.Length > 63 ? text[..63] : text;

    /// <summary>Told once, the first time the window is closed to the tray, so the user knows
    /// where it went rather than thinking the app quit.</summary>
    public void ShowHiddenHint()
    {
        if (_shownHint) return;
        _shownHint = true;
        _icon.ShowBalloonTip(3000, "AudioBridge",
            "Still running in the system tray. Right-click the icon to exit.", ToolTipIcon.Info);
    }

    private static Icon LoadAppIcon()
    {
        // The green icon is compiled into the .exe; reuse it rather than shipping a second copy.
        try
        {
            var path = Environment.ProcessPath;
            if (path is not null && Icon.ExtractAssociatedIcon(path) is { } icon) return icon;
        }
        catch (Exception) { }
        return SystemIcons.Application;
    }

    public void Dispose()
    {
        // Hide before disposing, or Windows leaves a ghost icon until the mouse passes over it.
        _icon.Visible = false;
        _icon.Dispose();
    }
}
