// Checks the IP Monitor core rules without a screen: dotnet run -- [path to an ip-monitor-db.json]
using System.Text.Json;
using System.Text.Json.Nodes;
using IpMonitor.Core;

int fails = 0, passes = 0;
void Ok(bool cond, string what) { if (cond) passes++; else { fails++; Console.WriteLine("FAIL  " + what); } }
void Throws(Action a, string contains, string what)
{
    try { a(); fails++; Console.WriteLine($"FAIL  {what}: no error"); }
    catch (RuleException e) { Ok(e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase), $"{what}: got “{e.Message}”"); }
}

// --- passwords are the same as in the web version (hash made with the browser's WebCrypto)
Ok(Auth.Pbkdf2("Pässw0rd!x", "00112233445566778899aabbccddeeff", 150000) == "fe52f49cdef649e57b147cbb1b2b20edfa18685b9baed365fa74cd18a48ff81a", "PBKDF2 matches the web version");
Ok(Auth.Hex(Auth.Pbkdf2Managed(System.Text.Encoding.UTF8.GetBytes("Pässw0rd!x"), Convert.FromHexString("00112233445566778899aabbccddeeff"), 150000)) == "fe52f49cdef649e57b147cbb1b2b20edfa18685b9baed365fa74cd18a48ff81a", "fallback PBKDF2 matches too");
Ok(!Auth.CheckPsk("wrong"), "wrong pre-shared key refused");

// --- IP maths
Ok(IpMath.Parse("192.168.1.77 255.255.255.0").Text == "192.168.1.0/24", "ip + mask");
Ok(IpMath.Parse("10.0.0.5").Text == "10.0.0.5" && !IpMath.Parse("10.0.0.5").IsSubnet, "single ip");
Ok(IpMath.Parse("2001:db8::1/64").Text == "2001:db8::/64", "ipv6");
Ok(IpMath.Parse("300.1.1.1") == null && IpMath.Parse("10.0.0.0/33") == null, "invalid refused");
Ok(IpMath.NormMac("aa-bb-cc-dd-ee-0f") == "AA:BB:CC:DD:EE:0F" && IpMath.NormMac("xyz") == null, "mac");
Ok(IpMath.Parse("10.0.0.0/30").Usable == 2, "usable /30");

