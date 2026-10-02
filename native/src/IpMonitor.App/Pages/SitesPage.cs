namespace IpMonitor.App;

public class SitesPage : PageBase
{
    public override string Title => "Sites";
    public override string Glyph => "";
    string search = "";

    public class SiteRow
    {
        public string Id { get; set; }
        public string Number { get; set; }
        public string NumberKey { get; set; }
        public string Name { get; set; }
        public string Location { get; set; }
        public string Contact { get; set; }
        public int Networks { get; set; }
        public int Hosts { get; set; }
        public int Devices { get; set; }
        public int Connections { get; set; }
        public string Status { get; set; }
        public Brush Fg { get; set; }
        public Brush Bg { get; set; }
    }

    public static List<SiteRow> Rows(string search) => S.Db.Sites
        .Where(s => Match(search, s.Name, s.SiteNumber, s.Location, s.Contact, s.Notes))
        .OrderBy(s => s.SiteNumber?.PadLeft(10, '0'), StringComparer.OrdinalIgnoreCase)
        .Select(s =>
        {
            var devs = S.Db.Devices.Where(d => d.SiteId == s.Id).Select(d => d.Id).ToHashSet();
            var (fg, bg) = Theme.Status(s.Status);
            return new SiteRow
            {
                Id = s.Id, Number = s.SiteNumber, NumberKey = s.SiteNumber?.PadLeft(10, '0'), Name = s.Name, Location = s.Location, Contact = s.Contact,
                Networks = S.NetsOf(s.Id).Count(), Hosts = S.NetsOf(s.Id).Sum(S.HostCount), Devices = devs.Count,
                Connections = S.Db.Links.Count(l => devs.Contains(l.A) || devs.Contains(l.B)),
                Status = Store.StatusLabel[Store.StatusOf(s.Status)], Fg = fg, Bg = bg
            };
        }).ToList();

    public override FrameworkElement Build()
    {
        var g = Ui.Table();
        g.Columns.Add(Ui.Col("#", nameof(SiteRow.Number), 70, sortPath: nameof(SiteRow.NumberKey)));
        g.Columns.Add(Ui.Col("Site", nameof(SiteRow.Name), -2));
        g.Columns.Add(Ui.Col("Location", nameof(SiteRow.Location), -2));
        g.Columns.Add(Ui.Col("Contact", nameof(SiteRow.Contact), -1.5));
        g.Columns.Add(Ui.Col("Networks", nameof(SiteRow.Networks)));
        g.Columns.Add(Ui.Col("Hosts", nameof(SiteRow.Hosts)));
        g.Columns.Add(Ui.Col("Devices", nameof(SiteRow.Devices)));
        g.Columns.Add(Ui.Col("Connections", nameof(SiteRow.Connections)));
        g.Columns.Add(Ui.BadgeCol("Status", nameof(SiteRow.Status), nameof(SiteRow.Fg), nameof(SiteRow.Bg)));
        var empty = Ui.Muted("", 15);
        void Refresh()
        {
            var rows = Rows(search); g.ItemsSource = rows;
            empty.Text = rows.Count > 0 ? "" : S.Db.Sites.Count == 0 ? "No sites yet. Click “Add site” to create the first one." : "No site matches the search.";
        }
        Refresh();
        Ui.OnRowDoubleClick(g, r => Edit(S.SiteById(((SiteRow)r).Id)));

        SiteRow Sel() => g.SelectedItem as SiteRow;
        var edit = Ui.IconBtn("", "Edit", () => { if (Sel() is SiteRow r) Edit(S.SiteById(r.Id)); });
        var nets = Ui.IconBtn("", "Networks", () => { if (Sel() is SiteRow r) { W.SiteFilter = r.Id; W.Navigate<NetworksPage>(); } });
        var devs = Ui.IconBtn("", "Devices", () => { if (Sel() is SiteRow r) { W.SiteFilter = r.Id; W.Navigate<DevicesPage>(); } });
        var del = Ui.IconBtn("", "Delete", () =>
        {
            if (Sel() is not SiteRow r) return;
            Confirm($"Delete site #{r.Number} {r.Name}?\n\nIts {r.Networks} networks, {r.Devices} devices and {r.Connections} connections are deleted too. This can't be undone (restore a backup if needed).", () => S.DeleteSite(r.Id));
        }, "Danger");
        foreach (var b in new[] { edit, nets, devs, del }) b.IsEnabled = false;
        g.SelectionChanged += (_, _) => { var on = Sel() != null; edit.IsEnabled = on; nets.IsEnabled = on; devs.IsEnabled = on; del.IsEnabled = on && S.FullAccess; };

        var bar = FilterBar(() => search, v => search = v, Refresh, false, Ui.Row(8, edit, nets, devs, del));
        return Layout(Header("Sites", $"{S.Db.Sites.Count} sites. Double-click a site to edit it.", AddBtn("Add site", () => Edit(null))), bar, TableWithEmpty(g, empty));
    }

    /// <summary>Add (site = null) or edit a site. Returns true when saved.</summary>
    public static bool Edit(Site site)
    {
        var s = site == null ? new Site { Status = "active" } : DbIo.Clone(site);
        var d = new Dlg(site == null ? "Add site" : $"Edit site #{site.SiteNumber}", 620);
        var name = Ui.Box(s.Name); var num = Ui.Box(s.SiteNumber);
        var loc = Ui.Box(s.Location); var contact = Ui.Box(s.Contact);
        var status = Ui.Choice(Store.StatusLabel, Store.StatusOf(s.Status));
        var notes = Ui.Box(s.Notes, true);
        d.Body.Children.Add(Ui.Cols(Ui.Field("Site name", name), Ui.Field("Site number", num, "Must be unique.")));
        d.Body.Children.Add(Ui.Cols(Ui.Field("Location", loc), Ui.Field("Contact", contact)));
        d.Body.Children.Add(Ui.Field("Status", status));
        d.Body.Children.Add(Ui.Field("Notes", notes));
        if (!S.CanWrite) { d.Cancel("Close"); d.Open(); return false; }
        d.Ok(site == null ? "Add site" : "Save", () =>
        {
            s.Name = name.Text; s.SiteNumber = num.Text; s.Location = loc.Text.Trim(); s.Contact = contact.Text.Trim(); s.Status = status.Val(); s.Notes = notes.Text.Trim();
            S.SaveSite(s); return true;
        });
        d.Cancel();
        d.Loaded += (_, _) => name.Focus();
        return d.Open();
    }
}
