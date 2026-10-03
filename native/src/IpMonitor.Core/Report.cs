namespace IpMonitor.Core;

/// <summary>PDF report and Excel workbook of the database (or of some sites).</summary>
public class Report
{
    public class Options
    {
        /// <summary>Sites in the report; null = all sites.</summary>
        public HashSet<string> SiteIds;
        public bool Networks = true, Hosts = true, Devices = true, Links = true, Vlans = true, Configs = true;
        /// <summary>Excel only, and only for Full access: device passwords and wireless keys.</summary>
        public bool Passwords;
        /// <summary>PDF only: a picture of each site's map (JPEG, pixel size).</summary>
        public Dictionary<string, (byte[] jpeg, int w, int h)> Maps = new();
    }

    readonly Store S;
    readonly Options O;
    public Report(Store store, Options o) { S = store; O = o ?? new Options(); }

    List<Site> SitesIn() => S.Db.Sites.Where(s => O.SiteIds == null || O.SiteIds.Contains(s.Id)).OrderBy(s => s.SiteNumber?.PadLeft(10, '0'), StringComparer.OrdinalIgnoreCase).ToList();
    static string When(string at) => DateTime.TryParse(at, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : at ?? "";
    string Status(string s) => Store.StatusLabel[Store.StatusOf(s)];
    string Range(Network n) { var e = IpMath.Parse(n.Ip); return e == null ? "" : e.IsSubnet ? $"{e.FirstU} – {e.LastU}" : "single IP"; }
    string Used(Network n) { var e = IpMath.Parse(n.Ip); return e == null ? "" : e.IsSubnet ? $"{n.HostList.Count} / {IpMath.CountText(e.Usable)}" : "1"; }
    IEnumerable<Device> DevsOf(string siteId) => S.Db.Devices.Where(d => d.SiteId == siteId).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase);
    IEnumerable<Link> LinksOf(string siteId)
    {
        var ids = S.Db.Devices.Where(d => d.SiteId == siteId).Select(d => d.Id).ToHashSet();
        return S.Db.Links.Where(l => ids.Contains(l.A) || ids.Contains(l.B));
    }
    static string MainIp(Device d) => !string.IsNullOrEmpty(d.Ip) ? d.Ip : d.Addrs.Select(a => IpMath.IpOf(a.Address)).FirstOrDefault(x => x != "") ?? "";

    // ================================================================== PDF
    const double M = 36;          // page margin
    Pdf pdf; double y;
    const string Navy = "#0B2240", Ink = "#1B2533", Muted = "#5E6B7B", Line = "#E4E8ED", Head = "#F3F5F8", Acc = "#0B5CD5";

