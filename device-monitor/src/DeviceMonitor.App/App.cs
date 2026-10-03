using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;
using System.Windows.Threading;

namespace DeviceMonitor.App;

/// <summary>A rule the user broke in a form (shown in red on the dialog).</summary>
public class RuleException : Exception { public RuleException(string m) : base(m) { } }

/// <summary>
/// The parts of the program that every screen uses: saved data, settings, the monitor and the log.
/// Screens never talk to each other directly: they change things here and listen to <see cref="DevicesChanged"/>.
/// </summary>
public static class App
{
    public const string Name = "Device Monitor";
    public static string Version => typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    public static Storage Storage { get; private set; }
    public static MonitorSettings Settings { get; private set; }
    public static MonitorEngine Engine { get; private set; }
    public static EventLog Log { get; private set; }
    static List<Device> devices = new();
    static List<Site> sites = new();
    static Dispatcher ui;

    /// <summary>The device or site list changed (added, edited, removed, paused). Raised on the window thread.</summary>
    public static event Action DevicesChanged;
    /// <summary>Monitoring was started or stopped, or the interval changed. Raised on the window thread.</summary>
    public static event Action MonitorChanged;
    /// <summary>A new event was logged. Raised on the window thread.</summary>
    public static event Action<MonitorEvent> EventAdded;
    /// <summary>The site chosen in the top bar ("" = all sites). Raised on the window thread.</summary>
    public static event Action SiteFilterChanged;
    static string currentSite = "";
    /// <summary>The site chosen in the top bar; "" = all sites. Every page shows only this site's devices.</summary>
    public static string CurrentSiteId
    {
        get => FindSite(currentSite) != null ? currentSite : "";
        set { value ??= ""; if (value == currentSite) return; currentSite = value; SiteFilterChanged?.Invoke(); }
    }
    /// <summary>Does the device belong to the site chosen in the top bar?</summary>
    public static bool InCurrentSite(Device d) => CurrentSiteId == "" || d.SiteId == CurrentSiteId;

    /// <summary>Asks the main window to show a device (from a pop-up or the tray).</summary>
    public static event Action<string> ShowDeviceRequested;

    public static void Init()
    {
        ui = Dispatcher.CurrentDispatcher;
        Storage = new Storage();
        Settings = Storage.LoadSettings();
        (sites, devices) = Storage.LoadAll();
        Log = new EventLog(Settings.LogFolder);
        Log.LoadFromFile();
        Log.CleanUp(Settings.KeepLogDays);
        Engine = new MonitorEngine(new PingProbe(), () => Settings);
        Engine.SetDevices(devices);
        Engine.StatusChanged += OnStatusChanged;
        Log.Added += e => ui.BeginInvoke(() => EventAdded?.Invoke(e));
    }

    public static IReadOnlyList<Device> Devices => devices;
    public static Device Find(string id) => devices.FirstOrDefault(d => d.Id == id);
    /// <summary>Sites in number order.</summary>
    public static IReadOnlyList<Site> Sites => sites.OrderBy(s => s.Number).ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    public static Site FindSite(string id) => sites.FirstOrDefault(s => s.Id == id);
    public static IEnumerable<Device> DevicesOf(string siteId) => devices.Where(d => d.SiteId == siteId);

    // ------------------------------------------------------------------ sites

