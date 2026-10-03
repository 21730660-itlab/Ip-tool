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

    /// <summary>Frequency, band, width, protocol, signal and distance of a wireless connection, with "Read from routers".</summary>
    UIElement RadioSection(Link l, Dlg d, Action redraw)
    {
        string X(string k) => Health.X(l, k);
        void Set(string k, string v, bool number)
        {
            v = (v ?? "").Trim();
            if (v == "") Store.SetX(l, k, null);
            else if (number && double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)) Store.SetX(l, k, n == Math.Floor(n) ? (object)(int)n : n);
            else if (!number) Store.SetX(l, k, v);
        }
        var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
        var head = new DockPanel { Margin = new Thickness(0, 6, 0, 8) };
        var status = Ui.Muted(X("radioAt") == "" ? "Not read from the routers yet." : $"Last read from the routers: {Health.When(X("radioAt"))}", 12.5);
        var read = Ui.IconBtn("\uE895", "Read from routers", async () =>
        {
            if (string.IsNullOrEmpty(l.A) || string.IsNullOrEmpty(l.B)) { d.ErrorText = "Choose both devices first."; return; }
            status.Text = "Connecting to the routers…"; d.ErrorText = "";
            var r = await Radio.ReadLinkAsync(S, l);
            if (r.Freq is double f) Store.SetX(l, "freq", (int)Math.Round(f));
            if (r.Band != "") Store.SetX(l, "band", r.Band);
            if (r.Width != "") Store.SetX(l, "width", r.Width);
            if (r.Proto != "") Store.SetX(l, "proto", r.Proto);
            if (r.Signal is double s) Store.SetX(l, "signal", (int)Math.Round(s));
            Store.SetX(l, "signalA", r.SignalA is double sa ? (int)Math.Round(sa) : null); Store.SetX(l, "signalB", r.SignalB is double sb ? (int)Math.Round(sb) : null);
            if (r.Distance is double dd) Store.SetX(l, "dist", Math.Round(dd, 2));
            if (r.Ssid != "") l.Ssid = r.Ssid;
            if (r.Ok) Store.SetX(l, "radioAt", Entity.Now());
            var notes = string.Join("  ·  ", new[] { r.NoteA, r.NoteB }.Where(n => n != ""));
            redraw();
            if (!r.Ok) d.ErrorText = "Nothing could be read. " + notes;
            else
            {
                MainWindow.Instance.Toast("Read from the routers: " + r.Summary() + " — click Save to keep it.");
                if (!(r.NoteA.EndsWith(": read") && r.NoteB.EndsWith(": read"))) d.ErrorText = "Only one end could be read: " + notes;
            }
        }, "Primary", "Log in to both MikroTiks with their saved username and password and read the values");
        read.IsEnabled = S.CanWrite;
        DockPanel.SetDock(read, Dock.Right); head.Children.Add(read);
        var ht = new StackPanel(); ht.Children.Add(Ui.Text("Radio", 15, FontWeights.Bold)); ht.Children.Add(status); head.Children.Add(ht);
        sp.Children.Add(head);
        var band = Ui.Choice(new[] { ("", "—"), ("2.4", "2.4 GHz"), ("5", "5 GHz"), ("60", "60 GHz") }, X("band")); band.SelectionChanged += (_, _) => Set("band", band.Val(), false);
        var width = Ui.Choice(new[] { ("", "—"), ("20", "20 MHz"), ("40", "40 MHz"), ("80", "80 MHz"), ("160", "160 MHz"), ("2160", "2160 MHz") }, X("width")); width.SelectionChanged += (_, _) => Set("width", width.Val(), false);
        var freq = Ui.Box(X("freq")); freq.TextChanged += (_, _) => Set("freq", freq.Text, true);
        var proto = Ui.Choice(new[] { ("", "—"), ("nv2", "NV2 (MikroTik TDMA)"), ("802.11", "802.11"), ("nstreme", "Nstreme") }, X("proto")); proto.SelectionChanged += (_, _) => Set("proto", proto.Val(), false);
        var sig = Ui.Box(X("signal")); sig.TextChanged += (_, _) => Set("signal", sig.Text, true);
        var dist = Ui.Box(X("dist")); dist.TextChanged += (_, _) => Set("dist", dist.Text, true);
        sp.Children.Add(Ui.Cols(Ui.Field("Frequency (MHz)", freq), Ui.Field("Band", band), Ui.Field("Channel width", width)));
        var both = X("signalA") != "" || X("signalB") != "" ? $"{S.DevById(l.A)?.Name}: {(X("signalA") == "" ? "—" : X("signalA") + " dBm")} · {S.DevById(l.B)?.Name}: {(X("signalB") == "" ? "—" : X("signalB") + " dBm")}" : "e.g. -62. The weaker side is kept.";
        sp.Children.Add(Ui.Cols(Ui.Field("Protocol", proto), Ui.Field("Signal (dBm)", sig, both), Ui.Field("Distance (km)", dist)));
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
                body.Children.Add(RadioSection(l, d, Render));
            }
            var notes = Ui.Box(l.Notes, true); notes.TextChanged += (_, _) => l.Notes = notes.Text.Trim();
            body.Children.Add(Ui.Field("Notes", notes));
            body.IsEnabled = !ro;
        }
        Render();
        if (ro) d.Cancel("Close");
        else
        {
            d.Ok(link == null ? "Add connection" : "Save", () => { S.SaveLink(l); selId = l.Id; return true; });
            d.Cancel();
        }
        d.Open();
    }
}
