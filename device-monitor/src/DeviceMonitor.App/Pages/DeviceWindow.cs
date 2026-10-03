using System.Windows.Threading;
using DeviceMonitor.App.Controls;

namespace DeviceMonitor.App.Pages;

/// <summary>Details of one device: live figures, reply-time graph (15 min / 1 h / 6 h), ON/OFF timeline and its events.</summary>
public class DeviceWindow : Window
{
    static readonly Dictionary<string, DeviceWindow> open = new();

    public static void Open(string id)
    {
        if (App.Find(id) == null) return;
        if (open.TryGetValue(id, out var w)) { if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal; w.Activate(); return; }
        w = new DeviceWindow(id);
        open[id] = w;
        w.Show();
    }

    readonly string id;
    readonly ContentControl badge = new(), pill = new();
    readonly TextBlock name = Ui.Text("", 24, FontWeights.Bold, "Ink", false);
    readonly TextBlock sub = Ui.Muted("", 14);
    readonly TextBlock vNow, vAvg, vMin, vMax, vLoss, vUp, vDowns, vSince;
    readonly LatencyChart chart = new() { Height = 300 };
    readonly StatusStrip strip = new() { Height = 16 };
    readonly TextBlock stripFrom = Ui.Muted("", 12), stripTo = Ui.Muted("now", 12);
    readonly StackPanel events = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    TimeSpan range = TimeSpan.FromMinutes(15);
    readonly List<Button> rangeButtons = new();
    DeviceKind? shownKind;
    DeviceStatus? shownStatus;

    DeviceWindow(string id)
    {
        this.id = id;
        Width = 1000; Height = 820; MinWidth = 760; MinHeight = 560;
        var owner = Application.Current.MainWindow;
        WindowStartupLocation = owner is { IsVisible: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
        if (owner is { IsVisible: true }) Owner = owner;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 15;
        this.SetResourceReference(BackgroundProperty, "Bg");
        this.SetResourceReference(ForegroundProperty, "Ink");

        var root = new StackPanel { Margin = new Thickness(26, 22, 26, 22) };

        // header
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        badge.Margin = new Thickness(0, 0, 16, 0);
        DockPanel.SetDock(badge, Dock.Left); head.Children.Add(badge);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        Button A(string glyph, string text, Action a, string style = null) { var b = Ui.IconBtn(glyph, text, a, style); b.Margin = new Thickness(8, 0, 0, 0); actions.Children.Add(b); return b; }
        A("\uE72C", "Check now", () => _ = App.Engine.CheckNowAsync(new[] { id }), "Primary");
        A("\uE756", "Ping window", () => { if (App.Find(id) is Device d) App.PingWindow(d); });
        A("\uE774", "Open", () => { if (App.Find(id) is Device d) App.OpenDevice(d); });
        A("\uE70F", "Edit", () => { if (App.Find(id) is Device d) DeviceDialog.Edit(d); });
        DockPanel.SetDock(actions, Dock.Right); head.Children.Add(actions);
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, pill } };
        pill.Margin = new Thickness(14, 0, 0, 0); pill.VerticalAlignment = VerticalAlignment.Center;
        titles.Children.Add(nameRow); titles.Children.Add(sub);
        head.Children.Add(titles);
        root.Children.Add(head);

        // figures
        var figs = new UniformGrid8();
        TextBlock F(string label)
        {
            var v = Ui.Text("—", 22, FontWeights.Bold, "Ink", false);
            figs.Add(Ui.Card(new StackPanel { Children = { Ui.Muted(label, 13), v } }, 14));
            return v;
        }
        vNow = F("Last reply"); vAvg = F("Average"); vMin = F("Fastest"); vMax = F("Slowest");
        vLoss = F("Packet loss"); vUp = F("Uptime"); vDowns = F("Times OFF"); vSince = F("Current status for");
        root.Children.Add(figs.Grid);

