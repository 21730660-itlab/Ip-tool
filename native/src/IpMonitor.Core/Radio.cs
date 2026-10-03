using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IpMonitor.Core;

/// <summary>
/// Reads the radio details of wireless connections straight from the MikroTik devices (RouterOS API, with the
/// username and password saved on the device): frequency, band, channel width, SSID, wireless protocol
/// (NV2 / 802.11 / Nstreme), signal and distance. Supports /interface wireless (RouterOS 6 and 7) and /interface wifi.
/// </summary>
public static class Radio
{
    public record Peer(string Mac, string RadioName, double? Signal, double? Distance);

    /// <summary>The address used to reach a device's API (normally its IP; tests point it elsewhere).</summary>
    public static Func<Device, string> HostOf = DeviceMonitor.IpOf;

    /// <summary>What one device says about its wireless interface.</summary>
    public class End
    {
        public string Iface = "", Ssid = "", Band = "", Width = "", Proto = "", Mode = "", Mac = "", RadioName = "";
        public double? Freq, Distance;
        public List<Peer> Peers = new();
    }

    /// <summary>The values for a connection, and what happened at each end.</summary>
    public class LinkReading
    {
        public double? Freq, Signal, SignalA, SignalB, Distance;
        public string Band = "", Width = "", Ssid = "", Proto = "";
        public string NoteA = "", NoteB = "";
        public bool Ok => Freq != null || Signal != null || Ssid != "";
        public string Summary() => string.Join(" · ", new[]
        {
            Freq is double f ? $"{f:0} MHz" : "", Band == "" ? "" : Band + " GHz", Width == "" ? "" : Width + " MHz wide", Ssid == "" ? "" : "SSID " + Ssid,
            Proto == "" ? "" : Proto.ToUpperInvariant() == "NV2" ? "NV2" : Proto, Signal is double s ? $"signal {s:0} dBm" : "", Distance is double d ? $"{d:0.##} km" : ""
        }.Where(t => t != ""));
    }

