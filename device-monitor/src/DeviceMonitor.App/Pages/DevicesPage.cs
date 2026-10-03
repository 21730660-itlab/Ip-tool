using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace DeviceMonitor.App.Pages;

/// <summary>The device list as a table: add, edit, delete, pause, check now, import / export.</summary>
public class DevicesPage : IPage
{
    public string Title => "Devices";
    public string Subtitle => "Add the devices to watch; double-click a row for its graphs";
    public FrameworkElement View { get; }

    readonly ObservableCollection<DeviceRow> rows = new();
    readonly ICollectionView view;
    readonly DataGrid grid = Ui.Table();
    readonly TextBox search = Ui.Box(tip: "Search by name, address, site or kind");
    readonly TextBlock count = Ui.Muted("", 13.5);

    public DevicesPage()
    {
        var root = new DockPanel();

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        void Add(Button b) { if (left.Children.Count > 0) b.Margin = new Thickness(8, 0, 0, 0); left.Children.Add(b); }
        Add(Ui.IconBtn("\uE710", "Add IP", () => DeviceDialog.Add(), "Primary"));
        Add(Ui.IconBtn("\uE70F", "Edit", EditSelected));
        Add(Ui.IconBtn("\uE72C", "Check now", () => _ = App.Engine.CheckNowAsync(Selected().Select(d => d.Id).ToList()), null, "Ping the selected devices right away"));
        Add(Ui.IconBtn("\uE769", "Pause", () => App.SetEnabled(Selected().Select(d => d.Id), false), null, "Stop pinging the selected devices (they stay in the list)"));
        Add(Ui.IconBtn("\uE768", "Resume", () => App.SetEnabled(Selected().Select(d => d.Id), true)));
        Add(Ui.IconBtn("\uE74D", "Delete", DeleteSelected, "Danger"));
        DockPanel.SetDock(left, Dock.Left); bar.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        search.Width = 240;
        right.Children.Add(search);
        var imp = Ui.IconBtn("\uE8B5", "Import", () => ImportCsv(), null, "Add devices from a CSV file (Excel)"); imp.Margin = new Thickness(8, 0, 0, 0); right.Children.Add(imp);
        var exp = Ui.IconBtn("\uE78C", "Export", ExportCsv, null, "Save the device list as a CSV file (Excel)"); exp.Margin = new Thickness(8, 0, 0, 0); right.Children.Add(exp);
        bar.Children.Add(right);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);

        count.Margin = new Thickness(2, 10, 0, 0);
        DockPanel.SetDock(count, Dock.Bottom); root.Children.Add(count);

        grid.SelectionMode = DataGridSelectionMode.Extended;
        grid.Columns.Add(Ui.BadgeCol("Status", nameof(DeviceRow.StatusText), nameof(DeviceRow.StatusFg), nameof(DeviceRow.StatusBg)));
        grid.Columns.Add(Ui.BadgeCol("Kind", nameof(DeviceRow.KindText), nameof(DeviceRow.KindFg), nameof(DeviceRow.KindBg)));
        grid.Columns.Add(Ui.Col("Name", nameof(DeviceRow.Name), -2));
        grid.Columns.Add(Ui.Col("IP address", nameof(DeviceRow.Address), -1.4, true, nameof(DeviceRow.AddressKey)));
        grid.Columns.Add(Ui.Col("Site", nameof(DeviceRow.Site), -1.2));
        grid.Columns.Add(Ui.Col("Reply", nameof(DeviceRow.Reply), 90, sortPath: nameof(DeviceRow.ReplyKey)));
        grid.Columns.Add(Ui.Col("Average", nameof(DeviceRow.Avg), 90, sortPath: nameof(DeviceRow.AvgKey)));
        grid.Columns.Add(Ui.Col("Uptime", nameof(DeviceRow.Uptime), 90, sortPath: nameof(DeviceRow.UptimeKey)));
        grid.Columns.Add(Ui.Col("Loss", nameof(DeviceRow.Loss), 70, sortPath: nameof(DeviceRow.LossKey)));
        grid.Columns.Add(Ui.Col("Interval", nameof(DeviceRow.Interval), 110));
        grid.Columns.Add(Ui.Col("Status since", nameof(DeviceRow.Since), 130));
        grid.Columns.Add(Ui.Col("Last check", nameof(DeviceRow.LastCheck), 110));
        grid.ItemsSource = rows;
        view = CollectionViewSource.GetDefaultView(rows);
        view.Filter = o => Match((DeviceRow)o);
        Ui.OnRowDoubleClick(grid, o => DeviceWindow.Open(((DeviceRow)o).Id));
        grid.KeyDown += (_, e) => { if (e.Key == Key.Delete) { DeleteSelected(); e.Handled = true; } };

