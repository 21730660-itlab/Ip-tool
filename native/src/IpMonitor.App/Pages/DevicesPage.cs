using System.Diagnostics;
using Microsoft.Win32;

namespace IpMonitor.App;

public class DevicesPage : PageBase
{
    public override string Title => "Devices";
    public override string Glyph => "";
    string search = "", selId;

    public class DevRow
    {
        public string Id { get; set; }
        public string Site { get; set; }
        public string Name { get; set; }
        public string Brand { get; set; }
        public string Type { get; set; }
        public string Model { get; set; }
        public string Ip { get; set; }
        public string IpKey { get; set; }
        public string Addrs { get; set; }
        public string Mac { get; set; }
        public string Status { get; set; }
        public Brush Fg { get; set; }
        public Brush Bg { get; set; }
        public string Ping { get; set; }
        public Brush PingFg { get; set; }
        public Brush PingBg { get; set; }
        public string Configs { get; set; }
    }

    /// <summary>The address used to reach a device: its IP / management IP, else its first address.</summary>
    public static string MainIp(Device d) => !string.IsNullOrEmpty(d.Ip) ? d.Ip : d.Addrs.Select(a => IpMath.IpOf(a.Address)).FirstOrDefault(x => x != "") ?? "";

    public override FrameworkElement Build()
    {
        var g = Ui.Table();
        g.Columns.Add(Ui.Col("Site", nameof(DevRow.Site), -1.2));
        g.Columns.Add(Ui.Col("Device", nameof(DevRow.Name), -1.3));
        g.Columns.Add(Ui.Col("Brand", nameof(DevRow.Brand)));
        g.Columns.Add(Ui.Col("Type", nameof(DevRow.Type)));
        g.Columns.Add(Ui.Col("Model", nameof(DevRow.Model), -1));
        g.Columns.Add(Ui.Col("IP / management", nameof(DevRow.Ip), 0, true, nameof(DevRow.IpKey)));
        g.Columns.Add(Ui.Col("Addresses", nameof(DevRow.Addrs), -2, true));
        g.Columns.Add(Ui.Col("MAC", nameof(DevRow.Mac), 0, true));
        g.Columns.Add(Ui.BadgeCol("Status", nameof(DevRow.Status), nameof(DevRow.Fg), nameof(DevRow.Bg)));
        g.Columns.Add(Ui.Col("Configs", nameof(DevRow.Configs)));
        g.Columns.Add(Ui.BadgeCol("Ping", nameof(DevRow.Ping), nameof(DevRow.PingFg), nameof(DevRow.PingBg)));
        var empty = Ui.Muted("", 15);
        List<Device> shown = new();
        void Refresh()
        {
            shown = S.Db.Devices.Where(d => InSite(d.SiteId) && Match(search, d.Name, d.Model, d.Ip, d.Mac, d.Notes, d.User, S.SiteById(d.SiteId)?.Name, string.Join(" ", d.Addrs.Select(a => a.Address + " " + a.Iface))))
                .OrderBy(d => S.SiteById(d.SiteId)?.SiteNumber?.PadLeft(10, '0')).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var rows = shown.Select(d =>
            {
                var (fg, bg) = Theme.Status(d.Status); var ip = MainIp(d);
                return new DevRow
                {
                    Id = d.Id, Site = S.SiteById(d.SiteId)?.ToString() ?? "—", Name = d.Name, Brand = d.IsMikroTik ? "MikroTik" : "Other", Type = Store.TypeLabel.GetValueOrDefault(d.Type, d.Type),
                    Model = d.Model, Ip = d.Ip, IpKey = Ui.IpKey(ip), Addrs = string.Join(", ", d.Addrs.Select(a => $"{a.Iface} {a.Address}")), Mac = d.Mac,
                    Status = Store.StatusLabel[Store.StatusOf(d.Status)], Fg = fg, Bg = bg,
                    Ping = NetworksPage.PingText(ip, out var pf, out var pb), PingFg = pf, PingBg = pb,
                    Configs = S.ConfigCount(d.Id) is var cc && cc > 0 ? cc.ToString() : ""
                };
            }).ToList();
            g.ItemsSource = rows;
            empty.Text = rows.Count > 0 ? "" : S.Db.Sites.Count == 0 ? "Add a site first." : S.Db.Devices.Count == 0 ? "No devices yet. Click “Add device”." : "No device matches.";
            var keep = rows.FirstOrDefault(r => r.Id == selId);
            if (keep != null) { g.SelectedItem = keep; g.ScrollIntoView(keep); }
        }
        Device Sel() => g.SelectedItem is DevRow r ? S.DevById(r.Id) : null;

        var edit = Ui.IconBtn("", "Edit", () => { if (Sel() is Device d) DeviceDialog.Edit(d, id => selId = id); });
        var winbox = Ui.IconBtn("", "WinBox", () => { if (Sel() is Device d) Winbox(d); }, null, "Open this MikroTik in WinBox");
        var cfg = Ui.IconBtn("\uE8A5", "Config backups", () => { if (Sel() is Device d) ConfigDialog.Open(d); }, null, "RouterOS config backups (.rsc) of this device");
        var web = Ui.IconBtn("", "Web", () => { if (Sel() is Device d) OpenWeb(d); }, null, "Open the device's web page in the browser");
        var del = Ui.IconBtn("", "Delete", () =>
        {
            if (Sel() is not Device d) return;
            var links = S.Db.Links.Count(l => l.A == d.Id || l.B == d.Id); var cfg = S.ConfigCount(d.Id);
            Confirm($"Delete device {d.Name}?" + (links > 0 ? $"\n\nIts {links} connections are deleted too." : "") + (cfg > 0 ? $"\nIts {cfg} saved configurations are deleted too." : ""), () => S.DeleteDevice(d.Id));
        }, "Danger");
        Button pingAll = null;
        pingAll = Ui.IconBtn("", "Ping all", async () =>
        {
            var ips = shown.Select(MainIp).Where(ip => ip != "").Distinct().ToList();
            if (ips.Count == 0) { W.Toast("None of these devices has an IP address."); return; }
            pingAll.IsEnabled = false;
            foreach (var r in await Pinger.PingManyAsync(ips)) NetworksPage.Pings[r.Ip] = r;
            pingAll.IsEnabled = true;
            Refresh();
            W.Toast($"{ips.Count(ip => NetworksPage.Pings[ip].Up)} of {ips.Count} devices answered.");
        }, null, "Pings every device in the list from this computer.");
        void Buttons()
        {
            var d = Sel();
            edit.IsEnabled = d != null; del.IsEnabled = d != null && S.FullAccess;
            winbox.IsEnabled = d != null && d.IsMikroTik && MainIp(d) != ""; web.IsEnabled = d != null && MainIp(d) != "";
            cfg.IsEnabled = d != null && d.IsMikroTik;
        }
        g.SelectionChanged += (_, _) => { if (g.SelectedItem is DevRow r) selId = r.Id; Buttons(); };
        Ui.OnRowDoubleClick(g, r => DeviceDialog.Edit(S.DevById(((DevRow)r).Id), id => selId = id));
        Refresh(); Buttons();
        var bar = FilterBar(() => search, v => search = v, Refresh, true, Ui.Row(8, edit, cfg, winbox, web, pingAll, del));
        return Layout(Header("Devices", "MikroTik routers and wireless, and devices of other brands (PCs, servers, cameras…).", AddBtn("Add device", () => DeviceDialog.Edit(null, id => selId = id))), bar, TableWithEmpty(g, empty));
    }

