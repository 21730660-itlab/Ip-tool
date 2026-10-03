using System.Text.Json.Serialization;

namespace DeviceMonitor.Core;

/// <summary>What kind of device it is (only changes the icon and colour; every kind is checked the same way).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceKind { MikroTik, Router, Switch, AccessPoint, Server, Computer, Camera, Printer, Other }

/// <summary>A device to watch.</summary>
public class Device
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    /// <summary>IPv4 / IPv6 address or a host name.</summary>
    public string Address { get; set; } = "";
    public DeviceKind Kind { get; set; } = DeviceKind.MikroTik;
    /// <summary>The site the device belongs to (see <see cref="Site"/> class / <see cref="Sites"/>).</summary>
    public string SiteId { get; set; } = "";
    /// <summary>Name of that site, kept in step by the program (used in the logs and pop-ups).</summary>
    public string Site { get; set; } = "";
    /// <summary>Version 1.0 files called the site "Group": read it once, never written again.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string Group { get => null; set { if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(Site)) Site = value; } }
    public string Notes { get; set; } = "";
    /// <summary>false = paused: kept in the list but not pinged.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Own ping interval in seconds; null = use the global interval.</summary>
    public int? IntervalSeconds { get; set; }
    public DateTime Added { get; set; } = DateTime.Now;

    public Device Clone() => (Device)MemberwiseClone();
}

/// <summary>A place (office, branch, customer, tower...) that holds devices. Create the site first, then add its IPs.</summary>
public class Site
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Location { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTime Added { get; set; } = DateTime.Now;
    public Site Clone() => (Site)MemberwiseClone();
}

/// <summary>Helpers that keep devices and sites consistent.</summary>
public static class Sites
{
    /// <summary>
    /// Every device gets a valid site: old files (site written only as a name) get their sites created,
    /// devices without a site go to "Unassigned". Device.Site is refreshed from the site name. Returns true when something changed.
    /// </summary>
    public static bool Repair(List<Site> sites, List<Device> devices)
    {
        bool changed = false;
        foreach (var d in devices)
        {
            var s = sites.FirstOrDefault(x => x.Id == d.SiteId);
            if (s == null)
            {
                var name = string.IsNullOrWhiteSpace(d.Site) ? "Unassigned" : d.Site.Trim();
                s = sites.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (s == null) { s = new Site { Name = name }; sites.Add(s); }
                d.SiteId = s.Id; changed = true;
            }
            if (d.Site != s.Name) { d.Site = s.Name; changed = true; }
        }
        return changed;
    }

    /// <summary>The site with this name, created when missing (CSV import).</summary>
    public static Site GetOrAdd(List<Site> sites, string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Unassigned" : name.Trim();
        var s = sites.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (s == null) { s = new Site { Name = name }; sites.Add(s); }
        return s;
    }
}

/// <summary>Everything the user can change on the Settings page.</summary>
public class MonitorSettings
{
    /// <summary>How often each device is pinged (seconds).</summary>
    public int IntervalSeconds { get; set; } = 15;
    /// <summary>How long to wait for a ping reply (milliseconds).</summary>
    public int TimeoutMs { get; set; } = 1000;
    /// <summary>Missed pings in a row before a device counts as OFF (1 = at the first miss).</summary>
    public int FailuresBeforeDown { get; set; } = 2;
    /// <summary>Above this reply time (ms) a device shows as "slow" (yellow).</summary>
    public int SlowMs { get; set; } = 150;
    public bool PopupOnDown { get; set; } = true;
    public bool PopupOnUp { get; set; } = true;
    public bool Sound { get; set; } = true;
    /// <summary>Seconds a pop-up stays on screen; 0 = until it is closed.</summary>
    public int PopupSeconds { get; set; } = 10;
    public bool DarkTheme { get; set; } = true;
    /// <summary>Closing the window keeps the monitor running in the notification area (system tray).</summary>
    public bool CloseToTray { get; set; } = true;
    public bool StartMonitoringOnLaunch { get; set; } = true;
    /// <summary>Folder of the log files; empty = the default folder (Documents\Device Monitor\Logs).</summary>
    public string LogFolder { get; set; } = "";
    /// <summary>Delete daily log files older than this many days (0 = keep all).</summary>
    public int KeepLogDays { get; set; } = 90;

    public static readonly int[] IntervalChoices = { 5, 10, 15, 30, 60, 120, 300, 600 };
    public const int MinInterval = 1, MaxInterval = 86400;

    public MonitorSettings Clone() => (MonitorSettings)MemberwiseClone();

    /// <summary>Puts every value back in its allowed range.</summary>
    public void Normalize()
    {
        IntervalSeconds = Math.Clamp(IntervalSeconds, MinInterval, MaxInterval);
        TimeoutMs = Math.Clamp(TimeoutMs, 100, 10000);
        FailuresBeforeDown = Math.Clamp(FailuresBeforeDown, 1, 10);
        SlowMs = Math.Clamp(SlowMs, 1, 10000);
        PopupSeconds = Math.Clamp(PopupSeconds, 0, 3600);
        KeepLogDays = Math.Clamp(KeepLogDays, 0, 36500);
        LogFolder ??= "";
    }

    /// <summary>"15" / "15 s" / "15 seconds" / "2 min" / "1h" → seconds; null when it is not a valid interval.</summary>
    public static int? ParseInterval(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().ToLowerInvariant();
        var digits = new string(t.TakeWhile(c => char.IsDigit(c) || c == '.' || c == ',').ToArray()).Replace(',', '.');
        if (!double.TryParse(digits, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) || n <= 0) return null;
        var unit = t[digits.Length..].Trim();
        double mult = unit switch
        {
            "" or "s" or "sec" or "secs" or "second" or "seconds" => 1,
            "m" or "min" or "mins" or "minute" or "minutes" => 60,
            "h" or "hr" or "hour" or "hours" => 3600,
            _ => -1
        };
        if (mult < 0) return null;
        var s = (int)Math.Round(n * mult);
        return s is >= MinInterval and <= MaxInterval ? s : null;
    }

    /// <summary>15 → "15 seconds", 120 → "2 minutes".</summary>
    public static string IntervalText(int s) =>
        s % 3600 == 0 ? $"{s / 3600} hour{(s == 3600 ? "" : "s")}" :
        s % 60 == 0 && s >= 60 ? $"{s / 60} minute{(s == 60 ? "" : "s")}" :
        $"{s} second{(s == 1 ? "" : "s")}";
}

public enum DeviceStatus { Unknown, Up, Down, Paused }

/// <summary>A device went OFF or came back ON (or a note about the monitor itself).</summary>
/// <param name="Duration">For "back ON": how long the device was OFF.</param>
/// <param name="Initial">The first result after the monitor started (a device found ON is no news: no pop-up).</param>
public record MonitorEvent(DateTime At, EventKind Kind, string DeviceId, string DeviceName, string Address, string Site, string Detail, TimeSpan? Duration = null, bool Initial = false);

public enum EventKind { Down, Up, Info }

/// <summary>One ping: when, and the reply time in ms (null = no reply).</summary>
public readonly record struct Sample(DateTime At, long? Ms);