// --- a full scenario in a temporary file
var dir = Path.Combine(Path.GetTempPath(), "ipm-check-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(dir);
var file = Path.Combine(dir, "ip-monitor-db.json");
var st = new Store(); st.CreateNew(file);
var admin = Auth.NewUser("admin", "password1", "admin"); st.Db.Users.Add(admin); st.Me = admin;
Throws(() => st.SignUp("bob", "password1", "password1", "nope", "guest"), "pre-shared key", "signup without key");
Ok(st.Login("ADMIN ", "password1") != null && st.Login("admin", "bad") == null, "login");

var s1 = new Site { Name = "HQ", SiteNumber = "1" }; st.SaveSite(s1);
var s2 = new Site { Name = "Branch", SiteNumber = "2" }; st.SaveSite(s2);
Throws(() => st.SaveSite(new Site { Name = "X", SiteNumber = "1" }), "already used", "duplicate site number");

var lan = new Network { SiteId = s1.Id, Ip = "192.168.10.0/24", Name = "LAN", Gateway = "192.168.10.1" }; st.SaveNetwork(lan);
Throws(() => st.SaveNetwork(new Network { SiteId = s2.Id, Ip = "192.168.10.128/25" }), "overlaps", "overlapping network");
Throws(() => st.SaveNetwork(new Network { SiteId = s2.Id, Ip = "10.9.0.0/24", Gateway = "10.9.0.255" }), "not a usable", "gateway = broadcast");
Throws(() => st.SaveNetwork(new Network { SiteId = s2.Id, Ip = "10.9.0.0/24", Vlan = "5000" }), "4094", "bad vlan");
var hn = new Network { SiteId = s2.Id, Ip = "10.20.0.0/24", HostList = new() { new Host { Num = 1, Ip = "10.20.0.10", Name = "printer", Mac = "aabbccddeeff" } } };
st.SaveNetwork(hn);
Ok(hn.HostList[0].Mac == "AA:BB:CC:DD:EE:FF" && hn.Hosts == 1, "host saved with mac");
Throws(() => { var n = st.Db.Networks.First(x => x.Id == hn.Id); var c = JsonSerializer.Deserialize<Network>(JsonSerializer.Serialize(n)); c.HostList.Add(new Host { Num = 2, Ip = "10.21.0.1" }); st.SaveNetwork(c); }, "not a usable", "host outside subnet");

// MikroTik with a bridge and addresses; a typed prefix creates a network
var r1 = new Device { SiteId = s1.Id, Name = "R1", Model = "hEX", Type = "both", Ip = "192.168.10.1", Mac = "11:22:33:44:55:66",
    Bridges = new() { new Bridge { Name = "bridge1", Ports = new() { "ether2", "ether3" } } },
    Addrs = new() { new Addr { Iface = "bridge1", Address = "192.168.10.2" }, new Addr { Iface = "ether1", Address = "172.16.5.2/30" } } };
var created = st.SaveDevice(r1);
Ok(created.SequenceEqual(new[] { "172.16.5.0/30" }), "auto network created: " + string.Join(",", created));
Ok(r1.Addrs[0].Address == "192.168.10.2/24", "prefix taken from site network: " + r1.Addrs[0].Address);
Ok(lan.HostList.Any(h => h.Ip == "192.168.10.2" && h.Dev == r1.Id) && lan.HostList.Any(h => h.Ip == "192.168.10.1" && h.Dev == r1.Id), "device IPs listed as hosts");
Throws(() => st.SaveDevice(new Device { SiteId = s1.Id, Name = "R2", Model = "hEX", Ip = "192.168.10.1" }), "already used", "duplicate management ip");
Throws(() => st.SaveDevice(new Device { SiteId = s1.Id, Name = "R2", Model = "hEX", Bridges = new() { new Bridge { Name = "br", Ports = new() { "ether2" } } }, Addrs = new() { new Addr { Iface = "ether2", Address = "10.1.1.1/24" } } }), "inside", "ip on bridged port");
Throws(() => st.SaveDevice(new Device { SiteId = s1.Id, Name = "R2", Model = "hEX", Addrs = new() { new Addr { Iface = "ether1", Address = "10.1.1.0/24" } } }), "network address", "network address refused");
Throws(() => st.SaveDevice(new Device { SiteId = s2.Id, Name = "R2", Model = "hEX", Ip = "192.168.10.50" }), "belongs to", "ip of another site");
Throws(() => st.SaveDevice(new Device { SiteId = s1.Id, Name = "PCx", Model = "Dell", Vendor = "mikrotik", Type = "pc" }), "can't be", "mikrotik pc");

var pc = new Device { SiteId = s1.Id, Name = "PC1", Model = "Dell", Vendor = "other", Type = "pc", Ip = "192.168.10.20", Uplink = new Uplink { DeviceId = r1.Id, Bridge = "bridge1" } };
st.SaveDevice(pc);
Ok(lan.HostList.Any(h => h.Ip == "192.168.10.20" && h.Name == "PC1"), "other-brand ip listed as host");

var ap2 = new Device { SiteId = s2.Id, Name = "AP2", Model = "SXT", Type = "wireless", Ip = "10.20.0.2" }; st.SaveDevice(ap2);
Throws(() => st.SaveLink(new Link { A = r1.Id, B = ap2.Id, Type = "wired", PortA = "ether4", PortB = "ether1" }), "one site", "wired across sites");
Throws(() => st.SaveLink(new Link { A = pc.Id, B = ap2.Id, Type = "wireless" }), "wireless devices", "wireless to a pc");
var wl = new Link { A = r1.Id, B = ap2.Id, Type = "wireless", PortA = "wlan1", PortB = "wlan1", Subnet = "10.5.5.0/30", IpA = "10.5.5.1", IpB = "10.5.5.2", Ssid = "bridge" };
st.SaveLink(wl);
Ok(r1.Addrs.Any(a => a.Address == "10.5.5.1/30" && a.Link == wl.Id) && ap2.Addrs.Any(a => a.Address == "10.5.5.2/30"), "WLAN ips written to devices");
Throws(() => st.SaveNetwork(new Network { SiteId = s1.Id, Ip = "10.5.5.0/29" }), "link subnet", "network overlapping link subnet");
var wd = new Link { A = r1.Id, B = pc.Id, Type = "wired", PortA = "ether2", PortB = "eth0" }; st.SaveLink(wd);
Throws(() => st.SaveLink(new Link { A = r1.Id, B = pc.Id, Type = "wired", PortA = "ether3" }), "already have", "duplicate connection");

var v = new Vlan { SiteId = s1.Id, Vid = 20, Name = "Voice", DeviceId = r1.Id, Parent = "bridge1" }; st.SaveVlan(v);
Ok(v.IfName == "vlan20", "vlan interface name");
Throws(() => st.SaveVlan(new Vlan { SiteId = s1.Id, Vid = 20 }), "already exists", "duplicate vlan");

// permissions
var guest = Auth.NewUser("guest1", "password1", "guest"); st.Db.Users.Add(guest);
st.Me = guest; Throws(() => st.SaveSite(new Site { Name = "Z", SiteNumber = "9" }), "only view", "read-only cannot write");
st.Me = admin;
Throws(() => st.SetPermission(admin.Id, "read"), "your own", "cannot change own permission");
st.SetPermission(guest.Id, "write"); Ok(guest.Perm == "write", "permission changed");

// the file on disk has everything, and re-opens the same
var st2 = new Store(); st2.Open(file);
Ok(st2.Db.Sites.Count == 2 && st2.Db.Devices.Count == 3 && st2.Db.Links.Count == 2 && st2.Db.Vlans.Count == 1 && st2.Db.Users.Count == 2, $"auto-saved file reloads: {st2.Db.Sites.Count} {st2.Db.Devices.Count} {st2.Db.Links.Count} {st2.Db.Vlans.Count} {st2.Db.Users.Count}");
Ok(st2.Db.Changes.Count >= 10, "history kept: " + st2.Db.Changes.Count);
Ok(st.NextFreeIn(lan) == "192.168.10.3", "next free ip: " + st.NextFreeIn(lan));

// another program changes the file: the save is held back, nothing is overwritten
File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(5));
var before = File.ReadAllText(file);
st.SaveSite(new Site { Name = "Conflict", SiteNumber = "77" });
Ok(st.Conflict && st.SaveError != null && File.ReadAllText(file) == before, "outside change detected, file not overwritten");
Ok(st.SaveNow(true) && !st.Conflict && File.ReadAllText(file).Contains("Conflict"), "overwrite on request");
st.DeleteSite(st.Db.Sites.First(x => x.SiteNumber == "77").Id);
Ok(st.SaveError == null, "saves normally afterwards");

