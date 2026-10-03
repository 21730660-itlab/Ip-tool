using DeviceMonitor.App.Pages;

namespace DeviceMonitor.App.Controls;

/// <summary>A device on the dashboard: kind, name, address, status, reply time, a mini graph and uptime. Click = details.</summary>
public class DeviceCard : Border
{
    public string DeviceId { get; }
    readonly TextBlock name, addr, ms, uptime, loss, since;
    readonly ContentControl pill = new();
    readonly Sparkline spark = new() { Height = 46, Margin = new Thickness(0, 10, 0, 8) };
    readonly Border accent;
    DeviceStatus shown = (DeviceStatus)(-1);

    public DeviceCard(Device d)
    {
        DeviceId = d.Id;
        Width = 300; Margin = new Thickness(0, 0, 14, 14); CornerRadius = new CornerRadius(10); BorderThickness = new Thickness(1); Cursor = Cursors.Hand;
        this.Res(BackgroundProperty, "Panel").Res(BorderBrushProperty, "Line");
        ToolTip = "Click for details";

        var head = new DockPanel();
        var badge = UiExtra.KindBadge(d.Kind, 40); badge.Margin = new Thickness(0, 0, 12, 0);
        DockPanel.SetDock(badge, Dock.Left); head.Children.Add(badge);
        DockPanel.SetDock(pill, Dock.Right); head.Children.Add(pill);
        name = Ui.Text(d.Name, 15.5, FontWeights.SemiBold, "Ink", false);
        addr = Ui.Text(d.Address, 13, null, "Muted", false); addr.FontFamily = new FontFamily(Ui.Mono);
        head.Children.Add(new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { name, addr } });

        ms = Ui.Text("—", 26, FontWeights.Bold, "Ink", false);
        var msRow = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        since = Ui.Muted("", 12.5); since.VerticalAlignment = VerticalAlignment.Bottom; since.Margin = new Thickness(0, 0, 0, 5);
        DockPanel.SetDock(since, Dock.Right); msRow.Children.Add(since);
        msRow.Children.Add(ms);

        uptime = Ui.Text("—", 13, FontWeights.SemiBold, "Ink2", false);
        loss = Ui.Text("—", 13, FontWeights.SemiBold, "Ink2", false);
        var stats = new Grid();
        stats.ColumnDefinitions.Add(new ColumnDefinition()); stats.ColumnDefinitions.Add(new ColumnDefinition());
        var a = new StackPanel { Children = { Ui.Muted("Uptime", 12), uptime } };
        var b = new StackPanel { Children = { Ui.Muted("Packet loss", 12), loss } };
        Grid.SetColumn(b, 1); stats.Children.Add(a); stats.Children.Add(b);

        var body = new StackPanel { Margin = new Thickness(16, 14, 16, 14), Children = { head, msRow, spark, stats } };
        accent = new Border { Height = 4, CornerRadius = new CornerRadius(10, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var g = new Grid { Children = { body, accent } };
        Child = g;

        MouseEnter += (_, _) => this.Res(BorderBrushProperty, "AccLine");
        MouseLeave += (_, _) => this.Res(BorderBrushProperty, "Line");
        MouseLeftButtonUp += (_, _) => DeviceWindow.Open(DeviceId);
        Update(d);
    }

    public void Update(Device d)
    {
        var st = App.Engine.StateOf(d.Id);
        var status = d.Enabled ? st.Status : DeviceStatus.Paused;
        if (status != shown)
        {
            shown = status;
            pill.Content = UiExtra.StatusPill(status);
            accent.Res(BackgroundProperty, Theme.StatusKey(status));
            spark.LineKey = status == DeviceStatus.Down ? "Sig" : "Acc";
        }
        name.Text = d.Name; addr.Text = d.Address;
        if (status == DeviceStatus.Down) { ms.Text = "No reply"; ms.Res(TextBlock.ForegroundProperty, "Sig"); }
        else
        {
            ms.Text = UiExtra.Ms(st.LastMs);
            ms.Res(TextBlock.ForegroundProperty, st.LastMs > App.Settings.SlowMs ? "Warn" : "Ink");
        }
        since.Text = status switch
        {
            DeviceStatus.Down => "OFF for " + EventLog.Duration(DateTime.Now - st.Since),
            DeviceStatus.Up => "ON for " + EventLog.Duration(DateTime.Now - st.Since),
            DeviceStatus.Paused => "Paused",
            _ => "Checking…"
        };
        uptime.Text = UiExtra.Pct(st.UptimePercent);
        loss.Text = st.Sent == 0 ? "—" : $"{st.LossPercent:0.#}%";
        var h = st.History();
        spark.SetData(h.Length > 60 ? h[^60..] : h);
    }
}
