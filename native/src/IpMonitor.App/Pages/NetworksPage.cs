namespace IpMonitor.App;

public class NetworksPage : PageBase
{
    public override string Title => "IPs & networks";
    public override string Glyph => "";
    string search = "", selId;
    /// <summary>Last ping result per IP (kept while the app is open).</summary>
    public static readonly Dictionary<string, Pinger.Result> Pings = new();

    public class NetRow
    {
        public string Id { get; set; }
        public string Site { get; set; }
        public string Ip { get; set; }
        public string IpKey { get; set; }
        public string Name { get; set; }
        public string Vlan { get; set; }
        public string Gateway { get; set; }
        public string Range { get; set; }
        public string Used { get; set; }
        public string Status { get; set; }
        public Brush Fg { get; set; }
        public Brush Bg { get; set; }
    }
    public class HostRow
    {
        public string Id { get; set; }
        public int Num { get; set; }
        public string Ip { get; set; }
        public string IpKey { get; set; }
        public string Name { get; set; }
        public string Mac { get; set; }
        public string Source { get; set; }
        public string Notes { get; set; }
        public string Ping { get; set; }
        public Brush PingFg { get; set; }
        public Brush PingBg { get; set; }
    }

    public static string PingText(string ip, out Brush fg, out Brush bg)
    {
        fg = Theme.B("Muted"); bg = Brushes.Transparent;
        if (!Pings.TryGetValue(ip ?? "", out var p)) return "";
        if (p.Up) { fg = Theme.B("Ok"); bg = Theme.B("OkSoft"); return $"Up · {p.Ms} ms"; }
        fg = Theme.B("Sig"); bg = Theme.B("SigSoft"); return "No reply";
    }

