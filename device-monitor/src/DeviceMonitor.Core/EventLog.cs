using System.Globalization;
using System.Text;

namespace DeviceMonitor.Core;

/// <summary>
/// The log files. Every event is written to two files in the log folder:
///  - events.csv               all events, one per line (opens in Excel)
///  - DeviceMonitor-YYYY-MM-DD.log   a readable text log, one file per day
/// Also keeps the newest events in memory for the window.
/// </summary>
public class EventLog
{
    public const string CsvName = "events.csv";
    const string CsvHeader = "Date,Time,Event,Device,Address,Group,Detail,Down for,Down for (seconds)";
    public const int Keep = 5000;

    readonly object gate = new();
    readonly List<MonitorEvent> recent = new();
    string folder;

    public event Action<MonitorEvent> Added;

    public EventLog(string folder) { Folder = folder; }

    public string Folder
    {
        get => folder;
        set { folder = string.IsNullOrWhiteSpace(value) ? Storage.DefaultLogFolder : value; Directory.CreateDirectory(folder); }
    }
    public string CsvPath => Path.Combine(Folder, CsvName);
    public string DayPath(DateTime d) => Path.Combine(Folder, $"DeviceMonitor-{d:yyyy-MM-dd}.log");

    /// <summary>Newest first.</summary>
    public List<MonitorEvent> Recent { get { lock (gate) return recent.ToList(); } }

    public void Write(MonitorEvent e)
    {
        lock (gate)
        {
            recent.Insert(0, e);
            if (recent.Count > Keep) recent.RemoveRange(Keep, recent.Count - Keep);
            try
            {
                var newCsv = !File.Exists(CsvPath);
                var line = string.Join(",", e.At.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.At.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                    KindText(e.Kind), Csv.Quote(e.DeviceName), Csv.Quote(e.Address), Csv.Quote(e.Group), Csv.Quote(e.Detail),
                    e.Duration is TimeSpan t ? Duration(t) : "", e.Duration is TimeSpan t2 ? ((long)t2.TotalSeconds).ToString(CultureInfo.InvariantCulture) : "");
                File.AppendAllText(CsvPath, (newCsv ? CsvHeader + "\r\n" : "") + line + "\r\n", new UTF8Encoding(newCsv));
                File.AppendAllText(DayPath(e.At), TextLine(e) + "\r\n", Encoding.UTF8);
            }
            catch (IOException) { /* file open in Excel: the event stays in the window, monitoring goes on */ }
            catch (UnauthorizedAccessException) { }
        }
        Added?.Invoke(e);
    }

    public void Info(string text) => Write(new MonitorEvent(DateTime.Now, EventKind.Info, "", "", "", "", text));

    public static string KindText(EventKind k) => k switch { EventKind.Down => "OFF", EventKind.Up => "ON", _ => "INFO" };

    public static string TextLine(MonitorEvent e)
    {
        var who = e.Kind == EventKind.Info ? "" : $"{e.DeviceName} ({e.Address}){(string.IsNullOrEmpty(e.Group) ? "" : " [" + e.Group + "]")} ";
        var what = e.Kind switch
        {
            EventKind.Down => $"is OFF - {e.Detail}",
            EventKind.Up => e.Duration is TimeSpan t ? $"is back ON after {Duration(t)} - {e.Detail}" : $"is ON - {e.Detail}",
            _ => e.Detail
        };
        return $"{e.At:yyyy-MM-dd HH:mm:ss}  {KindText(e.Kind),-4}  {who}{what}";
    }

    /// <summary>1 h 05 min, 3 min 20 s, 12 s.</summary>
    public static string Duration(TimeSpan t)
    {
        if (t.TotalDays >= 1) return $"{(int)t.TotalDays} d {t.Hours} h {t.Minutes:00} min";
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes:00} min";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} min {t.Seconds:00} s";
        return $"{Math.Max(0, (int)t.TotalSeconds)} s";
    }

    /// <summary>Reads the newest events back from events.csv (so the window shows history after a restart).</summary>
    public void LoadFromFile(int max = 1000)
    {
        if (!File.Exists(CsvPath)) return;
        List<string> lines;
        try
        {
            using var fs = new FileStream(CsvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var sr = new StreamReader(fs);
            lines = new List<string>();
            string l;
            while ((l = sr.ReadLine()) != null) { lines.Add(l); if (lines.Count > max * 2) lines.RemoveRange(0, max); }
        }
        catch (IOException) { return; }
        var loaded = new List<MonitorEvent>();
        foreach (var line in lines.TakeLast(max))
        {
            var r = Csv.Parse(line).FirstOrDefault();
            if (r == null || r.Count < 7 || r[0] == "Date") continue;
            if (!DateTime.TryParseExact(r[0] + " " + r[1], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)) continue;
            var kind = r[2] switch { "OFF" => EventKind.Down, "ON" => EventKind.Up, _ => EventKind.Info };
            TimeSpan? dur = r.Count > 8 && long.TryParse(r[8], out var secs) ? TimeSpan.FromSeconds(secs) : null;
            loaded.Add(new MonitorEvent(at, kind, "", r[3], r[4], r[5], r[6], dur));
        }
        loaded.Reverse();
        lock (gate)
        {
            recent.AddRange(loaded);
            if (recent.Count > Keep) recent.RemoveRange(Keep, recent.Count - Keep);
        }
    }

    /// <summary>Deletes daily log files older than the given number of days (events.csv is kept).</summary>
    public int CleanUp(int keepDays)
    {
        if (keepDays <= 0) return 0;
        int n = 0;
        var limit = DateTime.Today.AddDays(-keepDays);
        try
        {
            foreach (var f in Directory.GetFiles(Folder, "DeviceMonitor-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(f)["DeviceMonitor-".Length..];
                if (DateTime.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) && d < limit)
                {
                    try { File.Delete(f); n++; } catch (IOException) { }
                }
            }
        }
        catch (IOException) { }
        return n;
    }
}
