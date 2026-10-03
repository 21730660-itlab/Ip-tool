using System.Net.NetworkInformation;

namespace DeviceMonitor.Core;

/// <summary>Result of one check: reply time in ms, or null with the reason.</summary>
public readonly record struct ProbeResult(long? Ms, string Error)
{
    public bool Ok => Ms.HasValue;
    public static ProbeResult Reply(long ms) => new(ms, "");
    public static ProbeResult Fail(string why) => new(null, why);
}

/// <summary>
/// A way to check whether a device answers. Version 1 uses ICMP ping; other checks (TCP port, HTTP, SNMP, MikroTik API)
/// can be added later as further implementations without touching the monitor.
/// </summary>
public interface IProbe
{
    Task<ProbeResult> CheckAsync(string address, int timeoutMs, CancellationToken ct);
}

/// <summary>ICMP echo (the same as "ping" in a command prompt).</summary>
public class PingProbe : IProbe
{
    static readonly byte[] Payload = new byte[32];

    public async Task<ProbeResult> CheckAsync(string address, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var p = new Ping();
            var r = await p.SendPingAsync(address.Trim(), TimeSpan.FromMilliseconds(timeoutMs), Payload, new PingOptions(64, true), ct);
            return r.Status == IPStatus.Success ? ProbeResult.Reply(r.RoundtripTime) : ProbeResult.Fail(Describe(r.Status));
        }
        catch (OperationCanceledException) { throw; }
        catch (PingException ex) { return ProbeResult.Fail(ex.InnerException?.Message ?? ex.Message); }
        catch (Exception ex) { return ProbeResult.Fail(ex.Message); }
    }

    static string Describe(IPStatus s) => s switch
    {
        IPStatus.TimedOut => "No reply (timed out)",
        IPStatus.DestinationHostUnreachable => "Host unreachable",
        IPStatus.DestinationNetworkUnreachable => "Network unreachable",
        IPStatus.DestinationUnreachable => "Destination unreachable",
        IPStatus.TtlExpired => "TTL expired",
        _ => s.ToString()
    };
}