        var menu = new ContextMenu();
        void M(string text, Action a) { var mi = new MenuItem { Header = text }; mi.Click += (_, _) => a(); menu.Items.Add(mi); }
        M("Details and graphs", () => { if (Selected().FirstOrDefault() is Device d) DeviceWindow.Open(d.Id); });
        M("Edit…", EditSelected);
        M("Check now", () => _ = App.Engine.CheckNowAsync(Selected().Select(d => d.Id).ToList()));
        M("Continuous ping (command prompt)", () => { if (Selected().FirstOrDefault() is Device d) App.PingWindow(d); });
        M("Open in WinBox / web browser", () => { if (Selected().FirstOrDefault() is Device d) App.OpenDevice(d); });
        M("Copy IP address", () => { if (Selected().FirstOrDefault() is Device d) Clipboard.SetText(d.Address); });
        menu.Items.Add(new Separator());
        M("Pause", () => App.SetEnabled(Selected().Select(d => d.Id), false));
        M("Resume", () => App.SetEnabled(Selected().Select(d => d.Id), true));
        M("Delete", DeleteSelected);
        grid.ContextMenu = menu;
        root.Children.Add(grid);

        search.TextChanged += (_, _) => { view.Refresh(); UpdateCount(); };
        App.DevicesChanged += Sync;
        App.SiteFilterChanged += () => { view.Refresh(); UpdateCount(); };
        Sync();
        View = root;
    }

    bool Match(DeviceRow r)
    {
        if (App.Find(r.Id) is not Device d || !App.InCurrentSite(d)) return false;
        var q = search.Text.Trim();
        return q == "" || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Address.Contains(q, StringComparison.OrdinalIgnoreCase)
            || r.Site.Contains(q, StringComparison.OrdinalIgnoreCase) || r.KindText.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    List<Device> Selected() => grid.SelectedItems.Cast<DeviceRow>().Select(r => App.Find(r.Id)).Where(d => d != null).ToList();

    void EditSelected()
    {
        var d = Selected().FirstOrDefault();
        if (d == null) { Ui.Info("Select a device in the list first."); return; }
        DeviceDialog.Edit(d);
    }

    void DeleteSelected()
    {
        var list = Selected();
        if (list.Count == 0) { Ui.Info("Select the devices to delete first."); return; }
        var what = list.Count == 1 ? $"\"{list[0].Name}\" ({list[0].Address})" : $"these {list.Count} devices";
        if (Ui.Ask($"Delete {what} from the monitor?\n\nThe log files keep their past events.", "Delete devices", "Delete", true))
            App.Delete(list.Select(d => d.Id));
    }

    /// <summary>Keeps the rows in step with the device list (without losing sorting or the selection).</summary>
    void Sync()
    {
        var devs = App.Devices;
        foreach (var r in rows.Where(r => devs.All(d => d.Id != r.Id)).ToList()) rows.Remove(r);
        foreach (var d in devs)
        {
            var r = rows.FirstOrDefault(x => x.Id == d.Id);
            if (r == null) rows.Add(r = new DeviceRow(d.Id));
            r.Update(d);
        }
        view.Refresh();
        UpdateCount();
    }

    void UpdateCount()
    {
        var shown = rows.Count(Match);
        var site = App.FindSite(App.CurrentSiteId);
        count.Text = (shown == rows.Count ? $"{rows.Count} devices" : $"{shown} of {rows.Count} devices") + (site != null ? $" · site: {site.Name} (choose \"All sites\" at the top to see every device)" : "");
    }

    public void Shown() => Sync();
    public void Hidden() { }
    public void Refresh()
    {
        foreach (var r in rows) if (App.Find(r.Id) is Device d) r.Update(d);
    }

    // ------------------------------------------------------------------ CSV

    public static void ImportCsv()
    {
        var dlg = new OpenFileDialog { Title = "Import devices", Filter = "CSV file (*.csv;*.txt)|*.csv;*.txt|All files|*.*" };
        if (dlg.ShowDialog() != true) return;
        string text;
        try { text = File.ReadAllText(dlg.FileName); }
        catch (Exception e) { Ui.Info("Could not read the file:\n" + e.Message); return; }
        var list = Storage.FromCsv(text, out var problems);
        int added = App.AddMany(list, App.CurrentSiteId);
        var msg = $"{added} device(s) added.";
        if (list.Count - added > 0) msg += $"\n{list.Count - added} skipped (address already in the list).";
        if (problems.Count > 0) msg += "\n\nNot imported:\n" + string.Join("\n", problems.Take(12)) + (problems.Count > 12 ? $"\n… and {problems.Count - 12} more" : "");
        msg += "\n\nColumns: " + Storage.CsvHeader + " (only the address is required). Sites named in the Site column are created when missing; rows without a site go to the site chosen at the top.";
        Ui.Info(msg, "Import devices");
    }

    static void ExportCsv()
    {
        var dlg = new SaveFileDialog { Title = "Export devices", Filter = "CSV file (*.csv)|*.csv", FileName = $"devices-{DateTime.Now:yyyy-MM-dd}.csv" };
        if (dlg.ShowDialog() != true) return;
        try { File.WriteAllText(dlg.FileName, Storage.ToCsv(App.Devices.Where(App.InCurrentSite)), new System.Text.UTF8Encoding(true)); }
        catch (Exception e) { Ui.Info("Could not save the file:\n" + e.Message); }
    }
}

