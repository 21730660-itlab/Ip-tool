using DeviceMonitor.Core;

// Automatic checks of the monitoring rules (no network needed: a fake probe answers).
// Run: dotnet run --project tests/DeviceMonitor.Check
int failed = 0, passed = 0;
void Check(bool ok, string what) { if (ok) passed++; else { failed++; Console.WriteLine("FAIL: " + what); } }

// --- interval parsing (manual entry in the drop-down)
Check(MonitorSettings.ParseInterval("15") == 15, "15");
Check(MonitorSettings.ParseInterval("30 seconds") == 30, "30 seconds");
Check(MonitorSettings.ParseInterval("2 min") == 120, "2 min");
Check(MonitorSettings.ParseInterval("1h") == 3600, "1h");
Check(MonitorSettings.ParseInterval("0") == null, "0 rejected");
Check(MonitorSettings.ParseInterval("abc") == null, "abc rejected");
Check(MonitorSettings.ParseInterval("5 parsecs") == null, "bad unit rejected");
Check(MonitorSettings.IntervalText(15) == "15 seconds" && MonitorSettings.IntervalText(120) == "2 minutes" && MonitorSettings.IntervalText(60) == "1 minute", "interval text");

// --- addresses
Check(Validation.IsAddress("192.168.88.1"), "ipv4");
Check(Validation.IsAddress("fe80::1"), "ipv6");
Check(Validation.IsAddress("router.local"), "host name");
Check(!Validation.IsAddress("192.168.1"), "short ipv4 rejected");
Check(!Validation.IsAddress("bad host"), "space rejected");
Check(!Validation.IsAddress(""), "empty rejected");

// --- state machine: 2 misses in a row = OFF
var st = new DeviceState("x");
var t0 = DateTime.Now;
Check(st.Record(t0, 5, "", 2) == DeviceStatus.Up, "first reply = ON");
Check(st.Record(t0.AddSeconds(15), null, "timeout", 2) == null, "one miss is no alarm");
Check(st.Status == DeviceStatus.Up, "still ON after one miss");
Check(st.Record(t0.AddSeconds(17), null, "timeout", 2) == DeviceStatus.Down, "second miss = OFF");
Check(st.Record(t0.AddSeconds(32), null, "timeout", 2) == null, "stays OFF");
Check(st.Record(t0.AddSeconds(47), 3, "", 2) == DeviceStatus.Up, "reply = back ON");
Check(st.DownCount == 1, "down count");
Check(st.Sent == 5 && st.Received == 2, "counters");
Check(Math.Abs(st.LossPercent - 60) < 0.01, "loss %");
var up = st.UptimePercent ?? -1;
Check(up > 0 && up < 100, "uptime between 0 and 100: " + up);
Check(st.History().Length == 5 && st.History()[0].Ms == 5, "history in order");
for (int i = 0; i < DeviceState.HistorySize + 10; i++) st.Record(t0.AddSeconds(60 + i), i, "", 2);
Check(st.History().Length == DeviceState.HistorySize, "history ring is bounded");
var one = new DeviceState("y");
Check(one.Record(t0, null, "x", 1) == DeviceStatus.Down, "threshold 1 = OFF at first miss");

// --- CSV import / export round trip
var devs = new List<Device>
{
    new() { Name = "Core router, \"main\"", Address = "10.0.0.1", Kind = DeviceKind.MikroTik, Group = "HQ", IntervalSeconds = 30 },
    new() { Name = "Printer", Address = "printer.local", Kind = DeviceKind.Printer, Enabled = false, Notes = "2nd floor" },
};
var back = Storage.FromCsv(Storage.ToCsv(devs), out var probs);
Check(probs.Count == 0, "csv no problems");
Check(back.Count == 2 && back[0].Name == devs[0].Name && back[0].IntervalSeconds == 30 && back[0].Kind == DeviceKind.MikroTik, "csv row 1");
Check(!back[1].Enabled && back[1].Notes == "2nd floor" && back[1].Kind == DeviceKind.Printer, "csv row 2");
var semi = Storage.FromCsv("Name;Address\nAP;10.0.0.5\nbad;10.0\n", out probs);
Check(semi.Count == 1 && semi[0].Address == "10.0.0.5" && probs.Count == 1, "semicolon csv + bad line reported");

