namespace IpMonitor.App;

public class LinksPage : PageBase
{
    public override string Title => "Connections";
    public override string Glyph => "";
    string search = "", selId;

    public class LinkRow
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string From { get; set; }
        public string FromIf { get; set; }
        public string FromIp { get; set; }
        public string To { get; set; }
        public string ToIf { get; set; }
        public string ToIp { get; set; }
        public string Subnet { get; set; }
        public string Ssid { get; set; }
        public string Freq { get; set; }
        public string Band { get; set; }
        public string Dist { get; set; }
        public Brush Fg { get; set; }
        public Brush Bg { get; set; }
    }

    public override FrameworkElement Build()
    {
        var g = Ui.Table();
        g.Columns.Add(Ui.BadgeCol("Type", nameof(LinkRow.Type), nameof(LinkRow.Fg), nameof(LinkRow.Bg)));
        g.Columns.Add(Ui.Col("From", nameof(LinkRow.From), -1.3)); g.Columns[^1].MinWidth = 120;
        g.Columns.Add(Ui.Col("Interface · IP", nameof(LinkRow.FromIf), -1.1, true)); g.Columns[^1].MinWidth = 110;
        g.Columns.Add(Ui.Col("To", nameof(LinkRow.To), -1.3)); g.Columns[^1].MinWidth = 120;
        g.Columns.Add(Ui.Col("Interface · IP", nameof(LinkRow.ToIf), -1.1, true)); g.Columns[^1].MinWidth = 110;
        g.Columns.Add(Ui.Col("SSID", nameof(LinkRow.Ssid), -1)); g.Columns[^1].MinWidth = 80;
        g.Columns.Add(Ui.Col("Freq.", nameof(LinkRow.Freq)));
        g.Columns.Add(Ui.Col("Band", nameof(LinkRow.Band)));
        g.Columns.Add(Ui.Col("Distance", nameof(LinkRow.Dist)));
        var empty = Ui.Muted("", 15);
        string DevLabel(Device d) => d == null ? "?" : $"{d.Name}  (#{S.SiteById(d.SiteId)?.SiteNumber})";
        static string End(string port, string ip) => string.Join(" · ", new[] { port, ip }.Where(x => !string.IsNullOrEmpty(x)));
        void Refresh()
        {
            var rows = S.Db.Links.Where(l =>
                {
                    var a = S.DevById(l.A); var b = S.DevById(l.B);
                    return (InSite(a?.SiteId) || InSite(b?.SiteId)) && Match(search, a?.Name, b?.Name, l.PortA, l.PortB, l.IpA, l.IpB, l.Subnet, l.Ssid, l.Notes, l.Type);
                })
                .Select(l => new LinkRow
                {
                    Id = l.Id, Type = l.Type == "wireless" ? "Wireless" : "Wired", From = DevLabel(S.DevById(l.A)), FromIf = End(l.PortA, l.IpA), FromIp = l.IpA,
                    To = DevLabel(S.DevById(l.B)), ToIf = End(l.PortB, l.IpB), ToIp = l.IpB, Subnet = l.Subnet, Ssid = l.Ssid,
                    Freq = Health.X(l, "freq") == "" ? "" : Health.X(l, "freq") + " MHz", Band = Health.X(l, "band") == "" ? "" : Health.X(l, "band") + " GHz",
                    Dist = Health.X(l, "dist") == "" ? "" : Health.X(l, "dist") + " km",
                    Fg = Theme.B(l.Type == "wireless" ? "Warn" : "Acc"), Bg = Theme.B(l.Type == "wireless" ? "WarnSoft" : "AccSoft")
                }).OrderBy(r => r.From).ToList();
            g.ItemsSource = rows;
            empty.Text = rows.Count > 0 ? "" : S.Db.Devices.Count < 2 ? "Add at least two devices first." : S.Db.Links.Count == 0 ? "No connections yet. Click “Add connection”." : "No connection matches.";
            var keep = rows.FirstOrDefault(r => r.Id == selId);
            if (keep != null) g.SelectedItem = keep;
        }
        Link Sel() => g.SelectedItem is LinkRow r ? S.Db.Links.FirstOrDefault(l => l.Id == r.Id) : null;
        var edit = Ui.IconBtn("", "Edit", () => { if (Sel() is Link l) Edit(l); });
        var del = Ui.IconBtn("", "Delete", () => { if (Sel() is Link l) Confirm($"Delete the {l.Type} connection {S.LinkLabel(l)}?", () => S.DeleteLink(l.Id)); }, "Danger");
        void Buttons() { edit.IsEnabled = Sel() != null; del.IsEnabled = Sel() != null && S.CanWrite; }
        g.SelectionChanged += (_, _) => { if (g.SelectedItem is LinkRow r) selId = r.Id; Buttons(); };
        Ui.OnRowDoubleClick(g, r => Edit(S.Db.Links.First(l => l.Id == ((LinkRow)r).Id)));
        Refresh(); Buttons();
        var bar = FilterBar(() => search, v => search = v, Refresh, true, Ui.Row(8, edit, del));
        return Layout(Header("Connections", "Cables inside a site and wireless links (also between sites).", AddBtn("Add connection", () => Edit(null))), bar, TableWithEmpty(g, empty));
    }

    /// <summary>Frequency, band and distance of a wireless connection (typed in; the same fields as the web version).</summary>
    static UIElement RadioFields(Link l)
    {
        string X(string k) => Health.X(l, k);
        void SetNum(string k, string v)
        {
            v = (v ?? "").Trim().Replace(',', '.');
            if (v == "") Store.SetX(l, k, null);
            else if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)) Store.SetX(l, k, n == Math.Floor(n) ? (object)(int)n : n);
            else Store.SetX(l, k, v);   // kept as typed; the save explains what is wrong
        }
        var freq = Ui.Box(X("freq")); freq.TextChanged += (_, _) => SetNum("freq", freq.Text);
        var band = Ui.Choice(new[] { ("", "—"), ("2.4", "2.4 GHz"), ("5", "5 GHz"), ("60", "60 GHz") }, X("band"));
        band.SelectionChanged += (_, _) => Store.SetX(l, "band", band.Val() == "" ? null : band.Val());
        var dist = Ui.Box(X("dist")); dist.TextChanged += (_, _) => SetNum("dist", dist.Text);
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var h = Ui.Text("Radio", 15, FontWeights.Bold); h.Margin = new Thickness(0, 4, 0, 8); sp.Children.Add(h);
        sp.Children.Add(Ui.Cols(Ui.Field("Frequency (MHz)", freq, "e.g. 5180"), Ui.Field("Band", band), Ui.Field("Distance (km)", dist, "e.g. 3.5")));
        return sp;
    }

    /// <summary>Add (link = null, optionally pre-filled by draft) or edit a connection.</summary>
    public void Edit(Link link, Link draft = null)
    {
        if (S.Db.Devices.Count < 2) { Ui.Info("Add at least two devices first.", "Connections"); return; }
        var l = link == null ? draft ?? new Link { Type = "wired" } : DbIo.Clone(link);
        var ro = !S.CanWrite;
        var d = new Dlg(link == null ? "Add connection" : "Edit connection", 760);
        var devs = S.Db.Devices.OrderBy(x => S.SiteById(x.SiteId)?.SiteNumber?.PadLeft(10, '0')).ThenBy(x => x.Name)
            .Select(x => (x.Id, $"{x.Name} · {Store.TypeLabel.GetValueOrDefault(x.Type, x.Type)} · #{S.SiteById(x.SiteId)?.SiteNumber}")).ToList();
        var body = new StackPanel(); d.Body.Children.Add(body);

        void Render()
        {
            body.Children.Clear();
            var type = Ui.Choice(new[] { ("wired", "Wired (cable, same site)"), ("wireless", "Wireless (can join two sites)") }, l.Type);
            type.SelectionChanged += (_, _) => { l.Type = type.Val(); d.Dispatcher.BeginInvoke(Render); };
            body.Children.Add(Ui.Field("Type", type));
            UIElement Side(string label, string devId, Action<string> setDev, string port, Action<string> setPort, string ip, Action<string> setIp)
            {
                var sp = new StackPanel();
                var dc = Ui.Choice(new[] { ("", "— choose —") }.Concat(devs), devId);
                dc.SelectionChanged += (_, _) => { setDev(dc.Val()); d.Dispatcher.BeginInvoke(Render); };
                sp.Children.Add(Ui.Field(label, dc));
                var dev = S.DevById(devId);
                var ports = dev == null ? new List<string>() : S.PortsOf(dev).Where(p => l.Type == "wireless" ? Store.IsWifiPort(p) : !Store.IsWifiPort(p)).ToList();
                if (dev != null && !dev.IsMikroTik && ports.Count == 0) ports = l.Type == "wireless" ? new() { "wlan1" } : new() { "eth0", "lan" };
                var pc = Ui.Editable(ports, port);
                pc.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => setPort(pc.Text.Trim())));
                pc.SelectionChanged += (_, _) => { if (pc.SelectedItem is string s) setPort(s); };
                sp.Children.Add(Ui.Field("Interface", pc, l.Type == "wired" && dev != null && S.EndIp(dev, port) is var eip && eip != "" ? "IP on this interface: " + eip : null));
                if (l.Type == "wireless")
                {
                    var ipb = Ui.Box(ip, false, true); ipb.TextChanged += (_, _) => setIp(ipb.Text);
                    sp.Children.Add(Ui.Field("WLAN IP", ipb));
                }
                return sp;
            }
            body.Children.Add(Ui.Cols(
                Side("From device", l.A, v => l.A = v, l.PortA, v => l.PortA = v, l.IpA, v => l.IpA = v),
                Side("To device", l.B, v => l.B = v, l.PortB, v => l.PortB = v, l.IpB, v => l.IpB = v)));
            if (l.Type == "wireless")
            {
                var sub = Ui.Box(l.Subnet, false, true); sub.TextChanged += (_, _) => l.Subnet = sub.Text;
                var fill = Ui.Btn("Fill both IPs", () =>
                {
                    var (a, b) = Store.SplitSubnet(l.Subnet);
                    if (a == "") { d.ErrorText = "Type a link subnet with at least two usable addresses, e.g. 10.5.5.0/30."; return; }
                    l.IpA = a; l.IpB = b; Render();
                });
                var row = new DockPanel(); DockPanel.SetDock(fill, Dock.Right); fill.Margin = new Thickness(8, 0, 0, 0); row.Children.Add(fill); row.Children.Add(sub);
                var ssid = Ui.Box(l.Ssid); ssid.TextChanged += (_, _) => l.Ssid = ssid.Text.Trim();
                body.Children.Add(Ui.Cols(Ui.Field("Link subnet", row, "e.g. 10.5.5.0/30 — the WLAN IPs are added to both devices."), Ui.Field("SSID", ssid)));
                body.Children.Add(RadioFields(l));
            }
            var notes = Ui.Box(l.Notes, true); notes.TextChanged += (_, _) => l.Notes = notes.Text.Trim();
            body.Children.Add(Ui.Field("Notes", notes));
            body.IsEnabled = !ro;
        }
        Render();
        if (ro) d.Cancel("Close");
        else
        {
            d.Ok(link == null ? "Add connection" : "Save", () =>
            {
                S.SaveLink(l); selId = l.Id;
                return true;
            });
            d.Cancel();
        }
        d.Open();
    }
}
