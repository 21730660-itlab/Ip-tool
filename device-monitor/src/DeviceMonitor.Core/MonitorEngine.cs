using System.Collections.Concurrent;

namespace DeviceMonitor.Core;

/// <summary>
/// Pings every enabled device on its interval, many at a time, and reports each result and each change ON ↔ OFF.
/// Scales to hundreds of devices: one scheduler loop, pings run in parallel (limited), a device is never pinged twice at once.
/// After a missed ping the device is re-checked quickly (every 2 s) until it is declared OFF, so an outage is seen fast
/// without false alarms from one lost packet.
/// Events are raised on worker threads.
/// </summary>
public sealed class MonitorEngine : IDisposable
{
    public const int MaxParallel = 64;
    static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(250);
    static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    readonly IProbe probe;
    readonly Func<MonitorSettings> settings;
    readonly object gate = new();
    readonly List<Device> devices = new();
    readonly ConcurrentDictionary<string, DeviceState> states = new();
    readonly ConcurrentDictionary<string, DateTime> due = new();
    readonly ConcurrentDictionary<string, byte> busy = new();
    readonly SemaphoreSlim slots = new(MaxParallel);
    CancellationTokenSource cts;
    Task loop;

    /// <summary>After every ping.</summary>
    public event Action<Device, DeviceState> Sampled;
    /// <summary>A device went OFF / came back ON (also its first result, with Initial = true).</summary>
    public event Action<MonitorEvent> StatusChanged;

    public MonitorEngine(IProbe probe, Func<MonitorSettings> settings)
    {
        this.probe = probe;
        this.settings = settings;
    }

    public bool IsRunning => loop != null && !loop.IsCompleted;
    public DateTime? StartedAt { get; private set; }

    public IReadOnlyList<Device> Devices { get { lock (gate) return devices.ToList(); } }
    public DeviceState StateOf(string id) => states.GetOrAdd(id, k => new DeviceState(k));
    public Device Find(string id) { lock (gate) return devices.FirstOrDefault(d => d.Id == id); }

    // ------------------------------------------------------------------ device list

    public void SetDevices(IEnumerable<Device> list)
    {
        lock (gate)
        {
            devices.Clear();
            devices.AddRange(list);
            foreach (var id in states.Keys.Where(id => !devices.Any(d => d.Id == id)).ToList()) { states.TryRemove(id, out _); due.TryRemove(id, out _); }
            foreach (var d in devices) { var st = StateOf(d.Id); if (!d.Enabled) st.Pause(); else due.TryAdd(d.Id, DateTime.MinValue); }
        }
    }

    /// <summary>Adds a device, or replaces the one with the same Id.</summary>
    public void Upsert(Device d)
    {
        lock (gate)
        {
            var i = devices.FindIndex(x => x.Id == d.Id);
            var old = i >= 0 ? devices[i] : null;
            if (i >= 0) devices[i] = d; else devices.Add(d);
            var st = StateOf(d.Id);
            if (!d.Enabled) { st.Pause(); due.TryRemove(d.Id, out _); return; }
            var changed = old == null || !old.Enabled || !string.Equals(old.Address?.Trim(), d.Address?.Trim(), StringComparison.OrdinalIgnoreCase);
            if (changed) { st.Reset(); due[d.Id] = DateTime.MinValue; }
            else if (old.IntervalSeconds != d.IntervalSeconds) due[d.Id] = DateTime.MinValue;
        }
    }

    public void Remove(string id)
    {
        lock (gate) devices.RemoveAll(d => d.Id == id);
        states.TryRemove(id, out _);
        due.TryRemove(id, out _);
    }

    // ------------------------------------------------------------------ running

    public void Start()
    {
        if (IsRunning) return;
        cts = new CancellationTokenSource();
        StartedAt = DateTime.Now;
        lock (gate) foreach (var d in devices.Where(d => d.Enabled)) { due[d.Id] = DateTime.MinValue; var st = StateOf(d.Id); if (st.Status == DeviceStatus.Paused) st.Reset(); }
        var token = cts.Token;
        loop = Task.Run(() => RunAsync(token));
    }