    public override FrameworkElement Build()
    {
        var g = Ui.Table();
        g.Columns.Add(Ui.Col("Site", nameof(NetRow.Site), -1.3));
        g.Columns.Add(Ui.Col("Network", nameof(NetRow.Ip), -1.4, true, nameof(NetRow.IpKey)));
        g.Columns.Add(Ui.Col("Name", nameof(NetRow.Name), -1.3));
        g.Columns.Add(Ui.Col("VLAN", nameof(NetRow.Vlan)));
        g.Columns.Add(Ui.Col("Gateway", nameof(NetRow.Gateway), 0, true));
        g.Columns.Add(Ui.Col("Usable range", nameof(NetRow.Range), -2, true));
        g.Columns.Add(Ui.Col("Hosts", nameof(NetRow.Used)));
        g.Columns.Add(Ui.BadgeCol("Status", nameof(NetRow.Status), nameof(NetRow.Fg), nameof(NetRow.Bg)));
        var empty = Ui.Muted("", 15);
        void Refresh()
        {
            var rows = S.Db.Networks.Where(n => InSite(n.SiteId) && Match(search, n.Ip, n.Name, n.Notes, n.Gateway, n.Vlan, S.SiteById(n.SiteId)?.Name, string.Join(" ", n.HostList.Select(h => h.Ip + " " + h.Name + " " + h.Mac))))
                .OrderBy(n => S.SiteById(n.SiteId)?.SiteNumber?.PadLeft(10, '0')).ThenBy(n => Ui.IpKey(n.Ip))
                .Select(n =>
                {
                    var e = IpMath.Parse(n.Ip); var (fg, bg) = Theme.Status(n.Status);
                    return new NetRow
                    {
                        Id = n.Id, Site = S.SiteById(n.SiteId)?.ToString() ?? "—", Ip = n.Ip, IpKey = Ui.IpKey(n.Ip), Name = n.Name, Vlan = n.Vlan, Gateway = n.Gateway,
                        Range = e == null ? "" : e.IsSubnet ? $"{e.FirstU} – {e.LastU}" : "single IP",
                        Used = e == null ? "" : e.IsSubnet ? $"{n.HostList.Count} / {IpMath.CountText(e.Usable)}" : "1",
                        Status = Store.StatusLabel[Store.StatusOf(n.Status)], Fg = fg, Bg = bg
                    };
                }).ToList();
            g.ItemsSource = rows;
            empty.Text = rows.Count > 0 ? "" : S.Db.Sites.Count == 0 ? "Add a site first, then its networks." : S.Db.Networks.Count == 0 ? "No networks yet. Click “Add network”, or add a device with an IP address and the network is created for you." : "No network matches.";
            var keep = rows.FirstOrDefault(r => r.Id == selId);
            if (keep != null) { g.SelectedItem = keep; g.ScrollIntoView(keep); }
        }

        // hosts of the selected network
        var hosts = Ui.Table();
        hosts.Columns.Add(Ui.Col("#", nameof(HostRow.Num), 56));
        hosts.Columns.Add(Ui.Col("IP address", nameof(HostRow.Ip), 160, true, nameof(HostRow.IpKey)));
        hosts.Columns.Add(Ui.Col("Hostname", nameof(HostRow.Name), -2));
        hosts.Columns.Add(Ui.Col("MAC", nameof(HostRow.Mac), 170, true));
        hosts.Columns.Add(Ui.Col("From", nameof(HostRow.Source), -1.2));
        hosts.Columns.Add(Ui.Col("Notes", nameof(HostRow.Notes), -1.5));
        hosts.Columns.Add(Ui.BadgeCol("Ping", nameof(HostRow.Ping), nameof(HostRow.PingFg), nameof(HostRow.PingBg)));
        var hostTitle = Ui.Text("Select a network to see its hosts.", 16, FontWeights.Bold);
        var hostInfo = Ui.Muted("", 13);
        Network Net() => S.Db.Networks.FirstOrDefault(n => n.Id == selId);

        void ShowHosts()
        {
            var n = Net();
            if (n == null) { hostTitle.Text = "Select a network to see its hosts."; hostInfo.Text = ""; hosts.ItemsSource = null; return; }
            var e = IpMath.Parse(n.Ip);
            hostTitle.Text = $"Hosts of {n.Ip}{(string.IsNullOrEmpty(n.Name) ? "" : " · " + n.Name)}";
            var free = S.NextFreeIn(n);
            hostInfo.Text = e == null || !e.IsSubnet ? "A single IP has no host list." :
                $"Network {e.Network} · mask {(e.V == 4 ? e.Mask : "/" + e.Prefix)} · usable {e.FirstU} – {e.LastU} · {n.HostList.Count} of {IpMath.CountText(e.Usable)} used" + (free == "" ? " · FULL" : $" · next free {free}");
            hosts.ItemsSource = n.HostList.OrderBy(h => h.Num).Select(h => new HostRow
            {
                Id = h.Id, Num = h.Num, Ip = h.Ip, IpKey = Ui.IpKey(h.Ip), Name = h.Name, Mac = h.Mac, Notes = h.Notes,
                Source = h.Dev == null ? "" : $"device {S.DevById(h.Dev)?.Name}{(string.IsNullOrEmpty(h.Iface) ? "" : " · " + h.Iface)}",
                Ping = PingText(h.Ip, out var pf, out var pb), PingFg = pf, PingBg = pb
            }).ToList();
        }

        NetRow Sel() => g.SelectedItem as NetRow;
        var edit = Ui.IconBtn("", "Edit", () => { if (Net() is Network n) Edit(n); });
        var del = Ui.IconBtn("", "Delete", () =>
        {
            if (Net() is not Network n) return;
            Confirm($"Delete network {n.Ip}{(string.IsNullOrEmpty(n.Name) ? "" : " (" + n.Name + ")")} and its {n.HostList.Count} hosts?", () => S.DeleteNetwork(n.Id));
        }, "Danger");
        var addHost = Ui.IconBtn("", "Add host", () => { if (Net() is Network n) EditHost(n, null); });
        var editHost = Ui.IconBtn("", "Edit host", () => { if (Net() is Network n && hosts.SelectedItem is HostRow h) EditHost(n, h.Id); });
        var delHost = Ui.IconBtn("", "Delete host", () =>
        {
            if (Net() is not Network n || hosts.SelectedItem is not HostRow h) return;
            var host = n.HostList.First(x => x.Id == h.Id);
            if (host.Dev != null) { Ui.Info($"{h.Ip} is an address of device {S.DevById(host.Dev)?.Name}. Remove it from the device (Devices page) instead.", "Can't delete"); return; }
            Confirm($"Delete host {h.Ip} {h.Name}?", () => { var c = DbIo.Clone(n); c.HostList.RemoveAll(x => x.Id == h.Id); S.SaveNetwork(c); });
        }, "Danger");
        Button ping = null;
        ping = Ui.IconBtn("", "Ping hosts", async () =>
        {
            if (Net() is not Network n) return;
            var ips = n.HostList.Select(h => h.Ip).ToList();
            var e = IpMath.Parse(n.Ip); if (e != null && !e.IsSubnet) ips.Add(e.Text);
            if (!string.IsNullOrEmpty(n.Gateway)) ips.Add(n.Gateway);
            if (ips.Count == 0) { W.Toast("This network has no hosts to ping."); return; }
            ping.IsEnabled = false;
            foreach (var r in await Pinger.PingManyAsync(ips)) Pings[r.Ip] = r;
            ping.IsEnabled = true;
            ShowHosts();
            W.Toast($"{ips.Distinct().Count(ip => Pings[ip].Up)} of {ips.Distinct().Count()} addresses answered.");
        }, null, "Pings every host of this network from this computer.");

        void Buttons()
        {
            var n = Net(); var h = hosts.SelectedItem as HostRow;
            edit.IsEnabled = n != null; del.IsEnabled = n != null && S.FullAccess; ping.IsEnabled = n != null;
            var subnet = n != null && IpMath.Parse(n.Ip)?.IsSubnet == true;
            addHost.IsEnabled = subnet && S.CanWrite; editHost.IsEnabled = h != null; delHost.IsEnabled = h != null && S.CanWrite;
        }
        g.SelectionChanged += (_, _) => { if (Sel() is NetRow r) selId = r.Id; ShowHosts(); Buttons(); };
        hosts.SelectionChanged += (_, _) => Buttons();
        Ui.OnRowDoubleClick(g, r => Edit(S.Db.Networks.First(n => n.Id == ((NetRow)r).Id)));
        Ui.OnRowDoubleClick(hosts, r => { if (Net() is Network n) EditHost(n, ((HostRow)r).Id); });
        Refresh(); ShowHosts(); Buttons();

        // layout: networks on top, hosts below, with a splitter
        var body = new Grid();
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 120 });
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.2, GridUnitType.Star), MinHeight = 160 });
        body.Children.Add(TableWithEmpty(g, empty));
        var split = new GridSplitter { Height = 10, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, ResizeDirection = GridResizeDirection.Rows };
        Grid.SetRow(split, 1); body.Children.Add(split);
        var lower = new DockPanel();
        var lh = new DockPanel { Margin = new Thickness(0, 6, 0, 10) };
        var hb = Ui.Row(8, addHost, editHost, delHost, ping); DockPanel.SetDock(hb, Dock.Right); lh.Children.Add(hb);
        var ht = new StackPanel(); ht.Children.Add(hostTitle); ht.Children.Add(hostInfo); lh.Children.Add(ht);
        DockPanel.SetDock(lh, Dock.Top); lower.Children.Add(lh); lower.Children.Add(hosts);
        Grid.SetRow(lower, 2); body.Children.Add(lower);

        var bar = FilterBar(() => search, v => search = v, Refresh, true, Ui.Row(8, edit, del));
        return Layout(Header("IPs & networks", "Subnets and single IPs of each site, with their hosts. Addresses set on devices are listed as hosts automatically.", AddBtn("Add network", () => Edit(null))), bar, body);
    }

    /// <summary>Add (net = null) or edit a network.</summary>
    public void Edit(Network net, string prefillIp = null)
    {
        var n = net == null ? new Network { SiteId = W.SiteFilter != "" ? W.SiteFilter : S.Db.Sites.FirstOrDefault()?.Id ?? "", Status = "active", Ip = prefillIp ?? "" } : DbIo.Clone(net);
        if (S.Db.Sites.Count == 0) { Ui.Info("Add a site first.", "No sites"); return; }
        var d = new Dlg(net == null ? "Add network" : $"Edit network {net.Ip}", 640);
        var site = Ui.Choice(S.Db.Sites.OrderBy(s => s.SiteNumber?.PadLeft(10, '0')).Select(s => (s.Id, s.ToString())), n.SiteId);
        var ip = Ui.Box(n.Ip, false, true);
        var info = Ui.Muted("", 12.5); info.Margin = new Thickness(1, 4, 0, 0);
        var name = Ui.Box(n.Name); var vlan = Ui.Box(n.Vlan); var gw = Ui.Box(n.Gateway, false, true);
        var status = Ui.Choice(Store.StatusLabel, Store.StatusOf(n.Status));
        var notes = Ui.Box(n.Notes, true);
        void Info()
        {
            var e = IpMath.Parse(ip.Text);
            info.Text = ip.Text.Trim() == "" ? "Examples: 192.168.1.0/24 · 10.0.0.0 255.255.255.0 · 172.16.5.10 (a single IP)" :
                e == null ? "Not a valid address yet." : e.IsSubnet ? $"{e.Text} · mask {(e.V == 4 ? e.Mask : "/" + e.Prefix)} · usable {e.FirstU} – {e.LastU} ({IpMath.CountText(e.Usable)} hosts)" : $"Single IP {e.Text}";
        }
        ip.TextChanged += (_, _) => Info(); Info();
        var ipField = new StackPanel(); ipField.Children.Add(ip); ipField.Children.Add(info);
        d.Body.Children.Add(Ui.Field("Site", site));
        d.Body.Children.Add(Ui.Field("IP address or subnet", ipField));
        d.Body.Children.Add(Ui.Cols(Ui.Field("Name", name, "e.g. LAN, Cameras, Management"), Ui.Field("VLAN ID", vlan, "1–4094, optional")));
        d.Body.Children.Add(Ui.Cols(Ui.Field("Gateway", gw, "Optional."), Ui.Field("Status", status)));
        d.Body.Children.Add(Ui.Field("Notes", notes));
        if (!S.CanWrite) { d.Cancel("Close"); d.Open(); return; }
        d.Ok(net == null ? "Add network" : "Save", () =>
        {
            n.SiteId = site.Val(); n.Ip = ip.Text; n.Name = name.Text.Trim(); n.Vlan = vlan.Text; n.Gateway = gw.Text; n.Status = status.Val(); n.Notes = notes.Text.Trim();
            S.SaveNetwork(n); selId = n.Id; return true;
        });
        d.Cancel();
        d.Loaded += (_, _) => ip.Focus();
        d.Open();
    }

    void EditHost(Network net, string hostId)
    {
        var n = DbIo.Clone(net);
        var h = hostId == null ? null : n.HostList.First(x => x.Id == hostId);
        var isNew = h == null;
        h ??= new Host { Num = n.HostList.Count == 0 ? 1 : n.HostList.Max(x => x.Num) + 1, Ip = S.NextFreeIn(net) };
        var fromDev = h.Dev != null;
        var d = new Dlg(isNew ? $"Add host to {n.Ip}" : $"Host {h.Ip}", 560);
        var num = Ui.Box(h.Num.ToString()); var ip = Ui.Box(h.Ip, false, true); ip.IsReadOnly = fromDev;
        var name = Ui.Box(h.Name); var mac = Ui.Box(h.Mac, false, true); var notes = Ui.Box(h.Notes, true);
        if (fromDev) { var t = Ui.Text($"This address is set on device {S.DevById(h.Dev)?.Name}. Change the IP on the device; here you can rename it and add notes.", 13.5, null, "Acc"); t.Margin = new Thickness(0, 0, 0, 12); d.Body.Children.Add(t); }
        d.Body.Children.Add(Ui.Cols(Ui.Field("Host #", num), Ui.Field("IP address", ip, isNew ? "The next free address is filled in." : null)));
        d.Body.Children.Add(Ui.Field("Hostname", name));
        d.Body.Children.Add(Ui.Field("MAC address", mac, "Optional, e.g. AA:BB:CC:DD:EE:FF"));
        d.Body.Children.Add(Ui.Field("Notes", notes));
        if (!S.CanWrite) { d.Cancel("Close"); d.Open(); return; }
        d.Ok(isNew ? "Add host" : "Save", () =>
        {
            if (!int.TryParse(num.Text.Trim(), out var nr)) throw new RuleException("Host # must be a number.");
            h.Num = nr; h.Ip = ip.Text.Trim(); h.Name = name.Text.Trim(); h.Mac = mac.Text; h.Notes = notes.Text.Trim();
            if (isNew) n.HostList.Add(h);
            S.SaveNetwork(n); return true;
        });
        d.Cancel();
        d.Loaded += (_, _) => (fromDev ? name : ip).Focus();
        d.Open();
    }
}
