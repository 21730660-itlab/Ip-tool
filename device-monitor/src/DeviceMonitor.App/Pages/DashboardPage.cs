using DeviceMonitor.App.Controls;

namespace DeviceMonitor.App.Pages;

/// <summary>Overview: key figures, status ring, slowest devices, latest events and a live card per device.</summary>
public class DashboardPage : IPage
{
    public string Title => "Dashboard";
    public string Subtitle => "Live status of every device";
    public FrameworkElement View { get; }

    readonly TextBlock kTotal, kTotalNote, kUp, kUpNote, kDown, kDownNote, kAvg, kAvgNote, kAvail, kAvailNote;
    readonly DonutChart donut = new() { Height = 190 };
    readonly StackPanel legend = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
    readonly BarChart slowest = new() { Height = 200, Empty = "No replies yet" };
    readonly StackPanel recent = new();
    readonly WrapPanel cards = new();
    readonly Dictionary<string, DeviceCard> cardById = new();
    readonly TextBox search = Ui.Box(tip: "Search by name, address or group");
    readonly ComboBox filter = Ui.Choice(new[] { ("all", "All devices"), ("down", "OFF only"), ("up", "ON only"), ("paused", "Paused") }, "all");
    readonly ComboBox group = new() { Width = 180 };
    readonly Border empty;
    int tick;
    string lastSig = "";
    bool dirty = true;