    // ------------------------------------------------------------------ WinBox and web
    static string FindWinbox()
    {
        var s = W.Settings.WinboxPath;
        if (!string.IsNullOrEmpty(s) && File.Exists(s)) return s;
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var down = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        foreach (var dir in new[] { AppContext.BaseDirectory, IOPath.Combine(pf, "WinBox"), IOPath.Combine(pf, "MikroTik", "WinBox"), IOPath.Combine(pf, "Mikrotik"), IOPath.Combine(pf86, "WinBox"), IOPath.Combine(local, "WinBox"), IOPath.Combine(local, "Programs", "WinBox"), desk, down })
            foreach (var exe in new[] { "winbox64.exe", "winbox.exe", "WinBox.exe" })
            {
                var p = IOPath.Combine(dir, exe);
                if (File.Exists(p)) return p;
            }
        return null;
    }

    public static void Winbox(Device d)
    {
        var ip = MainIp(d);
        if (ip == "") { Ui.Info("This device has no IP address.", "WinBox"); return; }
        var exe = FindWinbox();
        if (exe == null)
        {
            if (!Ui.Ask("WinBox was not found on this computer. Show me where winbox64.exe is.\n\n(Download it from mikrotik.com if you don't have it.)", "WinBox", "Locate WinBox…")) return;
            var f = new OpenFileDialog { Title = "Where is WinBox?", Filter = "WinBox|winbox*.exe;WinBox*.exe|Programs|*.exe" };
            if (f.ShowDialog(W) != true) return;
            exe = f.FileName;
        }
        W.Settings.WinboxPath = exe; W.Settings.Save();
        var target = string.IsNullOrEmpty(d.Winbox) || d.Winbox == "8291" ? ip : ip.Contains(':') ? $"[{ip}]:{d.Winbox}" : $"{ip}:{d.Winbox}";
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = IOPath.GetDirectoryName(exe) };
        psi.ArgumentList.Add(target);
        if (!string.IsNullOrEmpty(d.User))
        {
            psi.ArgumentList.Add(d.User);
            if (S.CanWrite && !string.IsNullOrEmpty(d.Pass)) psi.ArgumentList.Add(d.Pass);
        }
        try { Process.Start(psi); W.Toast($"Opening {d.Name} ({target}) in WinBox…"); }
        catch (Exception e) { Ui.Info("Couldn't start WinBox: " + e.Message, "WinBox"); }
    }

    public static void OpenWeb(Device d)
    {
        var ip = MainIp(d);
        if (ip == "") return;
        try { Process.Start(new ProcessStartInfo("http://" + (ip.Contains(':') ? $"[{ip}]" : ip)) { UseShellExecute = true }); }
        catch (Exception e) { Ui.Info("Couldn't open the browser: " + e.Message); }
    }
}