    static double? Num(string v)
    {
        var m = Regex.Match(v ?? "", @"-?\d+(\.\d+)?");
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    public static string BandOf(string band, double? freq)
    {
        band = (band ?? "").ToLowerInvariant();
        if (band.StartsWith("2ghz") || band.StartsWith("2.4")) return "2.4";
        if (band.StartsWith("5ghz") || band == "5") return "5";
        if (band.Contains("60")) return "60";
        return freq switch { >= 2300 and < 2600 => "2.4", >= 4900 and < 6500 => "5", >= 50000 => "60", _ => "" };
    }

    /// <summary>"20/40/80mhz-Ceee" → "80"; "20mhz" → "20"; only the widths the web version knows.</summary>
    public static string WidthOf(string w)
    {
        var nums = Regex.Matches(w ?? "", @"\d+").Select(m => int.Parse(m.Value)).Where(n => n is 20 or 40 or 80 or 160 or 2160).ToList();
        return nums.Count == 0 ? "" : nums.Max().ToString();
    }

    public static string ProtoOf(string p)
    {
        p = (p ?? "").ToLowerInvariant();
        return p switch { "nv2" => "nv2", "802.11" => "802.11", "nstreme" => "nstreme", _ when p.StartsWith("nv2") => "nv2", _ => "" };
    }

    /// <summary>Reads one end. iface = the wireless interface of the connection (empty = the first one).</summary>
    public static async Task<End> ReadEndAsync(string host, string user, string pass, string iface, int port = 0)
    {
        using var api = await RouterOsApi.ConnectAsync(host, user, pass, port);
        try { return await ReadWirelessAsync(api, iface); }
        catch (RouterOsApi.ApiException e) when (e.Message.Contains("no such command", StringComparison.OrdinalIgnoreCase))
        {
            return await ReadWifiAsync(api, iface);   // RouterOS 7 "wifi" package
        }
    }

    static IEnumerable<string> ByName(string iface) => string.IsNullOrEmpty(iface) ? Array.Empty<string>() : new[] { "?name=" + iface };

    static async Task<End> ReadWirelessAsync(RouterOsApi api, string iface)
    {
        var e = new End();
        var cfg = (await api.RunAsync("/interface/wireless/print", ByName(iface))).Rows.FirstOrDefault()
                  ?? throw new RouterOsApi.ApiException($"No wireless interface {(string.IsNullOrEmpty(iface) ? "" : "“" + iface + "” ")}on this router.");
        e.Iface = cfg.GetValueOrDefault("name", iface); e.Ssid = cfg.GetValueOrDefault("ssid", ""); e.Mode = cfg.GetValueOrDefault("mode", "");
        e.Mac = cfg.GetValueOrDefault("mac-address", "").ToUpperInvariant(); e.RadioName = cfg.GetValueOrDefault("radio-name", "");
        e.Freq = Num(cfg.GetValueOrDefault("frequency")); e.Width = WidthOf(cfg.GetValueOrDefault("channel-width"));
        e.Proto = ProtoOf(cfg.GetValueOrDefault("wireless-protocol")); e.Distance = Num(cfg.GetValueOrDefault("distance"));
        var band = cfg.GetValueOrDefault("band", "");
        try
        {
            // the live values (frequency when set to auto, the protocol actually in use)
            var mon = (await api.RunAsync("/interface/wireless/monitor", new[] { "=numbers=" + e.Iface, "=once=" })).Rows.FirstOrDefault();
            if (mon != null)
            {
                if (Num(mon.GetValueOrDefault("frequency")) is double f) e.Freq = f;
                if (ProtoOf(mon.GetValueOrDefault("wireless-protocol")) is var p && p != "") e.Proto = p;
                if (mon.GetValueOrDefault("band") is string b && b != "") band = b;
                if (WidthOf(mon.GetValueOrDefault("channel-width")) is var w && w != "") e.Width = w;
                if (mon.GetValueOrDefault("ssid") is string ss && ss != "") e.Ssid = ss;
                if (Num(mon.GetValueOrDefault("signal-strength")) is double st && mon.ContainsKey("signal-strength"))   // station mode: its own signal
                    e.Peers.Add(new Peer(mon.GetValueOrDefault("mac-address", "").ToUpperInvariant(), mon.GetValueOrDefault("radio-name", ""), st, Num(mon.GetValueOrDefault("distance"))));
            }
        }
        catch (RouterOsApi.ApiException) { /* monitor is optional */ }
        e.Band = BandOf(band, e.Freq);
        var reg = await api.RunAsync("/interface/wireless/registration-table/print", new[] { "?interface=" + e.Iface });
        foreach (var r in reg.Rows)
            e.Peers.Add(new Peer(r.GetValueOrDefault("mac-address", "").ToUpperInvariant(), r.GetValueOrDefault("radio-name", ""), Num(r.GetValueOrDefault("signal-strength")), Num(r.GetValueOrDefault("distance"))));
        return e;
    }

    static async Task<End> ReadWifiAsync(RouterOsApi api, string iface)
    {
        var e = new End();
        var cfg = (await api.RunAsync("/interface/wifi/print", ByName(iface))).Rows.FirstOrDefault()
                  ?? throw new RouterOsApi.ApiException($"No wireless interface {(string.IsNullOrEmpty(iface) ? "" : "“" + iface + "” ")}on this router.");
        e.Iface = cfg.GetValueOrDefault("name", iface);
        e.Ssid = cfg.GetValueOrDefault("configuration.ssid", cfg.GetValueOrDefault("ssid", ""));
        e.Mode = cfg.GetValueOrDefault("configuration.mode", "");
        e.Mac = cfg.GetValueOrDefault("mac-address", "").ToUpperInvariant();
        e.Freq = Num(cfg.GetValueOrDefault("channel.frequency"));
        e.Width = WidthOf(cfg.GetValueOrDefault("channel.width"));
        var band = cfg.GetValueOrDefault("channel.band", "");
        e.Proto = "802.11";
        try
        {
            var mon = (await api.RunAsync("/interface/wifi/monitor", new[] { "=numbers=" + e.Iface, "=once=" })).Rows.FirstOrDefault();
            if (mon != null && Regex.Match(mon.GetValueOrDefault("channel", ""), @"^(\d+)") is { Success: true } m) e.Freq = double.Parse(m.Groups[1].Value);
            if (mon != null && WidthOf(mon.GetValueOrDefault("channel", "")) is var w && w != "") e.Width = w;
        }
        catch (RouterOsApi.ApiException) { }
        e.Band = BandOf(band, e.Freq);
        var reg = await api.RunAsync("/interface/wifi/registration-table/print", new[] { "?interface=" + e.Iface });
        foreach (var r in reg.Rows) e.Peers.Add(new Peer(r.GetValueOrDefault("mac-address", "").ToUpperInvariant(), "", Num(r.GetValueOrDefault("signal")), null));
        return e;
    }

    /// <summary>The peer of 'me' that is the other end (by MAC address, radio name or device name; or the only one).</summary>
    static Peer PeerOf(End me, End other, Device otherDev)
    {
        if (me == null || me.Peers.Count == 0) return null;
        if (other != null && other.Mac != "" && me.Peers.FirstOrDefault(p => p.Mac == other.Mac) is Peer byMac) return byMac;
        var names = new[] { other?.RadioName, otherDev?.Name }.Where(n => !string.IsNullOrEmpty(n)).ToList();
        if (me.Peers.FirstOrDefault(p => names.Any(n => string.Equals(p.RadioName, n, StringComparison.OrdinalIgnoreCase))) is Peer byName) return byName;
        return me.Peers.Count == 1 ? me.Peers[0] : null;
    }

    /// <summary>Puts the two ends together into the values of the connection.</summary>
    public static LinkReading Combine(End a, End b, Device devA, Device devB)
    {
        var r = new LinkReading();
        // the access point / bridge side has the real channel; otherwise take whichever end answered
        var ap = new[] { a, b }.FirstOrDefault(e => e != null && (e.Mode.Contains("bridge") || e.Mode.StartsWith("ap"))) ?? a ?? b;
        foreach (var e in new[] { ap, a, b }.Where(e => e != null))
        {
            r.Freq ??= e.Freq; if (r.Band == "") r.Band = e.Band; if (r.Width == "") r.Width = e.Width;
            if (r.Ssid == "") r.Ssid = e.Ssid; if (r.Proto == "") r.Proto = e.Proto;
        }
        var pa = PeerOf(a, b, devB); var pb = PeerOf(b, a, devA);
        r.SignalA = pa?.Signal; r.SignalB = pb?.Signal;
        var sigs = new[] { r.SignalA, r.SignalB }.Where(x => x != null).Select(x => x.Value).ToList();
        r.Signal = sigs.Count > 0 ? sigs.Min() : null;   // the weaker side decides how good the link is
        var dists = new[] { pa?.Distance, pb?.Distance, a?.Distance, b?.Distance }.Where(x => x is > 0).Select(x => x.Value).ToList();
        r.Distance = dists.Count > 0 ? dists.Max() : null;
        return r;
    }

    /// <summary>Reads both ends of a wireless connection with the devices' saved credentials.</summary>
    public static async Task<LinkReading> ReadLinkAsync(Store s, Link l)
    {
        var A = s.DevById(l.A); var B = s.DevById(l.B);
        async Task<(End e, string note)> One(Device d, string port)
        {
            if (d == null) return (null, "device missing");
            if (!d.IsMikroTik) return (null, $"{d.Name}: not a MikroTik");
            var ip = HostOf(d);
            if (ip == "") return (null, $"{d.Name}: no IP address");
            if (string.IsNullOrEmpty(d.User)) return (null, $"{d.Name}: no username saved on the device");
            int port0 = d.Extra != null && d.Extra.TryGetValue("apiPort", out var ap) && ap.ValueKind == JsonValueKind.Number ? ap.GetInt32() : 0;
            try { return (await ReadEndAsync(ip, d.User, d.Pass, port, port0), $"{d.Name}: read"); }
            catch (RouterOsApi.ApiException e) { return (null, $"{d.Name}: {e.Message}"); }
            catch (Exception e) { return (null, $"{d.Name}: {e.Message}"); }
        }
        var ta = One(A, l.PortA); var tb = One(B, l.PortB);
        var (ea, na) = await ta; var (eb, nb) = await tb;
        var r = Combine(ea, eb, A, B);
        r.NoteA = na; r.NoteB = nb;
        return r;
    }
}