    public DashboardPage()
    {
        var root = new StackPanel();

        // key figures
        var kpis = new Grid { Margin = new Thickness(0, 0, 0, 16) };
        Border t1, t2, t3, t4, t5;
        (t1, kTotal, kTotalNote) = UiExtra.Kpi("Devices", "Acc", "\uE839");
        (t2, kUp, kUpNote) = UiExtra.Kpi("ON", "Ok", "\uE73E");
        (t3, kDown, kDownNote) = UiExtra.Kpi("OFF", "Sig", "\uE711");
        (t4, kAvg, kAvgNote) = UiExtra.Kpi("Average reply", "Warn", "\uE916");
        (t5, kAvail, kAvailNote) = UiExtra.Kpi("Availability", "Acc", "\uE774");
        var tiles = new[] { t1, t2, t3, t4, t5 };
        for (int i = 0; i < tiles.Length; i++)
        {
            kpis.ColumnDefinitions.Add(new ColumnDefinition());
            tiles[i].Margin = new Thickness(i == 0 ? 0 : 7, 0, i == tiles.Length - 1 ? 0 : 7, 0);
            Grid.SetColumn(tiles[i], i); kpis.Children.Add(tiles[i]);
        }
        root.Children.Add(kpis);

        // charts row
        var row = new Grid { Margin = new Thickness(0, 0, 0, 20) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.15, GridUnitType.Star) });
        var donutRow = new Grid();
        donutRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        donutRow.ColumnDefinitions.Add(new ColumnDefinition());
        donutRow.Children.Add(donut); Grid.SetColumn(legend, 1); donutRow.Children.Add(legend);
        var c1 = CardWithTitle("Status", donutRow);
        var c2 = CardWithTitle("Slowest replies (average)", slowest);
        var recentHead = new DockPanel();
        var all = Ui.Btn("All events →", () => (Application.Current.MainWindow as MainWindow)?.Go("events"), "Link");
        DockPanel.SetDock(all, Dock.Right); recentHead.Children.Add(all);
        recentHead.Children.Add(Ui.Section("Latest events"));
        var c3 = Ui.Card(new StackPanel { Children = { recentHead, recent } });
        c1.Margin = new Thickness(0, 0, 7, 0); c2.Margin = new Thickness(7, 0, 7, 0); c3.Margin = new Thickness(7, 0, 0, 0);
        Grid.SetColumn(c2, 1); Grid.SetColumn(c3, 2);
        row.Children.Add(c1); row.Children.Add(c2); row.Children.Add(c3);
        root.Children.Add(row);

        // device cards with search / filter
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var add = Ui.IconBtn("\uE710", "Add device", () => DeviceDialog.Add(), "Primary");
        DockPanel.SetDock(add, Dock.Right); bar.Children.Add(add);
        var t = Ui.Text("Devices", 18, FontWeights.Bold); t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 20, 0);
        DockPanel.SetDock(t, Dock.Left); bar.Children.Add(t);
        search.Width = 260; filter.Width = 160;
        filter.Margin = new Thickness(10, 0, 0, 0); group.Margin = new Thickness(10, 0, 0, 0);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Children = { search, filter, group } };
        bar.Children.Add(filters);
        root.Children.Add(bar);
        search.TextChanged += (_, _) => { dirty = true; Refresh(); };
        filter.SelectionChanged += (_, _) => { dirty = true; Refresh(); };
        group.SelectionChanged += (_, _) => { dirty = true; Refresh(); };

        var emptyBox = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 30) };
        var logo = MainWindow.Logo(64); logo.Margin = new Thickness(0, 0, 0, 14); emptyBox.Children.Add(logo);
        var et = Ui.Text("No devices yet", 20, FontWeights.Bold); et.HorizontalAlignment = HorizontalAlignment.Center; emptyBox.Children.Add(et);
        var es = Ui.Muted("Add your MikroTik routers, switches, access points, servers or any device with an IP address.\nEach one is pinged on the interval you choose, and you get a pop-up when it turns OFF or comes back ON.", 14);
        es.TextAlignment = TextAlignment.Center; es.Margin = new Thickness(0, 6, 0, 16); emptyBox.Children.Add(es);
        var eb = Ui.IconBtn("\uE710", "Add your first device", () => DeviceDialog.Add(), "Primary"); eb.HorizontalAlignment = HorizontalAlignment.Center; emptyBox.Children.Add(eb);
        var imp = Ui.Btn("or import a list (CSV)…", () => DevicesPage.ImportCsv(), "Link"); imp.HorizontalAlignment = HorizontalAlignment.Center; imp.Margin = new Thickness(0, 8, 0, 0); emptyBox.Children.Add(imp);
        empty = Ui.Card(emptyBox, 24);
        root.Children.Add(empty);
        root.Children.Add(cards);

        View = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 0) };
        App.DevicesChanged += () => { dirty = true; FillGroups(); };
        App.EventAdded += _ => FillRecent();
        FillGroups();
        FillRecent();
    }

    static Border CardWithTitle(string title, UIElement body) => Ui.Card(new StackPanel { Children = { Ui.Section(title), body } });

    void FillGroups()
    {
        var sel = group.SelectedValue as string ?? "";
        var items = new List<Opt> { new("", "All groups") };
        items.AddRange(App.Groups.Select(g => new Opt(g, g)));
        group.DisplayMemberPath = "Label"; group.SelectedValuePath = "Value";
        group.ItemsSource = items;
        group.SelectedValue = items.Any(i => i.Value == sel) ? sel : "";
        group.Visibility = items.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    void FillRecent()
    {
        recent.Children.Clear();
        var list = App.Log.Recent.Where(e => e.Kind != EventKind.Info).Take(7).ToList();
        if (list.Count == 0) { recent.Children.Add(Ui.Muted("No device has turned OFF or ON yet.", 13.5)); return; }
        foreach (var e in list)
        {
            var key = e.Kind == EventKind.Down ? "Sig" : "Ok";
            var dot = new Shapes.Ellipse { Width = 10, Height = 10, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.SetResourceReference(Shapes.Shape.FillProperty, key);
            var when = Ui.Muted(e.At.Date == DateTime.Today ? e.At.ToString("HH:mm:ss") : e.At.ToString("dd MMM HH:mm"), 12.5); when.VerticalAlignment = VerticalAlignment.Center;
            var line = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
            DockPanel.SetDock(dot, Dock.Left); line.Children.Add(dot);
            DockPanel.SetDock(when, Dock.Right); line.Children.Add(when);
            var what = e.Kind == EventKind.Down ? "turned OFF" : e.Duration is TimeSpan t ? $"back ON ({EventLog.Duration(t)})" : "is ON";
            var tb = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 14 };
            tb.Inlines.Add(new System.Windows.Documents.Run(e.DeviceName + " ") { FontWeight = FontWeights.SemiBold });
            var r = new System.Windows.Documents.Run(what); r.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, key); tb.Inlines.Add(r);
            tb.SetResourceReference(TextBlock.ForegroundProperty, "Ink");
            line.Children.Add(tb);
            recent.Children.Add(line);
        }
    }

    public void Shown() { dirty = true; FillRecent(); Refresh(); }
    public void Hidden() { }

    public void Refresh()
    {
        var devices = App.Devices;
        var (total, up, down, paused, unknown) = App.Engine.Counts();
        kTotal.Text = total.ToString();
        kTotalNote.Text = paused > 0 ? $"{paused} paused" : unknown > 0 ? $"{unknown} waiting for first reply" : "all watched";
        kUp.Text = up.ToString(); kUpNote.Text = total == 0 ? "" : $"{100.0 * up / Math.Max(1, total - paused):0}% of watched devices";
        kDown.Text = down.ToString(); kDownNote.Text = down == 0 ? "everything answers" : string.Join(", ", devices.Where(d => d.Enabled && App.Engine.StateOf(d.Id).Status == DeviceStatus.Down).Select(d => d.Name).Take(3));
        kDown.Res(TextBlock.ForegroundProperty, down > 0 ? "Sig" : "Ink");
        var lat = devices.Where(d => d.Enabled).Select(d => App.Engine.StateOf(d.Id)).Where(s => s.Status == DeviceStatus.Up && s.LastMs.HasValue).Select(s => (double)s.LastMs.Value).ToList();
        kAvg.Text = lat.Count == 0 ? "—" : $"{lat.Average():0} ms";
        kAvgNote.Text = lat.Count == 0 ? "" : $"fastest {lat.Min():0} ms · slowest {lat.Max():0} ms";
        var ups = devices.Where(d => d.Enabled).Select(d => App.Engine.StateOf(d.Id).UptimePercent).Where(p => p.HasValue).Select(p => p.Value).ToList();
        kAvail.Text = ups.Count == 0 ? "—" : UiExtra.Pct(ups.Average());
        kAvailNote.Text = App.Engine.StartedAt is DateTime st ? "since " + st.ToString("HH:mm") + (st.Date != DateTime.Today ? st.ToString(" dd MMM") : "") : "monitoring paused";

        donut.SetData(new[] { ((double)up, "Ok"), ((double)down, "Sig"), ((double)(paused + unknown), "Idle") },
            total == 0 ? "0" : $"{100.0 * up / Math.Max(1, total):0}%", total == 0 ? "no devices" : "ON");
        legend.Children.Clear();
        AddLegend("Ok", "ON", up); AddLegend("Sig", "OFF", down); AddLegend("Idle", "Paused / waiting", paused + unknown);

        if (tick++ % 3 == 0)
        {
            var slow = devices.Where(d => d.Enabled)
                .Select(d => (d, s: App.Engine.StateOf(d.Id).LatencyStats()))
                .Where(x => x.s.HasValue).OrderByDescending(x => x.s.Value.avg).Take(6)
                .Select(x => (x.d.Name, x.s.Value.avg, $"{x.s.Value.avg:0} ms", x.s.Value.avg > App.Settings.SlowMs ? "Warn" : "Acc"));
            slowest.SetData(slow);
        }

        empty.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // a device changed state: re-sort (OFF first) and re-filter
        var sig = string.Join(",", devices.Select(d => d.Enabled ? (int)App.Engine.StateOf(d.Id).Status : 9));
        if (sig != lastSig) { lastSig = sig; dirty = true; }
        if (dirty) RebuildCards(devices);
        foreach (var d in devices) if (cardById.TryGetValue(d.Id, out var c) && c.Visibility == Visibility.Visible) c.Update(d);
    }

    void AddLegend(string key, string text, int n)
    {
        var dot = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, key);
        var sp = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(dot, Dock.Left); sp.Children.Add(dot);
        var num = Ui.Text(n.ToString(), 15, FontWeights.Bold); DockPanel.SetDock(num, Dock.Right); num.Margin = new Thickness(14, 0, 0, 0); sp.Children.Add(num);
        sp.Children.Add(Ui.Text(text, 14, null, "Ink2"));
        legend.Children.Add(sp);
    }

    void RebuildCards(IReadOnlyList<Device> devices)
    {
        dirty = false;
        var q = search.Text.Trim();
        var f = filter.Val();
        var g = group.SelectedValue as string ?? "";
        // OFF devices first, then by group and name
        var shown = devices
            .Where(d => q == "" || d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || d.Address.Contains(q, StringComparison.OrdinalIgnoreCase) || d.Group.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Where(d => g == "" || d.Group.Equals(g, StringComparison.OrdinalIgnoreCase))
            .Where(d =>
            {
                var s = d.Enabled ? App.Engine.StateOf(d.Id).Status : DeviceStatus.Paused;
                return f switch { "down" => s == DeviceStatus.Down, "up" => s == DeviceStatus.Up, "paused" => s == DeviceStatus.Paused, _ => true };
            })
            .OrderBy(d => d.Enabled && App.Engine.StateOf(d.Id).Status == DeviceStatus.Down ? 0 : 1).ThenBy(d => d.Group).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        cards.Children.Clear();
        foreach (var id in cardById.Keys.Where(id => devices.All(d => d.Id != id)).ToList()) cardById.Remove(id);
        foreach (var d in shown)
        {
            if (!cardById.TryGetValue(d.Id, out var c) || c.Tag as string != d.Kind.ToString()) { c = new DeviceCard(d) { Tag = d.Kind.ToString() }; cardById[d.Id] = c; }
            cards.Children.Add(c);
        }
        if (shown.Count == 0 && devices.Count > 0)
            cards.Children.Add(Ui.Muted("No device matches the search / filter.", 14));
    }
}
