using DeviceMonitor.App.Controls;

namespace DeviceMonitor.App.Pages;

/// <summary>Overview: key figures, status ring, slowest devices, latest events and a live card per device.</summary>
public class DashboardPage : IPage
{
    public string Title => "Dashboard";
    public string Subtitle => "Live status of your sites and devices";
    public FrameworkElement View { get; }

    readonly TextBlock kTotal, kTotalNote, kUp, kUpNote, kDown, kDownNote, kAvg, kAvgNote, kAvail, kAvailNote;
    readonly DonutChart donut = new() { Height = 190 };
    readonly StackPanel legend = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
    readonly BarChart slowest = new() { Height = 200, Empty = "No replies yet" };
    readonly StackPanel recent = new();
    readonly StackPanel cards = new();
    readonly Dictionary<string, DeviceCard> cardById = new();
    readonly TextBox search = Ui.Box(tip: "Search by name or IP address");
    readonly ComboBox filter = Ui.Choice(new[] { ("all", "All devices"), ("down", "OFF only"), ("up", "ON only"), ("paused", "Paused") }, "all");
    readonly Border empty;
    readonly ComboBox siteBox = new() { Width = 420, MinHeight = 42, FontSize = 16, MaxDropDownHeight = 460, DisplayMemberPath = "Label", SelectedValuePath = "Value" };
    readonly TextBlock siteInfo = Ui.Muted("", 14);
    readonly Button allBtn;
    readonly WrapPanel overview = new() { Margin = new Thickness(0, 0, 0, 6) };
    bool fillingSites;
    int tick;
    string lastSig = "";
    bool dirty = true;

