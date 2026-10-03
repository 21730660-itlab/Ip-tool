using System.Text.Json;

namespace IpMonitor.App;

/// <summary>This computer's preferences, in %APPDATA%\IPMonitor\settings.json (the data itself is in the database file).</summary>
public class Settings
{
    public string LastFile { get; set; }
    public string LastUser { get; set; }
    public string Theme { get; set; } = "light";
    public string WinboxPath { get; set; }
    /// <summary>Live monitoring (ping every device) and how often, in seconds.</summary>
    public bool MonitorOn { get; set; } = true;
    public int MonitorSeconds { get; set; } = 60;
    public bool MonitorSound { get; set; } = true;
    /// <summary>Read the wireless connections' radio details from the routers automatically, and how often (minutes).</summary>
    public bool RadioAuto { get; set; } = true;
    public int RadioMinutes { get; set; } = 10;

    static string Dir => IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "IPMonitor");
    static string FilePath => IOPath.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try { return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings(); }
        catch { return new Settings(); }
    }

    public void Save()
    {
        try { Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { /* preferences only */ }
    }
}