// --- storage + logs in a temp folder
var tmp = Path.Combine(Path.GetTempPath(), "dm-check-" + Guid.NewGuid().ToString("N"));
var store = new Storage(tmp);
store.SaveDevices(devs); store.SaveDevices(devs);
Check(store.LoadDevices().Count == 2, "devices saved and loaded");
var s = new MonitorSettings { IntervalSeconds = 30 }; store.SaveSettings(s);
Check(store.LoadSettings().IntervalSeconds == 30, "settings saved");
File.WriteAllText(store.SettingsFile, "{broken");
Check(store.LoadSettings().IntervalSeconds == 15, "broken settings fall back to defaults");

var log = new EventLog(Path.Combine(tmp, "logs"));
log.Write(new MonitorEvent(DateTime.Now, EventKind.Down, "a", "Router, 1", "10.0.0.1", "HQ", "No reply"));
log.Write(new MonitorEvent(DateTime.Now, EventKind.Up, "a", "Router, 1", "10.0.0.1", "HQ", "Reply in 3 ms", TimeSpan.FromSeconds(95)));
Check(File.ReadAllLines(log.CsvPath).Length == 3, "csv log lines");
Check(File.ReadAllText(log.DayPath(DateTime.Now)).Contains("back ON after 1 min 35 s"), "text log");
var log2 = new EventLog(Path.Combine(tmp, "logs")); log2.LoadFromFile();
Check(log2.Recent.Count == 2 && log2.Recent[0].Kind == EventKind.Up && log2.Recent[0].DeviceName == "Router, 1" && log2.Recent[0].Duration == TimeSpan.FromSeconds(95), "log read back");

// --- engine with a fake probe: device goes OFF, then back ON
var fake = new FakeProbe();
var cfg = new MonitorSettings { IntervalSeconds = 1, FailuresBeforeDown = 2 };
using var eng = new MonitorEngine(fake, () => cfg);
var events = new System.Collections.Concurrent.ConcurrentQueue<MonitorEvent>();
eng.StatusChanged += e => events.Enqueue(e);
var dev = new Device { Name = "R1", Address = "10.1.1.1" };
var paused = new Device { Name = "Off", Address = "10.1.1.2", Enabled = false };
eng.SetDevices(new[] { dev, paused });
eng.Start();
await WaitFor(() => events.Any(e => e.Kind == EventKind.Up), "first ON");
Check(events.First().Initial, "first result is marked initial");
fake.Answer = false;
await WaitFor(() => events.Any(e => e.Kind == EventKind.Down), "goes OFF");
fake.Answer = true;
await WaitFor(() => events.Count(e => e.Kind == EventKind.Up) == 2, "back ON");
var backOn = events.Last();
Check(!backOn.Initial && backOn.Duration > TimeSpan.Zero, "back ON has the down time");
Check(!fake.Seen.Contains("10.1.1.2"), "paused device never pinged");
Check(eng.Counts() is (2, 1, 0, 1, 0), "counts " + eng.Counts());
await eng.StopAsync();
Check(!eng.IsRunning, "stopped");

// many devices at once
var many = Enumerable.Range(1, 300).Select(i => new Device { Name = "D" + i, Address = $"10.2.{i / 250}.{i % 250 + 1}" }).ToList();
var fast = new FakeProbe { DelayMs = 50 };
using var eng2 = new MonitorEngine(fast, () => cfg);
eng2.SetDevices(many);
var sw = System.Diagnostics.Stopwatch.StartNew();
await eng2.CheckNowAsync();
Check(many.All(d => eng2.StateOf(d.Id).Status == DeviceStatus.Up), "300 devices checked");
Check(sw.ElapsedMilliseconds < 3000, "300 devices in parallel: " + sw.ElapsedMilliseconds + " ms");

Directory.Delete(tmp, true);
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

async Task WaitFor(Func<bool> cond, string what)
{
    var until = DateTime.Now.AddSeconds(15);
    while (!cond() && DateTime.Now < until) await Task.Delay(50);
    Check(cond(), what);
}

class FakeProbe : IProbe
{
    public volatile bool Answer = true;
    public int DelayMs = 5;
    public readonly System.Collections.Concurrent.ConcurrentBag<string> Seen = new();
    public async Task<ProbeResult> CheckAsync(string address, int timeoutMs, CancellationToken ct)
    {
        Seen.Add(address);
        await Task.Delay(DelayMs, ct);
        return Answer ? ProbeResult.Reply(3) : ProbeResult.Fail("No reply (timed out)");
    }
}
