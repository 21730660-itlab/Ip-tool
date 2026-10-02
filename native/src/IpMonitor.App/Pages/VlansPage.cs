namespace IpMonitor.App;

public class VlansPage : PageBase
{
    public override string Title => "VLANs";
    public override string Glyph => "";
    string search = "", selId;

    public class VlanRow
    {
        public string Id { get; set; }
        public string Site { get; set; }
        public int Vid { get; set; }
        public string Name { get; set; }
        public string Device { get; set; }
        public string Parent { get; set; }
        public string IfName { get; set; }
        public string Networks { get; set; }
        public string Notes { get; set; }
    }

    public override FrameworkElement Build()
    {
        var g = Ui.Table();
        g.Columns.Add(Ui.Col("Site", nameof(VlanRow.Site), -1.3));
        g.Columns.Add(Ui.Col("VLAN ID", nameof(VlanRow.Vid)));
        g.Columns.Add(Ui.Col("Name", nameof(VlanRow.Name), -1.3));
        g.Columns.Add(Ui.Col("Device", nameof(VlanRow.Device), -1));
        g.Columns.Add(Ui.Col("On", nameof(VlanRow.Parent), 0, true));
        g.Columns.Add(Ui.Col("Interface", nameof(VlanRow.IfName), 0, true));
        g.Columns.Add(Ui.Col("Networks with this VLAN", nameof(VlanRow.Networks), -1.5, true));
        g.Columns.Add(Ui.Col("Notes", nameof(VlanRow.Notes), -1.5));
        var empty = Ui.Muted("", 15);
        void Refresh()
        {
            var rows = S.Db.Vlans.Where(v => InSite(v.SiteId) && Match(search, v.Vid.ToString(), v.Name, v.Notes, v.IfName, S.DevById(v.DeviceId)?.Name))
                .OrderBy(v => S.SiteById(v.SiteId)?.SiteNumber?.PadLeft(10, '0')).ThenBy(v => v.Vid)
                .Select(v => new VlanRow
                {
                    Id = v.Id, Site = S.SiteById(v.SiteId)?.ToString() ?? "—", Vid = v.Vid, Name = v.Name, Device = S.DevById(v.DeviceId)?.Name ?? "", Parent = v.Parent, IfName = v.IfName, Notes = v.Notes,
                    Networks = string.Join(", ", S.NetsOf(v.SiteId).Where(n => n.Vlan == v.Vid.ToString()).Select(n => n.Ip))
                }).ToList();
            g.ItemsSource = rows;
            empty.Text = rows.Count > 0 ? "" : S.Db.Vlans.Count == 0 ? "No VLANs yet. Click “Add VLAN”." : "No VLAN matches.";
            var keep = rows.FirstOrDefault(r => r.Id == selId); if (keep != null) g.SelectedItem = keep;
        }
        Vlan Sel() => g.SelectedItem is VlanRow r ? S.Db.Vlans.FirstOrDefault(v => v.Id == r.Id) : null;
        var edit = Ui.IconBtn("", "Edit", () => { if (Sel() is Vlan v) Edit(v); });
        var del = Ui.IconBtn("", "Delete", () => { if (Sel() is Vlan v) Confirm($"Delete VLAN {v.Vid} {v.Name}?", () => S.DeleteVlan(v.Id)); }, "Danger");
        void Buttons() { edit.IsEnabled = Sel() != null; del.IsEnabled = Sel() != null && S.FullAccess; }
        g.SelectionChanged += (_, _) => { if (g.SelectedItem is VlanRow r) selId = r.Id; Buttons(); };
        Ui.OnRowDoubleClick(g, r => Edit(S.Db.Vlans.First(v => v.Id == ((VlanRow)r).Id)));
        Refresh(); Buttons();
        var bar = FilterBar(() => search, v => search = v, Refresh, true, Ui.Row(8, edit, del));
        return Layout(Header("VLANs", "VLAN IDs of each site, optionally with the VLAN interface on a MikroTik.", AddBtn("Add VLAN", () => Edit(null))), bar, TableWithEmpty(g, empty));
    }

    void Edit(Vlan vlan)
    {
        if (S.Db.Sites.Count == 0) { Ui.Info("Add a site first.", "No sites"); return; }
        var v = vlan == null ? new Vlan { SiteId = W.SiteFilter != "" ? W.SiteFilter : S.Db.Sites[0].Id } : DbIo.Clone(vlan);
        var d = new Dlg(vlan == null ? "Add VLAN" : $"VLAN {vlan.Vid}", 640);
        var body = new StackPanel(); d.Body.Children.Add(body);
        void Render()
        {
            body.Children.Clear();
            var site = Ui.Choice(S.Db.Sites.OrderBy(s => s.SiteNumber?.PadLeft(10, '0')).Select(s => (s.Id, s.ToString())), v.SiteId);
            site.SelectionChanged += (_, _) => { v.SiteId = site.Val(); v.DeviceId = ""; d.Dispatcher.BeginInvoke(Render); };
            var vid = Ui.Box(v.Vid == 0 ? "" : v.Vid.ToString()); vid.TextChanged += (_, _) => v.Vid = int.TryParse(vid.Text.Trim(), out var n) ? n : 0;
            var name = Ui.Box(v.Name); name.TextChanged += (_, _) => v.Name = name.Text.Trim();
            body.Children.Add(Ui.Field("Site", site));
            body.Children.Add(Ui.Cols(Ui.Field("VLAN ID", vid, "1–4094"), Ui.Field("Name", name)));
            var mts = S.Db.Devices.Where(x => x.SiteId == v.SiteId && x.IsMikroTik).OrderBy(x => x.Name).Select(x => (x.Id, x.Name)).ToList();
            var dev = Ui.Choice(new[] { ("", "— none —") }.Concat(mts), v.DeviceId ?? "");
            dev.SelectionChanged += (_, _) => { v.DeviceId = dev.Val(); d.Dispatcher.BeginInvoke(Render); };
            body.Children.Add(Ui.Field("VLAN interface on a MikroTik", dev, mts.Count == 0 ? "No MikroTik in this site." : "Optional."));
            if (S.DevById(v.DeviceId) is Device m)
            {
                var parents = m.Bridges.Select(b => b.Name).Concat(S.PortsOf(m)).ToList();
                var parent = Ui.Choice(parents.Select(p => (p, p)), string.IsNullOrEmpty(v.Parent) ? parents.FirstOrDefault() : v.Parent);
                v.Parent = parent.Val(); parent.SelectionChanged += (_, _) => v.Parent = parent.Val();
                var ifn = Ui.Box(v.IfName, false, true); ifn.TextChanged += (_, _) => v.IfName = ifn.Text.Trim();
                body.Children.Add(Ui.Cols(Ui.Field("Runs on", parent), Ui.Field("Interface name", ifn, "Empty = vlan" + (v.Vid == 0 ? "ID" : v.Vid.ToString()))));
            }
            var notes = Ui.Box(v.Notes, true); notes.TextChanged += (_, _) => v.Notes = notes.Text.Trim();
            body.Children.Add(Ui.Field("Notes", notes));
            body.IsEnabled = S.CanWrite;
        }
        Render();
        if (!S.CanWrite) d.Cancel("Close");
        else { d.Ok(vlan == null ? "Add VLAN" : "Save", () => { S.SaveVlan(v); selId = v.Id; return true; }); d.Cancel(); }
        d.Open();
    }
}