    public DashboardPage()
    {
        var root = new StackPanel();

        // site drop-down: "All sites" or one site ("1. Main office"); the whole dashboard follows it
        var pickLbl = Ui.Text("Site", 17, FontWeights.Bold); pickLbl.VerticalAlignment = VerticalAlignment.Center; pickLbl.Margin = new Thickness(0, 0, 12, 0);
        siteBox.ToolTip = "Choose \"All sites\" to see every site, or one site to see only its devices";
        siteBox.SelectionChanged += (_, _) => { if (!fillingSites && siteBox.SelectedValue is string v) App.CurrentSiteId = v; };
        allBtn = Ui.Btn("Show all sites", () => App.CurrentSiteId = "");
        allBtn.Margin = new Thickness(10, 0, 0, 0);
        var addSite = Ui.IconBtn("\uE710", "Add site", () => { var ns = SiteDialog.Add(); if (ns != null) App.CurrentSiteId = ns.Id; });
        addSite.Margin = new Thickness(10, 0, 0, 0);
        siteInfo.VerticalAlignment = VerticalAlignment.Center; siteInfo.Margin = new Thickness(16, 0, 0, 0);
        var pick = new DockPanel();
        DockPanel.SetDock(addSite, Dock.Right); pick.Children.Add(addSite);
        pick.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { pickLbl, siteBox, allBtn, siteInfo } });
        var pickCard = Ui.Card(pick, 14); pickCard.Margin = new Thickness(0, 0, 0, 12);
        root.Children.Add(pickCard);
        root.Children.Add(overview);

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
        var add = Ui.IconBtn("\uE710", "Add IP", () => DeviceDialog.Add(), "Primary");
        DockPanel.SetDock(add, Dock.Right); bar.Children.Add(add);
        var t = Ui.Text("Devices", 18, FontWeights.Bold); t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 20, 0);
        DockPanel.SetDock(t, Dock.Left); bar.Children.Add(t);
        search.Width = 260; filter.Width = 160;
        filter.Margin = new Thickness(10, 0, 0, 0);
        var filters = new StackPanel { Orientation = Orientation.Horizontal, Children = { search, filter } };
        bar.Children.Add(filters);
        root.Children.Add(bar);
        search.TextChanged += (_, _) => { dirty = true; Refresh(); };
        filter.SelectionChanged += (_, _) => { dirty = true; Refresh(); };

        var emptyBox = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 30) };
        var logo = MainWindow.Logo(64); logo.Margin = new Thickness(0, 0, 0, 14); emptyBox.Children.Add(logo);
        var et = Ui.Text("No devices yet", 20, FontWeights.Bold); et.HorizontalAlignment = HorizontalAlignment.Center; emptyBox.Children.Add(et);
        var es = Ui.Muted("Step 1: add a site (office, branch, customer…).  Step 2: add the IP addresses of its MikroTik routers and other devices.\nEach one is pinged on the interval you choose, and you get a pop-up when it turns OFF or comes back ON.", 14);
        es.TextAlignment = TextAlignment.Center; es.Margin = new Thickness(0, 6, 0, 16); emptyBox.Children.Add(es);
        var eb = Ui.IconBtn("\uE710", "Add a site and its first IP", () => DeviceDialog.Add(), "Primary"); eb.HorizontalAlignment = HorizontalAlignment.Center; emptyBox.Children.Add(eb);
        var imp = Ui.Btn("or import a list (CSV)…", () => DevicesPage.ImportCsv(), "Link"); imp.HorizontalAlignment = HorizontalAlignment.Center; imp.Margin = new Thickness(0, 8, 0, 0); emptyBox.Children.Add(imp);
        empty = Ui.Card(emptyBox, 24);
        root.Children.Add(empty);
        root.Children.Add(cards);

        View = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 4, 0) };
        App.DevicesChanged += () => { dirty = true; FillSiteBox(); };
        App.SiteFilterChanged += FillSiteBox;
        FillSiteBox();
        App.EventAdded += _ => FillRecent();
        FillRecent();
    }

    /// <summary>Fills the site drop-down and the line next to it.</summary>
    void FillSiteBox()
    {
        if (siteBox.IsDropDownOpen) return;
        fillingSites = true;
        siteBox.ItemsSource = UiExtra.SiteOptions();
        siteBox.SelectedValue = App.CurrentSiteId;
        fillingSites = false;
        var site = App.FindSite(App.CurrentSiteId);
        allBtn.Visibility = site == null ? Visibility.Collapsed : Visibility.Visible;
        siteInfo.Text = site == null ? (App.Sites.Count == 0 ? "No sites yet: click \"Add site\"" : "Showing every site. Pick one to see only its devices.")
                                     : string.Join("   ·   ", new[] { site.Location, site.Notes }.Where(x => !string.IsNullOrWhiteSpace(x)));
        dirty = true;
    }

    /// <summary>With "All sites": one small tile per site (number, name, ON / OFF); click = show that site.</summary>
    void FillOverview(IReadOnlyList<Device> all)
    {
        overview.Children.Clear();
        overview.Visibility = App.CurrentSiteId == "" && App.Sites.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (overview.Visibility != Visibility.Visible) return;
        foreach (var site in App.Sites)
        {
            var devs = all.Where(d => d.SiteId == site.Id).ToList();
            var on = devs.Count(d => d.Enabled && App.Engine.StateOf(d.Id).Status == DeviceStatus.Up);
            var off = devs.Count(d => d.Enabled && App.Engine.StateOf(d.Id).Status == DeviceStatus.Down);
            var key = off > 0 ? "Sig" : devs.Count > 0 && on == devs.Count ? "Ok" : "Idle";
            var num = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = site.Number.ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } }
                .Res(Border.BackgroundProperty, key);
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            txt.Children.Add(Ui.Text(site.Name, 15, FontWeights.SemiBold, "Ink", false));
            txt.Children.Add(Ui.Text($"{devs.Count} device{(devs.Count == 1 ? "" : "s")} · {on} ON" + (off > 0 ? $" · {off} OFF" : ""), 13, off > 0 ? FontWeights.SemiBold : FontWeights.Normal, off > 0 ? "Sig" : "Muted", false));
            var tile = new Border
            {
                Width = 250, Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 10, 10), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1), Cursor = Cursors.Hand,
                Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { num, txt } }, ToolTip = $"Show only {site.Label}"
            }.Res(Border.BackgroundProperty, "Panel").Res(Border.BorderBrushProperty, off > 0 ? "Sig" : "Line");
            var id = site.Id;
            tile.MouseLeftButtonUp += (_, _) => App.CurrentSiteId = id;
            overview.Children.Add(tile);
        }
    }

    static Border CardWithTitle(string title, UIElement body) => Ui.Card(new StackPanel { Children = { Ui.Section(title), body } });

    void FillRecent()
    {
        recent.Children.Clear();
        var siteName = App.FindSite(App.CurrentSiteId)?.Name;
        var list = App.Log.Recent.Where(e => e.Kind != EventKind.Info && (siteName == null || e.Site == siteName)).Take(7).ToList();
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
        var all = App.Devices;
        var devices = all.Where(App.InCurrentSite).ToList();
        int total = devices.Count, up = 0, down = 0, paused = 0, unknown = 0;
        foreach (var d in devices)
            switch (d.Enabled ? App.Engine.StateOf(d.Id).Status : DeviceStatus.Paused)
            {
                case DeviceStatus.Up: up++; break;
                case DeviceStatus.Down: down++; break;
                case DeviceStatus.Paused: paused++; break;
                default: unknown++; break;
            }
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

        empty.Visibility = all.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        // a device changed state: re-sort (OFF first) and re-filter
        var sig = App.CurrentSiteId + ":" + string.Join(",", devices.Select(d => d.Enabled ? (int)App.Engine.StateOf(d.Id).Status : 9));
        if (sig != lastSig) { lastSig = sig; dirty = true; FillSiteBox(); }
        if (dirty) { FillOverview(all); RebuildCards(devices, all); }
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

    /// <summary>One section per site (name, counts, "Add IP"), each with the cards of its devices; OFF devices first.</summary>
    void RebuildCards(List<Device> devices, IReadOnlyList<Device> all)
    {
        dirty = false;
        var q = search.Text.Trim();
        var f = filter.Val();
        var shown = devices
            .Where(d => q == "" || d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || d.Address.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Where(d =>
            {
                var s = d.Enabled ? App.Engine.StateOf(d.Id).Status : DeviceStatus.Paused;
                return f switch { "down" => s == DeviceStatus.Down, "up" => s == DeviceStatus.Up, "paused" => s == DeviceStatus.Paused, _ => true };
            })
            .ToList();
        foreach (var w in cards.Children.OfType<WrapPanel>()) w.Children.Clear();   // free the cards before re-using them
        cards.Children.Clear();
        foreach (var id in cardById.Keys.Where(id => all.All(d => d.Id != id)).ToList()) cardById.Remove(id);

        var sites = App.CurrentSiteId == "" ? App.Sites : App.Sites.Where(s => s.Id == App.CurrentSiteId).ToList();
        foreach (var site in sites)
        {
            var mine = shown.Where(d => d.SiteId == site.Id)
                .OrderBy(d => d.Enabled && App.Engine.StateOf(d.Id).Status == DeviceStatus.Down ? 0 : 1).ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            if (mine.Count == 0 && (q != "" || f != "all")) continue;   // while searching / filtering, hide sites without a match
            var siteAll = devices.Where(d => d.SiteId == site.Id).ToList();
            var off = siteAll.Count(d => d.Enabled && App.Engine.StateOf(d.Id).Status == DeviceStatus.Down);
            var on = siteAll.Count(d => d.Enabled && App.Engine.StateOf(d.Id).Status == DeviceStatus.Up);

            var head = new DockPanel { Margin = new Thickness(0, cards.Children.Count == 0 ? 0 : 8, 0, 10) };
            var siteId = site.Id;
            var addIp = Ui.IconBtn("", "Add IP to this site", () => DeviceDialog.Add(siteId));
            addIp.MinHeight = 32; addIp.Padding = new Thickness(12, 4, 12, 4);
            DockPanel.SetDock(addIp, Dock.Right); head.Children.Add(addIp);
            var pin = new Border { Width = 6, Height = 26, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center }
                .Res(Border.BackgroundProperty, off > 0 ? "Sig" : siteAll.Count > 0 && on == siteAll.Count ? "Ok" : "Idle");
            DockPanel.SetDock(pin, Dock.Left); head.Children.Add(pin);
            var title = Ui.Text(site.Label, 18, FontWeights.Bold, "Ink", false); title.VerticalAlignment = VerticalAlignment.Center;
            var info = Ui.Text($"   {siteAll.Count} device(s) · {on} ON" + (off > 0 ? $" · {off} OFF" : ""), 14, FontWeights.SemiBold, off > 0 ? "Sig" : "Muted", false);
            info.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { title, info } });
            cards.Children.Add(head);

            var wrap = new WrapPanel();
            foreach (var d in mine)
            {
                if (!cardById.TryGetValue(d.Id, out var c) || c.Tag as string != d.Kind.ToString()) { c = new DeviceCard(d) { Tag = d.Kind.ToString() }; cardById[d.Id] = c; }
                wrap.Children.Add(c);
            }
            if (mine.Count == 0)
            {
                var none = Ui.Muted("No IP addresses in this site yet. Click \"Add IP to this site\".", 14);
                none.Margin = new Thickness(18, 0, 0, 14);
                wrap.Children.Add(none);
            }
            cards.Children.Add(wrap);
        }
        if (cards.Children.Count == 0 && all.Count > 0)
            cards.Children.Add(Ui.Muted("No device matches the search / filter.", 14));
    }
}
