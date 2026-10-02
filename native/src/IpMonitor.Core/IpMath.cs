using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.RegularExpressions;

namespace IpMonitor.Core;

/// <summary>An address typed by a person: version, address and the prefix if one was typed.</summary>
public record RawIp(int V, int Bits, BigInteger Addr, int? Prefix);

/// <summary>An address or subnet, fully worked out (same rules as the web version).</summary>
public class IpEntry
{
    public int V, Bits, Prefix;
    public BigInteger Addr, Start, End, Size, Usable, FirstUsable, LastUsable;
    public string Kind => Prefix == Bits ? "ip" : "subnet";
    public bool IsSubnet => Prefix != Bits;
    public string Text => IsSubnet ? IpMath.Format(V, Start) + "/" + Prefix : IpMath.Format(V, Addr);
    public string Network => IpMath.Format(V, Start);
    public string FirstU => IpMath.Format(V, FirstUsable);
    public string LastU => IpMath.Format(V, LastUsable);
    public string Mask => V == 4 ? IpMath.Format(4, ((BigInteger.One << 32) - 1) ^ ((BigInteger.One << (32 - Prefix)) - 1)) : "";
    public override string ToString() => Text;
}

public static class IpMath
{
    static readonly BigInteger V4Max = (BigInteger.One << 32) - 1;

    public static BigInteger? ParseV4(string t)
    {
        var p = t.Split('.');
        if (p.Length != 4) return null;
        BigInteger n = 0;
        foreach (var x in p)
        {
            if (!Regex.IsMatch(x, @"^\d{1,3}$")) return null;
            int v = int.Parse(x);
            if (v > 255) return null;
            n = (n << 8) | v;
        }
        return n;
    }

    public static BigInteger? ParseV6(string t)
    {
        if (!t.Contains(':') || t.Contains('%')) return null;
        if (!IPAddress.TryParse(t, out var ip) || ip.AddressFamily != AddressFamily.InterNetworkV6) return null;
        var b = ip.GetAddressBytes();
        BigInteger n = 0;
        foreach (var x in b) n = (n << 8) | x;
        return n;
    }

    public static string Format(int v, BigInteger n)
    {
        if (v == 4) return string.Join(".", new[] { 24, 16, 8, 0 }.Select(s => ((n >> s) & 255).ToString()));
        var bytes = new byte[16];
        for (int i = 15; i >= 0; i--) { bytes[i] = (byte)(n & 255); n >>= 8; }
        return new IPAddress(bytes).ToString().ToLowerInvariant();
    }

    public static int? MaskToPrefix(string m)
    {
        var n = ParseV4(m);
        if (n == null) return null;
        var inv = ~n.Value & V4Max;
        if ((inv & (inv + 1)) != 0) return null;
        int ones = 0; for (var x = inv; x > 0; x >>= 1) ones++;
        return 32 - ones;
    }