    public async Task StopAsync()
    {
        if (cts == null) return;
        cts.Cancel();
        try { if (loop != null) await loop; } catch (OperationCanceledException) { }
        cts.Dispose(); cts = null; loop = null;
    }

    /// <summary>Pings these devices (or all enabled ones) right away, even while the monitor is stopped.</summary>
    public Task CheckNowAsync(IEnumerable<string> ids = null)
    {
        List<Device> list;
        lock (gate) list = devices.Where(d => d.Enabled && (ids == null || ids.Contains(d.Id))).ToList();
        return Task.WhenAll(list.Select(d => CheckOneAsync(d, cts?.Token ?? CancellationToken.None)));
    }

    /// <summary>After a change of the interval: ping everything now, then continue on the new interval.</summary>
    public void RescheduleAll()
    {
        lock (gate) foreach (var d in devices.Where(d => d.Enabled)) due[d.Id] = DateTime.MinValue;
    }

    /// <summary>When the device will be pinged next (null = not scheduled).</summary>
    public DateTime? NextCheck(string id) => IsRunning && due.TryGetValue(id, out var t) ? t : null;

    public int IntervalOf(Device d) => d.IntervalSeconds is int s and > 0 ? s : settings().IntervalSeconds;

    async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(Tick);
        do
        {
            var now = DateTime.Now;
            List<Device> work;
            lock (gate) work = devices.Where(d => d.Enabled && !busy.ContainsKey(d.Id) && due.GetValueOrDefault(d.Id, DateTime.MinValue) <= now).ToList();
            foreach (var d in work) _ = CheckOneAsync(d, ct);
        }
        while (await timer.WaitForNextTickAsync(ct));
    }

    async Task CheckOneAsync(Device d, CancellationToken ct)
    {
        if (!busy.TryAdd(d.Id, 0)) return;
        try
        {
            await slots.WaitAsync(ct);
            ProbeResult r;
            var cfg = settings();
            try { r = await probe.CheckAsync(d.Address, cfg.TimeoutMs, ct); }
            finally { slots.Release(); }

            var now = DateTime.Now;
            var st = StateOf(d.Id);
            var before = st.Status;
            var beforeSince = st.Since;
            var changed = st.Record(now, r.Ms, r.Error, cfg.FailuresBeforeDown);
            // a missed ping that is not yet an outage: look again soon
            var next = !r.Ok && st.Status != DeviceStatus.Down && st.FailuresInRow < cfg.FailuresBeforeDown
                ? TimeSpan.FromTicks(Math.Min(RetryDelay.Ticks, TimeSpan.FromSeconds(IntervalOf(d)).Ticks))
                : TimeSpan.FromSeconds(IntervalOf(d));
            if (Find(d.Id) is { Enabled: true }) due[d.Id] = now + next;

            Sampled?.Invoke(d, st);
            if (changed is DeviceStatus s && Find(d.Id) != null)
            {
                var initial = before is DeviceStatus.Unknown or DeviceStatus.Paused;
                var ev = s == DeviceStatus.Down
                    ? new MonitorEvent(now, EventKind.Down, d.Id, d.Name, d.Address, d.Group, string.IsNullOrEmpty(r.Error) ? "No reply" : r.Error, null, initial)
                    : new MonitorEvent(now, EventKind.Up, d.Id, d.Name, d.Address, d.Group, $"Reply in {r.Ms} ms", initial ? null : now - beforeSince, initial);
                StatusChanged?.Invoke(ev);
            }
        }
        catch (OperationCanceledException) { }
        finally { busy.TryRemove(d.Id, out _); }
    }

    /// <summary>Counts for the dashboard: (total, on, off, paused, unknown).</summary>
    public (int total, int up, int down, int paused, int unknown) Counts()
    {
        var list = Devices;
        int up = 0, down = 0, paused = 0, unknown = 0;
        foreach (var d in list)
        {
            switch (d.Enabled ? StateOf(d.Id).Status : DeviceStatus.Paused)
            {
                case DeviceStatus.Up: up++; break;
                case DeviceStatus.Down: down++; break;
                case DeviceStatus.Paused: paused++; break;
                default: unknown++; break;
            }
        }
        return (list.Count, up, down, paused, unknown);
    }

    public void Dispose()
    {
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
    }
}