    public byte[] Pdf(string by)
    {
        pdf = new Pdf { Title = "IP Monitor report" };
        var sites = SitesIn();
        // cover
        NewPage();
        pdf.Text(M, y + 22, "Network report", 26, Core.Pdf.Font.Bold, Navy); y += 34;
        pdf.Text(M, y + 14, $"{(O.SiteIds == null ? "All sites" : sites.Count == 1 ? $"Site #{sites[0].SiteNumber} {sites[0].Name}" : $"{sites.Count} sites")} · created {DateTime.Now:yyyy-MM-dd HH:mm}{(string.IsNullOrEmpty(by) ? "" : " by " + by)}", 11, Core.Pdf.Font.Regular, Muted);
        y += 34;
        var ids = sites.Select(s => s.Id).ToHashSet();
        var devs = S.Db.Devices.Where(d => ids.Contains(d.SiteId)).ToList();
        var devIds = devs.Select(d => d.Id).ToHashSet();
        var nets = S.Db.Networks.Where(n => ids.Contains(n.SiteId)).ToList();
        var tiles = new (string, string)[] { (sites.Count.ToString(), "Sites"), (nets.Count.ToString(), "Networks"), (nets.Sum(S.HostCount).ToString(), "Hosts"),
            (devs.Count.ToString(), "Devices"), (S.Db.Links.Count(l => devIds.Contains(l.A) || devIds.Contains(l.B)).ToString(), "Connections"), (S.Db.Vlans.Count(v => ids.Contains(v.SiteId)).ToString(), "VLANs") };
        double tw = (pdf.PageW - 2 * M - 5 * 10) / 6;
        for (int i = 0; i < tiles.Length; i++)
        {
            var x = M + i * (tw + 10);
            pdf.Rect(x, y, tw, 58, "#FFFFFF", Line, 0.8);
            pdf.Text(x + 12, y + 20, tiles[i].Item2, 10, Core.Pdf.Font.Bold, Muted);
            pdf.Text(x + 12, y + 46, tiles[i].Item1, 22, Core.Pdf.Font.Bold, Ink);
        }
        y += 80;
        Heading("Sites");
        Table(new[] { ("#", 40.0), ("Site", 160), ("Location", 170), ("Contact", 130), ("Networks", 60), ("Hosts", 50), ("Devices", 55), ("Status", 105) },
            sites.Select(s => new[] { s.SiteNumber, s.Name, s.Location, s.Contact, S.NetsOf(s.Id).Count().ToString(), S.NetsOf(s.Id).Sum(S.HostCount).ToString(), DevsOf(s.Id).Count().ToString(), Status(s.Status) }));

        foreach (var s in sites)
        {
            NewPage();
            pdf.Rect(M, y, pdf.PageW - 2 * M, 30, Navy);
            pdf.Text(M + 12, y + 20, $"#{s.SiteNumber}", 14, Core.Pdf.Font.Bold, "#4DA3FF");
            pdf.Text(M + 20 + Core.Pdf.Width($"#{s.SiteNumber}", 14, Core.Pdf.Font.Bold), y + 20, s.Name, 14, Core.Pdf.Font.Bold, "#FFFFFF");
            pdf.TextRight(pdf.PageW - M - 12, y + 19, Status(s.Status), 10, Core.Pdf.Font.Bold, "#9DB0C9");
            y += 38;
            var info = string.Join("   ·   ", new[] { s.Location, s.Contact == "" ? "" : "Contact: " + s.Contact }.Where(t => !string.IsNullOrWhiteSpace(t)));
            if (info != "") { pdf.Text(M, y + 10, info, 10, Core.Pdf.Font.Regular, Muted); y += 16; }
            if (!string.IsNullOrWhiteSpace(s.Notes)) { foreach (var l in Core.Pdf.Wrap(s.Notes, pdf.PageW - 2 * M, 9.5)) { pdf.Text(M, y + 10, l, 9.5, Core.Pdf.Font.Regular, Ink); y += 12; } }
            y += 6;

            if (O.Maps.TryGetValue(s.Id, out var map))
            {
                double maxW = pdf.PageW - 2 * M, maxH = pdf.PageH - y - M - 20;
                double k = Math.Min(maxW / map.w, maxH / map.h); k = Math.Min(k, 0.75);   // 2x pictures: 0.5 = actual size
                double w = map.w * k, h = map.h * k;
                pdf.Rect(M, y, w + 2, h + 2, null, Line, 0.8);
                pdf.Image(map.jpeg, map.w, map.h, M + 1, y + 1, w, h);
                y += h + 14;
            }
            var snets = S.NetsOf(s.Id).OrderBy(n => n.Ip).ToList();
            if (O.Networks && snets.Count > 0)
            {
                Heading($"Networks ({snets.Count})");
                Table(new[] { ("Network", 120.0), ("Name", 140), ("VLAN", 40), ("Gateway", 95), ("Usable range", 175), ("Hosts", 65), ("Status", 60), ("Notes", 75) },
                    snets.Select(n => new[] { n.Ip, n.Name, n.Vlan, n.Gateway, Range(n), Used(n), Status(n.Status), n.Notes }), monoCols: new[] { 0, 3, 4 });
            }
            if (O.Hosts && snets.Any(n => n.HostList.Count > 0))
            {
                Heading($"Hosts ({snets.Sum(n => n.HostList.Count)})");
                Table(new[] { ("Network", 110.0), ("#", 30), ("IP address", 100), ("Hostname", 170), ("MAC", 110), ("From device", 120), ("Notes", 130) },
                    snets.SelectMany(n => n.HostList.OrderBy(h => h.Num).Select(h => new[] { n.Ip, h.Num.ToString(), h.Ip, h.Name, h.Mac, h.Dev == null ? "" : S.DevById(h.Dev)?.Name ?? "", h.Notes })), monoCols: new[] { 0, 2, 4 });
            }
            var sdevs = DevsOf(s.Id).ToList();
            if (O.Devices && sdevs.Count > 0)
            {
                Heading($"Devices ({sdevs.Count})");
                Table(new[] { ("Device", 95.0), ("Brand", 52), ("Type", 70), ("Model", 70), ("IP / management", 88), ("IP addresses", 150), ("MAC", 92), ("RouterOS", 50), ("Status", 50), ("Configs", 43) },
                    sdevs.Select(d => new[] { d.Name, d.IsMikroTik ? "MikroTik" : "Other", Store.TypeLabel.GetValueOrDefault(d.Type, d.Type), d.Model, d.Ip,
                        string.Join(", ", d.Addrs.Select(a => $"{a.Iface} {a.Address}")), d.Mac, d.Ros, Status(d.Status), S.ConfigCount(d.Id) == 0 ? "" : S.ConfigCount(d.Id).ToString() }), monoCols: new[] { 4, 5, 6 });
            }
            var slinks = LinksOf(s.Id).ToList();
            if (O.Links && slinks.Count > 0)
            {
                Heading($"Connections ({slinks.Count})");
                string Dn(string id) { var d = S.DevById(id); return d == null ? "?" : d.SiteId == s.Id ? d.Name : $"{d.Name} (#{S.SiteById(d.SiteId)?.SiteNumber})"; }
                Table(new[] { ("Type", 55.0), ("From", 105), ("Interface", 70), ("IP", 80), ("To", 105), ("Interface", 70), ("IP", 80), ("Link subnet", 90), ("SSID", 115) },
                    slinks.Select(l => new[] { l.Type == "wireless" ? "Wireless" : "Wired", Dn(l.A), l.PortA, l.IpA, Dn(l.B), l.PortB, l.IpB, l.Subnet, l.Ssid }), monoCols: new[] { 2, 3, 5, 6, 7 });
            }
            var svl = S.Db.Vlans.Where(v => v.SiteId == s.Id).OrderBy(v => v.Vid).ToList();
            if (O.Vlans && svl.Count > 0)
            {
                Heading($"VLANs ({svl.Count})");
                Table(new[] { ("VLAN ID", 55.0), ("Name", 140), ("Device", 120), ("Runs on", 90), ("Interface", 90), ("Networks", 170), ("Notes", 105) },
                    svl.Select(v => new[] { v.Vid.ToString(), v.Name, S.DevById(v.DeviceId)?.Name ?? "", v.Parent, v.IfName, string.Join(", ", snets.Where(n => n.Vlan == v.Vid.ToString()).Select(n => n.Ip)), v.Notes }), monoCols: new[] { 3, 4, 5 });
            }
            var scfg = S.Db.Configs.Where(c => sdevs.Any(d => d.Id == c.DeviceId)).OrderBy(c => S.DevById(c.DeviceId)?.Name).ThenByDescending(c => c.At, StringComparer.Ordinal).ToList();
            if (O.Configs && scfg.Count > 0)
            {
                Heading($"Config backups ({scfg.Count})");
                Table(new[] { ("Device", 120.0), ("Saved", 100), ("By", 80), ("Lines", 45), ("Size", 55), ("RouterOS", 60), ("Model", 90), ("Note", 220) },
                    scfg.Select(c => new[] { S.DevById(c.DeviceId)?.Name ?? "", When(c.At), c.By, c.Lines.ToString(), Rsc.Size(c.Size), c.Ros, c.Model, c.Note }));
            }
            if (snets.Count == 0 && sdevs.Count == 0) { pdf.Text(M, y + 12, "This site has no networks or devices yet.", 10, Core.Pdf.Font.Regular, Muted); y += 20; }
        }

        // footers: "page n of m"
        for (int i = 0; i < pdf.PageCount; i++)
        {
            pdf.OnPage(i);
            pdf.Line(M, pdf.PageH - 26, pdf.PageW - M, pdf.PageH - 26, Line, 0.6);
            pdf.Text(M, pdf.PageH - 14, "IP Monitor · network report", 8.5, Core.Pdf.Font.Regular, Muted);
            pdf.TextRight(pdf.PageW - M, pdf.PageH - 14, $"Page {i + 1} of {pdf.PageCount}", 8.5, Core.Pdf.Font.Regular, Muted);
        }
        return pdf.Save();
    }

    /// <summary>The history (who changed what, and when) as a PDF, newest first.</summary>
    public byte[] HistoryPdf(IEnumerable<Change> changes, string scope, string by)
    {
        pdf = new Pdf { Title = "IP Monitor history" };
        var list = changes.OrderByDescending(c => c.At, StringComparer.Ordinal).ToList();
        NewPage();
        pdf.Text(M, y + 22, "History", 26, Core.Pdf.Font.Bold, Navy); y += 34;
        var span = list.Count == 0 ? "" : $" · from {When(list[^1].At)} to {When(list[0].At)}";
        pdf.Text(M, y + 14, $"{scope} · {list.Count} change{(list.Count == 1 ? "" : "s")}{span} · created {DateTime.Now:yyyy-MM-dd HH:mm}{(string.IsNullOrEmpty(by) ? "" : " by " + by)}", 10.5, Core.Pdf.Font.Regular, Muted);
        y += 30;
        string Act(string a) => a switch { "add" => "Added", "edit" => "Changed", "delete" => "Deleted", "import" => "Imported", "clear" => "Cleared", _ => a };
        Table(new[] { ("When", 85.0), ("Who", 70), ("Action", 55), ("What", 75), ("Item", 150), ("Site", 95), ("Details", 240) },
            list.Select(c => new[] { When(c.At), c.By, Act(c.Act), c.Kind, c.Label, S.SiteById(c.SiteId)?.ToString() ?? "", string.Join("\n", c.Lines ?? new()) }), maxLines: 14);
        for (int i = 0; i < pdf.PageCount; i++)
        {
            pdf.OnPage(i);
            pdf.Line(M, pdf.PageH - 26, pdf.PageW - M, pdf.PageH - 26, Line, 0.6);
            pdf.Text(M, pdf.PageH - 14, "IP Monitor · history", 8.5, Core.Pdf.Font.Regular, Muted);
            pdf.TextRight(pdf.PageW - M, pdf.PageH - 14, $"Page {i + 1} of {pdf.PageCount}", 8.5, Core.Pdf.Font.Regular, Muted);
        }
        return pdf.Save();
    }

    void NewPage() { pdf.NewPage(); y = M; }
    double Bottom => pdf.PageH - M - 16;

    void Heading(string t)
    {
        if (y + 60 > Bottom) NewPage();
        y += 6;
        pdf.Text(M, y + 13, t, 12.5, Core.Pdf.Font.Bold, Navy);
        y += 20;
    }

    /// <summary>A table that continues on the next page (with its header again). Column widths are scaled to the page.</summary>
    void Table((string head, double w)[] cols, IEnumerable<string[]> rows, int[] monoCols = null, int maxLines = 5)
    {
        double total = cols.Sum(c => c.w), avail = pdf.PageW - 2 * M, k = avail / total;
        var ws = cols.Select(c => c.w * k).ToArray();
        var mono = (monoCols ?? Array.Empty<int>()).ToHashSet();
        const double fs = 8.5, lh = 11, pad = 4;
        void HeaderRow()
        {
            pdf.Rect(M, y, avail, 18, Head);
            double x = M;
            for (int i = 0; i < cols.Length; i++) { pdf.Text(x + pad, y + 12.5, Core.Pdf.Fit(cols[i].head, ws[i] - 2 * pad, 8, Core.Pdf.Font.Bold), 8, Core.Pdf.Font.Bold, Muted); x += ws[i]; }
            pdf.Line(M, y + 18, M + avail, y + 18, Line, 0.8);
            y += 18;
        }
        HeaderRow();
        int n = 0;
        foreach (var r in rows)
        {
            var cells = Enumerable.Range(0, cols.Length).Select(i =>
            {
                var f = mono.Contains(i) ? Core.Pdf.Font.Mono : Core.Pdf.Font.Regular;
                var size = f == Core.Pdf.Font.Mono ? fs - 0.5 : fs;
                return (lines: Core.Pdf.Wrap(i < r.Length ? r[i] ?? "" : "", ws[i] - 2 * pad, size, f, maxLines), f, size);
            }).ToArray();
            double h = Math.Max(1, cells.Max(c => c.lines.Count)) * lh + 6;
            if (y + h > Bottom) { NewPage(); HeaderRow(); }
            if (n++ % 2 == 1) pdf.Rect(M, y, avail, h, "#FAFBFC");
            double x = M;
            for (int i = 0; i < cols.Length; i++)
            {
                for (int j = 0; j < cells[i].lines.Count; j++) pdf.Text(x + pad, y + 11 + j * lh, cells[i].lines[j], cells[i].size, cells[i].f, Ink);
                x += ws[i];
            }
            pdf.Line(M, y + h, M + avail, y + h, Line, 0.5);
            y += h;
        }
        if (n == 0) { pdf.Text(M + pad, y + 12, "None", fs, Core.Pdf.Font.Regular, Muted); y += 18; }
        y += 12;
    }

    // ================================================================== Excel
    public byte[] Excel(string by)
    {
        var x = new Xlsx();
        var sites = SitesIn(); var ids = sites.Select(s => s.Id).ToHashSet();
        string SiteNo(string id) => S.SiteById(id)?.SiteNumber ?? "";
        string SiteName(string id) => S.SiteById(id)?.Name ?? "";
        object Num(string s) => int.TryParse(s, out var i) ? i : s;

        var sum = x.AddSheet("Summary", 26, 60);
        sum.Add(Xlsx.Style.Title, "IP Monitor · network report");
        sum.Add(Xlsx.Style.Muted, $"Created {DateTime.Now:yyyy-MM-dd HH:mm}{(string.IsNullOrEmpty(by) ? "" : " by " + by)} · {(O.SiteIds == null ? "all sites" : $"{sites.Count} site(s)")}");
        sum.Add(Xlsx.Style.Normal);
        var devs = S.Db.Devices.Where(d => ids.Contains(d.SiteId)).ToList(); var devIds = devs.Select(d => d.Id).ToHashSet();
        var nets = S.Db.Networks.Where(n => ids.Contains(n.SiteId)).ToList();
        foreach (var (k, v) in new (string, int)[] { ("Sites", sites.Count), ("Networks", nets.Count), ("Hosts", nets.Sum(S.HostCount)), ("Devices", devs.Count),
            ("Connections", S.Db.Links.Count(l => devIds.Contains(l.A) || devIds.Contains(l.B))), ("VLANs", S.Db.Vlans.Count(v => ids.Contains(v.SiteId))), ("Config backups", S.Db.Configs.Count(c => devIds.Contains(c.DeviceId))) })
            sum.Add(Xlsx.Style.Normal, k, v);

        var sh = x.AddSheet("Sites", 8, 26, 30, 22, 10, 8, 9, 13, 11, 40);
        sh.Header("Site #", "Site", "Location", "Contact", "Networks", "Hosts", "Devices", "Connections", "Status", "Notes");
        foreach (var s in sites) sh.Add(Xlsx.Style.Normal, Num(s.SiteNumber), s.Name, s.Location, s.Contact, S.NetsOf(s.Id).Count(), S.NetsOf(s.Id).Sum(S.HostCount), DevsOf(s.Id).Count(), LinksOf(s.Id).Count(), Status(s.Status), s.Notes);

        if (O.Networks)
        {
            var ns = x.AddSheet("Networks", 8, 22, 20, 24, 7, 16, 32, 11, 11, 36); ns.MonoCols = new() { 2, 5, 6 };
            ns.Header("Site #", "Site", "Network", "Name", "VLAN", "Gateway", "Usable range", "Hosts", "Status", "Notes");
            foreach (var s in sites)
            {
                var list = S.NetsOf(s.Id).OrderBy(n => n.Ip).ToList(); if (list.Count == 0) continue;
                ns.Add(Xlsx.Style.Group, Num(s.SiteNumber), s.Name, $"{list.Count} network{(list.Count == 1 ? "" : "s")}");
                foreach (var n in list) ns.Add(Xlsx.Style.Normal, Num(s.SiteNumber), s.Name, n.Ip, n.Name, Num(n.Vlan), n.Gateway, Range(n), Used(n), Status(n.Status), n.Notes);
            }
        }
        if (O.Hosts)
        {
            var hs = x.AddSheet("Hosts", 8, 22, 20, 7, 17, 28, 19, 24, 34); hs.MonoCols = new() { 2, 4, 6 };
            hs.Header("Site #", "Site", "Network", "#", "IP address", "Hostname", "MAC", "From device", "Notes");
            foreach (var s in sites) foreach (var n in S.NetsOf(s.Id).OrderBy(n => n.Ip)) foreach (var h in n.HostList.OrderBy(h => h.Num))
                hs.Add(Xlsx.Style.Normal, Num(s.SiteNumber), s.Name, n.Ip, h.Num, h.Ip, h.Name, h.Mac, h.Dev == null ? "" : S.DevById(h.Dev)?.Name ?? "", h.Notes);
        }
        if (O.Devices)
        {
            var head = new List<object> { "Site #", "Site", "Device", "Brand", "Type", "Model", "Role", "Status", "IP / management IP", "IP addresses", "Bridges", "MAC", "RouterOS", "Username" };
            var widths = new List<double> { 8, 20, 22, 11, 15, 18, 13, 10, 18, 40, 30, 19, 10, 14 };
            if (O.Passwords) { head.Add("Password"); widths.Add(16); }
            head.AddRange(new object[] { "WinBox port", "Wireless protocol", "Security" }); widths.AddRange(new double[] { 11, 15, 16 });
            if (O.Passwords) { head.Add("Pre-shared key"); widths.Add(18); }
            head.AddRange(new object[] { "Config backups", "Notes" }); widths.AddRange(new double[] { 13, 36 });
            var ds = x.AddSheet("Devices", widths.ToArray()); ds.MonoCols = new() { 8, 9, 11 };
            ds.Header(head.ToArray());
            foreach (var s in sites) foreach (var d in DevsOf(s.Id))
            {
                var r = new List<object> { Num(s.SiteNumber), s.Name, d.Name, d.IsMikroTik ? "MikroTik" : "Other brand", Store.TypeLabel.GetValueOrDefault(d.Type, d.Type), d.Model,
                    Store.RoleLabel.GetValueOrDefault(d.Role ?? "", "") is var rl && rl == "—" ? "" : rl, Status(d.Status), d.Ip,
                    string.Join("\n", d.Addrs.Select(a => $"{a.Iface}: {a.Address}")), string.Join("\n", d.Bridges.Select(b => $"{b.Name}: {string.Join(", ", b.Ports)}")), d.Mac, d.Ros, d.User };
                if (O.Passwords) r.Add(d.Pass);
                r.AddRange(new object[] { Num(d.Winbox), Store.WProtoLabel.GetValueOrDefault(d.WProto ?? "", "") is var wp && wp == "—" ? "" : wp, Store.WSecLabel.GetValueOrDefault(d.WSec ?? "", "") is var ws && ws == "—" ? "" : ws });
                if (O.Passwords) r.Add(d.Psk);
                r.Add(S.ConfigCount(d.Id) == 0 ? "" : S.ConfigCount(d.Id)); r.Add(d.Notes);
                ds.Add(Xlsx.Style.Normal, r.ToArray());
            }
        }
        if (O.Links)
        {
            var ls = x.AddSheet("Connections", 10, 18, 20, 13, 15, 18, 20, 13, 15, 18, 18, 30); ls.MonoCols = new() { 3, 4, 7, 8, 9 };
            ls.Header("Type", "From site", "From device", "From interface", "From IP", "To site", "To device", "To interface", "To IP", "Link subnet", "SSID", "Notes");
            foreach (var l in S.Db.Links.Where(l => devIds.Contains(l.A) || devIds.Contains(l.B)))
            {
                var A = S.DevById(l.A); var B = S.DevById(l.B);
                ls.Add(Xlsx.Style.Normal, l.Type == "wireless" ? "Wireless" : "Wired", S.SiteById(A?.SiteId)?.ToString(), A?.Name, l.PortA, l.IpA, S.SiteById(B?.SiteId)?.ToString(), B?.Name, l.PortB, l.IpB, l.Subnet, l.Ssid, l.Notes);
            }
        }
        if (O.Vlans)
        {
            var vs = x.AddSheet("VLANs", 8, 22, 10, 22, 20, 14, 14, 34, 30); vs.MonoCols = new() { 5, 6, 7 };
            vs.Header("Site #", "Site", "VLAN ID", "Name", "Device", "Runs on", "Interface", "Networks", "Notes");
            foreach (var s in sites) foreach (var v in S.Db.Vlans.Where(v => v.SiteId == s.Id).OrderBy(v => v.Vid))
                vs.Add(Xlsx.Style.Normal, Num(s.SiteNumber), s.Name, v.Vid, v.Name, S.DevById(v.DeviceId)?.Name ?? "", v.Parent, v.IfName, string.Join(", ", S.NetsOf(s.Id).Where(n => n.Vlan == v.Vid.ToString()).Select(n => n.Ip)), v.Notes);
        }
        if (O.Configs)
        {
            var cs = x.AddSheet("Config backups", 8, 20, 22, 17, 14, 8, 10, 11, 18, 18, 40);
            cs.Header("Site #", "Site", "Device", "Saved", "By", "Lines", "Size", "RouterOS", "Model", "Identity", "Note");
            foreach (var c in S.Db.Configs.Where(c => devIds.Contains(c.DeviceId)).OrderBy(c => SiteNo(S.DevById(c.DeviceId)?.SiteId)).ThenBy(c => S.DevById(c.DeviceId)?.Name).ThenByDescending(c => c.At, StringComparer.Ordinal))
            {
                var d = S.DevById(c.DeviceId);
                cs.Add(Xlsx.Style.Normal, Num(SiteNo(d?.SiteId)), SiteName(d?.SiteId), d?.Name, When(c.At), c.By, c.Lines, Rsc.Size(c.Size), c.Ros, c.Model, c.Identity, c.Note);
            }
        }
        return x.Save();
    }
}