    public static void SaveSite(Site s)
    {
        s.Name = s.Name?.Trim() ?? ""; s.Location = s.Location?.Trim() ?? ""; s.Notes = s.Notes?.Trim() ?? "";
        if (s.Name == "") throw new RuleException("Enter a name for the site.");
        if (s.Number <= 0) s.Number = Core.Sites.NextNumber(sites.Where(x => x.Id != s.Id).Select(x => x.Number));
        if (sites.Any(x => x.Id != s.Id && x.Number == s.Number)) throw new RuleException($"Site number {s.Number} is already used by \"{sites.First(x => x.Id != s.Id && x.Number == s.Number).Name}\".");
        if (sites.Any(x => x.Id != s.Id && x.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase)))
            throw new RuleException($"There is already a site called \"{s.Name}\".");
        var i = sites.FindIndex(x => x.Id == s.Id);
        var old = i >= 0 ? sites[i].Name : null;
        if (i >= 0) sites[i] = s; else sites.Add(s);
        // the devices carry the site name (logs and pop-ups)
        foreach (var d in devices.Where(d => d.SiteId == s.Id)) d.Site = s.Name;
        Persist();
        if (old == null) Log.Info($"Site added: {s.Name}");
        else if (old != s.Name) Log.Info($"Site renamed: {old} → {s.Name}");
        DevicesChanged?.Invoke();
    }

    /// <summary>Deletes a site together with its devices.</summary>
    public static void DeleteSite(string id)
    {
        var s = FindSite(id); if (s == null) return;
        foreach (var d in devices.Where(d => d.SiteId == id).ToList()) { devices.Remove(d); Engine.Remove(d.Id); }
        sites.Remove(s);
        Persist();
        Log.Info($"Site removed: {s.Name}");
        DevicesChanged?.Invoke();
    }

    // ------------------------------------------------------------------ devices

    public static void Save(Device d)
    {
        d.Name = d.Name?.Trim() ?? ""; d.Address = d.Address?.Trim() ?? "";
        var site = FindSite(d.SiteId) ?? throw new RuleException("Choose the site of this device (add a site first if the list is empty).");
        d.Site = site.Name;
        if (!Validation.IsAddress(d.Address)) throw new RuleException("Enter a valid IP address (for example 192.168.88.1) or host name.");
        if (d.Name == "") d.Name = d.Address;
        if (devices.Any(x => x.Id != d.Id && x.Address.Equals(d.Address, StringComparison.OrdinalIgnoreCase)))
            throw new RuleException($"{d.Address} is already in the list.");
        var i = devices.FindIndex(x => x.Id == d.Id);
        var isNew = i < 0;
        if (isNew) devices.Add(d); else devices[i] = d;
        Persist();
        Engine.Upsert(d);
        if (isNew) Log.Info($"Device added: {d.Name} ({d.Address}) to site {d.Site}");
        DevicesChanged?.Invoke();
    }

    /// <summary>
    /// Adds many devices at once (CSV import); addresses already in the list are skipped. Returns how many were added.
    /// Each device goes to the site named in its Site column (created when missing), or to <paramref name="defaultSiteId"/>.
    /// </summary>
    public static int AddMany(IEnumerable<Device> list, string defaultSiteId = null)
    {
        int n = 0;
        foreach (var d in list)
        {
            if (devices.Any(x => x.Address.Equals(d.Address, StringComparison.OrdinalIgnoreCase))) continue;
            var site = string.IsNullOrWhiteSpace(d.Site) && FindSite(defaultSiteId) is Site def ? def : Core.Sites.GetOrAdd(sites, d.Site);
            d.SiteId = site.Id; d.Site = site.Name;
            devices.Add(d); Engine.Upsert(d); n++;
        }
        if (n > 0) { Persist(); Log.Info($"{n} device(s) imported"); DevicesChanged?.Invoke(); }
        return n;
    }

    public static void Delete(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet();
        foreach (var d in devices.Where(d => set.Contains(d.Id)).ToList())
        {
            devices.Remove(d); Engine.Remove(d.Id);
            Log.Info($"Device removed: {d.Name} ({d.Address})");
        }
        Persist();
        DevicesChanged?.Invoke();
    }

    public static void SetEnabled(IEnumerable<string> ids, bool on)
    {
        var set = ids.ToHashSet();
        foreach (var d in devices.Where(d => set.Contains(d.Id) && d.Enabled != on))
        {
            d.Enabled = on; Engine.Upsert(d);
            Log.Info($"{(on ? "Monitoring resumed" : "Monitoring paused")}: {d.Name} ({d.Address})");
        }
        Persist();
        DevicesChanged?.Invoke();
    }

    static void Persist()
    {
        try { Storage.SaveAll(sites, devices); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new RuleException("Could not save the device list: " + e.Message); }
    }

    // ------------------------------------------------------------------ monitoring

    public static void Start()
    {
        if (Engine.IsRunning) return;
        Engine.Start();
        Log.Info($"Monitoring started ({devices.Count(d => d.Enabled)} devices, every {MonitorSettings.IntervalText(Settings.IntervalSeconds)})");
        MonitorChanged?.Invoke();
    }

    public static async Task StopAsync()
    {
        if (!Engine.IsRunning) return;
        await Engine.StopAsync();
        Log.Info("Monitoring stopped");
        MonitorChanged?.Invoke();
    }

    public static void SetInterval(int seconds)
    {
        if (seconds == Settings.IntervalSeconds) return;
        Settings.IntervalSeconds = seconds;
        SaveSettings();
        Engine.RescheduleAll();
        Log.Info($"Ping interval set to {MonitorSettings.IntervalText(seconds)}");
        MonitorChanged?.Invoke();
    }

    public static void SaveSettings()
    {
        Settings.Normalize();
        try { Storage.SaveSettings(Settings); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { throw new RuleException("Could not save the settings: " + e.Message); }
        Log.Folder = Settings.LogFolder;
        MonitorChanged?.Invoke();
    }

    public static void RequestShowDevice(string id) => ShowDeviceRequested?.Invoke(id);

    static void OnStatusChanged(MonitorEvent e)
    {
        var logged = e.Initial && e.Kind == EventKind.Up ? e with { Detail = "Online at start - " + e.Detail } : e;
        Log.Write(logged);
        ui.BeginInvoke(() =>
        {
            var s = Settings;
            if (e.Kind == EventKind.Down && s.PopupOnDown)
                Toast.Show(e, true, s.Sound, s.PopupSeconds);
            else if (e.Kind == EventKind.Up && !e.Initial && s.PopupOnUp)
                Toast.Show(e, false, s.Sound, s.PopupSeconds);
        });
    }

    // ------------------------------------------------------------------ helpers

    public static void OpenPath(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception e) { Ui.Info("Could not open " + path + "\n\n" + e.Message); }
    }

    /// <summary>A command prompt with a continuous ping (ping -t).</summary>
    public static void PingWindow(Device d)
    {
        try { Process.Start(new ProcessStartInfo("cmd.exe", $"/k title Ping {d.Name} & ping -t {d.Address}") { UseShellExecute = true }); }
        catch (Exception e) { Ui.Info(e.Message); }
    }

    /// <summary>Opens WinBox for a MikroTik when winbox.exe is found next to the program, in Downloads or on the PATH; else the web page (WebFig).</summary>
    public static void OpenDevice(Device d)
    {
        if (d.Kind == DeviceKind.MikroTik)
        {
            var downloads = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var candidates = new[] { IOPath.Combine(AppContext.BaseDirectory, "winbox.exe"), IOPath.Combine(AppContext.BaseDirectory, "winbox64.exe"), IOPath.Combine(downloads, "winbox64.exe"), IOPath.Combine(downloads, "winbox.exe") };
            var wb = candidates.FirstOrDefault(File.Exists);
            if (wb != null) { try { Process.Start(new ProcessStartInfo(wb, d.Address) { UseShellExecute = true }); return; } catch (Exception) { } }
        }
        var host = d.Address.Contains(':') ? "[" + d.Address + "]" : d.Address;
        OpenPath("http://" + host + "/");
    }
}
