using Forms = System.Windows.Forms;

namespace DeviceMonitor.App;

/// <summary>The icon in the notification area (next to the clock): the monitor keeps running there when the window is closed.</summary>
public sealed class Tray : IDisposable
{
    readonly Forms.NotifyIcon icon;
    readonly Forms.ToolStripMenuItem toggle;

    public event Action OpenRequested, ExitRequested, ToggleRequested;

    public Tray()
    {
        icon = new Forms.NotifyIcon { Text = App.Name, Visible = true };
        try { icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? ""); }
        catch (Exception) { icon.Icon = System.Drawing.SystemIcons.Application; }
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Device Monitor", null, (_, _) => OpenRequested?.Invoke());
        toggle = new Forms.ToolStripMenuItem("Pause monitoring", null, (_, _) => ToggleRequested?.Invoke());
        menu.Items.Add(toggle);
        menu.Items.Add("Open log folder", null, (_, _) => App.OpenPath(App.Log.Folder));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke());
        icon.ContextMenuStrip = menu;
        icon.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenRequested?.Invoke(); };
    }

    /// <summary>Tooltip text such as "3 ON · 1 OFF" (Windows limits it to 63 characters).</summary>
    public void Update(int up, int down, bool running)
    {
        var t = running ? $"{App.Name} - {up} ON, {down} OFF" : $"{App.Name} - paused";
        icon.Text = t.Length > 63 ? t[..63] : t;
        toggle.Text = running ? "Pause monitoring" : "Start monitoring";
    }

    public void Balloon(string title, string text)
    {
        icon.BalloonTipTitle = title; icon.BalloonTipText = text;
        icon.ShowBalloonTip(4000);
    }

    public void Dispose() { icon.Visible = false; icon.Dispose(); }
}