        // chart
        var chartHead = new DockPanel();
        var ranges = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (label, span) in new[] { ("15 min", TimeSpan.FromMinutes(15)), ("1 hour", TimeSpan.FromHours(1)), ("6 hours", TimeSpan.FromHours(6)) })
        {
            var b = Ui.Btn(label, () => { range = span; MarkRange(); Refresh(); });
            b.Tag = span; b.MinHeight = 30; b.Padding = new Thickness(12, 3, 12, 3); b.Margin = new Thickness(6, 0, 0, 0);
            rangeButtons.Add(b); ranges.Children.Add(b);
        }
        DockPanel.SetDock(ranges, Dock.Right); chartHead.Children.Add(ranges);
        chartHead.Children.Add(Ui.Section("Reply time"));
        var stripLabels = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        DockPanel.SetDock(stripTo, Dock.Right); stripLabels.Children.Add(stripTo); stripLabels.Children.Add(stripFrom);
        var stripTitle = Ui.Text("ON / OFF timeline", 13.5, FontWeights.SemiBold, "Ink2"); stripTitle.Margin = new Thickness(0, 16, 0, 6);
        var chartCard = Ui.Card(new StackPanel { Children = { chartHead, chart, stripTitle, strip, stripLabels } });
        chartCard.Margin = new Thickness(0, 14, 0, 14);
        root.Children.Add(chartCard);

        // events
        root.Children.Add(Ui.Card(new StackPanel { Children = { Ui.Section("Events of this device"), events } }));

        Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        MarkRange();
        timer.Tick += (_, _) => Refresh();
        timer.Start();
        App.DevicesChanged += OnDevicesChanged;
        App.EventAdded += OnEvent;
        Closed += (_, _) => { timer.Stop(); App.DevicesChanged -= OnDevicesChanged; App.EventAdded -= OnEvent; open.Remove(id); };
        Refresh(); FillEvents();
    }

    void OnDevicesChanged() { if (App.Find(id) == null) Close(); else Refresh(); }
    void OnEvent(MonitorEvent e) { if (App.Find(id) is Device d && (e.DeviceId == id || e.Address == d.Address)) FillEvents(); }

    void MarkRange()
    {
        foreach (var b in rangeButtons) b.Style = (TimeSpan)b.Tag == range ? (Style)Application.Current.Resources["Primary"] : null;
    }

    void Refresh()
    {
        if (App.Find(id) is not Device d) return;
        var st = App.Engine.StateOf(id);
        var status = d.Enabled ? st.Status : DeviceStatus.Paused;
        Title = $"{d.Name} ({d.Address}) - {App.Name}";
        if (shownKind != d.Kind) { shownKind = d.Kind; badge.Content = UiExtra.KindBadge(d.Kind, 60); }
        if (shownStatus != status) { shownStatus = status; pill.Content = UiExtra.StatusPill(status, 14); }
        name.Text = d.Name;
        var interval = MonitorSettings.IntervalText(App.Engine.IntervalOf(d));
        sub.Text = $"{d.Address}   ·   {Theme.KindText(d.Kind)}{(d.Group == "" ? "" : "   ·   " + d.Group)}   ·   pinged every {interval}"
                   + (st.LastCheck.HasValue ? $"   ·   last check {UiExtra.Ago(st.LastCheck)}" : "")
                   + (status == DeviceStatus.Down && st.LastError != "" ? $"\n{st.LastError}" : "")
                   + (d.Notes != "" ? $"\n{d.Notes}" : "");

        var stats = st.LatencyStats();
        vNow.Text = status == DeviceStatus.Down ? "No reply" : UiExtra.Ms(st.LastMs);
        vNow.Res(TextBlock.ForegroundProperty, status == DeviceStatus.Down ? "Sig" : st.LastMs > App.Settings.SlowMs ? "Warn" : "Ink");
        vAvg.Text = stats.HasValue ? $"{stats.Value.avg:0.#} ms" : "—";
        vMin.Text = stats.HasValue ? $"{stats.Value.min} ms" : "—";
        vMax.Text = stats.HasValue ? $"{stats.Value.max} ms" : "—";
        vLoss.Text = st.Sent == 0 ? "—" : $"{st.LossPercent:0.#}%";
        vLoss.Res(TextBlock.ForegroundProperty, st.LossPercent > 5 ? "Sig" : st.LossPercent > 0 ? "Warn" : "Ink");
        vUp.Text = UiExtra.Pct(st.UptimePercent);
        vDowns.Text = st.DownCount.ToString();
        vSince.Text = status is DeviceStatus.Up or DeviceStatus.Down ? EventLog.Duration(DateTime.Now - st.Since) : "—";

        var to = DateTime.Now; var from = to - range;
        var h = st.History(from);
        chart.SlowMs = App.Settings.SlowMs;
        chart.SetData(h, from, to);
        strip.SetData(h, from, to);
        stripFrom.Text = from.ToString("HH:mm");
    }

    void FillEvents()
    {
        events.Children.Clear();
        if (App.Find(id) is not Device d) return;
        var list = App.Log.Recent.Where(e => e.Kind != EventKind.Info && (e.DeviceId == id || e.DeviceId == "" && e.Address == d.Address)).Take(30).ToList();
        if (list.Count == 0) { events.Children.Add(Ui.Muted("No ON / OFF events yet.", 14)); return; }
        foreach (var e in list)
        {
            var key = e.Kind == EventKind.Down ? "Sig" : "Ok";
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var tag = Ui.Badge(e.Kind == EventKind.Down ? "OFF" : "ON", Theme.B(key), Theme.B(key + "Soft"));
            tag.Width = 52; tag.Margin = new Thickness(0, 0, 12, 0);
            DockPanel.SetDock(tag, Dock.Left); line.Children.Add(tag);
            var when = Ui.Text(e.At.ToString("yyyy-MM-dd HH:mm:ss"), 13.5, null, "Muted", false); when.Width = 160;
            DockPanel.SetDock(when, Dock.Left); line.Children.Add(when);
            var text = e.Kind == EventKind.Down ? e.Detail : (e.Duration is TimeSpan t ? $"Back after {EventLog.Duration(t)} · " : "") + e.Detail;
            line.Children.Add(Ui.Text(text, 14, null, "Ink2", false));
            events.Children.Add(line);
        }
    }

    /// <summary>Four figures per row.</summary>
    class UniformGrid8
    {
        public readonly Grid Grid = new();
        int n;
        public void Add(FrameworkElement e)
        {
            int col = n % 4, row = n / 4;
            if (Grid.ColumnDefinitions.Count < 4) for (int i = 0; i < 4; i++) Grid.ColumnDefinitions.Add(new ColumnDefinition());
            while (Grid.RowDefinitions.Count <= row) Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            e.Margin = new Thickness(col == 0 ? 0 : 6, row == 0 ? 0 : 12, col == 3 ? 0 : 6, 0);
            Grid.SetColumn(e, col); Grid.SetRow(e, row); Grid.Children.Add(e);
            n++;
        }
    }
}
