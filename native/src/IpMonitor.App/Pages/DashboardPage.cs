namespace IpMonitor.App;

public class DashboardPage : PageBase
{
    public override string Title => "Dashboard";
    public override string Glyph => "";

    public override FrameworkElement Build()
    {
        var db = S.Db;
        var sp = new StackPanel();
        sp.Children.Add(Header("Dashboard", $"Welcome, {S.Me.Username}. Database: {S.FilePath}"));

        // counters
        var tiles = new UniformGridLike(5);
        void Tile(string label, int n, string glyph, Action go)
        {
            var c = new StackPanel();
            var top = new DockPanel();
            var ic = new TextBlock { Text = glyph, FontFamily = Ui.Icons, FontSize = 20 }.Res(TextBlock.ForegroundProperty, "Acc");
            DockPanel.SetDock(ic, Dock.Right); if (Ui.HasIcons) top.Children.Add(ic);
            top.Children.Add(Ui.Text(label, 14, FontWeights.SemiBold, "Muted"));
            c.Children.Add(top);
            c.Children.Add(Ui.Text(n.ToString("N0"), 32, FontWeights.Bold));
            var card = Ui.Card(c, 16); card.Cursor = Cursors.Hand;
            card.MouseLeftButtonUp += (_, _) => go();
            tiles.Add(card);
        }
        Tile("Sites", db.Sites.Count, "", () => W.Navigate<SitesPage>());
        Tile("Networks", db.Networks.Count, "", () => W.Navigate<NetworksPage>());
        Tile("Devices", db.Devices.Count, "", () => W.Navigate<DevicesPage>());
        Tile("Connections", db.Links.Count, "", () => W.Navigate<LinksPage>());
        Tile("VLANs", db.Vlans.Count, "", () => W.Navigate<VlansPage>());
        sp.Children.Add(tiles.Panel);

        // per site
        var s = Ui.Section("Sites"); s.Margin = new Thickness(0, 22, 0, 10); sp.Children.Add(s);
        if (db.Sites.Count == 0)
        {
            var empty = new StackPanel();
            empty.Children.Add(Ui.Text("No sites yet. Start by adding your first site, then its networks and devices.", 15));
            var add = AddBtn("Add a site", () => { if (SitesPage.Edit(null)) W.Navigate<SitesPage>(); });
            add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 12, 0, 0);
            empty.Children.Add(add);
            sp.Children.Add(Ui.Card(empty));
        }
        else
        {
            var g = Ui.Table();
            g.Columns.Add(Ui.Col("#", nameof(SitesPage.SiteRow.Number), 70, sortPath: nameof(SitesPage.SiteRow.NumberKey)));
            g.Columns.Add(Ui.Col("Site", nameof(SitesPage.SiteRow.Name), -2));
            g.Columns.Add(Ui.Col("Location", nameof(SitesPage.SiteRow.Location), -2));
            g.Columns.Add(Ui.Col("Networks", nameof(SitesPage.SiteRow.Networks)));
            g.Columns.Add(Ui.Col("Hosts", nameof(SitesPage.SiteRow.Hosts)));
            g.Columns.Add(Ui.Col("Devices", nameof(SitesPage.SiteRow.Devices)));
            g.Columns.Add(Ui.BadgeCol("Status", nameof(SitesPage.SiteRow.Status), nameof(SitesPage.SiteRow.Fg), nameof(SitesPage.SiteRow.Bg)));
            g.ItemsSource = SitesPage.Rows("");
            g.MaxHeight = 360;
            Ui.OnRowDoubleClick(g, r => { W.SiteFilter = ((SitesPage.SiteRow)r).Id; W.Navigate<NetworksPage>(); });
            sp.Children.Add(g);
            sp.Children.Add(Ui.Muted("Double-click a site to see its networks.", 12.5));
        }

        // recent changes
        var h = Ui.Section("Recent changes"); h.Margin = new Thickness(0, 22, 0, 10); sp.Children.Add(h);
        var recent = db.Changes.OrderByDescending(c => c.At, StringComparer.Ordinal).Take(8).ToList();
        if (recent.Count == 0) sp.Children.Add(Ui.Muted("Nothing changed yet.", 14));
        else
        {
            var g = Ui.Table();
            g.Columns.Add(Ui.Col("When", nameof(HistoryPage.ChangeRow.When), 160));
            g.Columns.Add(Ui.Col("Who", nameof(HistoryPage.ChangeRow.By), 120));
            g.Columns.Add(Ui.BadgeCol("Action", nameof(HistoryPage.ChangeRow.Action), nameof(HistoryPage.ChangeRow.Fg), nameof(HistoryPage.ChangeRow.Bg)));
            g.Columns.Add(Ui.Col("What", nameof(HistoryPage.ChangeRow.What), -1));
            g.ItemsSource = recent.Select(HistoryPage.ToRow).ToList();
            g.IsHitTestVisible = false;
            sp.Children.Add(g);
        }
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Padding = new Thickness(28, 22, 28, 22), Child = sp } };
    }
}

/// <summary>Equal columns with a gap.</summary>
public class UniformGridLike
{
    public readonly Grid Panel = new();
    int n;
    public UniformGridLike(int cols) { for (int i = 0; i < cols; i++) Panel.ColumnDefinitions.Add(new ColumnDefinition()); }
    public void Add(FrameworkElement e)
    {
        e.Margin = new Thickness(n == 0 ? 0 : 7, 0, n == Panel.ColumnDefinitions.Count - 1 ? 0 : 7, 0);
        Grid.SetColumn(e, n++); Panel.Children.Add(e);
    }
}
