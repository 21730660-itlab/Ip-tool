namespace DeviceMonitor.Core;

/// <summary>
/// Live state of one device: current status, recent ping history (for the graphs) and counters (for uptime and loss).
/// Updated by the monitor on a worker thread; read by the window — every access goes through the lock.
/// </summary>
public class DeviceState
{
    /// <summary>How many pings are kept per device for the graphs (at 15 s this is 6 hours).</summary>
    public const int HistorySize = 1440;

    readonly object gate = new();
    readonly Sample[] ring = new Sample[HistorySize];
    int head, count;

    public string DeviceId { get; }
    public DeviceStatus Status { get; private set; } = DeviceStatus.Unknown;
    /// <summary>When the current status started.</summary>
    public DateTime Since { get; private set; } = DateTime.Now;
    public DateTime? LastCheck { get; private set; }
    public long? LastMs { get; private set; }
    public string LastError { get; private set; } = "";
    public int FailuresInRow { get; private set; }
    public long Sent { get; private set; }
    public long Received { get; private set; }
    /// <summary>Time spent up / down since the monitor started (for the uptime %).</summary>
    public TimeSpan UpTime { get; private set; }
    public TimeSpan DownTime { get; private set; }
    public int DownCount { get; private set; }
    DateTime? lastStatusAt;

    public DeviceState(string deviceId) { DeviceId = deviceId; }

    /// <summary>Records one ping. Returns the new status when it changed (Up ↔ Down, or the first result), else null.</summary>
    public DeviceStatus? Record(DateTime at, long? ms, string error, int failuresBeforeDown)
    {
        lock (gate)
        {
            ring[head] = new Sample(at, ms);
            head = (head + 1) % HistorySize;
            if (count < HistorySize) count++;
            Sent++;
            LastCheck = at;
            LastMs = ms;
            LastError = ms.HasValue ? "" : (error ?? "");

            // time since the last check counts for the status the device had
            if (lastStatusAt.HasValue)
            {
                var span = at - lastStatusAt.Value;
                if (span > TimeSpan.Zero && span < TimeSpan.FromHours(6))
                {
                    if (Status == DeviceStatus.Up) UpTime += span;
                    else if (Status == DeviceStatus.Down) DownTime += span;
                }
            }
            lastStatusAt = at;

            DeviceStatus next = Status;
            if (ms.HasValue)
            {
                Received++;
                FailuresInRow = 0;
                next = DeviceStatus.Up;
            }
            else
            {
                FailuresInRow++;
                if (FailuresInRow >= Math.Max(1, failuresBeforeDown)) next = DeviceStatus.Down;
            }
            if (next == Status) return null;
            Status = next;
            Since = at;
            if (next == DeviceStatus.Down) DownCount++;
            return next;
        }
    }

    public void Pause()
    {
        lock (gate) { Status = DeviceStatus.Paused; Since = DateTime.Now; FailuresInRow = 0; lastStatusAt = null; }
    }

    /// <summary>Clears the status so the next ping decides again (after a change of address, for example).</summary>
    public void Reset()
    {
        lock (gate) { Status = DeviceStatus.Unknown; Since = DateTime.Now; FailuresInRow = 0; lastStatusAt = null; }
    }

    /// <summary>The pings in time order (oldest first), optionally only those after a moment.</summary>
    public Sample[] History(DateTime? after = null)
    {
        lock (gate)
        {
            var list = new List<Sample>(count);
            for (int i = 0; i < count; i++)
            {
                var s = ring[(head - count + i + HistorySize) % HistorySize];
                if (after == null || s.At >= after) list.Add(s);
            }
            return list.ToArray();
        }
    }

    /// <summary>Uptime in % since the monitor started (null before the first result).</summary>
    public double? UptimePercent
    {
        get { lock (gate) { var t = UpTime + DownTime; return t.TotalSeconds < 1 ? (Status == DeviceStatus.Up ? 100 : Status == DeviceStatus.Down ? 0 : null) : 100.0 * UpTime.TotalSeconds / t.TotalSeconds; } }
    }

    public double LossPercent { get { lock (gate) return Sent == 0 ? 0 : 100.0 * (Sent - Received) / Sent; } }

    /// <summary>Average / min / max reply time over the kept history.</summary>
    public (double avg, long min, long max)? LatencyStats()
    {
        var h = History().Where(s => s.Ms.HasValue).Select(s => s.Ms.Value).ToList();
        if (h.Count == 0) return null;
        return (h.Average(), h.Min(), h.Max());
    }
}