    /// <summary>"10.0.0.1", "10.0.0.0/24", "10.0.0.0 255.255.255.0", "2001:db8::/64" … ; null when not valid.</summary>
    public static RawIp ParseRaw(string raw)
    {
        var t = Regex.Replace((raw ?? "").Trim(), @"\s*/\s*", "/");
        var words = t.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 2 && !t.Contains('/')) t = words[0] + "/" + words[1];
        else if (words.Length > 1) return null;
        t = t.TrimEnd('.');
        if (t.Length == 0) return null;
        var parts = t.Split('/');
        if (parts.Length > 2) return null;
        var addrS = parts[0].TrimEnd('.');
        int v = 4, bits = 32;
        var addr = ParseV4(addrS);
        if (addr == null) { addr = ParseV6(addrS); v = 6; bits = 128; if (addr == null) return null; }
        int? prefix = null;
        if (parts.Length == 2)
        {
            var pre = parts[1];
            if (Regex.IsMatch(pre, @"^\d{1,3}$")) { prefix = int.Parse(pre); if (prefix > bits) return null; }
            else if (v == 4 && pre.Contains('.')) { prefix = MaskToPrefix(pre); if (prefix == null) return null; }
            else return null;
        }
        return new RawIp(v, bits, addr.Value, prefix);
    }

    public static IpEntry Analyze(int v, BigInteger addr, int prefix)
    {
        int bits = v == 4 ? 32 : 128;
        var full = (BigInteger.One << bits) - 1;
        var size = BigInteger.One << (bits - prefix);
        var mask = (full << (bits - prefix)) & full;
        var start = addr & mask; var end = start + size - 1;
        var e = new IpEntry { V = v, Bits = bits, Prefix = prefix, Addr = addr, Start = start, End = end, Size = size };
        if (v == 4 && prefix < 31) { e.Usable = size - 2; e.FirstUsable = start + 1; e.LastUsable = end - 1; }
        else { e.Usable = size; e.FirstUsable = start; e.LastUsable = end; }
        return e;
    }

    /// <summary>An address without prefix is a single IP (/32 or /128).</summary>
    public static IpEntry Parse(string raw)
    {
        var p = ParseRaw(raw);
        return p == null ? null : Analyze(p.V, p.Addr, p.Prefix ?? p.Bits);
    }

    /// <summary>The address part only ("10.0.0.1/24" → "10.0.0.1").</summary>
    public static string IpOf(string addr)
    {
        var p = ParseRaw(addr);
        return p == null ? "" : Format(p.V, p.Addr);
    }

    public static bool Overlaps(IpEntry a, IpEntry b) => a.V == b.V && a.Start <= b.End && b.Start <= a.End;

    public static bool Contains(IpEntry net, IpEntry ip) => net.V == ip.V && ip.Addr >= net.Start && ip.Addr <= net.End;

    /// <summary>Can this address be a host of the subnet (not its network or broadcast address)?</summary>
    public static bool HostRangeOk(IpEntry e, BigInteger ip)
    {
        if (e.V == 4 && e.Prefix < 31) return ip > e.Start && ip < e.End;
        return ip >= e.Start && ip <= e.End;
    }

    /// <summary>The first usable address not in <paramref name="used"/>; "" when the subnet is full.</summary>
    public static string NextFree(IpEntry e, ISet<string> used)
    {
        if (!e.IsSubnet) return used.Contains(e.Text) ? "" : e.Text;
        var a = e.FirstUsable;
        for (int i = 0; i < 200000 && a <= e.LastUsable; i++, a++)
        {
            var t = Format(e.V, a);
            if (!used.Contains(t)) return t;
        }
        return "";
    }

    /// <summary>"aa-bb-cc-dd-ee-ff" → "AA:BB:CC:DD:EE:FF"; "" for empty; null when not a MAC.</summary>
    public static string NormMac(string m)
    {
        m = (m ?? "").Trim();
        if (m.Length == 0) return "";
        var hex = Regex.Replace(m, "[^0-9a-fA-F]", "");
        if (hex.Length != 12 || !Regex.IsMatch(m, @"^[0-9A-Fa-f:\-. ]+$")) return null;
        hex = hex.ToUpperInvariant();
        return string.Join(":", Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)));
    }

    // ------------------------------------------------------------------ subnet calculator
    public static string Wildcard(IpEntry e) => e.V == 4 ? Format(4, ((BigInteger.One << (32 - e.Prefix)) - 1)) : "";
    public static string V4Class(BigInteger a)
    {
        var o = (int)(a >> 24);
        return o < 128 ? "A" : o < 192 ? "B" : o < 224 ? "C" : o < 240 ? "D (multicast)" : "E (reserved)";
    }
    /// <summary>Private, public, loopback, link-local… of the address range.</summary>
    public static string Scope(IpEntry e)
    {
        bool In(string net) { var n = Parse(net); return n.V == e.V && e.Start >= n.Start && e.End <= n.End; }
        if (e.V == 4)
        {
            if (In("10.0.0.0/8") || In("172.16.0.0/12") || In("192.168.0.0/16")) return "Private (RFC 1918)";
            if (In("100.64.0.0/10")) return "Carrier-grade NAT (RFC 6598)";
            if (In("127.0.0.0/8")) return "Loopback";
            if (In("169.254.0.0/16")) return "Link-local (APIPA)";
            if (In("224.0.0.0/4")) return "Multicast";
            if (In("240.0.0.0/4")) return "Reserved";
            if (In("0.0.0.0/8")) return "This network";
            if (In("192.0.2.0/24") || In("198.51.100.0/24") || In("203.0.113.0/24")) return "Documentation";
            return e.Prefix == 0 ? "Everything" : "Public";
        }
        if (In("fc00::/7")) return "Unique local (private)";
        if (In("fe80::/10")) return "Link-local";
        if (In("ff00::/8")) return "Multicast";
        if (In("2001:db8::/32")) return "Documentation";
        if (In("::1/128")) return "Loopback";
        if (In("2000::/3")) return "Global unicast (public)";
        return "Other";
    }
    /// <summary>The smallest subnet (largest prefix) with at least this many usable addresses; null if impossible.</summary>
    public static int? PrefixForHosts(int v, BigInteger hosts)
    {
        int bits = v == 4 ? 32 : 128;
        for (int p = bits; p >= 0; p--)
        {
            var size = BigInteger.One << (bits - p);
            var usable = v == 4 && p < 31 ? size - 2 : size;
            if (usable >= hosts) return p;
        }
        return null;
    }
    public static string Expanded6(BigInteger a) => string.Join(":", Enumerable.Range(0, 8).Select(i => ((a >> (112 - i * 16)) & 0xFFFF).ToString("x4")));
    public static string Binary(int v, BigInteger a)
    {
        int bits = v == 4 ? 32 : 128, g = v == 4 ? 8 : 16;
        var s = new System.Text.StringBuilder();
        for (int i = bits - 1; i >= 0; i--) { s.Append(((a >> i) & 1) == 1 ? '1' : '0'); if (i % g == 0 && i > 0) s.Append(v == 4 ? '.' : ':'); }
        return s.ToString();
    }

    public static string CountText(BigInteger n) => n < BigInteger.Pow(10, 15) ? n.ToString("N0") : "2^" + (int)Math.Round(BigInteger.Log(n, 2));
}
