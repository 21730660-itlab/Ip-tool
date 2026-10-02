using System.Text;

namespace IpMonitor.Core;

/// <summary>
/// The up/down log of live monitoring: a CSV file next to the database (opens in Excel), one line per event,
/// kept forever. Several computers can write to it; each line says which computer saw the event.
/// </summary>
public class UptimeLog
{
    public string Path { get; }
    public const string Header = "Date,Time,Event,Device,Site,IP address,Down for,Detail,Checked from,User";
    readonly string user;

    public UptimeLog(string path, string user = null) { Path = path; this.user = user ?? ""; }

    /// <summary>The log file that belongs to a database file: "ip-monitor-db.json" → "ip-monitor-db-uptime-log.csv".</summary>
    public static string For(string dbPath) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(dbPath) ?? "", System.IO.Path.GetFileNameWithoutExtension(dbPath) + "-uptime-log.csv");

    public static string Duration(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m" : t.TotalHours >= 1 ? $"{(int)t.TotalHours}h {t.Minutes}m" : t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes}m {t.Seconds}s" : $"{Math.Max(0, (int)t.TotalSeconds)}s";

    static string Q(string v) { v ??= ""; return v.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + v.Replace("\"", "\"\"") + "\"" : v; }

    public void Write(DeviceMonitor.Event e, Site site) =>
        Append(e.At, e.Down ? "DOWN" : "UP", e.Device.Name, site == null ? "" : $"#{site.SiteNumber} {site.Name}", e.Ip,
            e.DownFor is TimeSpan d ? Duration(d) : "", e.Down ? (string.IsNullOrEmpty(e.Note) || e.Note == "TimedOut" ? "no ping reply" : e.Note) : "answers ping again");

    /// <summary>Monitoring started / paused, so gaps in the log are explained.</summary>
    public void Note(string what) => Append(DateTime.Now, what, "", "", "", "", "");

    void Append(DateTime at, string ev, string dev, string site, string ip, string downFor, string detail)
    {
        var line = string.Join(",", new[] { at.ToString("yyyy-MM-dd"), at.ToString("HH:mm:ss"), ev, dev, site, ip, downFor, detail, Environment.MachineName, user }.Select(Q));
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var isNew = !File.Exists(Path) || new FileInfo(Path).Length == 0;
                using var fs = new FileStream(Path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var w = new StreamWriter(fs, new UTF8Encoding(isNew));   // BOM on a new file so Excel reads accents correctly
                if (isNew) w.Write(Header + "\r\n");
                w.Write(line + "\r\n");
                return;
            }
            catch (IOException) when (attempt < 4) { Thread.Sleep(150); }   // another computer is writing
        }
    }

    public record Line(DateTime At, string Event, string Device, string Site, string Ip, string DownFor, string Detail, string From);

    /// <summary>The newest lines first (only DOWN / UP events).</summary>
    public List<Line> Recent(int max = 200)
    {
        var res = new List<Line>();
        if (!File.Exists(Path)) return res;
        string[] lines;
        using (var fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var r = new StreamReader(fs)) lines = r.ReadToEnd().Split('\n');
        for (int i = lines.Length - 1; i > 0 && res.Count < max; i--)
        {
            var f = SplitCsv(lines[i].TrimEnd('\r'));
            if (f.Count < 9 || (f[2] != "DOWN" && f[2] != "UP")) continue;
            if (!DateTime.TryParse(f[0] + " " + f[1], out var at)) continue;
            res.Add(new Line(at, f[2], f[3], f[4], f[5], f[6], f[7], f[8]));
        }
        return res;
    }

    static List<string> SplitCsv(string s)
    {
        var res = new List<string>(); var cur = new StringBuilder(); bool q = false;
        for (int i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (q) { if (c == '"') { if (i + 1 < s.Length && s[i + 1] == '"') { cur.Append('"'); i++; } else q = false; } else cur.Append(c); }
            else if (c == '"') q = true;
            else if (c == ',') { res.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        res.Add(cur.ToString());
        return res;
    }
}
