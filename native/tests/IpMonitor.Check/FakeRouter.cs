using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using IpMonitor.Core;

/// <summary>A pretend MikroTik that speaks the RouterOS API protocol, for testing without real hardware.</summary>
public sealed class FakeRouter : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;
    public string User = "admin", Password = "secret";
    public bool OldLogin;   // RouterOS before 6.43: MD5 challenge
    public Dictionary<string, string> Wireless = new(), Monitor = new();
    public List<Dictionary<string, string>> Registrations = new();
    public List<string> Commands = new();

    public FakeRouter() { listener.Start(); _ = Serve(); }

    async Task Serve()
    {
        while (true)
        {
            TcpClient c;
            try { c = await listener.AcceptTcpClientAsync(); } catch { return; }
            _ = Handle(c);
        }
    }

    async Task Handle(TcpClient c)
    {
        using var _ = c; var s = c.GetStream();
        var chal = RandomNumberGenerator.GetBytes(16); bool logged = false;
        try
        {
            while (true)
            {
                var words = await Read(s); if (words.Count == 0) continue;
                var cmd = words[0]; Commands.Add(cmd);
                var a = words.Skip(1).Where(w => w.StartsWith('=')).ToDictionary(w => w[1..w.IndexOf('=', 1)], w => w[(w.IndexOf('=', 1) + 1)..]);
                var q = words.Skip(1).Where(w => w.StartsWith('?')).Select(w => w[1..]).ToList();
                if (cmd == "/login")
                {
                    if (OldLogin && !a.ContainsKey("response")) { await Send(s, "!done", "=ret=" + Convert.ToHexString(chal).ToLowerInvariant()); continue; }
                    bool ok = a.GetValueOrDefault("name") == User && (OldLogin
                        ? a.GetValueOrDefault("response") == "00" + Convert.ToHexString(MD5.HashData(new byte[] { 0 }.Concat(Encoding.UTF8.GetBytes(Password)).Concat(chal).ToArray())).ToLowerInvariant()
                        : a.GetValueOrDefault("password") == Password);
                    if (ok) { logged = true; await Send(s, "!done"); }
                    else { await Send(s, "!trap", "=message=invalid user name or password (6)"); await Send(s, "!done"); }
                    continue;
                }
                if (!logged) { await Send(s, "!fatal", "not logged in"); return; }
                IEnumerable<Dictionary<string, string>> rows = cmd switch
                {
                    "/interface/wireless/print" => q.All(x => x.StartsWith("name=") ? Wireless.GetValueOrDefault("name") == x[5..] : true) ? new[] { Wireless } : Array.Empty<Dictionary<string, string>>(),
                    "/interface/wireless/monitor" => new[] { Monitor },
                    "/interface/wireless/registration-table/print" => Registrations,
                    _ => null
                };
                if (rows == null) { await Send(s, "!trap", "=message=no such command prefix"); await Send(s, "!done"); continue; }
                foreach (var r in rows) await Send(s, new[] { "!re" }.Concat(r.Select(kv => $"={kv.Key}={kv.Value}")).ToArray());
                await Send(s, "!done");
            }
        }
        catch { }
    }

    static async Task Send(Stream s, params string[] words)
    {
        var ms = new MemoryStream();
        foreach (var w in words) { var b = Encoding.UTF8.GetBytes(w); RouterOsApi.WriteLen(ms, b.Length); ms.Write(b); }
        ms.WriteByte(0); await s.WriteAsync(ms.ToArray());
    }

    static async Task<List<string>> Read(Stream s)
    {
        var res = new List<string>();
        while (true)
        {
            int c = await B(s); int n;
            if ((c & 0x80) == 0) n = c;
            else if ((c & 0xC0) == 0x80) n = ((c & 0x3F) << 8) | await B(s);
            else n = ((c & 0x1F) << 16) | (await B(s) << 8) | await B(s);
            if (n == 0) return res;
            var buf = new byte[n]; int got = 0; while (got < n) got += await s.ReadAsync(buf.AsMemory(got));
            res.Add(Encoding.UTF8.GetString(buf));
        }
    }
    static async Task<int> B(Stream s) { var b = new byte[1]; if (await s.ReadAsync(b) == 0) throw new IOException(); return b[0]; }

    public void Dispose() => listener.Stop();
}
