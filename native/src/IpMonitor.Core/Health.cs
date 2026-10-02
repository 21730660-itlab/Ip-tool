using System.Numerics;
using System.Text.Json;

namespace IpMonitor.Core;

/// <summary>
/// The dashboard's numbers and health checks — the same rules as the web version's dashboard
/// (duplicate IPs, full subnets, wireless signal, wrong link types, missing backups…), plus the live ping state.
/// </summary>
public class Health
{
    public const int StaleDays = 30;

    /// <summary>What a check item opens when clicked.</summary>
    public enum Target { None, Network, Link, Device, Config, Site, IpList }

    public record Item(string T, string S, string SiteId, Target Open, string Ref);
    public record Check(string Sev, string Title, List<Item> Items, string Hint, Target Go);

    /// <summary>An address in use (host, gateway, device, interface, link end).</summary>
    public class IpRec
    {
        public string Ip, Kind, Owner, Sub, SiteId, DevId; public int? Prefix; public bool Dup;
        public Target Open; public string Ref;
    }
    /// <summary>A registered network or link subnet.</summary>
    public record Net(IpEntry E, string Kind, string Label, string Vlan, List<string> SiteIds, Target Open, string Id);
    public record Use(Net N, int Used, BigInteger Cap, double P);

    readonly Store S;
    public readonly List<IpRec> Recs;
    public readonly List<Net> Nets;
    public readonly List<Use> Uses;
    public readonly List<Check> Checks;
    public readonly int Score;
    /// <summary>Devices that do not answer ping (live monitoring), by id.</summary>
    readonly ISet<string> down;

    public Health(Store store, ISet<string> devicesDown = null)
    {
        S = store; down = devicesDown ?? new HashSet<string>();
        Recs = CollectIps(); Nets = AllNets(); Uses = NetUse();
        Checks = RunChecks(); Score = ScoreOf(Checks);
    }

    Device Dev(string id) => S.DevById(id);
    static string Sev(Check c) => c.Sev;
    static int Rank(string s) => s switch { "crit" => 0, "warn" => 1, "info" => 2, _ => 3 };
    string SiteTag(string id) => S.SiteById(id) is Site s ? $"#{s.SiteNumber}" : "";
    string SiteName(string id) => S.SiteById(id) is Site s ? $"#{s.SiteNumber} {s.Name}" : "";