// --demo <file>: keep a copy of this database to look at in the app (admin / password1)
if (args.Length > 1 && args[0] == "--demo") File.Copy(file, args[1], true);

// cascades
st.DeleteDevice(r1.Id);
Ok(!st.Db.Links.Any() && !ap2.Addrs.Any(a => a.Link != null), "device delete removes its connections and WLAN ips");
Ok(v.DeviceId == "" && !lan.HostList.Any(h => h.Dev == r1.Id), "vlan unlinked, hosts removed");
st.DeleteSite(s2.Id);
Ok(!st.Db.Devices.Any(d => d.SiteId == s2.Id) && !st.Db.Networks.Any(n => n.SiteId == s2.Id), "site delete cascades");

// exports
var csv = st.DevicesCsv(false); Ok(csv.Contains("PC1") && !csv.Contains("Password"), "devices csv without passwords");
Ok(st.NetworksCsv().Contains("192.168.10.0/24"), "networks csv");

// unknown fields survive a round trip
var j = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
j["future"] = "keep me"; j["sites"]![0]!["mapX"] = 123; j["devices"]![0]!["tags"] = new JsonArray("a", "b");
File.WriteAllText(file, j.ToJsonString());
var st3 = new Store { }; st3.Open(file); st3.Me = admin; st3.SaveSite(st3.Db.Sites[0]);
var k = JsonNode.Parse(File.ReadAllText(file))!;
Ok((string)k["future"] == "keep me" && (int)k["sites"]![0]!["mapX"] == 123 && k["devices"]![0]!["tags"]!.AsArray().Count == 2, "unknown fields kept");
Directory.Delete(dir, true);

// --- a real database file, if given: load, save to a copy, compare
if (args.Length == 1)
{
    var orig = JsonNode.Parse(File.ReadAllText(args[0]));
    var db = DbIo.Load(args[0]);
    Console.WriteLine($"Real file: {db.Sites.Count} sites, {db.Networks.Count} networks, {db.Devices.Count} devices, {db.Links.Count} links, {db.Users.Count} users");
    var again = JsonNode.Parse(DbIo.Serialize(db));
    var diffs = new List<string>(); Compare(orig, again, "$", diffs);
    foreach (var d in diffs.Take(20)) Console.WriteLine("  diff " + d);
    Ok(diffs.Count == 0, $"real file round-trips unchanged ({diffs.Count} differences)");
}

Console.WriteLine($"\n{passes} passed, {fails} failed");
return fails == 0 ? 0 : 1;

static void Compare(JsonNode a, JsonNode b, string path, List<string> diffs)
{
    if (a is JsonObject oa && b is JsonObject ob)
    {
        foreach (var (key, va) in oa) { if (!ob.ContainsKey(key)) { if (va != null) diffs.Add($"{path}.{key} missing"); } else Compare(va, ob[key], $"{path}.{key}", diffs); }
        foreach (var (key, vb) in ob) if (!oa.ContainsKey(key) && vb != null && !(vb is JsonValue jv && jv.ToJsonString() is "\"\"" or "0" or "[]")) diffs.Add($"{path}.{key} added: {vb.ToJsonString()}");
    }
    else if (a is JsonArray aa && b is JsonArray ab)
    {
        if (aa.Count != ab.Count) diffs.Add($"{path} length {aa.Count} → {ab.Count}");
        else for (int i = 0; i < aa.Count; i++) Compare(aa[i], ab[i], $"{path}[{i}]", diffs);
    }
    else if (a?.ToJsonString() != b?.ToJsonString() && !(a is JsonValue && b is JsonValue && a.ToString() == b.ToString())) diffs.Add($"{path}: {a?.ToJsonString()} → {b?.ToJsonString()}");
}
