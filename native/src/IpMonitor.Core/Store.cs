using System.Numerics;
using System.Text;
using System.Text.Json;

namespace IpMonitor.Core;

/// <summary>A rule was broken: the message says what is wrong and how to fix it.</summary>
public class RuleException : Exception { public RuleException(string m) : base(m) { } }

/// <summary>
/// The database in memory plus every rule of the web version: validation, cascades, the change history,
/// device addresses listed as hosts, and automatic saving to the database file after each change.
/// </summary>
public class Store
{
    public DbFile Db { get; private set; } = new();
    public string FilePath { get; private set; }
    public User Me { get; set; }
    public DateTime LastWrite { get; private set; }
    public event Action Changed;

    public static readonly string[] NetTypes = { "router", "wireless", "both" };
    public static readonly string[] EndTypes = { "pc", "phone", "server", "dbserver", "nvr", "camera" };
    public static readonly Dictionary<string, string> TypeLabel = new()
    {
        ["router"] = "Router", ["wireless"] = "Wireless", ["both"] = "Router + Wi-Fi", ["pc"] = "PC", ["phone"] = "IP phone",
        ["server"] = "Server", ["dbserver"] = "Database server", ["nvr"] = "NVR", ["camera"] = "IP camera"
    };
    public static readonly Dictionary<string, string> RoleLabel = new() { [""] = "—", ["ap"] = "Access point", ["station"] = "Station", ["ptp"] = "PtP bridge" };
    public static readonly Dictionary<string, string> WProtoLabel = new() { [""] = "—", ["802.11"] = "802.11", ["nstreme"] = "Nstreme", ["nv2"] = "NV2" };
    public static readonly Dictionary<string, string> WSecLabel = new() { [""] = "—", ["none"] = "None (open)", ["wpa2-psk"] = "WPA2-PSK", ["wpa-wpa2-psk"] = "WPA / WPA2-PSK", ["wpa3-psk"] = "WPA3-PSK" };
    public static readonly Dictionary<string, string> StatusLabel = new() { ["active"] = "Active", ["planned"] = "Planned", ["reserved"] = "Reserved", ["offline"] = "Offline", ["retired"] = "Retired" };
    public const int LogMax = 1500;

    // ------------------------------------------------------------------ file
    /// <summary>Why the last change could not be written to the file; null when everything is saved.</summary>
    public string SaveError { get; private set; }
    /// <summary>True when the last save was stopped because another program changed the file first.</summary>
    public bool Conflict { get; private set; }

    public void Open(string path)
    {
        var db = DbIo.Load(path);
        Db = db; FilePath = path; LastWrite = File.GetLastWriteTimeUtc(path); SaveError = null; Conflict = false;
        if (Me != null) Me = Db.Users.FirstOrDefault(u => u.Id == Me.Id);
        Changed?.Invoke();
    }
    public void CreateNew(string path)
    {
        Db = new DbFile(); FilePath = path; Me = null; SaveError = null; Conflict = false;
        DbIo.Save(Db, path, null); LastWrite = File.GetLastWriteTimeUtc(path);
        Changed?.Invoke();
    }
    /// <summary>The file was changed by someone else since this app last read or wrote it.</summary>
    public bool ChangedOutside => FilePath != null && File.Exists(FilePath) && File.GetLastWriteTimeUtc(FilePath) > LastWrite.AddSeconds(1);
    public void Reload() { if (FilePath != null) Open(FilePath); }

    /// <summary>Writes the database file now. With overwrite, also when another program changed the file meanwhile.</summary>
    public bool SaveNow(bool overwrite = false) { Persist(overwrite); return SaveError == null; }