/// <summary>One row of the device table; updates itself in place every second.</summary>
public class DeviceRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;
    public string Id { get; }
    public DeviceRow(string id) { Id = id; }

    public string Name { get; private set; } = "";
    public string Address { get; private set; } = "";
    public string AddressKey { get; private set; } = "";
    public string Site { get; private set; } = "";
    public string KindText { get; private set; } = "";
    public Brush KindFg { get; private set; }
    public Brush KindBg { get; private set; }
    public string StatusText { get; private set; } = "";
    public Brush StatusFg { get; private set; }
    public Brush StatusBg { get; private set; }
    public string Reply { get; private set; } = "";
    public long ReplyKey { get; private set; }
    public string Avg { get; private set; } = "";
    public double AvgKey { get; private set; }
    public string Uptime { get; private set; } = "";
    public double UptimeKey { get; private set; }
    public string Loss { get; private set; } = "";
    public double LossKey { get; private set; }
    public string Interval { get; private set; } = "";
    public string Since { get; private set; } = "";
    public string LastCheck { get; private set; } = "";

    public void Update(Device d)
    {
        var st = App.Engine.StateOf(d.Id);
        var status = d.Enabled ? st.Status : DeviceStatus.Paused;
        Set(nameof(Name), Name, d.Name, v => Name = v);
        Set(nameof(Address), Address, d.Address, v => Address = v);
        Set(nameof(AddressKey), AddressKey, SortKey(d.Address), v => AddressKey = v);
        Set(nameof(Site), Site, d.Site, v => Site = v);
        if (Set(nameof(KindText), KindText, Theme.KindText(d.Kind), v => KindText = v) || KindFg == null)
        {
            var c = Theme.KindColor(d.Kind);
            KindFg = new SolidColorBrush(c); KindBg = Controls.ChartBase.Soft(c, 40);
            Changed(nameof(KindFg)); Changed(nameof(KindBg));
        }
        if (Set(nameof(StatusText), StatusText, Theme.StatusText(status), v => StatusText = v) || StatusFg == null || Theme.Revision != themeRev)
        {
            themeRev = Theme.Revision;
            StatusFg = Theme.B(Theme.StatusKey(status)); StatusBg = Theme.B(Theme.StatusSoftKey(status));
            Changed(nameof(StatusFg)); Changed(nameof(StatusBg));
        }
        Set(nameof(Reply), Reply, status == DeviceStatus.Down ? "no reply" : UiExtra.Ms(st.LastMs), v => Reply = v);
        ReplyKey = status == DeviceStatus.Down ? long.MaxValue : st.LastMs ?? long.MaxValue - 1;
        var lat = st.LatencyStats();
        Set(nameof(Avg), Avg, lat.HasValue ? $"{lat.Value.avg:0} ms" : "—", v => Avg = v);
        AvgKey = lat?.avg ?? double.MaxValue;
        Set(nameof(Uptime), Uptime, UiExtra.Pct(st.UptimePercent), v => Uptime = v);
        UptimeKey = st.UptimePercent ?? -1;
        Set(nameof(Loss), Loss, st.Sent == 0 ? "—" : $"{st.LossPercent:0.#}%", v => Loss = v);
        LossKey = st.LossPercent;
        Set(nameof(Interval), Interval, d.IntervalSeconds is int s ? MonitorSettings.IntervalText(s) : $"{MonitorSettings.IntervalText(App.Settings.IntervalSeconds)} (default)", v => Interval = v);
        Set(nameof(Since), Since, status is DeviceStatus.Up or DeviceStatus.Down ? EventLog.Duration(DateTime.Now - st.Since) : "—", v => Since = v);
        Set(nameof(LastCheck), LastCheck, UiExtra.Ago(st.LastCheck), v => LastCheck = v);
    }
    int themeRev = -1;

    /// <summary>Sorts IPv4 addresses by number ("10.0.0.2" before "10.0.0.10"); names after them.</summary>
    static string SortKey(string a) =>
        System.Net.IPAddress.TryParse(a, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            ? "0" + string.Concat(ip.GetAddressBytes().Select(b => b.ToString("000")))
            : "1" + a.ToLowerInvariant();

    bool Set(string prop, string old, string value, Action<string> assign)
    {
        if (old == value) return false;
        assign(value); Changed(prop);
        return true;
    }
    void Changed([CallerMemberName] string p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}
