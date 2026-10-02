namespace IpMonitor.Core;

/// <summary>
/// Live monitoring: pings every device that has an IP (not Planned or Retired) and tells when one goes down or comes back.
/// A device is "down" only after it missed two pings in a row (one retry right away), so a single lost packet is no alarm.
/// </summary>
public class DeviceMonitor
{
    public enum State { Unknown, Up, Down }
    public class DevState
    {
        public string DeviceId, Ip;
        public State State = State.Unknown;
        public long Ms;
        public DateTime Since = DateTime.Now, LastCheck;
        public string Note = "";
    }
    /// <summary>A device went down, or came back (then DownFor says how long it was down).</summary>
    public record Event(DateTime At, Device Device, string Ip, bool Down, string Note, TimeSpan? DownFor = null);

    readonly Store S;
    readonly Func<IEnumerable<string>, Task<List<Pinger.Result>>> ping;
    public readonly Dictionary<string, DevState> States = new();
    public readonly List<Event> Events = new();
    public DateTime LastRun { get; private set; }
    /// <summary>Where up/down events are written (a CSV file next to the database); null = not written.</summary>
    public UptimeLog Log { get; set; }
    public bool Running { get; private set; }
    /// <summary>Raised for every change from up to down or down to up (and for devices found down on the first check).</summary>
    public event Action<Event> Changed;

    public DeviceMonitor(Store store, Func<IEnumerable<string>, Task<List<Pinger.Result>>> pingFunc = null)
    {
        S = store;
        ping = pingFunc ?? (ips => Pinger.PingManyAsync(ips, 48, 1500));
    }

    public static string IpOf(Device d) => !string.IsNullOrEmpty(d.Ip) ? d.Ip : d.Addrs.Select(a => IpMath.IpOf(a.Address)).FirstOrDefault(x => x != "") ?? "";
    public static bool Watched(Device d) => Store.StatusOf(d.Status) is not ("planned" or "retired") && IpOf(d) != "";

    public ISet<string> DownIds => States.Values.Where(s => s.State == State.Down).Select(s => s.DeviceId).ToHashSet();
    public State Of(string devId) => States.TryGetValue(devId, out var s) ? s.State : State.Unknown;
    public DevState Get(string devId) => States.GetValueOrDefault(devId);

    /// <summary>Pings everything once. Returns the changes found.</summary>
    public async Task<List<Event>> CheckAsync()
    {
        if (Running) return new();
        Running = true;
        try
        {
            var devs = S.Db.Devices.Where(Watched).ToList();
            foreach (var id in States.Keys.Where(id => !devs.Any(d => d.Id == id)).ToList()) States.Remove(id);   // deleted / retired
            var ips = devs.Select(IpOf).Distinct().ToList();
            var res = (await ping(ips)).ToDictionary(r => r.Ip);
            var retry = res.Values.Where(r => !r.Up).Select(r => r.Ip).ToList();
            if (retry.Count > 0) foreach (var r in await ping(retry)) res[r.Ip] = r;   // second chance before calling it down
            var changes = new List<Event>();
            foreach (var d in devs)
            {
                var ip = IpOf(d);
                if (!res.TryGetValue(ip, out var r)) continue;
                if (!States.TryGetValue(d.Id, out var st)) States[d.Id] = st = new DevState { DeviceId = d.Id };
                var now = r.Up ? State.Up : State.Down;
                var was = st.State;
                st.Ip = ip; st.Ms = r.Ms; st.Note = r.Note; st.LastCheck = DateTime.Now;
                if (now != was)
                {
                    TimeSpan? downFor = was == State.Down ? DateTime.Now - st.Since : null;
                    st.State = now; st.Since = DateTime.Now;
                    if (now == State.Down || was == State.Down) changes.Add(new Event(DateTime.Now, d, ip, now == State.Down, r.Note, downFor));
                }
            }
            LastRun = DateTime.Now;
            foreach (var c in changes)
            {
                Events.Insert(0, c);
                try { Log?.Write(c, S.SiteById(c.Device.SiteId)); } catch { /* a locked log file never stops monitoring */ }
                Changed?.Invoke(c);
            }
            if (Events.Count > 200) Events.RemoveRange(200, Events.Count - 200);
            return changes;
        }
        finally { Running = false; }
    }
}
