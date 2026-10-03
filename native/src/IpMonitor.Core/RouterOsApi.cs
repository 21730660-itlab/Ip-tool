using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace IpMonitor.Core;

/// <summary>
/// A small client for the MikroTik RouterOS API (the "api" service: port 8728, or 8729 with TLS).
/// It logs in with the device's username and password and runs read-only "print" / "monitor" commands.
/// </summary>
public sealed class RouterOsApi : IDisposable
{
    public class ApiException : Exception { public ApiException(string m) : base(m) { } }

    readonly TcpClient tcp;
    readonly Stream s;
    public int Port { get; }

    RouterOsApi(TcpClient c, Stream st, int port) { tcp = c; s = st; Port = port; }

    /// <summary>Connects (API port, or the TLS port if the plain one is closed) and logs in.</summary>
    public static async Task<RouterOsApi> ConnectAsync(string host, string user, string password, int port = 0, int timeoutMs = 5000)
    {
        var ports = port > 0 ? new[] { port } : new[] { 8728, 8729 };
        Exception last = null;
        foreach (var p in ports)
        {
            var c = new TcpClient { ReceiveTimeout = timeoutMs, SendTimeout = timeoutMs };
            try
            {
                using (var cts = new CancellationTokenSource(timeoutMs)) await c.ConnectAsync(host, p, cts.Token);
                Stream st = c.GetStream();
                if (p == 8729)
                {
                    var ssl = new SslStream(st, false, (_, _, _, _) => true);   // routers use self-signed certificates
                    using var cts = new CancellationTokenSource(timeoutMs);
                    await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host, RemoteCertificateValidationCallback = (_, _, _, _) => true }, cts.Token);
                    st = ssl;
                }
                st.ReadTimeout = timeoutMs; st.WriteTimeout = timeoutMs;
                var api = new RouterOsApi(c, st, p);
                await api.LoginAsync(user ?? "", password ?? "");
                return api;
            }
            catch (ApiException) { c.Dispose(); throw; }   // wrong password: no point trying the other port
            catch (Exception e) { c.Dispose(); last = e; }
        }
        throw new ApiException($"Can't reach the RouterOS API on {host} (port {string.Join(" / ", ports)}): {Short(last)}. On the router: IP → Services → enable \"api\".");
    }

    static string Short(Exception e) => e switch
    {
        null => "no answer",
        OperationCanceledException => "no answer (timeout)",
        SocketException se => se.SocketErrorCode switch { SocketError.ConnectionRefused => "connection refused", SocketError.TimedOut => "no answer (timeout)", SocketError.HostUnreachable or SocketError.NetworkUnreachable => "not reachable", _ => se.Message },
        _ => e.Message
    };

    async Task LoginAsync(string user, string password)
    {
        // RouterOS 6.43 and newer: name + password in one go
        var r = await RunAsync("/login", new[] { "=name=" + user, "=password=" + password });
        var ret = r.Done.GetValueOrDefault("ret");
        if (!string.IsNullOrEmpty(ret))
        {
            // older RouterOS: challenge / response with MD5
            var chal = Convert.FromHexString(ret);
            var data = new byte[] { 0 }.Concat(Encoding.UTF8.GetBytes(password)).Concat(chal).ToArray();
            var resp = "00" + Convert.ToHexString(MD5.HashData(data)).ToLowerInvariant();
            await RunAsync("/login", new[] { "=name=" + user, "=response=" + resp });
        }
    }

    public record Reply(List<Dictionary<string, string>> Rows, Dictionary<string, string> Done);

    /// <summary>Runs a command; each "!re" row becomes a dictionary. A "!trap" (error) throws.</summary>
    public async Task<Reply> RunAsync(string command, IEnumerable<string> args = null)
    {
        var words = new List<string> { command }; if (args != null) words.AddRange(args);
        await WriteSentenceAsync(words);
        var rows = new List<Dictionary<string, string>>(); string trap = null;
        while (true)
        {
            var sent = await ReadSentenceAsync();
            if (sent.Count == 0) continue;
            var kind = sent[0];
            var attrs = new Dictionary<string, string>();
            foreach (var w in sent.Skip(1))
                if (w.StartsWith('='))
                {
                    var i = w.IndexOf('=', 1);
                    if (i > 0) attrs[w[1..i]] = w[(i + 1)..];
                }
            switch (kind)
            {
                case "!re": rows.Add(attrs); break;
                case "!trap": trap ??= attrs.GetValueOrDefault("message", "error"); break;
                case "!fatal": throw new ApiException("The router closed the connection: " + string.Join(" ", sent.Skip(1)));
                case "!done":
                    if (trap != null) throw new ApiException(trap.Contains("invalid user name or password", StringComparison.OrdinalIgnoreCase) || trap.Contains("cannot log in", StringComparison.OrdinalIgnoreCase)
                        ? "Wrong username or password for the router (check the device's Login fields)." : trap);
                    return new Reply(rows, attrs);
            }
        }
    }

    // ------------------------------------------------------------------ wire format
    async Task WriteSentenceAsync(IEnumerable<string> words)
    {
        var ms = new MemoryStream();
        foreach (var w in words) { var b = Encoding.UTF8.GetBytes(w); WriteLen(ms, b.Length); ms.Write(b); }
        ms.WriteByte(0);
        await s.WriteAsync(ms.ToArray());
        await s.FlushAsync();
    }

    public static void WriteLen(Stream ms, int n)
    {
        if (n < 0x80) ms.WriteByte((byte)n);
        else if (n < 0x4000) { n |= 0x8000; ms.WriteByte((byte)(n >> 8)); ms.WriteByte((byte)n); }
        else if (n < 0x200000) { n |= 0xC00000; ms.WriteByte((byte)(n >> 16)); ms.WriteByte((byte)(n >> 8)); ms.WriteByte((byte)n); }
        else if (n < 0x10000000) { n |= unchecked((int)0xE0000000); ms.WriteByte((byte)(n >> 24)); ms.WriteByte((byte)(n >> 16)); ms.WriteByte((byte)(n >> 8)); ms.WriteByte((byte)n); }
        else { ms.WriteByte(0xF0); ms.WriteByte((byte)(n >> 24)); ms.WriteByte((byte)(n >> 16)); ms.WriteByte((byte)(n >> 8)); ms.WriteByte((byte)n); }
    }

    async Task<byte> ReadByteAsync()
    {
        var b = new byte[1];
        if (await s.ReadAsync(b) == 0) throw new ApiException("The router closed the connection.");
        return b[0];
    }

    async Task<int> ReadLenAsync()
    {
        int c = await ReadByteAsync();
        if ((c & 0x80) == 0) return c;
        if ((c & 0xC0) == 0x80) return ((c & 0x3F) << 8) | await ReadByteAsync();
        if ((c & 0xE0) == 0xC0) return ((c & 0x1F) << 16) | (await ReadByteAsync() << 8) | await ReadByteAsync();
        if ((c & 0xF0) == 0xE0) return ((c & 0x0F) << 24) | (await ReadByteAsync() << 16) | (await ReadByteAsync() << 8) | await ReadByteAsync();
        return (await ReadByteAsync() << 24) | (await ReadByteAsync() << 16) | (await ReadByteAsync() << 8) | await ReadByteAsync();
    }

    async Task<List<string>> ReadSentenceAsync()
    {
        var words = new List<string>();
        while (true)
        {
            var n = await ReadLenAsync();
            if (n == 0) return words;
            var buf = new byte[n]; int got = 0;
            while (got < n) { var k = await s.ReadAsync(buf.AsMemory(got, n - got)); if (k == 0) throw new ApiException("The router closed the connection."); got += k; }
            words.Add(Encoding.UTF8.GetString(buf));
        }
    }

    public void Dispose() { try { s.Dispose(); } catch { } tcp.Dispose(); }
}
