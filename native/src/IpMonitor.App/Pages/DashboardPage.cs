using System.Numerics;

namespace IpMonitor.App;

/// <summary>
/// The dashboard of the web version: health score, health checks (critical / warning / info), quick find, key figures,
/// busiest subnets, weakest wireless links, inventory, address space, sites, recent activity — plus live monitoring.
/// </summary>
public class DashboardPage : PageBase
{
    public override string Title => "Dashboard";
    public override string Glyph => "";

    readonly HashSet<string> openChecks = new();
    string query = "";
    TextBlock monText; StackPanel monList;

    static readonly string[] Palette = { "Acc", "Sig", "Ok", "Warn", "CSrv", "CCam" };

    public override FrameworkElement Build()
    {
        var h = new Health(S, W.Monitor.DownIds);
        var sp = new StackPanel();
        sp.Children.Add(Head(h));
        sp.Children.Add(QuickFind(h));
        sp.Children.Add(Kpis(h));
        sp.Children.Add(Spaced(MonitorPanel()));
        sp.Children.Add(Spaced(ChecksPanel(h)));
        var g1 = Ui.Cols(Panel("Busiest subnets", Busiest(h), ("All →", () => W.Navigate<NetworksPage>())), Panel("Weakest wireless links", Weakest(h), ("Map →", () => W.Navigate<MapPage>())));
        sp.Children.Add(Spaced(g1));
        var g2 = Ui.Cols(Panel($"Inventory · {S.Db.Devices.Count} devices", Inventory()), Panel("Address space · by scope", AddressSpace(h)));
        sp.Children.Add(Spaced(g2));
        sp.Children.Add(Spaced(Panel("Sites · click a row to open its map", SitesTable(h))));
        sp.Children.Add(Spaced(Panel("Recent activity", Recent(), ("History →", () => W.Navigate<HistoryPage>()))));
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Padding = new Thickness(28, 22, 28, 28), Child = sp } };
    }

    static FrameworkElement Spaced(FrameworkElement e) { e.Margin = new Thickness(0, 16, 0, 0); return e; }

    static Border Panel(string title, UIElement body, (string text, Action act)? link = null)
    {
        var sp = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        if (link is { } l) { var b = Ui.Btn(l.text, l.act, "Link"); DockPanel.SetDock(b, Dock.Right); head.Children.Add(b); }
        head.Children.Add(Ui.Text(title, 16, FontWeights.Bold));
        sp.Children.Add(head); sp.Children.Add(body);
        return Ui.Card(sp, 18);
    }

    // ------------------------------------------------------------------ opening things
    void Open(Health.Target t, string id)
    {
        switch (t)
        {
            case Health.Target.Network: if (S.Db.Networks.FirstOrDefault(n => n.Id == id) is Network n) { W.SiteFilter = ""; W.Page<NetworksPage>().Edit(n); } break;
            case Health.Target.Link: if (S.Db.Links.FirstOrDefault(l => l.Id == id) is Link l) W.Page<LinksPage>().Edit(l); break;
            case Health.Target.Device: if (S.DevById(id) is Device d) DeviceDialog.Edit(d, null); break;
            case Health.Target.Config: if (S.DevById(id) is Device c) ConfigDialog.Open(c); break;
            case Health.Target.Site: W.SiteFilter = id; W.Navigate<MapPage>(); break;
            case Health.Target.IpList: W.SiteFilter = ""; W.Navigate<NetworksPage>(); break;
        }
    }

    // ------------------------------------------------------------------ header: gauge + summary
    FrameworkElement Head(Health h)
    {
        var grade = h.Score >= 90 ? "Ok" : h.Score >= 70 ? "Warn" : "Sig";
        var gauge = new Grid { Width = 112, Height = 112 };
        gauge.Children.Add(new Shapes.Ellipse { StrokeThickness = 12, Stroke = Theme.B("Sunk"), Margin = new Thickness(6) });
        double a = Math.Min(359.9, h.Score / 100.0 * 360) * Math.PI / 180, r = 50, c = 56;
        if (h.Score > 0)
        {
            var end = new Point(c + r * Math.Sin(a), c - r * Math.Cos(a));
            var fig = new PathFigure { StartPoint = new Point(c, c - r), IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, a > Math.PI, SweepDirection.Clockwise, true));
            gauge.Children.Add(new Shapes.Path { Data = new PathGeometry(new[] { fig }), Stroke = Theme.B(grade), StrokeThickness = 12, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        }
        var num = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var n = Ui.Text(h.Score.ToString(), 30, FontWeights.Bold, grade); n.HorizontalAlignment = HorizontalAlignment.Center; num.Children.Add(n);
        var hl = Ui.Text("HEALTH", 10.5, FontWeights.Bold, "Muted"); hl.HorizontalAlignment = HorizontalAlignment.Center; num.Children.Add(hl);
        gauge.Children.Add(num);

        var sum = new StackPanel { Margin = new Thickness(22, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        sum.Children.Add(Ui.Text("Network overview", 26, FontWeights.Bold));
        int crit = h.Count("crit"), warn = h.Count("warn"), ok = h.Checks.Count(c => c.Sev == "ok");
        var leds = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        Border Led(string text, bool on, string col)
        {
            var b = Ui.Badge(text, on ? Theme.B(col == "Ok" ? "Ok" : col) : Theme.B("Muted"), on ? Theme.B(col + "Soft") : Theme.B("Sunk"));
            ((TextBlock)b.Child).FontSize = 13.5; b.Padding = new Thickness(10, 4, 10, 4); b.Margin = new Thickness(0, 0, 8, 0); return b;
        }
        leds.Children.Add(Led($"● {crit} critical", crit > 0, "Sig"));
        leds.Children.Add(Led($"● {warn} warnings", warn > 0, "Warn"));
        leds.Children.Add(Led($"● {ok} checks passed", crit == 0 && warn == 0, "Ok"));
        sum.Children.Add(leds);
        var stamp = Ui.Muted($"{DateTime.Now:dd MMM yyyy, HH:mm} · {IOPath.GetFileName(S.FilePath)} · {S.Me?.Username}", 13); stamp.Margin = new Thickness(0, 8, 0, 0);
        sum.Children.Add(stamp);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(gauge); row.Children.Add(sum);
        return row;
    }

    // ------------------------------------------------------------------ quick find
    FrameworkElement QuickFind(Health h)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 18, 0, 0) };
        var box = Ui.Box(query); box.FontSize = 15; box.MinHeight = 42;
        var res = new StackPanel();
        void Run()
        {
            query = box.Text; res.Children.Clear();
            var hits = h.Find(query);
            if (query.Trim() == "") return;
            if (hits.Count == 0) { var t = Ui.Muted($"Nothing found for “{query.Trim()}”.", 13.5); t.Margin = new Thickness(2, 8, 0, 0); res.Children.Add(t); return; }
            var list = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            foreach (var x in hits.Take(30))
            {
                var row = new DockPanel();
                var k = Ui.Badge(x.K, Theme.B("Acc"), Theme.B("AccSoft")); k.Width = 74; k.Margin = new Thickness(0, 0, 12, 0); ((TextBlock)k.Child).HorizontalAlignment = HorizontalAlignment.Center;
                row.Children.Add(k);
                var tx = new StackPanel { Orientation = Orientation.Horizontal };
                tx.Children.Add(Ui.Text(x.T, 14, FontWeights.SemiBold, "Ink", false, true));
                var s2 = Ui.Muted("   " + x.S, 13); s2.VerticalAlignment = VerticalAlignment.Center; tx.Children.Add(s2);
                row.Children.Add(tx);
                var b = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(10, 6, 10, 6) };
                var target = x; b.Click += (_, _) => Open(target.Open, target.Ref);
                list.Children.Add(b);
            }
            if (hits.Count > 30) list.Children.Add(Ui.Muted($"…{hits.Count - 30} more. Type more to narrow it down.", 13));
            res.Children.Add(list);
        }
        box.TextChanged += (_, _) => Run();
        sp.Children.Add(Ui.Field("Find anything: IP, subnet, MAC, device, VLAN, site…", box));
        ((FrameworkElement)sp.Children[0]).Margin = new Thickness(0);
        sp.Children.Add(res);
        Run();
        return sp;
    }

    // ------------------------------------------------------------------ key figures
    FrameworkElement Kpis(Health h)
    {
        var db = S.Db;
        var (cap4, used4, util) = h.Ipv4Use();
        var cov = h.BackupCoverage(out var routers);
        var wl = db.Links.Count(l => l.Type == "wireless");
        int offline = db.Devices.Count(d => Store.StatusOf(d.Status) == "offline"), down = W.Monitor.DownIds.Count;
        var g = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4, Margin = new Thickness(0, 16, 0, 0) };
        void K(string v, string label, string sub, bool bad, Action go)
        {
            var sp = new StackPanel();
            sp.Children.Add(Ui.Text(v, 28, FontWeights.Bold, bad ? "Sig" : "Ink"));
            sp.Children.Add(Ui.Text(label, 14, FontWeights.SemiBold, "Ink2"));
            sp.Children.Add(Ui.Muted(sub, 12.5));
            var c = Ui.Card(sp, 16); c.Margin = new Thickness(0, 0, 12, 12);
            if (bad) c.SetResourceReference(Border.BorderBrushProperty, "Sig");
            if (go != null) { c.Cursor = Cursors.Hand; c.MouseLeftButtonUp += (_, _) => go(); }
            g.Children.Add(c);
        }
        K(db.Sites.Count.ToString(), "Sites", $"{db.Sites.Count(s => Store.StatusOf(s.Status) == "active")} active", false, () => W.Navigate<SitesPage>());
        K(db.Networks.Count.ToString(), "Networks", $"{h.Uses.Count(o => o.N.Kind == "net")} subnets", false, () => W.Navigate<NetworksPage>());
        K(db.Networks.Sum(S.HostCount).ToString("N0"), "Hosts", $"{h.Recs.Select(r => r.Ip).Distinct().Count()} IPs in use", false, () => W.Navigate<NetworksPage>());
        K(db.Devices.Count.ToString(), "Devices", $"{offline} offline" + (W.Monitor.States.Count > 0 ? $" · {down} not answering ping" : ""), offline > 0 || down > 0, () => W.Navigate<MapPage>());
        K(db.Links.Count.ToString(), "Links", $"{wl} wireless · {db.Links.Count - wl} wired", false, () => W.Navigate<MapPage>());
        K(db.Vlans.Count.ToString(), "VLANs", $"{db.Vlans.Select(v => v.Vid).Distinct().Count()} distinct IDs", false, () => W.Navigate<VlansPage>());
        K(cap4 > 0 ? $"{util:0.0}%" : "—", "IPv4 used", cap4 > 0 ? $"{used4:N0} of {IpMath.CountText(cap4)}" : "no IPv4 subnets", util >= 80, () => W.Navigate<NetworksPage>());
        K(routers > 0 ? $"{cov:0}%" : "—", "Config backup", $"{db.Configs.Count} versions saved", routers > 0 && cov < 50, () => W.Navigate<DevicesPage>());
        return g;
    }

    // ------------------------------------------------------------------ live monitoring
    FrameworkElement MonitorPanel()
    {
        var m = W.Monitor; var st = W.Settings;
        var sp = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        var check = Ui.IconBtn("", "Check now", async () => await W.RunMonitor()); check.Margin = new Thickness(8, 0, 0, 0);
        var pause = Ui.Btn(st.MonitorOn ? "Pause" : "Start monitoring", () =>
        {
            st.MonitorOn = !st.MonitorOn; st.Save();
            if (st.MonitorOn) W.StartMonitor(); else W.StopMonitor();
            W.Navigate(this);
        }, st.MonitorOn ? null : "Primary");
        pause.Margin = new Thickness(8, 0, 0, 0);
        var every = Ui.Choice(new[] { ("30", "every 30 s"), ("60", "every 1 min"), ("120", "every 2 min"), ("300", "every 5 min"), ("600", "every 10 min") }, st.MonitorSeconds.ToString()); every.Width = 150; every.Margin = new Thickness(8, 0, 0, 0);
        every.SelectionChanged += (_, _) => { if (int.TryParse(every.Val(), out var s)) { st.MonitorSeconds = s; st.Save(); if (st.MonitorOn) W.StartMonitor(); } };
        var sound = new CheckBox { Content = "Sound", IsChecked = st.MonitorSound, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        sound.Checked += (_, _) => { st.MonitorSound = true; st.Save(); }; sound.Unchecked += (_, _) => { st.MonitorSound = false; st.Save(); };
        tools.Children.Add(sound); tools.Children.Add(every); tools.Children.Add(pause); tools.Children.Add(check);
        DockPanel.SetDock(tools, Dock.Right); head.Children.Add(tools);
        head.Children.Add(Ui.Text("Live monitoring · ping", 16, FontWeights.Bold));
        sp.Children.Add(head);
        monText = Ui.Text("", 14, FontWeights.SemiBold, "Ink2");
        sp.Children.Add(monText);
        monList = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        sp.Children.Add(monList);
        RefreshMonitorPanel();
        var card = Ui.Card(sp, 18);
        if (m.DownIds.Count > 0) card.SetResourceReference(Border.BorderBrushProperty, "Sig");
        return card;
    }

    /// <summary>Updates the monitoring text and lists without rebuilding the page.</summary>
    public void RefreshMonitorPanel()
    {
        if (monText == null) return;
        var m = W.Monitor; var st = W.Settings;
        var watched = S.Db.Devices.Where(DeviceMonitor.Watched).ToList();
        int up = m.States.Values.Count(s => s.State == DeviceMonitor.State.Up), dn = m.States.Values.Count(s => s.State == DeviceMonitor.State.Down);
        var noIp = S.Db.Devices.Count(d => DeviceMonitor.IpOf(d) == "");
        monText.Text = !st.MonitorOn ? "Paused. Devices are not being pinged." :
            m.LastRun == default ? "Checking…" :
            $"Every {(st.MonitorSeconds >= 60 ? st.MonitorSeconds / 60 + " min" : st.MonitorSeconds + " s")} · last check {m.LastRun:HH:mm:ss} · {up} up · {dn} down · {watched.Count} watched" + (noIp > 0 ? $" · {noIp} without IP" : "") +
            "\nA pop-up appears when a device stops answering, and again when it is back. Planned and Retired devices are not pinged.";
        monList.Children.Clear();
        foreach (var s in m.States.Values.Where(s => s.State == DeviceMonitor.State.Down).OrderBy(s => s.Since))
        {
            if (S.DevById(s.DeviceId) is not Device d) continue;
            var b = new Button { HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4), Padding = new Thickness(10, 6, 10, 6) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(Ui.Badge("DOWN", Theme.B("AccInk"), Theme.B("Sig")));
            var t = Ui.Text($"  {d.Name}", 14, FontWeights.Bold, "Ink"); row.Children.Add(t);
            row.Children.Add(Ui.Muted($"   {s.Ip} · {S.SiteById(d.SiteId)} · down since {s.Since:HH:mm:ss}", 13));
            b.Content = row; b.Click += (_, _) => { W.SiteFilter = d.SiteId; W.Navigate<MapPage>(); };
            monList.Children.Add(b);
        }
        var ev = m.Events.Take(8).ToList();
        if (ev.Count > 0)
        {
            var h = Ui.Text("Recent events", 13, FontWeights.Bold, "Muted"); h.Margin = new Thickness(0, 8, 0, 4); monList.Children.Add(h);
            foreach (var e in ev)
                monList.Children.Add(Ui.Text($"{e.At:HH:mm:ss}   {(e.Down ? "▼ down" : "▲ up")}   {e.Device.Name} ({e.Ip})", 13, null, e.Down ? "Sig" : "Ok"));
        }
    }

    // ------------------------------------------------------------------ health checks
    FrameworkElement ChecksPanel(Health h)
    {
        var sp = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var cnt = Ui.Muted($"{h.Checks.Count} rules", 13); DockPanel.SetDock(cnt, Dock.Right); head.Children.Add(cnt);
        head.Children.Add(Ui.Text("Health checks", 16, FontWeights.Bold));
        sp.Children.Add(head);
        foreach (var c in h.Checks)
        {
            var (fg, bg, tag) = c.Sev switch
            {
                "crit" => ("AccInk", "Sig", "CRIT"), "warn" => ("Ink", "WarnSoft", "WARN"), "info" => ("Acc", "AccSoft", "INFO"), _ => ("Ok", "OkSoft", "OK")
            };
            var isOpen = openChecks.Contains(c.Title) && c.Items.Count > 0;
            var row = new DockPanel();
            var sev = Ui.Badge(tag, Theme.B(fg), Theme.B(bg)); sev.Width = 56; ((TextBlock)sev.Child).HorizontalAlignment = HorizontalAlignment.Center; sev.Margin = new Thickness(0, 0, 12, 0);
            row.Children.Add(sev);
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            right.Children.Add(Ui.Text(c.Items.Count > 0 ? c.Items.Count.ToString() : "✓", 15, FontWeights.Bold, c.Sev == "crit" ? "Sig" : c.Sev == "ok" ? "Ok" : "Ink"));
            if (c.Items.Count > 0) { var ar = Ui.Muted(isOpen ? "  ▾" : "  ▸", 14); right.Children.Add(ar); }
            DockPanel.SetDock(right, Dock.Right); row.Children.Add(right);
            var tt = new StackPanel();
            tt.Children.Add(Ui.Text(c.Title, 14, FontWeights.SemiBold, c.Items.Count > 0 ? "Ink" : "Muted"));
            if (c.Items.Count > 0 && c.Hint != "") tt.Children.Add(Ui.Muted(c.Hint, 12.5));
            row.Children.Add(tt);
            var b = new Button { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10, 7, 12, 7), Margin = new Thickness(0, 0, 0, 4), IsEnabled = true };
            if (c.Items.Count == 0) { b.Cursor = Cursors.Arrow; b.BorderThickness = new Thickness(0); b.SetResourceReference(Control.BackgroundProperty, "Panel"); }
            else b.Click += (_, _) => { if (!openChecks.Remove(c.Title)) openChecks.Add(c.Title); W.Navigate(this); };
            if (c.Sev == "crit") b.SetResourceReference(Control.BorderBrushProperty, "Sig");
            sp.Children.Add(b);
            if (isOpen)
            {
                var items = new StackPanel { Margin = new Thickness(68, 0, 0, 8) };
                foreach (var it in c.Items.Take(25))
                {
                    var r = new StackPanel { Orientation = Orientation.Horizontal };
                    r.Children.Add(Ui.Text(it.T, 13.5, FontWeights.Bold, "Ink", false, true));
                    var s2 = Ui.Muted("   " + it.S, 13); s2.VerticalAlignment = VerticalAlignment.Center; r.Children.Add(s2);
                    var ib = new Button { Content = r, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 0, 0, 3), MinHeight = 0 };
                    var target = it; ib.Click += (_, _) => Open(target.Open, target.Ref);
                    items.Children.Add(ib);
                }
                if (c.Items.Count > 25) items.Children.Add(Ui.Muted($"…and {c.Items.Count - 25} more", 13));
                if (c.Go != Health.Target.None) { var go = Ui.Btn("Open full list →", () => Open(c.Go, null), "Link"); go.HorizontalAlignment = HorizontalAlignment.Left; items.Children.Add(go); }
                sp.Children.Add(items);
            }
        }
        var card = Ui.Card(sp, 18);
        return card;
    }

    // ------------------------------------------------------------------ meters and bars
    static FrameworkElement Meter(double pct, string color)
    {
        pct = Math.Clamp(pct, 0, 100);
        var g = new Grid { Height = 10, VerticalAlignment = VerticalAlignment.Center };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(pct, 0.001), GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - pct, 0.001), GridUnitType.Star) });
        var bgb = new Border { CornerRadius = new CornerRadius(5), Background = Theme.B("Sunk") }; Grid.SetColumnSpan(bgb, 2); g.Children.Add(bgb);
        g.Children.Add(new Border { CornerRadius = new CornerRadius(5), Background = Theme.B(color) });
        return g;
    }

    static FrameworkElement MeterRow(string name, string small, double pct, string color, string value, string valueSmall, Action click)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        var n = new StackPanel(); n.Children.Add(Ui.Text(name, 13.5, FontWeights.SemiBold, "Ink", false, true)); n.Children.Add(Ui.Muted(small, 12));
        var m = Meter(pct, color); m.Margin = new Thickness(10, 0, 10, 0); Grid.SetColumn(m, 1);
        var v = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var vt = Ui.Text(value, 14, FontWeights.Bold, color == "Sunk" ? "Ink" : color, false, true); vt.HorizontalAlignment = HorizontalAlignment.Right; v.Children.Add(vt);
        var vs = Ui.Muted(valueSmall, 11.5); vs.HorizontalAlignment = HorizontalAlignment.Right; v.Children.Add(vs);
        Grid.SetColumn(v, 2);
        g.Children.Add(n); g.Children.Add(m); g.Children.Add(v);
        var b = new Button { Content = g, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 4), BorderThickness = new Thickness(0) };
        b.SetResourceReference(Control.BackgroundProperty, "Panel");
        b.Click += (_, _) => click();
        return b;
    }

    FrameworkElement Busiest(Health h)
    {
        var top = h.Busiest();
        if (top.Count == 0) return Ui.Muted("No subnets yet.", 13.5);
        var sp = new StackPanel();
        foreach (var o in top)
        {
            var sites = string.Join("⟷", o.N.SiteIds.Select(id => S.SiteById(id) is Site s ? "#" + s.SiteNumber : ""));
            var col = o.P >= 90 ? "Sig" : o.P >= 75 ? "Warn" : "Acc";
            var u = o;
            sp.Children.Add(MeterRow(o.N.E.Text, $"{o.N.Label} {sites}".Trim(), o.P, col, $"{o.P:0}%", $"{o.Used}/{IpMath.CountText(o.Cap)}", () => Open(u.N.Open, u.N.Id)));
        }
        return sp;
    }

    FrameworkElement Weakest(Health h)
    {
        var rf = h.Weakest();
        if (rf.Count == 0) return Ui.Muted("No wireless links with a signal value yet. (Signal, frequency and distance are filled in on the web version's connection form.)", 13.5);
        var sp = new StackPanel();
        foreach (var l in rf)
        {
            var s = Health.Signal(l).Value; var w = Math.Max(4, Math.Min(100, (s + 95) / 45 * 100));
            var cls = Health.SigClass(s); var col = cls == "good" ? "Ok" : cls == "fair" ? "Warn" : "Sig";
            var small = string.Join(" · ", new[] { Health.X(l, "band") == "" ? "" : Health.X(l, "band") + " GHz", Health.X(l, "freq") == "" ? "" : Health.X(l, "freq") + " MHz", Health.X(l, "dist") == "" ? "" : Health.X(l, "dist") + " km" }.Where(t => t != ""));
            var link = l;
            sp.Children.Add(MeterRow($"{S.DevById(l.A)?.Name ?? "?"} ↔ {S.DevById(l.B)?.Name ?? "?"}", small, w, col, s.ToString(), "dBm", () => W.Page<LinksPage>().Edit(link)));
        }
        return sp;
    }

    // ------------------------------------------------------------------ inventory
    static FrameworkElement Stacked(List<(string label, int n, string color)> parts, int total)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var g = new Grid { Height = 14 };
        int col = 0;
        foreach (var p in parts.Where(p => p.n > 0))
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(p.n, GridUnitType.Star) });
            var b = new Border { Background = Theme.B(p.color), Margin = new Thickness(0, 0, 2, 0), CornerRadius = new CornerRadius(3), ToolTip = $"{p.label}: {p.n}" };
            Grid.SetColumn(b, col++); g.Children.Add(b);
        }
        if (col == 0) { g.Children.Add(new Border { Background = Theme.B("Sunk"), CornerRadius = new CornerRadius(3) }); }
        sp.Children.Add(g);
        var leg = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        foreach (var p in parts.Where(p => p.n > 0))
        {
            var it = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 2) };
            it.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = Theme.B(p.color), Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            it.Children.Add(Ui.Text($"{p.label} ", 12.5, null, "Ink2", false));
            it.Children.Add(Ui.Text(p.n.ToString(), 12.5, FontWeights.Bold, "Ink", false));
            leg.Children.Add(it);
        }
        sp.Children.Add(leg);
        return sp;
    }

    static TextBlock Sub(string t) { var s = Ui.Text(t, 12.5, FontWeights.Bold, "Muted"); s.Margin = new Thickness(0, 4, 0, 6); return s; }

    FrameworkElement Inventory()
    {
        var devs = S.Db.Devices;
        if (devs.Count == 0) return Ui.Muted("No devices yet.", 13.5);
        var sp = new StackPanel();
        var types = Store.NetTypes.Concat(Store.EndTypes).ToArray();
        sp.Children.Add(Sub("By type"));
        sp.Children.Add(Stacked(types.Select((t, i) => (Store.TypeLabel[t], devs.Count(d => d.Type == t), Palette[i % 6])).ToList(), devs.Count));
        sp.Children.Add(Sub("By status"));
        var stCol = new Dictionary<string, string> { ["active"] = "Ok", ["planned"] = "Acc", ["reserved"] = "Warn", ["offline"] = "Sig", ["retired"] = "Muted" };
        sp.Children.Add(Stacked(Store.StatusLabel.Select(kv => (kv.Value, devs.Count(d => Store.StatusOf(d.Status) == kv.Key), stCol[kv.Key])).ToList(), devs.Count));
        if (W.Monitor.States.Count > 0)
        {
            sp.Children.Add(Sub("Live ping"));
            int up = devs.Count(d => W.Monitor.Of(d.Id) == DeviceMonitor.State.Up), dn = devs.Count(d => W.Monitor.Of(d.Id) == DeviceMonitor.State.Down);
            sp.Children.Add(Stacked(new() { ("Up", up, "Ok"), ("Down", dn, "Sig"), ("Not pinged", devs.Count - up - dn, "Sunk") }, devs.Count));
        }
        var funcs = new (string, string)[] { ("core", "Core"), ("distribution", "Distribution"), ("access", "Access"), ("backhaul", "Backhaul"), ("customer", "Customer (CPE)"), ("management", "Management"), ("other", "Other") };
        sp.Children.Add(Sub("By function"));
        var fl = funcs.Select((f, i) => (f.Item2, devs.Count(d => d.Func == f.Item1), Palette[i % 6])).ToList();
        fl.Add(("Not set", devs.Count(d => string.IsNullOrEmpty(d.Func)), "Sunk"));
        sp.Children.Add(Stacked(fl, devs.Count));
        sp.Children.Add(Sub("RouterOS versions"));
        var ros = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var g in devs.GroupBy(d => string.IsNullOrEmpty(d.Ros) ? "unknown" : d.Ros).OrderBy(g => g.Key == "unknown" ? 1 : 0).ThenByDescending(g => g.Key))
        {
            var old = g.Key.StartsWith("6."); var unk = g.Key == "unknown";
            var b = Ui.Badge($"{(unk ? "unknown" : "v" + g.Key)}  {g.Count()}", Theme.B(old ? "Warn" : unk ? "Muted" : "Ink2"), Theme.B(old ? "WarnSoft" : "Sunk")); b.Margin = new Thickness(0, 0, 6, 6);
            ros.Children.Add(b);
        }
        sp.Children.Add(ros);
        sp.Children.Add(Sub("Top models"));
        var mods = new WrapPanel();
        foreach (var g in devs.GroupBy(d => d.Model).OrderByDescending(g => g.Count()).Take(6)) { var b = Ui.Badge($"{g.Key}  {g.Count()}", Theme.B("Ink2"), Theme.B("Sunk")); b.Margin = new Thickness(0, 0, 6, 6); mods.Children.Add(b); }
        sp.Children.Add(mods);
        return sp;
    }

    public class SpaceRow { public string Space { get; set; } public int Subnets { get; set; } public string Used { get; set; } public string Usable { get; set; } }
    FrameworkElement AddressSpace(Health h)
    {
        var rows = h.AddressSpace();
        if (rows.Count == 0) return Ui.Muted("No subnets yet.", 13.5);
        var g = Ui.Table();
        g.Columns.Add(Ui.Col("Space", nameof(SpaceRow.Space), -1));
        g.Columns.Add(Ui.Col("Subnets", nameof(SpaceRow.Subnets)));
        g.Columns.Add(Ui.Col("Used", nameof(SpaceRow.Used)));
        g.Columns.Add(Ui.Col("Usable", nameof(SpaceRow.Usable)));
        g.ItemsSource = rows.Select(r => new SpaceRow { Space = r.space, Subnets = r.subnets, Used = r.used.ToString("N0"), Usable = IpMath.CountText(r.usable) }).ToList();
        g.IsHitTestVisible = false;
        return g;
    }

    public class SiteRow2
    {
        public string Id { get; set; }
        public string Num { get; set; }
        public string NumKey { get; set; }
        public string Name { get; set; }
        public int Nets { get; set; }
        public int Devs { get; set; }
        public int Links { get; set; }
        public string Ipv4 { get; set; }
        public string Rf { get; set; }
        public string Down { get; set; }
        public string Issues { get; set; }
        public string Status { get; set; }
        public Brush Fg { get; set; }
        public Brush Bg { get; set; }
    }
    FrameworkElement SitesTable(Health h)
    {
        if (S.Db.Sites.Count == 0) return Ui.Muted("No sites yet.", 13.5);
        var g = Ui.Table();
        g.Columns.Add(Ui.Col("#", nameof(SiteRow2.Num), 60, true, nameof(SiteRow2.NumKey)));
        g.Columns.Add(Ui.Col("Site", nameof(SiteRow2.Name), -1));
        g.Columns.Add(Ui.BadgeCol("Status", nameof(SiteRow2.Status), nameof(SiteRow2.Fg), nameof(SiteRow2.Bg)));
        g.Columns.Add(Ui.Col("Nets", nameof(SiteRow2.Nets)));
        g.Columns.Add(Ui.Col("Devices", nameof(SiteRow2.Devs)));
        g.Columns.Add(Ui.Col("Links", nameof(SiteRow2.Links)));
        g.Columns.Add(Ui.Col("IPv4", nameof(SiteRow2.Ipv4)));
        g.Columns.Add(Ui.Col("Worst RF", nameof(SiteRow2.Rf)));
        g.Columns.Add(Ui.Col("Down (ping)", nameof(SiteRow2.Down)));
        g.Columns.Add(Ui.Col("Issues", nameof(SiteRow2.Issues)));
        g.ItemsSource = S.Db.Sites.OrderBy(s => s.SiteNumber?.PadLeft(10, '0')).Select(s =>
        {
            var ds = S.Db.Devices.Where(d => d.SiteId == s.Id).ToList(); var ids = ds.Select(d => d.Id).ToHashSet();
            var sl = S.Db.Links.Where(l => ids.Contains(l.A) || ids.Contains(l.B)).ToList();
            var (c, u) = h.SiteIpv4(s.Id);
            var worst = sl.Where(l => l.Type == "wireless").Select(Health.Signal).Where(x => x != null).DefaultIfEmpty(null).Min();
            var dn = ds.Count(d => W.Monitor.Of(d.Id) == DeviceMonitor.State.Down);
            var iss = h.SiteIssues(s.Id); var (fg, bg) = Theme.Status(s.Status);
            return new SiteRow2
            {
                Id = s.Id, Num = s.SiteNumber, NumKey = s.SiteNumber?.PadLeft(10, '0'), Name = s.Name, Nets = S.NetsOf(s.Id).Count(), Devs = ds.Count, Links = sl.Count,
                Ipv4 = c > 0 ? $"{u / (double)c * 100:0}%" : "—", Rf = worst == null ? "—" : $"{worst} dBm", Down = W.Monitor.States.Count == 0 ? "—" : dn == 0 ? "✓" : dn.ToString(),
                Issues = iss == 0 ? "✓" : iss.ToString(), Status = Store.StatusLabel[Store.StatusOf(s.Status)], Fg = fg, Bg = bg
            };
        }).ToList();
        Ui.OnRowDoubleClick(g, r => { W.SiteFilter = ((SiteRow2)r).Id; W.Navigate<MapPage>(); });
        g.MouseLeftButtonUp += (_, e) => { if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(g, d) is DataGridRow row && row.Item is SiteRow2 r) { W.SiteFilter = r.Id; W.Navigate<MapPage>(); } };
        g.Cursor = Cursors.Hand;
        return g;
    }

    FrameworkElement Recent()
    {
        var recent = S.Db.Changes.OrderByDescending(c => c.At, StringComparer.Ordinal).Take(6).ToList();
        if (recent.Count == 0) return Ui.Muted("No changes recorded yet.", 13.5);
        var g = Ui.Table();
        g.Columns.Add(Ui.Col("When", nameof(HistoryPage.ChangeRow.When), 150));
        g.Columns.Add(Ui.Col("Who", nameof(HistoryPage.ChangeRow.By), 110));
        g.Columns.Add(Ui.BadgeCol("Action", nameof(HistoryPage.ChangeRow.Action), nameof(HistoryPage.ChangeRow.Fg), nameof(HistoryPage.ChangeRow.Bg)));
        g.Columns.Add(Ui.Col("What", nameof(HistoryPage.ChangeRow.What), -1));
        g.Columns.Add(Ui.Col("Details", nameof(HistoryPage.ChangeRow.Details), -1.2));
        g.ItemsSource = recent.Select(HistoryPage.ToRow).ToList();
        g.IsHitTestVisible = false;
        return g;
    }
}
