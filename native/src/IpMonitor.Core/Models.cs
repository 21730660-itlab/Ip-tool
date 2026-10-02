using System.Text.Json;
using System.Text.Json.Serialization;

namespace IpMonitor.Core;

/// <summary>
/// The data model of ip-monitor-db.json, the same file the web version of IP Monitor uses.
/// Every class keeps fields it does not know in <see cref="Entity.Extra"/>, so a file written by the
/// web app (map positions, tags, wireless details, config backups…) survives a save from this app untouched.
/// </summary>
public abstract class Entity
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("createdAt"), JsonConverter(typeof(LooseString))] public string CreatedAt { get; set; }
    [JsonPropertyName("updatedAt"), JsonConverter(typeof(LooseString))] public string UpdatedAt { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }

    public static string NewId() => Guid.NewGuid().ToString();
    public static string Now() => DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
}

public class Site : Entity
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("siteNumber"), JsonConverter(typeof(LooseString))] public string SiteNumber { get; set; } = "";
    [JsonPropertyName("location")] public string Location { get; set; } = "";
    [JsonPropertyName("contact")] public string Contact { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; }
    public override string ToString() => $"#{SiteNumber} {Name}";
}

public class Host
{
    [JsonPropertyName("id")] public string Id { get; set; } = Entity.NewId();
    [JsonPropertyName("num")] public int Num { get; set; }
    [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("mac")] public string Mac { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    /// <summary>Set when the host is a device's own address (kept in sync with the device).</summary>
    [JsonPropertyName("dev"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Dev { get; set; }
    [JsonPropertyName("iface"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Iface { get; set; }
    [JsonPropertyName("aname"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string AutoName { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
    public Host Clone() => (Host)MemberwiseClone();
}

public class Network : Entity
{
    [JsonPropertyName("siteId")] public string SiteId { get; set; } = "";
    [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "subnet";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("vlan"), JsonConverter(typeof(LooseString))] public string Vlan { get; set; } = "";
    [JsonPropertyName("gateway")] public string Gateway { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; }
    [JsonPropertyName("hostList")] public List<Host> HostList { get; set; } = new();
    [JsonPropertyName("hosts")] public int Hosts { get; set; }
}

public class Uplink
{
    [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("bridge")] public string Bridge { get; set; } = "";
}

public class Bridge
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("ports")] public List<string> Ports { get; set; } = new();
}

public class Addr
{
    [JsonPropertyName("id")] public string Id { get; set; } = Entity.NewId();
    [JsonPropertyName("iface")] public string Iface { get; set; } = "";
    [JsonPropertyName("address")] public string Address { get; set; } = "";
    [JsonPropertyName("comment")] public string Comment { get; set; } = "";
    /// <summary>Set when the address belongs to a wireless connection (WLAN IP typed on the connection).</summary>
    [JsonPropertyName("link"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string Link { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
}

public class Device : Entity
{
    [JsonPropertyName("siteId")] public string SiteId { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "router";
    [JsonPropertyName("vendor")] public string Vendor { get; set; } = "mikrotik";
    [JsonPropertyName("uplink")] public Uplink Uplink { get; set; }
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "";
    [JsonPropertyName("user")] public string User { get; set; } = "";
    [JsonPropertyName("pass")] public string Pass { get; set; } = "";
    [JsonPropertyName("winbox"), JsonConverter(typeof(LooseString))] public string Winbox { get; set; } = "";
    [JsonPropertyName("wproto")] public string WProto { get; set; } = "";
    [JsonPropertyName("wsec")] public string WSec { get; set; } = "";
    [JsonPropertyName("psk")] public string Psk { get; set; } = "";
    [JsonPropertyName("ip")] public string Ip { get; set; } = "";
    [JsonPropertyName("mac")] public string Mac { get; set; } = "";
    [JsonPropertyName("ros")] public string Ros { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("func")] public string Func { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; }
    [JsonPropertyName("ports")] public List<string> Ports { get; set; } = new();
    [JsonPropertyName("bridges")] public List<Bridge> Bridges { get; set; } = new();
    [JsonPropertyName("addrs")] public List<Addr> Addrs { get; set; } = new();

    [JsonIgnore] public bool IsMikroTik => Vendor != "other";
    [JsonIgnore] public bool HasWifi => Type == "wireless" || Type == "both";
    public override string ToString() => Name;
}

public class Link : Entity
{
    [JsonPropertyName("a")] public string A { get; set; } = "";
    [JsonPropertyName("b")] public string B { get; set; } = "";
    [JsonPropertyName("type")] public string Type { get; set; } = "wired";
    [JsonPropertyName("portA")] public string PortA { get; set; } = "";
    [JsonPropertyName("portB")] public string PortB { get; set; } = "";
    [JsonPropertyName("ipA")] public string IpA { get; set; } = "";
    [JsonPropertyName("ipB")] public string IpB { get; set; } = "";
    [JsonPropertyName("subnet")] public string Subnet { get; set; } = "";
    [JsonPropertyName("ssid")] public string Ssid { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
}

public class Vlan : Entity
{
    [JsonPropertyName("siteId")] public string SiteId { get; set; } = "";
    [JsonPropertyName("vid"), JsonConverter(typeof(LooseInt))] public int Vid { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("parent")] public string Parent { get; set; } = "";
    [JsonPropertyName("ifname")] public string IfName { get; set; } = "";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "";
    [JsonPropertyName("status")] public string Status { get; set; }
}

public class Change
{
    [JsonPropertyName("id")] public string Id { get; set; } = Entity.NewId();
    [JsonPropertyName("at")] public string At { get; set; } = Entity.Now();
    [JsonPropertyName("by")] public string By { get; set; } = "";
    [JsonPropertyName("act")] public string Act { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("ref")] public string Ref { get; set; } = "";
    [JsonPropertyName("label")] public string Label { get; set; } = "";
    [JsonPropertyName("siteId")] public string SiteId { get; set; } = "";
    [JsonPropertyName("lines")] public List<string> Lines { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
}

public class User
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("username")] public string Username { get; set; } = "";
    [JsonPropertyName("role")] public string Role { get; set; } = "guest";
    [JsonPropertyName("perm")] public string Perm { get; set; } = "read";
    [JsonPropertyName("salt")] public string Salt { get; set; } = "";
    [JsonPropertyName("hash")] public string Hash { get; set; } = "";
    [JsonPropertyName("iter"), JsonConverter(typeof(LooseInt))] public int Iter { get; set; } = 150000;
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; }
    [JsonPropertyName("updatedAt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string UpdatedAt { get; set; }
    [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
    public override string ToString() => Username;
}

/// <summary>The whole database file.</summary>
public class DbFile
{
    [JsonPropertyName("app")] public string App { get; set; } = "ip-monitor";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "database";
    [JsonPropertyName("version")] public int Version { get; set; } = 4;
    [JsonPropertyName("savedAt")] public string SavedAt { get; set; }
    [JsonPropertyName("savedBy")] public string SavedBy { get; set; }
    [JsonPropertyName("sites")] public List<Site> Sites { get; set; } = new();
    [JsonPropertyName("networks")] public List<Network> Networks { get; set; } = new();
    [JsonPropertyName("devices")] public List<Device> Devices { get; set; } = new();
    [JsonPropertyName("links")] public List<Link> Links { get; set; } = new();
    [JsonPropertyName("vlans")] public List<Vlan> Vlans { get; set; } = new();
    /// <summary>RouterOS config backups: not edited by this app, kept exactly as they are.</summary>
    [JsonPropertyName("configs")] public List<JsonElement> Configs { get; set; } = new();
    [JsonPropertyName("changes")] public List<Change> Changes { get; set; } = new();
    [JsonPropertyName("users")] public List<User> Users { get; set; } = new();
    [JsonExtensionData] public Dictionary<string, JsonElement> Extra { get; set; }
}

/// <summary>Reads a string that may be stored as a number (or null) by older files.</summary>
public class LooseString : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => r.TokenType switch
    {
        JsonTokenType.String => r.GetString(),
        JsonTokenType.Number => r.TryGetInt64(out var l) ? l.ToString() : r.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
        JsonTokenType.True => "true",
        JsonTokenType.False => "false",
        _ => SkipNull(ref r)
    };
    static string SkipNull(ref Utf8JsonReader r) { if (r.TokenType != JsonTokenType.Null) r.Skip(); return null; }
    public override void Write(Utf8JsonWriter w, string v, JsonSerializerOptions o) { if (v == null) w.WriteNullValue(); else w.WriteStringValue(v); }
}

/// <summary>Reads an int that may be stored as a string.</summary>
public class LooseInt : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o)
    {
        if (r.TokenType == JsonTokenType.Number) return r.TryGetInt32(out var i) ? i : (int)r.GetDouble();
        if (r.TokenType == JsonTokenType.String && int.TryParse(r.GetString(), out var j)) return j;
        if (r.TokenType != JsonTokenType.Null && r.TokenType != JsonTokenType.String) r.Skip();
        return 0;
    }
    public override void Write(Utf8JsonWriter w, int v, JsonSerializerOptions o) => w.WriteNumberValue(v);
}