    /// <summary>Every change is written to the file at once. Errors are kept in SaveError rather than thrown, so the change stays on screen.</summary>
    void Persist(bool overwrite = false)
    {
        if (FilePath == null) return;
        try
        {
            if (!overwrite && ChangedOutside)
            {
                Conflict = true;
                SaveError = "The database file was changed by another program (or the web version) after this app read it. Your change was not written to the file yet.";
            }
            else
            {
                DbIo.Save(Db, FilePath, Me?.Username);
                LastWrite = File.GetLastWriteTimeUtc(FilePath); SaveError = null; Conflict = false;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Conflict = false; SaveError = "Couldn't write the database file: " + e.Message;
        }
        Changed?.Invoke();
    }

    // ------------------------------------------------------------------ permissions
    public string Perm => Me == null ? null : Auth.Perms.Contains(Me.Perm) ? Me.Perm : "read";
    public bool CanWrite => Perm == "write" || Perm == "full";
    public bool FullAccess => Perm == "full";
    void NeedWrite() { if (!CanWrite) throw new RuleException("Your account can only view. Ask an administrator for Write access."); }
    void NeedFull() { if (!FullAccess) throw new RuleException("Only accounts with Full access can do this."); }

    // ------------------------------------------------------------------ lookups
    public Site SiteById(string id) => Db.Sites.FirstOrDefault(s => s.Id == id);
    public Device DevById(string id) => Db.Devices.FirstOrDefault(d => d.Id == id);
    public IEnumerable<Network> NetsOf(string siteId) => Db.Networks.Where(n => n.SiteId == siteId);
    public string SiteLabel(string siteId) { var s = SiteById(siteId); return s == null ? "no site" : $"{s.Name} (Site #{s.SiteNumber})"; }
    public static string StatusOf(string s) => s != null && StatusLabel.ContainsKey(s) ? s : "active";
    public int HostCount(Network n) { var e = IpMath.Parse(n.Ip); return e != null && !e.IsSubnet ? 1 : n.HostList?.Count ?? 0; }
    public static bool IsWifiPort(string p) => System.Text.RegularExpressions.Regex.IsMatch(p ?? "", "^(wlan|wifi|wlan60)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static List<string> DefaultPorts(string model, string type)
    {
        var m = (model ?? "").ToLowerInvariant();
        var ports = new List<string>();
        int eth = m.Contains("rb4011") || m.Contains("rb5009") ? 10 : m.Contains("crs3") ? 24 : m.Contains("hex") || m.Contains("rb750") || m.Contains("rb760") ? 5 : m.Contains("sxt") || m.Contains("lhg") || m.Contains("ldf") || m.Contains("disc") ? 1 : type == "wireless" ? 1 : 5;
        for (int i = 1; i <= eth; i++) ports.Add("ether" + i);
        if (m.Contains("rb4011") || m.Contains("rb5009")) ports.Add("sfp-sfpplus1");
        if (type == "wireless" || type == "both") ports.Add("wlan1");
        return ports;
    }
    public List<string> PortsOf(Device d) => d.Ports != null && d.Ports.Count > 0 ? d.Ports : DefaultPorts(d.Model, d.Type);

    /// <summary>Networks and link subnets that overlap e.</summary>
    public List<string> NetConflicts(IpEntry e, string exceptNetId = null, string exceptLinkId = null)
    {
        var res = new List<string>();
        foreach (var n in Db.Networks)
        {
            if (n.Id == exceptNetId) continue;
            var o = IpMath.Parse(n.Ip); if (o == null || !IpMath.Overlaps(o, e)) continue;
            res.Add($"{o.Text} of {SiteLabel(n.SiteId)}{(string.IsNullOrEmpty(n.Name) ? "" : " · " + n.Name)}");
        }
        foreach (var l in Db.Links)
        {
            if (l.Id == exceptLinkId || string.IsNullOrEmpty(l.Subnet)) continue;
            var o = IpMath.Parse(l.Subnet); if (o == null || !IpMath.Overlaps(o, e)) continue;
            res.Add($"{o.Text}, the link subnet of {LinkLabel(l)}");
        }
        return res;
    }

    public string LinkLabel(Link l) => $"{DevById(l.A)?.Name ?? "?"} ↔ {DevById(l.B)?.Name ?? "?"}";

    /// <summary>Every address configured on devices: ip → (device, interface).</summary>
    public IEnumerable<(Device d, string ip, string iface)> DeviceIps()
    {
        foreach (var d in Db.Devices)
        {
            foreach (var a in d.Addrs ?? new()) { var ip = IpMath.IpOf(a.Address); if (ip != "") yield return (d, ip, a.Iface); }
            if (!string.IsNullOrEmpty(d.Ip)) yield return (d, d.Ip, d.IsMikroTik ? "management" : "");
        }
    }

    /// <summary>Used addresses inside a subnet (hosts, device addresses, gateway): for "next free IP".</summary>
    public HashSet<string> UsedIn(IpEntry e, Network net = null)
    {
        var used = new HashSet<string>();
        if (net != null) { foreach (var h in net.HostList) used.Add(h.Ip); if (!string.IsNullOrEmpty(net.Gateway)) used.Add(net.Gateway); }
        foreach (var (d, ip, _) in DeviceIps()) { var x = IpMath.Parse(ip); if (x != null && IpMath.Contains(e, x)) used.Add(x.Text); }
        foreach (var l in Db.Links) { foreach (var ip in new[] { l.IpA, l.IpB }) { var x = IpMath.Parse(ip); if (x != null && IpMath.Contains(e, x)) used.Add(x.Text); } }
        return used;
    }
    public string NextFreeIn(Network n)
    {
        var e = IpMath.Parse(n.Ip); if (e == null || !e.IsSubnet) return "";
        return IpMath.NextFree(e, UsedIn(e, n));
    }

    // ------------------------------------------------------------------ history
    public void Log(string act, string kind, string reference, string label, string siteId, List<string> lines = null)
    {
        Db.Changes.Add(new Change { At = Entity.Now(), By = Me?.Username ?? "?", Act = act, Kind = kind, Ref = reference ?? "", Label = label ?? "", SiteId = siteId ?? "", Lines = lines ?? new() });
        if (Db.Changes.Count > LogMax + 50) Db.Changes = Db.Changes.OrderBy(c => c.At, StringComparer.Ordinal).Skip(Db.Changes.Count - LogMax).ToList();
    }
    static List<string> Diff(params (string label, string before, string after)[] fields)
    {
        var out_ = new List<string>();
        foreach (var (label, b, a) in fields)
            if ((b ?? "") != (a ?? "")) out_.Add($"{label}: {(string.IsNullOrEmpty(b) ? "—" : b)} → {(string.IsNullOrEmpty(a) ? "—" : a)}");
        return out_;
    }
    public void ClearHistory()
    {
        NeedFull();
        var n = Db.Changes.Count; Db.Changes.Clear();
        Log("clear", "history", "", $"History cleared ({n} entries)", "");
        Persist();
    }

    // ------------------------------------------------------------------ sites
    public void SaveSite(Site s)
    {
        NeedWrite();
        s.Name = (s.Name ?? "").Trim(); s.SiteNumber = (s.SiteNumber ?? "").Trim();
        if (s.Name == "") throw new RuleException("Type the site name.");
        if (s.SiteNumber == "") throw new RuleException("Type the site number.");
        if (Db.Sites.Any(x => x.Id != s.Id && string.Equals(x.SiteNumber?.Trim(), s.SiteNumber, StringComparison.OrdinalIgnoreCase)))
            throw new RuleException($"Site number {s.SiteNumber} is already used by another site.");
        var old = SiteById(s.Id);
        s.UpdatedAt = Entity.Now(); s.CreatedAt ??= s.UpdatedAt;
        if (old == null) { if (string.IsNullOrEmpty(s.Id)) s.Id = Entity.NewId(); Db.Sites.Add(s); Log("add", "site", s.Id, $"#{s.SiteNumber} {s.Name}", s.Id); }
        else
        {
            var lines = Diff(("Name", old.Name, s.Name), ("Site number", old.SiteNumber, s.SiteNumber), ("Location", old.Location, s.Location), ("Contact", old.Contact, s.Contact), ("Notes", old.Notes, s.Notes), ("Status", StatusOf(old.Status), StatusOf(s.Status)));
            Db.Sites[Db.Sites.IndexOf(old)] = s;
            if (lines.Count > 0) Log("edit", "site", s.Id, $"#{s.SiteNumber} {s.Name}", s.Id, lines);
        }
        Persist();
    }

    public void DeleteSite(string id)
    {
        NeedFull();
        var s = SiteById(id); if (s == null) return;
        var devIds = Db.Devices.Where(d => d.SiteId == id).Select(d => d.Id).ToHashSet();
        foreach (var l in Db.Links.Where(l => devIds.Contains(l.A) || devIds.Contains(l.B)).ToList()) RemoveLink(l);
        Db.Configs.RemoveAll(c => devIds.Contains(ConfigDevice(c)));
        Db.Vlans.RemoveAll(v => v.SiteId == id);
        Db.Devices.RemoveAll(d => devIds.Contains(d.Id));
        var nets = Db.Networks.RemoveAll(n => n.SiteId == id);
        Db.Sites.Remove(s);
        Log("delete", "site", id, $"#{s.SiteNumber} {s.Name}", id, nets > 0 || devIds.Count > 0 ? new List<string> { $"with {nets} networks and {devIds.Count} devices" } : null);
        SyncDeviceHosts(); Persist();
    }
    static string ConfigDevice(JsonElement c) => c.ValueKind == JsonValueKind.Object && c.TryGetProperty("deviceId", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
    public int ConfigCount(string devId) => Db.Configs.Count(c => ConfigDevice(c) == devId);

    // ------------------------------------------------------------------ networks
    public void SaveNetwork(Network n)
    {
        NeedWrite();
        if (SiteById(n.SiteId) == null) throw new RuleException("Choose the site this network belongs to.");
        var e = IpMath.Parse(n.Ip) ?? throw new RuleException("That isn't a valid IP address or subnet. Examples: 192.168.1.10 or 192.168.1.0/24.");
        var c = NetConflicts(e, n.Id);
        if (c.Count > 0) throw new RuleException($"{e.Text} overlaps {c[0]}. An address can only belong to one network, so subnets can't overlap.");
        var vl = (n.Vlan ?? "").Trim();
        if (vl != "" && (!int.TryParse(vl, out var vid) || vid < 1 || vid > 4094)) throw new RuleException("VLAN ID must be a number from 1 to 4094.");
        var gw = (n.Gateway ?? "").Trim();
        if (gw != "")
        {
            var g = IpMath.Parse(gw);
            if (g == null || g.IsSubnet) throw new RuleException("The gateway must be a single IP address.");
            if (e.IsSubnet && (g.V != e.V || !IpMath.HostRangeOk(e, g.Addr))) throw new RuleException($"The gateway {g.Text} is not a usable address of {e.Text}.");
            gw = g.Text;
        }
        foreach (var (d, ip, _) in DeviceIps())
        {
            var x = IpMath.Parse(ip);
            if (x != null && IpMath.Contains(e, x) && d.SiteId != n.SiteId) throw new RuleException($"{x.Text} inside {e.Text} is used by device “{d.Name}” of {SiteLabel(d.SiteId)}.");
        }
        var hosts = n.HostList ?? new();
        if (!e.IsSubnet && hosts.Count > 0) throw new RuleException("A single IP can't hold a host list. Use a subnet or remove the hosts.");
        if (e.IsSubnet && hosts.Count > e.Usable) throw new RuleException($"{e.Text} only has {e.Usable} usable addresses, but {hosts.Count} hosts are listed.");
        var seenIp = new HashSet<string>(); var seenNum = new HashSet<int>();
        foreach (var h in hosts)
        {
            var x = IpMath.Parse(h.Ip);
            if (x == null || x.IsSubnet || x.V != e.V || !IpMath.HostRangeOk(e, x.Addr)) throw new RuleException($"Host {h.Ip} is not a usable address of {e.Text}.");
            h.Ip = x.Text;
            if (!seenIp.Add(h.Ip)) throw new RuleException($"{h.Ip} is listed twice.");
            if (h.Num < 1 || !seenNum.Add(h.Num)) throw new RuleException($"Host number #{h.Num} is used twice.");
            var mac = IpMath.NormMac(h.Mac); if (mac == null) throw new RuleException($"The MAC of {h.Ip} is not valid (12 hex digits, e.g. AA:BB:CC:DD:EE:FF)."); h.Mac = mac;
        }
        var old = Db.Networks.FirstOrDefault(x => x.Id == n.Id);
        n.Ip = e.Text; n.Kind = e.Kind; n.Vlan = vl == "" ? "" : int.Parse(vl).ToString(); n.Gateway = gw; n.HostList = hosts.OrderBy(h => h.Num).ToList();
        n.Hosts = e.IsSubnet ? n.HostList.Count : 1; n.UpdatedAt = Entity.Now(); n.CreatedAt ??= n.UpdatedAt;
        var label = $"{n.Ip}{(string.IsNullOrEmpty(n.Name) ? "" : " (" + n.Name + ")")}";
        if (old == null) { if (string.IsNullOrEmpty(n.Id)) n.Id = Entity.NewId(); Db.Networks.Add(n); Log("add", "network", n.Id, label, n.SiteId); }
        else
        {
            var lines = Diff(("Address", old.Ip, n.Ip), ("Name", old.Name, n.Name), ("VLAN", old.Vlan, n.Vlan), ("Gateway", old.Gateway, n.Gateway), ("Site", SiteById(old.SiteId)?.ToString(), SiteById(n.SiteId)?.ToString()), ("Notes", old.Notes, n.Notes));
            var A = old.HostList.ToDictionary(h => h.Ip); var B = n.HostList.ToDictionary(h => h.Ip);
            foreach (var h in n.HostList) if (!A.ContainsKey(h.Ip)) lines.Add($"+ host {h.Ip} {h.Name}".TrimEnd());
            foreach (var h in old.HostList) if (!B.ContainsKey(h.Ip)) lines.Add($"− host {h.Ip} {h.Name}".TrimEnd());
            Db.Networks[Db.Networks.IndexOf(old)] = n;
            if (lines.Count > 0) Log("edit", "network", n.Id, label, n.SiteId, lines);
        }
        SyncDeviceHosts(); Persist();
    }

    public void DeleteNetwork(string id)
    {
        NeedFull();
        var n = Db.Networks.FirstOrDefault(x => x.Id == id); if (n == null) return;
        Db.Networks.Remove(n);
        Log("delete", "network", id, $"{n.Ip}{(string.IsNullOrEmpty(n.Name) ? "" : " (" + n.Name + ")")}", n.SiteId);
        Persist();
    }

    // ------------------------------------------------------------------ devices
    /// <summary>Checks a device; returns the networks that will be created for addresses typed with a prefix.</summary>
    public List<IpEntry> CheckDevice(Device d)
    {
        if (SiteById(d.SiteId) == null) throw new RuleException("Choose the site of this device.");
        d.Name = (d.Name ?? "").Trim(); d.Model = (d.Model ?? "").Trim();
        if (d.Model == "") throw new RuleException(d.IsMikroTik ? "Type the MikroTik model." : "Type the brand and model.");
        if (d.Name == "") throw new RuleException("Type the device name.");
        if (d.IsMikroTik && EndTypes.Contains(d.Type)) throw new RuleException($"A MikroTik can't be a {TypeLabel[d.Type]}. Choose Router, Wireless or Router + Wi-Fi.");
        var mac = IpMath.NormMac(d.Mac); if (mac == null) throw new RuleException("MAC must be 12 hex digits, e.g. AA:BB:CC:DD:EE:FF."); d.Mac = mac;
        if (!string.IsNullOrEmpty(d.Winbox) && (!int.TryParse(d.Winbox, out var port) || port < 1 || port > 65535)) throw new RuleException("The port must be a number from 1 to 65535.");
        var others = Db.Devices.Where(x => x.Id != d.Id).ToList();
        string UsedBy(string ip) => others.FirstOrDefault(o => o.Ip == ip || (o.Addrs ?? new()).Any(a => IpMath.IpOf(a.Address) == ip))?.Name;
        var creates = new List<IpEntry>();

        if (!string.IsNullOrEmpty(d.Ip))
        {
            var x = IpMath.Parse(d.Ip);
            if (x == null || x.IsSubnet) throw new RuleException($"{(d.IsMikroTik ? "The management IP" : "The IP address")} must be a single IP address, without /prefix.");
            d.Ip = x.Text;
            var u = UsedBy(d.Ip); if (u != null) throw new RuleException($"{d.Ip} is already used by device “{u}”.");
            var inNet = Db.Networks.FirstOrDefault(n => { var e = IpMath.Parse(n.Ip); return e != null && IpMath.Contains(e, x); });
            if (inNet != null && inNet.SiteId != d.SiteId) throw new RuleException($"{d.Ip} belongs to {inNet.Ip} of {SiteLabel(inNet.SiteId)}.");
        }

        if (!d.IsMikroTik)
        {
            d.Bridges = new(); d.Ports ??= new(); d.WProto = ""; d.WSec = ""; d.Psk = ""; d.Ros = "";
            d.Addrs = (d.Addrs ?? new()).Where(a => a.Link != null).ToList();   // only addresses set by wireless connections
            if (d.Uplink != null)
            {
                var up = DevById(d.Uplink.DeviceId);
                var ba = up?.Addrs.FirstOrDefault(a => a.Iface == d.Uplink.Bridge);
                var be = ba == null ? null : IpMath.Parse(ba.Address);
                if (up == null || be == null) d.Uplink = null;
                else if (!string.IsNullOrEmpty(d.Ip) && !IpMath.Contains(be, IpMath.Parse(d.Ip))) throw new RuleException($"{d.Ip} is outside {be.Text} of {d.Uplink.Bridge} on {up.Name}.");
            }
            return creates;
        }

        d.Uplink = null;
        var ports = d.Ports ?? new();
        if (ports.Count == 0) ports = DefaultPorts(d.Model, d.Type);
        d.Ports = ports.Select(p => p.Trim()).Where(p => p != "").Distinct().ToList();
        var portSet = d.Ports.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var brNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in d.Bridges)
        {
            b.Name = (b.Name ?? "").Trim();
            if (b.Name == "") throw new RuleException("Every bridge needs a name.");
            if (!brNames.Add(b.Name) || portSet.Contains(b.Name)) throw new RuleException($"The name {b.Name} is used twice on this device.");
            b.Ports = b.Ports.Where(p => d.Ports.Contains(p)).Distinct().ToList();
        }
        var inBridge = new Dictionary<string, string>();
        foreach (var b in d.Bridges) foreach (var p in b.Ports) { if (inBridge.ContainsKey(p)) throw new RuleException($"{p} is in two bridges."); inBridge[p] = b.Name; }
        var vlanIfs = Db.Vlans.Where(v => v.DeviceId == d.Id && !string.IsNullOrEmpty(v.IfName)).Select(v => v.IfName).ToHashSet();
        var seen = new HashSet<string>();
        foreach (var a in d.Addrs)
        {
            if (a.Link != null) { seen.Add(IpMath.IpOf(a.Address)); continue; }   // set by a wireless connection
            if (string.IsNullOrWhiteSpace(a.Iface)) throw new RuleException("Choose the interface of every IP address.");
            if (IsWifiPort(a.Iface)) throw new RuleException($"{a.Iface} is a wireless interface. Its IP is typed on the wireless connection.");
            if (!brNames.Contains(a.Iface) && !portSet.Contains(a.Iface) && !vlanIfs.Contains(a.Iface)) throw new RuleException($"{a.Iface} doesn't exist on this device.");
            if (inBridge.TryGetValue(a.Iface, out var br)) throw new RuleException($"{a.Iface} is inside {br}. Put the IP on {br} instead.");
            var p = IpMath.ParseRaw(a.Address) ?? throw new RuleException($"“{a.Address}” on {a.Iface} is not a valid IP address.");
            int prefix;
            if (p.Prefix != null) prefix = p.Prefix.Value;
            else
            {
                var net = Db.Networks.FirstOrDefault(n => n.SiteId == d.SiteId && IpMath.Parse(n.Ip) is IpEntry ne && ne.IsSubnet && ne.V == p.V && p.Addr >= ne.Start && p.Addr <= ne.End);
                var ln = net == null ? Db.Links.FirstOrDefault(l => IpMath.Parse(l.Subnet) is IpEntry le && le.V == p.V && p.Addr >= le.Start && p.Addr <= le.End) : null;
                prefix = net != null ? IpMath.Parse(net.Ip).Prefix : ln != null ? IpMath.Parse(ln.Subnet).Prefix : p.Bits;
            }
            var e = IpMath.Analyze(p.V, p.Addr, prefix); var ip = IpMath.Format(p.V, p.Addr);
            if (p.V == 4 && prefix < 31 && (p.Addr == e.Start || p.Addr == e.End)) throw new RuleException($"{ip} is the {(p.Addr == e.Start ? "network" : "broadcast")} address of {e.Text}.");
            if (!seen.Add(ip)) throw new RuleException($"{ip} is listed twice on this device.");
            var used = UsedBy(ip); if (used != null) throw new RuleException($"{ip} is already used by device “{used}”.");
            a.Address = $"{ip}/{prefix}";
            var inNet = Db.Networks.FirstOrDefault(n => IpMath.Parse(n.Ip) is IpEntry ne && ne.V == p.V && p.Addr >= ne.Start && p.Addr <= ne.End);
            if (inNet != null && inNet.SiteId != d.SiteId) throw new RuleException($"{ip} belongs to {inNet.Ip} of {SiteLabel(inNet.SiteId)}.");
            if (inNet != null || prefix == p.Bits) continue;
            if (Db.Links.Any(l => IpMath.Parse(l.Subnet) is IpEntry le && le.V == p.V && p.Addr >= le.Start && p.Addr <= le.End)) continue;
            var c = NetConflicts(e);
            if (c.Count > 0) throw new RuleException($"Can't create {e.Text}: it overlaps {c[0]}. Use a prefix that fits.");
            if (creates.Any(x => x.Text != e.Text && IpMath.Overlaps(x, e))) throw new RuleException($"{e.Text} overlaps another new network of this device.");
            if (!creates.Any(x => x.Text == e.Text)) creates.Add(e);
        }
        if (!d.HasWifi) { d.WProto = ""; d.WSec = ""; d.Psk = ""; d.Role = ""; }
        if (d.WSec == "none" || string.IsNullOrEmpty(d.WSec)) d.Psk = "";
        return creates;
    }

    /// <summary>Saves the device; returns the networks created for its addresses.</summary>
    public List<string> SaveDevice(Device d)
    {
        NeedWrite();
        var old = DevById(d.Id);
        if (old != null && old.HasWifi && !d.HasWifi && Db.Links.Any(l => l.Type == "wireless" && (l.A == d.Id || l.B == d.Id)))
            throw new RuleException("This device has wireless connections. Remove them before changing it to a type without Wi-Fi.");
        var creates = CheckDevice(d);
        var now = Entity.Now();
        foreach (var e in creates)
            Db.Networks.Add(new Network { Id = Entity.NewId(), SiteId = d.SiteId, Ip = e.Text, Kind = e.Kind, Name = $"{d.Name} · {d.Addrs.First(a => IpMath.Parse(a.Address) is IpEntry x && IpMath.Overlaps(x, e)).Iface}", Notes = $"Created automatically from device {d.Name}.", Status = "active", CreatedAt = now, UpdatedAt = now });
        foreach (var e in creates) Log("add", "network", Db.Networks.Last(n => n.Ip == e.Text).Id, e.Text, d.SiteId);
        d.UpdatedAt = now; d.CreatedAt ??= now;
        var label = $"{d.Name} · {d.Model}";
        if (old == null) { if (string.IsNullOrEmpty(d.Id)) d.Id = Entity.NewId(); Db.Devices.Add(d); Log("add", "device", d.Id, label, d.SiteId); }
        else
        {
            var lines = Diff(("Name", old.Name, d.Name), ("Model", old.Model, d.Model), ("Type", TypeLabel.GetValueOrDefault(old.Type), TypeLabel.GetValueOrDefault(d.Type)),
                ("Site", SiteById(old.SiteId)?.ToString(), SiteById(d.SiteId)?.ToString()), ("Address", old.Ip, d.Ip), ("MAC", old.Mac, d.Mac), ("RouterOS", old.Ros, d.Ros),
                ("Username", old.User, d.User), ("Winbox port", old.Winbox, d.Winbox), ("Role", old.Role, d.Role), ("Notes", old.Notes, d.Notes), ("Status", StatusOf(old.Status), StatusOf(d.Status)));
            if ((old.Pass ?? "") != (d.Pass ?? "")) lines.Add("Device password changed");
            if ((old.Psk ?? "") != (d.Psk ?? "")) lines.Add("Wireless pre-shared key changed");
            var A = old.Addrs.Select(a => $"{a.Address} on {a.Iface}").ToHashSet(); var B = d.Addrs.Select(a => $"{a.Address} on {a.Iface}").ToHashSet();
            foreach (var t in B.Except(A)) lines.Add("+ IP " + t);
            foreach (var t in A.Except(B)) lines.Add("− IP " + t);
            Db.Devices[Db.Devices.IndexOf(old)] = d;
            if (lines.Count > 0) Log("edit", "device", d.Id, label, d.SiteId, lines);
        }
        SyncDeviceHosts(); Persist();
        return creates.Select(e => e.Text).ToList();
    }

    public void DeleteDevice(string id)
    {
        NeedFull();
        var d = DevById(id); if (d == null) return;
        foreach (var l in Db.Links.Where(l => l.A == id || l.B == id).ToList()) RemoveLink(l);
        Db.Configs.RemoveAll(c => ConfigDevice(c) == id);
        foreach (var v in Db.Vlans.Where(v => v.DeviceId == id)) { v.DeviceId = ""; v.Parent = ""; v.IfName = ""; v.UpdatedAt = Entity.Now(); }
        Db.Devices.Remove(d);
        Log("delete", "device", id, $"{d.Name} · {d.Model}", d.SiteId);
        SyncDeviceHosts(); Persist();
    }

    /// <summary>MikroTik bridges with an address in a site, plus the site's networks: where an other-brand device can take its IP.</summary>
    public List<(string key, string label, IpEntry e, Device dev, string bridge, string gw)> IpSources(string siteId, string exceptDevId)
    {
        var list = new List<(string, string, IpEntry, Device, string, string)>();
        foreach (var d in Db.Devices.Where(x => x.SiteId == siteId && x.Id != exceptDevId && x.IsMikroTik))
            foreach (var a in d.Addrs.Where(a => d.Bridges.Any(b => b.Name == a.Iface)))
            {
                var p = IpMath.ParseRaw(a.Address); if (p?.Prefix == null || p.Prefix >= p.Bits) continue;
                var e = IpMath.Analyze(p.V, p.Addr, p.Prefix.Value);
                list.Add(($"{d.Id}|{a.Iface}", $"{d.Name} · {a.Iface} · {e.Text}", e, d, a.Iface, IpMath.Format(p.V, p.Addr)));
            }
        foreach (var n in NetsOf(siteId))
        {
            var e = IpMath.Parse(n.Ip); if (e == null || !e.IsSubnet || list.Any(x => x.Item3.Text == e.Text)) continue;
            list.Add(($"net|{n.Id}", $"Network · {e.Text}{(string.IsNullOrEmpty(n.Name) ? "" : " · " + n.Name)}", e, null, "", n.Gateway ?? ""));
        }
        return list;
    }

    // ------------------------------------------------------------------ connections
    public void SaveLink(Link l)
    {
        NeedWrite();
        var A = DevById(l.A); var B = DevById(l.B);
        if (A == null || B == null) throw new RuleException("Choose both devices.");
        if (A.Id == B.Id) throw new RuleException("A connection needs two different devices.");
        if (l.Type == "wired" && A.SiteId != B.SiteId) throw new RuleException($"A wired connection must stay inside one site. {A.Name} is in #{SiteById(A.SiteId)?.SiteNumber} and {B.Name} is in #{SiteById(B.SiteId)?.SiteNumber}. Use a wireless connection between sites.");
        if (l.Type == "wireless" && (!A.HasWifi || !B.HasWifi)) throw new RuleException("Both ends of a wireless connection must be wireless devices (Wireless or Router + Wi-Fi).");
        if (Db.Links.Any(x => x.Id != l.Id && x.Type == l.Type && ((x.A == l.A && x.B == l.B) || (x.A == l.B && x.B == l.A))))
            throw new RuleException($"These two devices already have a {l.Type} connection.");
        foreach (var (dev, port) in new[] { (A, l.PortA), (B, l.PortB) })
        {
            if (string.IsNullOrEmpty(port)) continue;
            if (l.Type == "wireless" && !IsWifiPort(port)) throw new RuleException($"{port} on {dev.Name} is not a wireless interface.");
            if (l.Type == "wired" && IsWifiPort(port)) throw new RuleException($"{port} on {dev.Name} is a wireless interface. Use a wireless connection.");
            var busy = Db.Links.FirstOrDefault(x => x.Id != l.Id && ((x.A == dev.Id && x.PortA == port) || (x.B == dev.Id && x.PortB == port)));
            if (busy != null && !(IsWifiPort(port) && dev.Role == "ap")) throw new RuleException($"{port} on {dev.Name} is already used by {LinkLabel(busy)}.");
        }
        IpEntry sub = null;
        if (!string.IsNullOrWhiteSpace(l.Subnet))
        {
            sub = IpMath.Parse(l.Subnet);
            if (sub == null || !sub.IsSubnet) throw new RuleException("The link subnet must be a subnet, e.g. 10.5.5.0/30.");
            var c = NetConflicts(sub, null, l.Id);
            if (c.Count > 0) throw new RuleException($"The link subnet {sub.Text} overlaps {c[0]}.");
            l.Subnet = sub.Text;
        }
        else l.Subnet = "";
        if (l.Type == "wireless")
        {
            foreach (var (side, ipRef, dev) in new[] { ("A", l.IpA, A), ("B", l.IpB, B) })
            {
                if (string.IsNullOrWhiteSpace(ipRef)) continue;
                var x = IpMath.Parse(IpMath.IpOf(ipRef)) ?? throw new RuleException($"The IP on {dev.Name} is not valid.");
                if (sub != null && (x.V != sub.V || !IpMath.HostRangeOk(sub, x.Addr))) throw new RuleException($"{x.Text} is not a usable address of the link subnet {sub.Text}.");
                var other = Db.Devices.FirstOrDefault(o => o.Id != dev.Id && (o.Ip == x.Text || o.Addrs.Any(a => IpMath.IpOf(a.Address) == x.Text && a.Link != l.Id)));
                if (other != null) throw new RuleException($"{x.Text} is already used by device “{other.Name}”.");
                if (side == "A") l.IpA = x.Text; else l.IpB = x.Text;
            }
            if (!string.IsNullOrEmpty(l.IpA) && l.IpA == l.IpB) throw new RuleException("The two ends need different IP addresses.");
            if ((!string.IsNullOrEmpty(l.IpA) || !string.IsNullOrEmpty(l.IpB)) && sub == null) throw new RuleException("Type the link subnet (e.g. 10.5.5.0/30) so the prefix of the WLAN IPs is known.");
        }
        else
        {
            // wired: the IP of each end comes from the interface it plugs into
            l.IpA = EndIp(A, l.PortA); l.IpB = EndIp(B, l.PortB);
        }
        var old = Db.Links.FirstOrDefault(x => x.Id == l.Id);
        l.UpdatedAt = Entity.Now(); l.CreatedAt ??= l.UpdatedAt;
        if (old == null) { if (string.IsNullOrEmpty(l.Id)) l.Id = Entity.NewId(); Db.Links.Add(l); Log("add", "connection", l.Id, $"{LinkLabel(l)} · {l.Type}", A.SiteId); }
        else
        {
            var lines = Diff(("From", DevById(old.A)?.Name, A.Name), ("To", DevById(old.B)?.Name, B.Name), ("Type", old.Type, l.Type), ("Interface on From", old.PortA, l.PortA), ("Interface on To", old.PortB, l.PortB),
                ("Link subnet", old.Subnet, l.Subnet), ("IP on From", old.IpA, l.IpA), ("IP on To", old.IpB, l.IpB), ("SSID", old.Ssid, l.Ssid), ("Notes", old.Notes, l.Notes));
            Db.Links[Db.Links.IndexOf(old)] = l;
            if (lines.Count > 0) Log("edit", "connection", l.Id, $"{LinkLabel(l)} · {l.Type}", A.SiteId, lines);
        }
        // WLAN IPs live on the devices too, marked with the connection
        DropLinkAddrs(l.Id);
        if (l.Type == "wireless" && sub != null)
        {
            if (!string.IsNullOrEmpty(l.IpA) && !string.IsNullOrEmpty(l.PortA)) A.Addrs.Add(new Addr { Iface = l.PortA, Address = $"{l.IpA}/{sub.Prefix}", Link = l.Id });
            if (!string.IsNullOrEmpty(l.IpB) && !string.IsNullOrEmpty(l.PortB)) B.Addrs.Add(new Addr { Iface = l.PortB, Address = $"{l.IpB}/{sub.Prefix}", Link = l.Id });
        }
        SyncDeviceHosts(); Persist();
    }

    /// <summary>The address on a device's interface (or on the bridge the interface is in).</summary>
    public string EndIp(Device d, string port)
    {
        if (d == null || string.IsNullOrEmpty(port)) return "";
        var br = d.Bridges.FirstOrDefault(b => b.Ports.Contains(port));
        if (br != null) return "";   // bridged: the IP belongs to the bridge, not to this port
        var a = d.Addrs.FirstOrDefault(x => x.Iface == port);
        return a == null ? "" : IpMath.IpOf(a.Address);
    }

    void DropLinkAddrs(string linkId) { foreach (var d in Db.Devices) d.Addrs.RemoveAll(a => a.Link == linkId); }
    void RemoveLink(Link l) { DropLinkAddrs(l.Id); Db.Links.Remove(l); Log("delete", "connection", l.Id, $"{LinkLabel(l)} · {l.Type}", DevById(l.A)?.SiteId ?? ""); }
    public void DeleteLink(string id)
    {
        NeedWrite();
        var l = Db.Links.FirstOrDefault(x => x.Id == id); if (l == null) return;
        RemoveLink(l); SyncDeviceHosts(); Persist();
    }

    /// <summary>Fills a link subnet's first two usable addresses into the two ends.</summary>
    public static (string a, string b) SplitSubnet(string subnet)
    {
        var e = IpMath.Parse(subnet); if (e == null || !e.IsSubnet || e.Usable < 2) return ("", "");
        return (IpMath.Format(e.V, e.FirstUsable), IpMath.Format(e.V, e.FirstUsable + 1));
    }

    // ------------------------------------------------------------------ VLANs
    public void SaveVlan(Vlan v)
    {
        NeedWrite();
        if (SiteById(v.SiteId) == null) throw new RuleException("Choose the site.");
        if (v.Vid < 1 || v.Vid > 4094) throw new RuleException("VLAN ID must be a number from 1 to 4094.");
        if (Db.Vlans.Any(x => x.Id != v.Id && x.SiteId == v.SiteId && x.Vid == v.Vid)) throw new RuleException($"VLAN {v.Vid} already exists in this site.");
        if (!string.IsNullOrEmpty(v.DeviceId))
        {
            var d = DevById(v.DeviceId);
            if (d == null || d.SiteId != v.SiteId || !d.IsMikroTik) throw new RuleException("The VLAN interface must be on a MikroTik of the same site.");
            v.IfName = (v.IfName ?? "").Trim(); if (v.IfName == "") v.IfName = "vlan" + v.Vid;
            if (string.IsNullOrEmpty(v.Parent)) throw new RuleException("Choose the interface or bridge the VLAN runs on.");
            if (PortsOf(d).Contains(v.IfName) || d.Bridges.Any(b => b.Name == v.IfName) || Db.Vlans.Any(x => x.Id != v.Id && x.DeviceId == d.Id && x.IfName == v.IfName))
                throw new RuleException($"{v.IfName} already exists on {d.Name}.");
        }
        else { v.Parent = ""; v.IfName = ""; }
        var old = Db.Vlans.FirstOrDefault(x => x.Id == v.Id);
        v.UpdatedAt = Entity.Now(); v.CreatedAt ??= v.UpdatedAt;
        var label = $"VLAN {v.Vid}{(string.IsNullOrEmpty(v.Name) ? "" : " " + v.Name)}";
        if (old == null) { if (string.IsNullOrEmpty(v.Id)) v.Id = Entity.NewId(); Db.Vlans.Add(v); Log("add", "vlan", v.Id, label, v.SiteId); }
        else
        {
            var lines = Diff(("VLAN ID", old.Vid.ToString(), v.Vid.ToString()), ("Name", old.Name, v.Name), ("Device", DevById(old.DeviceId)?.Name, DevById(v.DeviceId)?.Name), ("Parent", old.Parent, v.Parent), ("Interface", old.IfName, v.IfName), ("Notes", old.Notes, v.Notes));
            Db.Vlans[Db.Vlans.IndexOf(old)] = v;
            if (lines.Count > 0) Log("edit", "vlan", v.Id, label, v.SiteId, lines);
        }
        Persist();
    }

    public void DeleteVlan(string id)
    {
        NeedFull();
        var v = Db.Vlans.FirstOrDefault(x => x.Id == id); if (v == null) return;
        if (!string.IsNullOrEmpty(v.IfName) && DevById(v.DeviceId) is Device d && d.Addrs.Any(a => a.Iface == v.IfName))
            throw new RuleException($"{d.Name} has an IP address on {v.IfName}. Remove it from the device first.");
        Db.Vlans.Remove(v);
        Log("delete", "vlan", id, $"VLAN {v.Vid}{(string.IsNullOrEmpty(v.Name) ? "" : " " + v.Name)}", v.SiteId);
        Persist();
    }

    // ------------------------------------------------------------------ accounts
    public User Login(string username, string password)
    {
        var u = Db.Users.FirstOrDefault(x => x.Id == Auth.UserKey(username));
        return u != null && Auth.Verify(u, password) ? u : null;
    }

    public User SignUp(string username, string password, string password2, string psk, string role)
    {
        var p = Auth.SignupProblem(username, password, password2); if (p != null) throw new RuleException(p);
        if (!Auth.CheckPsk(psk)) throw new RuleException("Wrong pre-shared key. You can't sign up without it.");
        if (Db.Users.Any(x => x.Id == Auth.UserKey(username))) throw new RuleException($"Username “{username.Trim()}” is already taken.");
        var u = Auth.NewUser(username, password, role);
        Db.Users.Add(u); Me = u;
        Log("add", "user", u.Id, u.Username, "");
        Persist();
        return u;
    }

    /// <summary>An administrator with Full access adds an account (no pre-shared key needed).</summary>
    public User AddUser(string username, string password, string password2, string perm)
    {
        NeedFull();
        var p = Auth.SignupProblem(username, password, password2); if (p != null) throw new RuleException(p);
        if (Db.Users.Any(x => x.Id == Auth.UserKey(username))) throw new RuleException($"Username “{username.Trim()}” is already taken.");
        if (!Auth.Perms.Contains(perm)) throw new RuleException("Choose a permission.");
        var u = Auth.NewUser(username, password, perm == "full" ? "admin" : "guest"); u.Perm = perm;
        Db.Users.Add(u);
        Log("add", "user", u.Id, u.Username, "", new() { $"Permission: {perm}" });
        Persist();
        return u;
    }

    int FullCount(string exceptId) => Db.Users.Count(u => u.Id != exceptId && u.Perm == "full");

    public void SetPermission(string userId, string perm)
    {
        NeedFull();
        var u = Db.Users.FirstOrDefault(x => x.Id == userId) ?? throw new RuleException("Account not found.");
        if (!Auth.Perms.Contains(perm)) throw new RuleException("Unknown permission.");
        if (u.Id == Me.Id) throw new RuleException("You can't change your own permission.");
        if (u.Perm == "full" && perm != "full" && FullCount(u.Id) == 0) throw new RuleException("At least one account must keep Full access.");
        var old = u.Perm; u.Perm = perm; u.UpdatedAt = Entity.Now();
        Log("edit", "user", u.Id, u.Username, "", new() { $"Permission: {old} → {perm}" });
        Persist();
    }

    public void DeleteUser(string userId)
    {
        NeedFull();
        var u = Db.Users.FirstOrDefault(x => x.Id == userId) ?? throw new RuleException("Account not found.");
        if (u.Id == Me.Id) throw new RuleException("You can't delete your own account.");
        if (u.Perm == "full" && FullCount(u.Id) == 0) throw new RuleException("At least one account must keep Full access.");
        Db.Users.Remove(u); Log("delete", "user", u.Id, u.Username, ""); Persist();
    }

    public void ChangeMyPassword(string current, string next, string next2)
    {
        if (Me == null || !Auth.Verify(Me, current)) throw new RuleException("The current password is wrong.");
        if ((next ?? "").Length < 8) throw new RuleException("Password must be at least 8 characters.");
        if (next != next2) throw new RuleException("Passwords don't match.");
        Auth.SetPassword(Me, next);
        Log("edit", "user", Me.Id, Me.Username, "", new() { "Password changed" });
        Persist();
    }

    // ------------------------------------------------------------------ device addresses as hosts
    /// <summary>Every address configured on a device is a host of the network holding it (same rules as the web version).</summary>
    public void SyncDeviceHosts()
    {
        var want = new Dictionary<string, (Device d, string iface, IpEntry x)>();
        foreach (var (d, ip, iface) in DeviceIps()) { var x = IpMath.Parse(ip); if (x != null && !x.IsSubnet && !want.ContainsKey(x.Text)) want[x.Text] = (d, iface, x); }
        foreach (var n in Db.Networks)
        {
            var e = IpMath.Parse(n.Ip); if (e == null || !e.IsSubnet) continue;
            bool InNet((Device d, string iface, IpEntry x) w) => w.x.V == e.V && IpMath.HostRangeOk(e, w.x.Addr) && (string.IsNullOrEmpty(w.d.SiteId) || w.d.SiteId == n.SiteId);
            string Name((Device d, string iface, IpEntry x) w) => w.d.Name + (string.IsNullOrEmpty(w.iface) ? "" : " · " + w.iface);
            var list = n.HostList ?? new();
            int num = list.Count == 0 ? 0 : list.Max(h => h.Num);
            var next = new List<Host>(); bool changed = false;
            foreach (var h in list)
            {
                if (h.Dev == null) { next.Add(h); continue; }
                if (!want.TryGetValue(h.Ip, out var w) || w.d.Id != h.Dev || !InNet(w)) { changed = true; continue; }
                var nm = Name(w); bool keep = !string.IsNullOrEmpty(h.Name) && h.Name != h.AutoName;
                var u = h.Clone(); u.Iface = w.iface; u.AutoName = nm; u.Name = keep ? h.Name : nm; if (string.IsNullOrEmpty(u.Mac)) u.Mac = w.d.Mac ?? "";
                if (u.Name != h.Name || u.Iface != h.Iface || u.AutoName != h.AutoName || u.Mac != h.Mac) changed = true;
                next.Add(u);
            }
            var have = next.Select(h => h.Ip).ToHashSet();
            foreach (var (ip, w) in want)
            {
                if (have.Contains(ip) || !InNet(w)) continue;
                if (next.Count >= e.Usable) break;
                var nm = Name(w);
                next.Add(new Host { Num = ++num, Ip = ip, Name = nm, AutoName = nm, Mac = w.d.Mac ?? "", Dev = w.d.Id, Iface = w.iface });
                have.Add(ip); changed = true;
            }
            if (changed) { n.HostList = next.OrderBy(h => h.Num).ToList(); n.Hosts = n.HostList.Count; n.UpdatedAt = Entity.Now(); }
        }
    }

    // ------------------------------------------------------------------ site map positions
    // Same fields as the web version: a site's "mx"/"my" is its box position (all-sites view),
    // a device's "mx"/"my" is its centre inside its site box. Moving things is not written to the history.
    public static double? MapX(Entity e) => Num(e, "mx");
    public static double? MapY(Entity e) => Num(e, "my");
    static double? Num(Entity e, string k) => e.Extra != null && e.Extra.TryGetValue(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
    static bool SetPos(Entity e, double? x, double? y)
    {
        if (x == null || y == null) return false;
        int nx = (int)Math.Round(x.Value), ny = (int)Math.Round(y.Value);
        if (MapX(e) == nx && MapY(e) == ny) return false;
        e.Extra ??= new();
        e.Extra["mx"] = JsonSerializer.SerializeToElement(nx);
        e.Extra["my"] = JsonSerializer.SerializeToElement(ny);
        return true;
    }

    /// <summary>Saves new map positions (from a drag on the site map).</summary>
    public void SaveLayout(IDictionary<string, (double x, double y)> devices, IDictionary<string, (double x, double y)> sites)
    {
        NeedWrite();
        bool changed = false;
        foreach (var (id, p) in devices ?? new Dictionary<string, (double, double)>()) if (DevById(id) is Device d && SetPos(d, p.x, p.y)) { d.UpdatedAt = Entity.Now(); changed = true; }
        foreach (var (id, p) in sites ?? new Dictionary<string, (double, double)>()) if (SiteById(id) is Site s && SetPos(s, p.x, p.y)) { s.UpdatedAt = Entity.Now(); changed = true; }
        if (changed) Persist();
    }

    /// <summary>Back to the automatic arrangement for these sites and devices.</summary>
    public void ResetLayout(IEnumerable<string> siteIds, IEnumerable<string> devIds)
    {
        NeedWrite();
        bool changed = false;
        void Clear(Entity e) { if (e?.Extra != null && (e.Extra.Remove("mx") | e.Extra.Remove("my"))) { e.UpdatedAt = Entity.Now(); changed = true; } }
        foreach (var id in siteIds ?? Array.Empty<string>()) Clear(SiteById(id));
        foreach (var id in devIds ?? Array.Empty<string>()) Clear(DevById(id));
        if (changed) Persist();
    }

    // ------------------------------------------------------------------ backup and export
    /// <summary>Replaces everything with the contents of another database or backup file. Accounts are kept if the file has none.</summary>
    public string RestoreFrom(string path)
    {
        NeedFull();
        var other = DbIo.Load(path);
        var users = other.Users.Count > 0 ? other.Users : Db.Users;
        var log = Db.Changes;
        Db = other; Db.Users = users;
        if (Db.Changes.Count == 0) Db.Changes = log;
        if (Me != null && !Db.Users.Any(u => u.Id == Me.Id)) Db.Users.Add(Me);
        Log("import", "import", "", $"Restored from {System.IO.Path.GetFileName(path)}: {Db.Sites.Count} sites, {Db.Networks.Count} networks, {Db.Devices.Count} devices", "");
        SyncDeviceHosts(); Persist();
        return $"{Db.Sites.Count} sites, {Db.Networks.Count} networks, {Db.Devices.Count} devices and {Db.Links.Count} connections restored.";
    }

    static string Csv(IEnumerable<IEnumerable<object>> rows)
    {
        string Q(object v) { var t = v?.ToString() ?? ""; return t.IndexOfAny(new[] { '"', ',', '\n', '\r', ';' }) >= 0 ? "\"" + t.Replace("\"", "\"\"") + "\"" : t; }
        return "﻿" + string.Join("\r\n", rows.Select(r => string.Join(",", r.Select(Q))));
    }

    public string NetworksCsv()
    {
        var rows = new List<object[]> { new object[] { "Site #", "Site", "Network", "Name", "VLAN", "Gateway", "Usable range", "Hosts", "Host #", "Host IP", "Hostname", "MAC", "From device", "Notes" } };
        foreach (var s in Db.Sites.OrderBy(s => s.SiteNumber, StringComparer.OrdinalIgnoreCase))
            foreach (var n in NetsOf(s.Id).OrderBy(n => n.Ip))
            {
                var e = IpMath.Parse(n.Ip); var range = e != null && e.IsSubnet ? $"{e.FirstU} – {e.LastU}" : "";
                rows.Add(new object[] { s.SiteNumber, s.Name, n.Ip, n.Name, n.Vlan, n.Gateway, range, HostCount(n), "", "", "", "", "", n.Notes });
                foreach (var h in n.HostList.OrderBy(h => h.Num)) rows.Add(new object[] { s.SiteNumber, s.Name, n.Ip, "", "", "", "", "", h.Num, h.Ip, h.Name, h.Mac, h.Dev == null ? "" : "yes", h.Notes });
            }
        return Csv(rows);
    }

    public string DevicesCsv(bool withPasswords)
    {
        var head = new List<object> { "Site #", "Site", "Device", "Brand", "Type", "Model", "Role", "Status", "IP / management IP", "IP addresses", "Bridges", "MAC", "RouterOS", "Username" };
        if (withPasswords) head.Add("Password");
        head.AddRange(new object[] { "Port", "Wireless protocol", "Security" });
        if (withPasswords) head.Add("Pre-shared key");
        head.Add("Notes");
        var rows = new List<List<object>> { head };
        foreach (var d in Db.Devices.OrderBy(d => SiteById(d.SiteId)?.SiteNumber).ThenBy(d => d.Name))
        {
            var s = SiteById(d.SiteId);
            var r = new List<object> { s?.SiteNumber, s?.Name, d.Name, d.IsMikroTik ? "MikroTik" : "Other brand", TypeLabel.GetValueOrDefault(d.Type, d.Type), d.Model, RoleLabel.GetValueOrDefault(d.Role ?? "", ""), StatusLabel[StatusOf(d.Status)],
                d.Ip, string.Join("\n", d.Addrs.Select(a => $"{a.Iface}: {a.Address}")), string.Join("\n", d.Bridges.Select(b => $"{b.Name}: {string.Join(", ", b.Ports)}")), d.Mac, d.Ros, d.User };
            if (withPasswords) r.Add(d.Pass);
            r.AddRange(new object[] { d.Winbox, WProtoLabel.GetValueOrDefault(d.WProto ?? "", ""), WSecLabel.GetValueOrDefault(d.WSec ?? "", "") });
            if (withPasswords) r.Add(d.Psk);
            r.Add(d.Notes);
            rows.Add(r);
        }
        return Csv(rows);
    }

    public string LinksCsv()
    {
        var rows = new List<object[]> { new object[] { "Type", "From site", "From device", "From interface", "From IP", "To site", "To device", "To interface", "To IP", "Link subnet", "SSID", "Notes" } };
        foreach (var l in Db.Links)
        {
            var A = DevById(l.A); var B = DevById(l.B);
            rows.Add(new object[] { l.Type, SiteById(A?.SiteId)?.ToString(), A?.Name, l.PortA, l.IpA, SiteById(B?.SiteId)?.ToString(), B?.Name, l.PortB, l.IpB, l.Subnet, l.Ssid, l.Notes });
        }
        return Csv(rows);
    }
}