    public static string X(Link l, string k) => l.Extra != null && l.Extra.TryGetValue(k, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";
    public static double? Signal(Link l) => double.TryParse(X(l, "signal"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var s) ? s : null;
    public static string SigClass(double? s) => s == null ? "" : s >= -65 ? "good" : s >= -75 ? "fair" : "poor";
    string Lbl(Link l) => $"{Dev(l.A)?.Name ?? "?"} ↔ {Dev(l.B)?.Name ?? "?"}";

    // ------------------------------------------------------------------ addresses in use
    List<IpRec> CollectIps()
    {
        var recs = new List<IpRec>();
        foreach (var n in S.Db.Networks)
        {
            var e = IpMath.Parse(n.Ip);
            if (e != null && !e.IsSubnet) recs.Add(new IpRec { Ip = e.Text, Kind = "Host", Owner = n.Name == "" ? "Single IP" : n.Name, Sub = "single-IP network", SiteId = n.SiteId, Open = Target.Network, Ref = n.Id });
            foreach (var h in n.HostList.Where(h => h.Dev == null))
                recs.Add(new IpRec { Ip = h.Ip, Kind = "Host", Owner = string.IsNullOrEmpty(h.Name) ? $"Host #{h.Num}" : h.Name, Sub = $"host #{h.Num}{(string.IsNullOrEmpty(h.Mac) ? "" : " · " + h.Mac)}", SiteId = n.SiteId, Open = Target.Network, Ref = n.Id });
            if (!string.IsNullOrEmpty(n.Gateway)) recs.Add(new IpRec { Ip = n.Gateway, Kind = "Gateway", Owner = (n.Name == "" ? "" : n.Name + " ") + "gateway", Sub = n.Ip, SiteId = n.SiteId, Open = Target.Network, Ref = n.Id });
        }
        foreach (var d in S.Db.Devices)
        {
            if (!string.IsNullOrEmpty(d.Ip))
                recs.Add(d.IsMikroTik
                    ? new IpRec { Ip = d.Ip, Kind = "Mgmt", Owner = d.Name, Sub = "management IP", SiteId = d.SiteId, DevId = d.Id, Open = Target.Device, Ref = d.Id }
                    : new IpRec { Ip = d.Ip, Kind = "Device", Owner = d.Name, Sub = $"{Store.TypeLabel.GetValueOrDefault(d.Type, "device")} IP{(d.Uplink == null ? "" : $" · from {d.Uplink.Bridge} on {Dev(d.Uplink.DeviceId)?.Name ?? "?"}")}", SiteId = d.SiteId, DevId = d.Id, Open = Target.Device, Ref = d.Id });
            foreach (var a in d.Addrs)
            {
                var p = IpMath.ParseRaw(a.Address); if (p == null) continue;
                var br = d.Bridges.FirstOrDefault(b => b.Name == a.Iface);
                recs.Add(new IpRec { Ip = IpMath.IpOf(a.Address), Prefix = p.Prefix, Kind = br != null ? "Bridge" : "Interface", Owner = d.Name,
                    Sub = $"{a.Iface}{(p.Prefix != null ? " · /" + p.Prefix : "")}{(br != null ? " · " + string.Join(", ", br.Ports) : "")}", SiteId = d.SiteId, DevId = d.Id, Open = Target.Device, Ref = d.Id });
            }
        }
        foreach (var l in S.Db.Links)
            foreach (var (dv, ip, port, far) in new[] { (l.A, l.IpA, l.PortA, l.B), (l.B, l.IpB, l.PortB, l.A) })
            {
                if (string.IsNullOrEmpty(ip)) continue;
                var D = Dev(dv); var F = Dev(far);
                if (D != null && (D.Ip == ip || D.Addrs.Any(a => IpMath.IpOf(a.Address) == ip))) continue;
                recs.Add(new IpRec { Ip = ip, Kind = "PtP", Owner = D?.Name ?? "?", Sub = $"{(string.IsNullOrEmpty(port) ? "?" : port)} → {F?.Name ?? "?"}{(F != null && D != null && F.SiteId != D.SiteId ? $" ({SiteName(F.SiteId)})" : "")}", SiteId = D?.SiteId ?? "", DevId = dv, Open = Target.Link, Ref = l.Id });
            }
        // the same IP owned by different devices, used in different sites, or listed twice as a host
        foreach (var g in recs.GroupBy(r => r.Ip))
        {
            var devs = g.Where(r => r.DevId != null).Select(r => r.DevId).Distinct().Count();
            var sites = g.Select(r => r.SiteId).Distinct().Count();
            if (devs > 1 || sites > 1 || g.Count(r => r.Kind == "Host") > 1) foreach (var r in g) r.Dup = true;
        }
        return recs;
    }

    List<Net> AllNets()
    {
        var res = new List<Net>();
        foreach (var n in S.Db.Networks) if (IpMath.Parse(n.Ip) is IpEntry e) res.Add(new Net(e, "net", n.Name, n.Vlan, new() { n.SiteId }, Target.Network, n.Id));
        foreach (var l in S.Db.Links)
        {
            if (string.IsNullOrEmpty(l.Subnet) || IpMath.Parse(l.Subnet) is not IpEntry e) continue;
            var A = Dev(l.A); var B = Dev(l.B);
            res.Add(new Net(e, "ptp", $"{A?.Name ?? "?"} ⟷ {B?.Name ?? "?"}", "", new[] { A?.SiteId, B?.SiteId }.Where(x => x != null).ToList(), Target.Link, l.Id));
        }
        return res.OrderBy(n => n.E.V).ThenBy(n => n.E.Start).ToList();
    }

    List<Use> NetUse()
    {
        var parsed = Recs.Select(r => (r, x: IpMath.Parse(r.Ip))).Where(o => o.x != null).ToList();
        var res = new List<Use>();
        foreach (var n in Nets)
        {
            if (!n.E.IsSubnet) continue;
            var used = parsed.Where(o => IpMath.Overlaps(n.E, o.x)).Select(o => o.r.Ip).Distinct().Count();
            var cap = n.E.Usable;
            var p = cap > 0 && cap < BigInteger.Pow(10, 15) ? used / (double)cap * 100 : 0;
            res.Add(new Use(n, used, cap, p));
        }
        return res;
    }

    /// <summary>Two wireless links at the same site, same band, overlapping channels, not sharing one radio.</summary>
    List<(Link A, Link B, string SiteId)> CoChannel()
    {
        var wl = S.Db.Links.Where(l => l.Type == "wireless" && X(l, "freq") != "" && X(l, "band") != "").ToList();
        var res = new List<(Link, Link, string)>();
        (double, double) Span(Link l)
        {
            var w = double.TryParse(X(l, "width"), out var ww) && ww > 0 ? ww : X(l, "band") == "60" ? 2160 : 20;
            double.TryParse(X(l, "freq"), out var f); return (f - w / 2, f + w / 2);
        }
        for (int i = 0; i < wl.Count; i++) for (int j = i + 1; j < wl.Count; j++)
        {
            Link A = wl[i], B = wl[j];
            if (X(A, "band") != X(B, "band")) continue;
            var endsA = new[] { (A.A, A.PortA), (A.B, A.PortB) }; var endsB = new[] { (B.A, B.PortA), (B.B, B.PortB) };
            if (endsA.Any(a => endsB.Any(b => a.Item1 == b.Item1 && !string.IsNullOrEmpty(a.Item2) && a.Item2 == b.Item2))) continue;
            var sitesA = endsA.Select(e => Dev(e.Item1)?.SiteId).Where(x => x != null).ToHashSet();
            var common = endsB.Select(e => Dev(e.Item1)?.SiteId).FirstOrDefault(x => x != null && sitesA.Contains(x));
            if (common == null) continue;
            var (a1, a2) = Span(A); var (b1, b2) = Span(B);
            if (a1 < b2 && b1 < a2) res.Add((A, B, common));
        }
        return res;
    }

    public static bool IsRouter(Device d) => d.IsMikroTik && (d.Type != "wireless" || d.Role == "ap" || d.Func == "core");

    // ------------------------------------------------------------------ checks
    List<Check> RunChecks()
    {
        var checks = new List<Check>();
        void Add(string sev, string title, IEnumerable<Item> items, string hint, Target go = Target.None)
        { var l = items.ToList(); checks.Add(new Check(l.Count > 0 ? sev : "ok", title, l, hint, go)); }
        var devs = S.Db.Devices; var links = S.Db.Links;

        // live monitoring
        Add("crit", "Devices not answering ping", devs.Where(d => down.Contains(d.Id)).Select(d => new Item(d.Name, $"{(string.IsNullOrEmpty(d.Ip) ? d.Addrs.Select(a => IpMath.IpOf(a.Address)).FirstOrDefault() : d.Ip)} · {d.Model} · {SiteTag(d.SiteId)}", d.SiteId, Target.Device, d.Id)),
            "Live monitoring from this computer: no reply. Check power, cable or radio link.");

        // addressing
        Add("crit", "Duplicate IP addresses", Recs.Where(r => r.Dup).Select(r => r.Ip).Distinct().Select(ip =>
        {
            var r = Recs.First(x => x.Ip == ip && x.Dup);
            return new Item(ip, string.Join(" · ", Recs.Where(x => x.Ip == ip).Select(x => x.Owner)), r.SiteId, r.Open, r.Ref);
        }), "The same address is used by two different things.", Target.IpList);
        var outside = Recs.Where(r => IpMath.Parse(r.Ip) is IpEntry x && !Nets.Any(n => IpMath.Overlaps(n.E, x))).Select(r => r.Ip).Distinct();
        Add("warn", "Addresses outside any registered network", outside.Select(ip => { var r = Recs.First(x => x.Ip == ip); return new Item(ip, $"{r.Owner} · {r.Sub}", r.SiteId, r.Open, r.Ref); }),
            "Register the subnet on the site so these are tracked.", Target.IpList);
        var pool = Uses.Where(o => o.N.Kind == "net" && o.Cap > 2).ToList();
        Item UseItem(Use o) => new(o.N.E.Text, $"{o.Used}/{IpMath.CountText(o.Cap)} · {o.P:0}%", o.N.SiteIds.FirstOrDefault(), o.N.Open, o.N.Id);
        Add("crit", "Subnets 90% full or more", pool.Where(o => o.P >= 90).OrderByDescending(o => o.P).Select(UseItem), "Plan a bigger subnet before it runs out.");
        Add("warn", "Subnets 75–90% full", pool.Where(o => o.P >= 75 && o.P < 90).OrderByDescending(o => o.P).Select(UseItem), "");
        var undef = new List<Item>();
        foreach (var s in S.Db.Sites)
            foreach (var vid in S.NetsOf(s.Id).Where(n => int.TryParse(n.Vlan, out _)).Select(n => int.Parse(n.Vlan)).Distinct())
                if (!S.Db.Vlans.Any(v => v.SiteId == s.Id && v.Vid == vid)) undef.Add(new Item($"VLAN {vid}", $"#{s.SiteNumber} {s.Name}", s.Id, Target.Site, s.Id));
        Add("warn", "VLAN IDs used but not defined", undef, "Add them on the VLANs page so the VLAN list is complete.");

        // RF
        var wl = links.Where(l => l.Type == "wireless").ToList();
        Add("crit", "Wireless links with poor signal (below −75 dBm)", wl.Where(l => SigClass(Signal(l)) == "poor").OrderBy(l => Signal(l)).Select(l => new Item($"{Signal(l)} dBm", Lbl(l), Dev(l.A)?.SiteId, Target.Link, l.Id)), "Re-align, raise the antenna or shorten the path.");
        Add("warn", "Wireless links with fair signal (−65 to −75 dBm)", wl.Where(l => SigClass(Signal(l)) == "fair").Select(l => new Item($"{Signal(l)} dBm", Lbl(l), Dev(l.A)?.SiteId, Target.Link, l.Id)), "");
        Add("warn", "Possible co-channel interference", CoChannel().Select(o => new Item($"{X(o.A, "freq")} / {X(o.B, "freq")} MHz", $"{Lbl(o.A)}  ×  {Lbl(o.B)} · {SiteTag(o.SiteId)}", o.SiteId, Target.Link, o.A.Id)), "Two links at the same site use overlapping channels.");
        Add("info", "Wireless links missing signal or frequency", wl.Where(l => X(l, "freq") == "" || X(l, "signal") == "").Select(l => new Item(Lbl(l), string.Join(" · ", new[] { X(l, "freq") == "" ? "no frequency" : "", X(l, "signal") == "" ? "no signal" : "" }.Where(t => t != "")), null, Target.Link, l.Id)), "");
        Add("crit", "Wireless connections to a device without Wi-Fi", wl.Where(l => new[] { Dev(l.A), Dev(l.B) }.Any(d => d != null && !d.HasWifi)).Select(l =>
            new Item(Lbl(l), string.Join(" · ", new[] { Dev(l.A), Dev(l.B) }.Where(d => d != null && !d.HasWifi).Select(d => $"{d.Name} is {Store.TypeLabel.GetValueOrDefault(d.Type, d.Type)}")), Dev(l.A)?.SiteId, Target.Link, l.Id)),
            "Both ends of a wireless link must be wireless devices. Change the link to wired or the device type.");
        Add("crit", "Wired connections between different sites", links.Where(l => l.Type == "wired" && Dev(l.A) is Device a && Dev(l.B) is Device b && a.SiteId != b.SiteId).Select(l =>
            new Item(Lbl(l), $"{SiteTag(Dev(l.A).SiteId)} ⟷ {SiteTag(Dev(l.B).SiteId)}", Dev(l.A).SiteId, Target.Link, l.Id)), "Wired links must stay inside one site. Change them to wireless or move a device.");
        Add("info", "Connections without a link subnet", links.Where(l => string.IsNullOrEmpty(l.Subnet) && string.IsNullOrEmpty(l.IpA) && string.IsNullOrEmpty(l.IpB)).Select(l => new Item(Lbl(l), l.Type, null, Target.Link, l.Id)), "Fine for bridged links; routed links need a /30 or /31.");

        // devices
        Item DevItem(Device d, string s) => new(d.Name, s, d.SiteId, Target.Device, d.Id);
        Add("crit", "Devices marked Offline", devs.Where(d => Store.StatusOf(d.Status) == "offline").Select(d => DevItem(d, $"{d.Model} · {SiteTag(d.SiteId)}")), "");
        Add("warn", "MikroTik devices without a management IP", devs.Where(d => d.IsMikroTik && Store.StatusOf(d.Status) != "retired" && string.IsNullOrEmpty(d.Ip)).Select(d => DevItem(d, $"{d.Model} · {SiteTag(d.SiteId)}")), "");
        Add("warn", "Routers without a config backup", devs.Where(d => IsRouter(d) && Store.StatusOf(d.Status) != "retired" && !S.Db.Configs.Any(c => c.DeviceId == d.Id))
            .Select(d => new Item(d.Name, $"{d.Model} · {SiteTag(d.SiteId)}", d.SiteId, Target.Config, d.Id)), "Open the device's Config backups and load its .rsc export.");
        var stale = devs.Select(d => (d, c: S.ConfigsOf(d.Id).FirstOrDefault())).Where(o => o.c != null && DateTime.TryParse(o.c.At, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) && (DateTime.UtcNow - t.ToUniversalTime()).TotalDays > StaleDays);
        Add("info", $"Config backups older than {StaleDays} days", stale.Select(o => new Item(o.d.Name, $"last {When(o.c.At)}", o.d.SiteId, Target.Config, o.d.Id)), "");
        Add("warn", "Devices still on RouterOS 6", devs.Where(d => (d.Ros ?? "").StartsWith("6.")).Select(d => DevItem(d, $"RouterOS {d.Ros} · {SiteTag(d.SiteId)}")), "RouterOS 6 no longer gets new features. Plan the upgrade to v7.");
        Add("info", "Devices without RouterOS version", devs.Where(d => d.IsMikroTik && string.IsNullOrEmpty(d.Ros)).Select(d => DevItem(d, $"{d.Model} · {SiteTag(d.SiteId)}")), "");
        Add("info", "Sites with no networks", S.Db.Sites.Where(s => !S.NetsOf(s.Id).Any()).Select(s => new Item($"#{s.SiteNumber} {s.Name}", "", s.Id, Target.Site, s.Id)), "");
        return checks.OrderBy(c => Rank(c.Sev)).ToList();
    }

    public static string When(string at) => DateTime.TryParse(at, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : at ?? "";

    /// <summary>100 minus points per failing check, like the web version.</summary>
    public static int ScoreOf(List<Check> checks)
    {
        double score = 100;
        foreach (var c in checks)
        {
            int n = c.Items.Count; if (n == 0) continue;
            score -= c.Sev == "crit" ? Math.Min(30, 10 + n * 4) : c.Sev == "warn" ? Math.Min(12, 3 + n) : Math.Min(3, n * 0.5);
        }
        return Math.Max(0, (int)Math.Round(score));
    }

    public int Count(string sev) => Checks.Where(c => c.Sev == sev).Sum(c => c.Items.Count);

    // ------------------------------------------------------------------ dashboard figures
    public (BigInteger cap, int used, double pct) Ipv4Use()
    {
        BigInteger cap = 0; int used = 0;
        foreach (var o in Uses.Where(o => o.N.E.V == 4 && o.N.Kind == "net")) { cap += o.Cap; used += o.Used; }
        return (cap, used, cap > 0 ? used / (double)cap * 100 : 0);
    }
    public double BackupCoverage(out int routers)
    {
        var r = S.Db.Devices.Where(IsRouter).ToList(); routers = r.Count;
        return r.Count == 0 ? 0 : r.Count(d => S.Db.Configs.Any(c => c.DeviceId == d.Id)) * 100.0 / r.Count;
    }
    public List<Use> Busiest(int n = 8) => Uses.Where(o => o.N.Kind == "net" && o.Cap > 2).OrderByDescending(o => o.P).Take(n).ToList();
    public List<Link> Weakest(int n = 8) => S.Db.Links.Where(l => l.Type == "wireless" && Signal(l) != null).OrderBy(l => Signal(l)).Take(n).ToList();
    /// <summary>Issues (critical + warning items) that belong to a site.</summary>
    public int SiteIssues(string siteId) => Checks.Where(c => c.Sev is "crit" or "warn").Sum(c => c.Items.Count(i => i.SiteId == siteId));
    public (BigInteger cap, int used) SiteIpv4(string siteId)
    {
        BigInteger c = 0; int u = 0;
        foreach (var o in Uses.Where(o => o.N.Kind == "net" && o.N.SiteIds.FirstOrDefault() == siteId && o.N.E.V == 4)) { c += o.Cap; u += o.Used; }
        return (c, u);
    }
    public List<(string space, int subnets, int used, BigInteger usable)> AddressSpace() => Uses.Where(o => o.N.Kind == "net")
        .GroupBy(o => $"IPv{o.N.E.V} · {IpMath.Scope(o.N.E)}")
        .Select(g => (g.Key, g.Count(), g.Sum(o => o.Used), g.Aggregate(BigInteger.Zero, (a, o) => a + o.Cap))).ToList();

    // ------------------------------------------------------------------ quick find
    public record Hit(string K, string T, string S, Target Open, string Ref);
    public List<Hit> Find(string q)
    {
        q = (q ?? "").Trim(); var res = new List<Hit>();
        if (q == "") return res;
        bool H(string v) => (v ?? "").Contains(q, StringComparison.OrdinalIgnoreCase);
        var qE = IpMath.Parse(q); var mac = IpMath.NormMac(q); if (mac == "") mac = null;
        foreach (var s in S.Db.Sites) if (H(s.Name) || H(s.SiteNumber) || H(s.Location) || H(s.Contact)) res.Add(new Hit("SITE", $"#{s.SiteNumber} {s.Name}", s.Location, Target.Site, s.Id));
        foreach (var n in Nets) if (H(n.E.Text) || H(n.Label) || (qE != null && IpMath.Overlaps(n.E, qE))) res.Add(new Hit(n.Kind == "ptp" ? "PtP" : "NET", n.E.Text, n.Label, n.Open, n.Id));
        foreach (var r in Recs)
        {
            var x = IpMath.Parse(r.Ip);
            if (H(r.Ip) || H(r.Owner) || (mac != null && H(r.Sub)) || (qE != null && !qE.IsSubnet && x != null && IpMath.Overlaps(x, qE))) res.Add(new Hit(r.Kind.ToUpperInvariant(), r.Ip, $"{r.Owner} · {r.Sub}", r.Open, r.Ref));
        }
        foreach (var d in S.Db.Devices) if (H(d.Name) || H(d.Model) || H(d.Mac) || H(d.Ros)) res.Add(new Hit("DEV", d.Name, $"{d.Model}{(string.IsNullOrEmpty(d.Mac) ? "" : " · " + d.Mac)}", Target.Device, d.Id));
        foreach (var v in S.Db.Vlans) if (H("vlan " + v.Vid) || H(v.Name) || H(v.IfName) || v.Vid.ToString() == q) res.Add(new Hit("VLAN", $"VLAN {v.Vid} {v.Name}", SiteName(v.SiteId), Target.Site, v.SiteId));
        foreach (var n in S.Db.Networks) foreach (var h in n.HostList) if (mac != null && IpMath.NormMac(h.Mac) == mac) res.Add(new Hit("MAC", h.Mac, $"{h.Ip} {h.Name}", Target.Network, n.Id));
        return res.GroupBy(r => r.K + r.T + r.S).Select(g => g.First()).ToList();
    }
}
