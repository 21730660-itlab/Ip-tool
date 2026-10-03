using System.Net.NetworkInformation;

namespace IpMonitor.Core;

/// <summary>Pings addresses (ICMP echo), several at a time.</summary>
public static class Pinger
{
    public record Result(string Ip, bool Up, long Ms, string Note);

    public static async Task<Result> PingAsync(string ip, int timeoutMs = 1500)
    {
        // the plain (synchronous) ping on a background thread: the most compatible way on every Windows version
        return await Task.Run(() =>
        {
            try
            {
                using var p = new Ping();
                var r = p.Send(ip, timeoutMs);
                return r.Status == IPStatus.Success ? new Result(ip, true, r.RoundtripTime, "") : new Result(ip, false, 0, r.Status.ToString());
            }
            catch (Exception ex) { return new Result(ip, false, 0, ex.InnerException?.Message ?? ex.Message); }
        });
    }

    public static async Task<List<Result>> PingManyAsync(IEnumerable<string> ips, int parallel = 32, int timeoutMs = 1500, IProgress<Result> progress = null)
    {
        using var gate = new SemaphoreSlim(parallel);
        var tasks = ips.Distinct().Select(async ip =>
        {
            await gate.WaitAsync();
            try { var r = await PingAsync(ip, timeoutMs); progress?.Report(r); return r; }
            finally { gate.Release(); }
        });
        return (await Task.WhenAll(tasks)).ToList();
    }
}
