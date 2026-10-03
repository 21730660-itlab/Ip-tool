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
        public string Proto { get; set; }
        public string Signal { get; set; }
        public Brush SigFg { get; set; }
        public Brush SigBg { get; set; }
        public string Dist { get; set; }
        public string ReadAt { get; set; }
        public Brush Fg { get; set; }
        public Brush Bg { get; set; }
    }

    public static string ProtoText(string p) => p switch { "nv2" => "NV2", "802.11" => "802.11", "nstreme" => "Nstreme", _ => "" };

    /// <summary>Reads the radio details of these wireless connections from the routers and saves them. Returns a summary.</summary>
    public static async Task<(int ok, List<string> problems)> ReadFromRouters(IEnumerable<Link> links)
    {
        var list = links.Where(l => l.Type == "wireless").ToList();
        var results = new List<(Link, Radio.LinkReading)>(); var problems = new List<string>();
        using var gate = new SemaphoreSlim(4);
        var tasks = list.Select(async l =>
        {
            await gate.WaitAsync();
            try { return (l, r: await Radio.ReadLinkAsync(S, l)); }
            finally { gate.Release(); }
        }).ToList();
        foreach (var t in tasks)
        {
            var (l, r) = await t;
            if (r.Ok) results.Add((l, r));
            foreach (var n in new[] { r.NoteA, r.NoteB }.Where(n => n != "" && !n.EndsWith(": read"))) problems.Add($"{S.LinkLabel(l)} — {n}");
        }
        int saved = 0;
        if (results.Count > 0 && S.CanWrite) saved = S.ApplyRadio(results);
        return (saved, problems);
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
        g.Columns.Add(Ui.Col("Protocol", nameof(LinkRow.Proto)));
        g.Columns.Add(Ui.BadgeCol("Signal", nameof(LinkRow.Signal), nameof(LinkRow.SigFg), nameof(LinkRow.SigBg)));
        g.Columns.Add(Ui.Col("Distance", nameof(LinkRow.Dist)));
        g.Columns.Add(Ui.Col("Read", nameof(LinkRow.ReadAt)));
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
                    Freq = Health.X(l, "freq") == "" ? "" : Health.X(l, "freq") + " MHz", Proto = ProtoText(Health.X(l, "proto")),
                    Signal = Health.Signal(l) is double sg ? $"{sg:0} dBm" : "", SigFg = Theme.B(Health.SigClass(Health.Signal(l)) switch { "good" => "Ok", "fair" => "Warn", "poor" => "Sig", _ => "Muted" }),
                    SigBg = Theme.B(Health.SigClass(Health.Signal(l)) switch { "good" => "OkSoft", "fair" => "WarnSoft", "poor" => "SigSoft", _ => "Transparent" }),
                    Dist = Health.X(l, "dist") == "" ? "" : Health.X(l, "dist") + " km", ReadAt = Health.When(Health.X(l, "radioAt")) is var ra && ra.Length >= 16 ? ra[5..] : "",
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
        Button readOne = null, readAll = null;
        async Task DoRead(IEnumerable<Link> links, string what)
        {
            readOne.IsEnabled = readAll.IsEnabled = false; Mouse.OverrideCursor = Cursors.AppStarting;
            try
            {
                var (ok, problems) = await ReadFromRouters(links);
                if (problems.Count == 0) W.Toast($"{ok} wireless connection{(ok == 1 ? "" : "s")} updated from the routers.");
                else Ui.Info($"{ok} wireless connection{(ok == 1 ? "" : "s")} updated from the routers.\n\nCould not read:\n• " + string.Join("\n• ", problems.Take(15)) + (problems.Count > 15 ? $"\n…and {problems.Count - 15} more" : "") +
                    "\n\nThe app logs in with the username and password saved on each device, through the RouterOS API (IP → Services → api, port 8728).", "Read from routers");
            }
            finally { Mouse.OverrideCursor = null; readAll.IsEnabled = S.CanWrite; Buttons(); }
        }
        readOne = Ui.IconBtn("\uE895", "Read from routers", () => { if (Sel() is Link l) _ = DoRead(new[] { l }, "this"); }, null, "Read frequency, SSID, protocol (NV2…), signal and distance from the two MikroTiks");
        readAll = Ui.IconBtn("\uE895", "Update all wireless", () => _ = DoRead(S.Db.Links.Where(l => l.Type == "wireless").ToList(), "all"), null, "Read every wireless connection from its routers");
        readAll.IsEnabled = S.CanWrite && S.Db.Links.Any(l => l.Type == "wireless");
        void Buttons() { edit.IsEnabled = Sel() != null; del.IsEnabled = Sel() != null && S.CanWrite; if (readOne != null) readOne.IsEnabled = S.CanWrite && Sel()?.Type == "wireless"; }
        g.SelectionChanged += (_, _) => { if (g.SelectedItem is LinkRow r) selId = r.Id; Buttons(); };
        Ui.OnRowDoubleClick(g, r => Edit(S.Db.Links.First(l => l.Id == ((LinkRow)r).Id)));
        Refresh(); Buttons();
        var bar = FilterBar(() => search, v => search = v, Refresh, true, Ui.Row(8, edit, readOne, readAll, del));
        var auto = W.Settings.RadioAuto && W.Settings.MonitorOn ? $" Wireless details are read from the routers automatically every {W.Settings.RadioMinutes} min." : "";
        return Layout(Header("Connections", "Cables inside a site and wireless links (also between sites)." + auto, AddBtn("Add connection", () => Edit(null))), bar, TableWithEmpty(g, empty));
    }

    /// <summary>A new wireless connection: read its radio values right away.</summary>
    static async Task ReadAfterSave(string linkId)
    {
        if (S.Db.Links.FirstOrDefault(x => x.Id == linkId) is not Link l) return;
        var (ok, problems) = await ReadFromRouters(new[] { l });
        W.Toast(ok > 0 ? $"Radio values of {S.LinkLabel(l)} read from the routers." : $"Radio values of {S.LinkLabel(l)} could not be read: {problems.FirstOrDefault()}");
    }

    static readonly string[] RadioKeys = { "freq", "band", "width", "proto", "signal", "signalA", "signalB", "dist", "radioAt" };

    /// <summary>
    /// Frequency, band, width, protocol, signal and distance of a wireless connection. These are not typed in:
    /// they are read from the two MikroTiks (automatically), and shown only when they could be read.
    /// </summary>
    UIElement RadioSection(Link l, Dlg d)
    {
        string X(string k) => Health.X(l, k);
        var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var status = Ui.Muted("", 12.5);
        var values = new StackPanel();
        var again = Ui.Btn("Read now", () => { }, "Link", "Read the values from the routers now");
        again.IsEnabled = S.CanWrite;
        DockPanel.SetDock(again, Dock.Right); head.Children.Add(again);
        var ht = new StackPanel(); ht.Children.Add(Ui.Text("Radio · read from the routers", 15, FontWeights.Bold)); ht.Children.Add(status); head.Children.Add(ht);
        sp.Children.Add(head); sp.Children.Add(values);

        void Show(string problem = null)
        {
            values.Children.Clear();
            if (X("radioAt") == "")
            {
                status.Text = problem == null ? "Not read yet." : "";
                var msg = problem ?? "Frequency, band, width, protocol (NV2…), signal and distance are read automatically from the two MikroTiks, with the username and password saved on each device.";
                var t = Ui.Text(msg, 13.5, null, problem == null ? "Ink2" : "Sig"); t.Margin = new Thickness(0, 0, 0, 4);
                values.Children.Add(t);
                if (problem != null) values.Children.Add(Ui.Muted("Check on each device: IP address, username and password; and on the router: IP → Services → api (port 8728) enabled.", 12.5));
                return;
            }
            status.Text = $"Read {Health.When(X("radioAt"))}" + (W.Settings.RadioAuto && W.Settings.MonitorOn ? $" · updated automatically every {W.Settings.RadioMinutes} min" : "");
            var g = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            void V(string label, string value, string brush = "Ink")
            {
                var c = new StackPanel { Margin = new Thickness(0, 0, 12, 10) };
                c.Children.Add(Ui.Text(label, 12.5, FontWeights.SemiBold, "Muted"));
                c.Children.Add(Ui.Text(value == "" ? "—" : value, 15.5, FontWeights.Bold, brush));
                g.Children.Add(c);
            }
            var sg = Health.Signal(l); var cls = Health.SigClass(sg);
            V("Frequency", X("freq") == "" ? "" : X("freq") + " MHz");
            V("Band · width", string.Join(" · ", new[] { X("band") == "" ? "" : X("band") + " GHz", X("width") == "" ? "" : X("width") + " MHz" }.Where(x => x != "")));
            V("Protocol", ProtoText(X("proto")));
            V("Signal", sg is double v ? $"{v:0} dBm" : "", cls == "good" ? "Ok" : cls == "fair" ? "Warn" : cls == "poor" ? "Sig" : "Ink");
            V("Each end", X("signalA") == "" && X("signalB") == "" ? "" : $"{S.DevById(l.A)?.Name}: {(X("signalA") == "" ? "—" : X("signalA"))} · {S.DevById(l.B)?.Name}: {(X("signalB") == "" ? "—" : X("signalB"))}");
            V("Distance", X("dist") == "" ? "" : X("dist") + " km");
            values.Children.Add(g);
            if (problem != null) { var t = Ui.Text("Last try: " + problem, 12.5, null, "Warn"); values.Children.Add(t); }
        }

        async Task Read()
        {
            if (string.IsNullOrEmpty(l.A) || string.IsNullOrEmpty(l.B) || !S.CanWrite) return;
            again.IsEnabled = false; status.Text = "Reading from the routers…";
            try
            {
                var r = await Radio.ReadLinkAsync(S, l);
                var notes = string.Join("  ·  ", new[] { r.NoteA, r.NoteB }.Where(n => n != "" && !n.EndsWith(": read")));
                if (!r.Ok) { Show("Could not be read. " + notes); return; }
                // saved at once on the connection (when it exists already), and copied into this form
                var saved = S.Db.Links.FirstOrDefault(x => x.Id == l.Id && x.A == l.A && x.B == l.B && x.PortA == l.PortA && x.PortB == l.PortB);   // same devices as saved
                if (saved != null) S.ApplyRadio(new[] { (saved, r) });
                var src = saved ?? l;
                if (saved == null)
                {
                    if (r.Freq is double f) Store.SetX(l, "freq", (int)Math.Round(f));
                    if (r.Band != "") Store.SetX(l, "band", r.Band); if (r.Width != "") Store.SetX(l, "width", r.Width); if (r.Proto != "") Store.SetX(l, "proto", r.Proto);
                    if (r.Signal is double sg2) Store.SetX(l, "signal", (int)Math.Round(sg2));
                    Store.SetX(l, "signalA", r.SignalA is double a1 ? (int)Math.Round(a1) : null); Store.SetX(l, "signalB", r.SignalB is double b1 ? (int)Math.Round(b1) : null);
                    if (r.Distance is double dd) Store.SetX(l, "dist", Math.Round(dd, 2));
                    Store.SetX(l, "radioAt", Entity.Now());
                }
                else foreach (var k in RadioKeys) Store.SetX(l, k, Health.X(saved, k) == "" ? null : saved.Extra[k]);
                if (r.Ssid != "") l.Ssid = r.Ssid;
                Show(notes == "" ? null : "only one end could be read — " + notes);
            }
            finally { again.IsEnabled = S.CanWrite; }
        }
        again.Click += async (_, _) => await Read();
        Show();
        // never read: read now, without waiting for the automatic round
        if (X("radioAt") == "" && S.CanWrite && !string.IsNullOrEmpty(l.A) && !string.IsNullOrEmpty(l.B) && S.Db.Links.Any(x => x.Id == l.Id))
            d.Dispatcher.BeginInvoke(async () => await Read());
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
        UIElement radio = null;   // kept when the form is redrawn, so a reading in progress is not lost
        string radioPair = "";

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
                if (radioPair != $"{l.A}|{l.B}|{l.PortA}|{l.PortB}")
                {
                    if (radioPair != "") foreach (var k in RadioKeys) Store.SetX(l, k, null);   // other devices: the old readings no longer apply
                    radio = null; radioPair = $"{l.A}|{l.B}|{l.PortA}|{l.PortB}";
                }
                if (radio == null) radio = RadioSection(l, d);
                else if (radio is FrameworkElement fe && fe.Parent is Panel pp) pp.Children.Remove(radio);
                body.Children.Add(radio);
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
                if (l.Type == "wireless" && Health.X(l, "radioAt") == "" && S.CanWrite)
                    _ = ReadAfterSave(l.Id);
                return true;
            });
            d.Cancel();
        }
        d.Open();
    }
}
