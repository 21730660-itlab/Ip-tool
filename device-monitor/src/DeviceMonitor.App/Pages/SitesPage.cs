using DeviceMonitor.App.Controls;

namespace DeviceMonitor.App.Pages;

/// <summary>The sites: one card each with its devices ON / OFF; add a site, then add its IPs.</summary>
public class SitesPage : IPage
{
    public string Title => "Sites";
    public string Subtitle => "Create a site first, then add the IP addresses of its devices";
    public FrameworkElement View { get; }

    readonly WrapPanel cards = new();
    readonly Border empty;
    readonly Dictionary<string, SiteCard> byId = new();
    bool dirty = true;

    public SitesPage()
    {
        var root = new StackPanel();
        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var add = Ui.IconBtn("", "Add site", () => SiteDialog.Add(), "Primary");
        DockPanel.SetDock(add, Dock.Left); bar.Children.Add(add);
        var hint = Ui.Muted("Click a site to see only its devices on the Dashboard, Devices and Event log pages.", 13.5);
        hint.VerticalAlignment = VerticalAlignment.Center; hint.Margin = new Thickness(16, 0, 0, 0);
        bar.Children.Add(hint);
        root.Children.Add(bar);

        var box = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 30) };
        var logo = MainWindow.Logo(64); logo.Margin = new Thickness(0, 0, 0, 14); box.Children.Add(logo);
        var t = Ui.Text("No sites yet", 20, FontWeights.Bold); t.HorizontalAlignment = HorizontalAlignment.Center; box.Children.Add(t);
        var s = Ui.Muted("Step 1: add a site (office, branch, customer, tower…).\nStep 2: add the IP addresses of the MikroTik routers and other devices at that site.", 14);
        s.TextAlignment = TextAlignment.Center; s.Margin = new Thickness(0, 6, 0, 16); box.Children.Add(s);
        var b = Ui.IconBtn("", "Add your first site", () => SiteDialog.Add(), "Primary"); b.HorizontalAlignment = HorizontalAlignment.Center; box.Children.Add(b);
        empty = Ui.Card(box, 24);
        root.Children.Add(empty);
        root.Children.Add(cards);

        View = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        App.DevicesChanged += () => dirty = true;
    }

    public void Shown() { dirty = true; Refresh(); }
    public void Hidden() { }

    public void Refresh()
    {
        var sites = App.Sites;
        empty.Visibility = sites.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (dirty)
        {
            dirty = false;
            cards.Children.Clear();
            foreach (var id in byId.Keys.Where(id => sites.All(x => x.Id != id)).ToList()) byId.Remove(id);
            foreach (var site in sites)
            {
                if (!byId.TryGetValue(site.Id, out var c)) byId[site.Id] = c = new SiteCard(site.Id);
                cards.Children.Add(c);
            }
        }
        foreach (var c in byId.Values) c.Update();
    }
}

/// <summary>A site on the Sites page: name, location, ring of ON / OFF, counts and buttons.</summary>
public class SiteCard : Border
{
    readonly string id;
    readonly TextBlock name = Ui.Text("", 18, FontWeights.Bold, "Ink", false);
    readonly TextBlock loc = Ui.Muted("", 13);
    readonly TextBlock cTotal, cUp, cDown, cPaused;
    readonly TextBlock offList = Ui.Text("", 13, null, "Sig");
    readonly DonutChart donut = new() { Width = 110, Height = 110 };
    readonly Border accent;

    public SiteCard(string siteId)
    {
        id = siteId;
        Width = 380; Margin = new Thickness(0, 0, 16, 16); CornerRadius = new CornerRadius(10); BorderThickness = new Thickness(1);
        this.Res(BackgroundProperty, "Panel").Res(BorderBrushProperty, "Line");

        TextBlock Count(Panel host, string label, string key)
        {
            var v = Ui.Text("0", 20, FontWeights.Bold, key, false);
            host.Children.Add(new StackPanel { Margin = new Thickness(0, 0, 18, 0), Children = { v, Ui.Muted(label, 12) } });
            return v;
        }
        var counts = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        cTotal = Count(counts, "devices", "Ink"); cUp = Count(counts, "ON", "Ok"); cDown = Count(counts, "OFF", "Sig"); cPaused = Count(counts, "paused", "Muted");

        var info = new StackPanel { Children = { name, loc, counts, offList } };
        offList.Margin = new Thickness(0, 8, 0, 0); offList.TextWrapping = TextWrapping.Wrap;
        var top = new DockPanel();
        donut.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(donut, Dock.Right); top.Children.Add(donut);
        top.Children.Add(info);

        var btns = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
        var open = Ui.IconBtn("", "Show devices", () => { App.CurrentSiteId = id; (Application.Current.MainWindow as MainWindow)?.Go("dash"); }, "Primary");
        var add = Ui.IconBtn("", "Add IP", () => DeviceDialog.Add(id)); add.Margin = new Thickness(8, 0, 0, 0);
        var edit = Ui.IconBtn("", "Edit", () => { if (App.FindSite(id) is Site s) SiteDialog.Edit(s); }); edit.Margin = new Thickness(8, 0, 0, 0);
        var del = Ui.IconBtn("", "", () => { if (App.FindSite(id) is Site s) SiteDialog.ConfirmDelete(s); }, "Danger", "Delete site"); del.Margin = new Thickness(8, 0, 0, 0);
        btns.Children.Add(open); btns.Children.Add(add); btns.Children.Add(edit); btns.Children.Add(del);

        var body = new StackPanel { Margin = new Thickness(18, 18, 18, 16), Children = { top, btns } };
        accent = new Border { Height = 5, CornerRadius = new CornerRadius(10, 10, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        Child = new Grid { Children = { body, accent } };
        Update();
    }

    public void Update()
    {
        if (App.FindSite(id) is not Site s) return;
        name.Text = s.Label;
        loc.Text = s.Location;
        loc.Visibility = s.Location == "" ? Visibility.Collapsed : Visibility.Visible;
        var devs = App.DevicesOf(id).ToList();
        int up = 0, down = 0, paused = 0, wait = 0;
        var off = new List<string>();
        foreach (var d in devs)
        {
            var st = d.Enabled ? App.Engine.StateOf(d.Id).Status : DeviceStatus.Paused;
            switch (st)
            {
                case DeviceStatus.Up: up++; break;
                case DeviceStatus.Down: down++; off.Add(d.Name); break;
                case DeviceStatus.Paused: paused++; break;
                default: wait++; break;
            }
        }
        cTotal.Text = devs.Count.ToString(); cUp.Text = up.ToString(); cDown.Text = down.ToString(); cPaused.Text = paused.ToString();
        offList.Text = down == 0 ? "" : "OFF: " + string.Join(", ", off.Take(4)) + (off.Count > 4 ? $" +{off.Count - 4}" : "");
        offList.Visibility = down == 0 ? Visibility.Collapsed : Visibility.Visible;
        donut.SetData(new[] { ((double)up, "Ok"), ((double)down, "Sig"), ((double)(paused + wait), "Idle") },
            devs.Count == 0 ? "0" : $"{100.0 * up / devs.Count:0}%", devs.Count == 0 ? "no IPs yet" : "ON");
        accent.Res(BackgroundProperty, down > 0 ? "Sig" : devs.Count > 0 && up == devs.Count ? "Ok" : "Idle");
        this.Res(BorderBrushProperty, down > 0 ? "Sig" : "Line");
    }
}
