using System.Windows.Controls.Primitives;

namespace IpMonitor.App;

/// <summary>Add or edit a device. Every field writes straight into a copy of the device, so the form can be redrawn when the brand or type changes.</summary>
public class DeviceDialog
{
    static Store S => MainWindow.Instance.Store;
    readonly Device d;
    readonly bool isNew, ro;
    readonly Dlg dlg;
    readonly StackPanel body = new();

    DeviceDialog(Device original)
    {
        isNew = original == null; ro = !S.CanWrite;
        var site = MainWindow.Instance.SiteFilter != "" ? MainWindow.Instance.SiteFilter : S.Db.Sites.FirstOrDefault()?.Id ?? "";
        d = isNew ? new Device { SiteId = site, Status = "active", Vendor = "mikrotik", Type = "router" } : DbIo.Clone(original);
        dlg = new Dlg(isNew ? "Add device" : $"Device {original.Name}", 780);
        dlg.Body.Children.Add(body);
    }

    public static void Edit(Device original, Action<string> saved)
    {
        if (S.Db.Sites.Count == 0) { Ui.Info("Add a site first.", "No sites"); return; }
        var x = new DeviceDialog(original);
        x.Render();
        if (original != null && original.IsMikroTik)
            x.dlg.Extra($"Config backups ({S.ConfigCount(original.Id)})", () => ConfigDialog.Open(original));
        if (x.ro) x.dlg.Cancel("Close");
        else
        {
            x.dlg.Ok(x.isNew ? "Add device" : "Save", () =>
            {
                var created = S.SaveDevice(x.d);
                saved?.Invoke(x.d.Id);
                if (created.Count > 0) MainWindow.Instance.Toast("Network created automatically: " + string.Join(", ", created));
                return true;
            });
            x.dlg.Cancel();
        }
        x.dlg.Open();
    }

