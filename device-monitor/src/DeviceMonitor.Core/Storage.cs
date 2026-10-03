using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeviceMonitor.Core;

/// <summary>
/// Where everything is kept. Default: devices and settings in %APPDATA%\DeviceMonitor, logs in Documents\Device Monitor\Logs.
/// Files are written to a temporary file first and then swapped in, so a crash never leaves a half-written list.
/// </summary>
public class Storage
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public string DataFolder { get; }
    public string DevicesFile => Path.Combine(DataFolder, "devices.json");
    public string SettingsFile => Path.Combine(DataFolder, "settings.json");

    public Storage(string dataFolder = null)
    {
        DataFolder = dataFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeviceMonitor");
        Directory.CreateDirectory(DataFolder);
    }

    public static string DefaultLogFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) is { Length: > 0 } docs ? docs : AppContext.BaseDirectory, "Device Monitor", "Logs");

    public List<Device> LoadDevices() => Load<DeviceFile>(DevicesFile)?.Devices ?? new List<Device>();
    public void SaveDevices(IEnumerable<Device> devices) => Save(DevicesFile, new DeviceFile { Devices = devices.ToList() });

    public MonitorSettings LoadSettings()
    {
        var s = Load<MonitorSettings>(SettingsFile) ?? new MonitorSettings();
        s.Normalize();
        return s;
    }
    public void SaveSettings(MonitorSettings s) => Save(SettingsFile, s);

    public class DeviceFile
    {
        public int Version { get; set; } = 1;
        public List<Device> Devices { get; set; } = new();
    }

    static T Load<T>(string path) where T : class
    {
        if (!File.Exists(path)) return null;
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json); }
        catch (JsonException)
        {
            // keep the broken file for the user and start again
            try { File.Copy(path, path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"), true); } catch (IOException) { }
            return null;
        }
    }

    static void Save<T>(string path, T value)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false));
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak", true);
        else File.Move(tmp, path);
    }

    // ------------------------------------------------------------------ CSV import / export of the device list

    public const string CsvHeader = "Name,Address,Kind,Group,Interval,Enabled,Notes";

    public static string ToCsv(IEnumerable<Device> devices)
    {
        var sb = new StringBuilder().AppendLine(CsvHeader);
        foreach (var d in devices)
            sb.AppendLine(string.Join(",", Csv.Quote(d.Name), Csv.Quote(d.Address), d.Kind, Csv.Quote(d.Group), d.IntervalSeconds?.ToString() ?? "", d.Enabled ? "yes" : "no", Csv.Quote(d.Notes)));
        return sb.ToString();
    }

    /// <summary>Reads devices from CSV (header row optional; columns as <see cref="CsvHeader"/>; only Address is required).</summary>
    public static List<Device> FromCsv(string text, out List<string> problems)
    {
        problems = new List<string>();
        var list = new List<Device>();
        var rows = Csv.Parse(text);
        int n = 0;
        foreach (var r in rows)
        {
            n++;
            if (r.Count == 0 || r.All(string.IsNullOrWhiteSpace)) continue;
            if (n == 1 && r[0].Trim().Equals("Name", StringComparison.OrdinalIgnoreCase)) continue;
            string F(int i) => i < r.Count ? r[i].Trim() : "";
            var addr = F(1);
            if (addr == "" && Validation.IsAddress(F(0))) addr = F(0);
            if (!Validation.IsAddress(addr)) { problems.Add($"Line {n}: \"{addr}\" is not an IP address or host name."); continue; }
            var d = new Device
            {
                Name = F(0) == "" || F(0) == addr ? addr : F(0),
                Address = addr,
                Kind = Enum.TryParse<DeviceKind>(F(2).Replace(" ", ""), true, out var k) ? k : DeviceKind.Other,
                Group = F(3),
                IntervalSeconds = MonitorSettings.ParseInterval(F(4)),
                Enabled = !(F(5).Equals("no", StringComparison.OrdinalIgnoreCase) || F(5).Equals("false", StringComparison.OrdinalIgnoreCase) || F(5) == "0"),
                Notes = F(6),
            };
            list.Add(d);
        }
        return list;
    }
}

public static class Csv
{
    public static string Quote(string s)
    {
        s ??= "";
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }

    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        bool quoted = false;
        text ??= "";
        // Excel in many countries writes ";" — use it when the first line has no comma
        var firstLine = text.Split('\n')[0];
        char sep = !firstLine.Contains(',') && firstLine.Contains(';') ? ';' : ',';
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
            }
            else if (c == '"') quoted = true;
            else if (c == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (c == '\n') { row.Add(cell.ToString().TrimEnd('\r')); cell.Clear(); rows.Add(row); row = new List<string>(); }
            else cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString().TrimEnd('\r')); rows.Add(row); }
        return rows;
    }
}

public static class Validation
{
    /// <summary>An IPv4 / IPv6 address or a host name (letters, digits, dots, hyphens).</summary>
    public static bool IsAddress(string s)
    {
        s = s?.Trim() ?? "";
        if (s.Length is 0 or > 253) return false;
        if (System.Net.IPAddress.TryParse(s, out var ip))
            return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 || s.Count(c => c == '.') == 3;
        if (s.All(c => char.IsDigit(c) || c == '.')) return false;   // "192.168.1" is a typo, not a host name
        return s.Split('.').All(p => p.Length is > 0 and <= 63 && p.All(c => char.IsLetterOrDigit(c) || c == '-') && p[0] != '-' && p[^1] != '-');
    }
}