    // ------------------------------------------------------------------ field helpers (write into d)
    TextBox T(string v, Action<string> set, bool mono = false, bool multi = false)
    {
        var t = Ui.Box(v, multi, mono); t.IsReadOnly = ro;
        t.TextChanged += (_, _) => set(t.Text);
        return t;
    }
    ComboBox C(IEnumerable<(string, string)> items, string v, Action<string> set, bool redraw = false)
    {
        var c = Ui.Choice(items, v); c.IsEnabled = !ro;
        set(c.Val());
        c.SelectionChanged += (_, _) => { set(c.Val()); if (redraw) dlg.Dispatcher.BeginInvoke(Render); };
        return c;
    }
    ComboBox E(IEnumerable<string> items, string v, Action<string> set)
    {
        var c = Ui.Editable(items, v); c.IsEnabled = !ro;
        c.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => set(c.Text)));
        c.SelectionChanged += (_, _) => { if (c.SelectedItem is string s) set(s); };
        return c;
    }
    static SecretBox P(string v, Action<string> set)
    {
        var p = new SecretBox(v);
        p.PasswordChanged += () => set(p.Password);
        return p;
    }
    void Add(UIElement e) => body.Children.Add(e);

    static List<string> SplitList(string s) => (s ?? "").Split(new[] { ',', ' ', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()).Where(x => x != "").ToList();

    // ------------------------------------------------------------------ the form
    void Render()
    {
        body.Children.Clear();
        var types = d.IsMikroTik ? Store.NetTypes : Store.NetTypes.Concat(Store.EndTypes).ToArray();
        if (!types.Contains(d.Type)) d.Type = types[0];

        Add(Ui.Cols(
            Ui.Field("Site", C(S.Db.Sites.OrderBy(s => s.SiteNumber?.PadLeft(10, '0')).Select(s => (s.Id, s.ToString())), d.SiteId, v => d.SiteId = v, true)),
            Ui.Field("Brand", C(new[] { ("mikrotik", "MikroTik"), ("other", "Other brand") }, d.IsMikroTik ? "mikrotik" : "other", v => d.Vendor = v, true))));
        Add(Ui.Cols(
            Ui.Field("Type", C(types.Select(t => (t, Store.TypeLabel[t])), d.Type, v => d.Type = v, true)),
            d.IsMikroTik ? Ui.Field("Model", ModelBox(), "Type part of the name (e.g. sq, lhg, 4011) and pick it from the list — the type and ports are filled in.")
                         : Ui.Field("Brand and model", OtherModelBox(), "e.g. Dell OptiPlex, Hikvision DS-2CD")));
        Add(Ui.Cols(
            Ui.Field("Device name", T(d.Name, v => d.Name = v)),
            Ui.Field("Status", C(Store.StatusLabel.Select(kv => (kv.Key, kv.Value)), Store.StatusOf(d.Status), v => d.Status = v))));

        if (d.IsMikroTik) RenderMikroTik(); else RenderOther();

        Add(Ui.Field("Notes", T(d.Notes, v => d.Notes = v, false, true)));
    }

    void Credentials(bool winbox)
    {
        Add(Ui.Section("Login"));
        var fields = new List<UIElement> { Ui.Field("Username", Ui.WithCopy(T(d.User, v => d.User = v), () => d.User, "username")) };
        if (!ro) fields.Add(Ui.Field("Password", Ui.WithCopy(P(d.Pass, v => d.Pass = v), () => d.Pass, "password", true)));
        if (winbox) fields.Add(Ui.Field("WinBox port", T(d.Winbox, v => d.Winbox = v), "Empty = 8291"));
        Add(Ui.Cols(fields.ToArray()));
    }

    /// <summary>MikroTik model: a searchable list of MikroTik devices (and models already used in this database).</summary>
    FrameworkElement ModelBox()
    {
        IEnumerable<SuggestBox.Item> Source(string typed)
        {
            var known = MikroTikModels.Search(typed).Select(m => new SuggestBox.Item(m.Name, m.Name, $"{m.Code} · {string.Join(", ", m.Ports.Count > 6 ? m.Ports.Take(6).Append("…") : m.Ports)}", m.Family, m));
            var key = new string((typed ?? "").Where(char.IsLetterOrDigit).ToArray());
            var used = S.Db.Devices.Where(x => x.IsMikroTik && !string.IsNullOrWhiteSpace(x.Model) && MikroTikModels.Find(x.Model) == null)
                .Select(x => x.Model.Trim()).Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(m => key == "" || new string(m.Where(char.IsLetterOrDigit).ToArray()).Contains(key, StringComparison.OrdinalIgnoreCase))
                .Select(m => new SuggestBox.Item(m, m, "used before in this database", "", null));
            return known.Concat(used);
        }
        var box = new SuggestBox(d.Model, Source, "MikroTik model");
        box.Box.IsReadOnly = ro;
        box.Typed += v => d.Model = v;
        box.Picked += it =>
        {
            var oldDefaults = Store.DefaultPorts(d.Model, d.Type);
            d.Model = it.Value;
            if (it.Data is MikroTikModels.Model m)
            {
                if (Store.NetTypes.Contains(m.Type)) d.Type = m.Type;
                // the model's ports, unless the ports were already changed by hand
                if (d.Ports.Count == 0 || d.Ports.SequenceEqual(oldDefaults) || isNew) d.Ports = m.Ports.ToList();
            }
            dlg.Dispatcher.BeginInvoke(Render);
        };
        return box;
    }

    /// <summary>Other brands: a box that suggests the models already used in this database.</summary>
    FrameworkElement OtherModelBox()
    {
        IEnumerable<SuggestBox.Item> Source(string typed) => S.Db.Devices.Where(x => !x.IsMikroTik && !string.IsNullOrWhiteSpace(x.Model))
            .GroupBy(x => x.Model.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => string.IsNullOrWhiteSpace(typed) || g.Key.Contains(typed.Trim(), StringComparison.OrdinalIgnoreCase))
            .Select(g => new SuggestBox.Item(g.Key, g.Key, $"{Store.TypeLabel.GetValueOrDefault(g.First().Type, g.First().Type)} · used on {g.Count()} device{(g.Count() == 1 ? "" : "s")}", "", g.First().Type));
        var box = new SuggestBox(d.Model, Source, "Brand and model");
        box.Box.IsReadOnly = ro;
        box.Typed += v => d.Model = v;
        box.Picked += it => { d.Model = it.Value; if (it.Data is string t && Store.EndTypes.Contains(t)) { d.Type = t; dlg.Dispatcher.BeginInvoke(Render); } };
        return box;
    }

    void RenderMikroTik()
    {
        Add(Ui.Cols(
            Ui.Field("Management IP", T(d.Ip, v => d.Ip = v, true), "The address you connect to (WinBox)."),
            Ui.Field("MAC address", T(d.Mac, v => d.Mac = v, true)),
            Ui.Field("RouterOS version", T(d.Ros, v => d.Ros = v))));
        Credentials(true);

        // interfaces
        Add(Ui.Section("Interfaces"));
        var ports = T(string.Join(", ", S.PortsOf(d)), v => d.Ports = SplitList(v), true);
        var reset = Ui.Btn("Default for this model", () => { d.Ports = Store.DefaultPorts(d.Model, d.Type); Render(); }); reset.IsEnabled = !ro;
        var pr = new DockPanel(); DockPanel.SetDock(reset, Dock.Right); reset.Margin = new Thickness(10, 0, 0, 0); pr.Children.Add(reset); pr.Children.Add(ports);
        Add(Ui.Field("Ports", pr, "Separated by commas, e.g. ether1, ether2, sfp1, wlan1."));

        // bridges
        var bh = new DockPanel { Margin = new Thickness(0, 4, 0, 8) };
        var addBr = Ui.Btn("+ Add bridge", () => { d.Bridges.Add(new Bridge { Name = "bridge" + (d.Bridges.Count + 1) }); Render(); }, "Link"); addBr.IsEnabled = !ro;
        DockPanel.SetDock(addBr, Dock.Right); bh.Children.Add(addBr);
        bh.Children.Add(Ui.Text("Bridges", 14, FontWeights.SemiBold, "Ink2"));
        Add(bh);
        if (d.Bridges.Count == 0) Add(Hint("No bridges."));
        foreach (var b in d.Bridges.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var name = T(b.Name, v => b.Name = v.Trim(), true); name.ToolTip = "Bridge name";
            var bp = T(string.Join(", ", b.Ports), v => b.Ports = SplitList(v), true); bp.ToolTip = "Ports in this bridge, separated by commas"; bp.Margin = new Thickness(8, 0, 8, 0);
            var rm = Ui.Btn("Remove", () => { d.Bridges.Remove(b); Render(); }, "Danger"); rm.IsEnabled = !ro;
            Grid.SetColumn(bp, 1); Grid.SetColumn(rm, 2);
            row.Children.Add(name); row.Children.Add(bp); row.Children.Add(rm);
            Add(row);
        }
        if (d.Bridges.Count > 0) Add(Hint("Left: bridge name · middle: its ports, e.g. ether2, ether3"));

        // addresses
        var ah = new DockPanel { Margin = new Thickness(0, 12, 0, 8) };
        var addA = Ui.Btn("+ Add IP address", () => { d.Addrs.Add(new Addr { Iface = d.Bridges.FirstOrDefault()?.Name ?? "ether1" }); Render(); }, "Link"); addA.IsEnabled = !ro;
        DockPanel.SetDock(addA, Dock.Right); ah.Children.Add(addA);
        ah.Children.Add(Ui.Text("IP addresses", 14, FontWeights.SemiBold, "Ink2"));
        Add(ah);
        var ifaces = d.Bridges.Select(b => b.Name).Concat(S.PortsOf(d).Where(p => !Store.IsWifiPort(p) && !d.Bridges.Any(b => b.Ports.Contains(p))))
            .Concat(S.Db.Vlans.Where(v => v.DeviceId == d.Id && !string.IsNullOrEmpty(v.IfName)).Select(v => v.IfName)).Distinct().ToList();
        if (d.Addrs.Count == 0) Add(Hint("No IP addresses."));
        foreach (var a in d.Addrs.ToList())
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (a.Link != null)
            {
                var t = Ui.Text($"{a.Iface}   {a.Address}", 14, FontWeights.SemiBold, "Ink2", false, true); t.VerticalAlignment = VerticalAlignment.Center;
                var n = Ui.Muted("set by a wireless connection (Connections page)", 12.5); n.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumnSpan(t, 2); Grid.SetColumn(n, 2);
                row.Children.Add(t); row.Children.Add(n);
                Add(row); continue;
            }
            var ifc = E(ifaces, a.Iface, v => a.Iface = v.Trim()); ifc.ToolTip = "Interface or bridge";
            var addr = T(a.Address, v => a.Address = v, true); addr.ToolTip = "e.g. 192.168.88.1/24 — without /prefix the prefix of the site's network is used"; addr.Margin = new Thickness(8, 0, 0, 0);
            var cm = T(a.Comment, v => a.Comment = v); cm.ToolTip = "Comment"; cm.Margin = new Thickness(8, 0, 8, 0);
            var rm = Ui.Btn("Remove", () => { d.Addrs.Remove(a); Render(); }, "Danger"); rm.IsEnabled = !ro;
            Grid.SetColumn(addr, 1); Grid.SetColumn(cm, 2); Grid.SetColumn(rm, 3);
            row.Children.Add(ifc); row.Children.Add(addr); row.Children.Add(cm); row.Children.Add(rm);
            Add(row);
        }
        if (d.Addrs.Any(a => a.Link == null)) Add(Hint("Interface · address (e.g. 192.168.88.1/24) · comment. A new prefix creates its network automatically; every address is listed as a host of its network."));

        if (d.HasWifi)
        {
            Add(Ui.Section("Wireless"));
            Add(Ui.Cols(
                Ui.Field("Role", C(Store.RoleLabel.Select(kv => (kv.Key, kv.Value)), d.Role ?? "", v => d.Role = v)),
                Ui.Field("Protocol", C(Store.WProtoLabel.Select(kv => (kv.Key, kv.Value)), d.WProto ?? "", v => d.WProto = v)),
                Ui.Field("Security", C(Store.WSecLabel.Select(kv => (kv.Key, kv.Value)), d.WSec ?? "", v => d.WSec = v, true))));
            if (!ro && !string.IsNullOrEmpty(d.WSec) && d.WSec != "none") Add(Ui.Field("Pre-shared key", P(d.Psk, v => d.Psk = v)));
        }
    }

    void RenderOther()
    {
        Add(Ui.Section("Address"));
        var sources = S.IpSources(d.SiteId, d.Id);
        var items = new List<(string, string)> { ("", "Type the IP myself") };
        items.AddRange(sources.Select(s => (s.key, s.label)));
        var current = d.Uplink != null ? $"{d.Uplink.DeviceId}|{d.Uplink.Bridge}" : "";
        if (!items.Any(i => i.Item1 == current)) current = "";
        var ip = T(d.Ip, v => d.Ip = v, true);
        var info = Ui.Muted("", 12.5); info.Margin = new Thickness(1, 4, 0, 0);
        string key = current;
        void ShowInfo()
        {
            var s = sources.FirstOrDefault(x => x.key == key);
            info.Text = s.e == null ? "Optional. A single IP address, without /prefix." : $"Usable {s.e.FirstU} – {s.e.LastU}" + (string.IsNullOrEmpty(s.gw) ? "" : $" · gateway {s.gw}");
        }
        var src = C(items, current, v =>
        {
            key = v;
            var s = sources.FirstOrDefault(x => x.key == v);
            d.Uplink = s.dev != null ? new Uplink { DeviceId = s.dev.Id, Bridge = s.bridge } : null;
        });
        src.SelectionChanged += (_, _) => ShowInfo();
        var next = Ui.Btn("Next free IP", () =>
        {
            var s = sources.FirstOrDefault(x => x.key == key);
            if (s.e == null) { info.Text = "Choose where the IP comes from first."; return; }
            var net = S.Db.Networks.FirstOrDefault(n => IpMath.Parse(n.Ip)?.Text == s.e.Text);
            var used = S.UsedIn(s.e, net); used.Remove(IpMath.Parse(d.Ip)?.Text ?? "");
            var free = IpMath.NextFree(s.e, used);
            if (free == "") info.Text = "No free address left in " + s.e.Text; else ip.Text = free;
        });
        next.IsEnabled = !ro;
        ShowInfo();
        var ipRow = new DockPanel(); DockPanel.SetDock(next, Dock.Right); next.Margin = new Thickness(8, 0, 0, 0); ipRow.Children.Add(next); ipRow.Children.Add(ip);
        var ipField = new StackPanel(); ipField.Children.Add(ipRow); ipField.Children.Add(info);
        Add(Ui.Field("Takes its IP from", src, sources.Count == 0 ? "No MikroTik bridge with an address and no network in this site yet." : "A MikroTik bridge or a network of this site."));
        Add(Ui.Cols(Ui.Field("IP address", ipField), Ui.Field("MAC address", T(d.Mac, v => d.Mac = v, true))));
        if (d.HasWifi)
            Add(Ui.Field("Wireless role", C(Store.RoleLabel.Select(kv => (kv.Key, kv.Value)), d.Role ?? "", v => d.Role = v)));
        Credentials(false);
    }

    static TextBlock Hint(string t) { var h = Ui.Muted(t, 12.5); h.Margin = new Thickness(0, 0, 0, 10); return h; }
}
