using System.Diagnostics;
using Microsoft.Win32;

namespace IpMonitor.App;

/// <summary>PDF report (with site map pictures) and Excel workbook.</summary>
public class ReportsPage : PageBase
{
    public override string Title => "Reports";
    public override string Glyph => "";

    // choices kept while the app is open
    string scope = "";
    bool maps = true, nets = true, hosts = true, devs = true, links = true, vlans = true, cfgs = true, passwords;

    public override FrameworkElement Build()
    {
        var sp = new StackPanel();
        sp.Children.Add(Header("Reports", "A PDF report to print or send, and an Excel workbook with one sheet per list."));

        var items = new List<(string, string)> { ("", "All sites") };
        items.AddRange(S.Db.Sites.OrderBy(s => s.SiteNumber?.PadLeft(10, '0')).Select(s => (s.Id, $"#{s.SiteNumber} · {s.Name}")));
        if (scope != "" && S.SiteById(scope) == null) scope = "";
        var site = Ui.Choice(items, scope); site.Width = 320;
        site.SelectionChanged += (_, _) => scope = site.Val();
        var scopeCard = Ui.Card(Ui.Field("Sites in the report", site, "Choose one site, or all sites."), 16);
        sp.Children.Add(scopeCard);

        CheckBox Cb(string text, bool value, Action<bool> set)
        {
            var c = new CheckBox { Content = text, IsChecked = value, Margin = new Thickness(0, 0, 22, 8), FontSize = 14 };
            c.Checked += (_, _) => set(true); c.Unchecked += (_, _) => set(false);
            return c;
        }
        WrapPanel Checks() => new() { Margin = new Thickness(0, 12, 0, 6) };

        // PDF
        var pdf = new StackPanel();
        pdf.Children.Add(Ui.Text("PDF report", 18, FontWeights.Bold));
        pdf.Children.Add(Ui.Muted("A summary page, then one section per site: its site map picture, networks, hosts, devices, connections, VLANs and config backups. Device passwords are never put in the PDF.", 13.5));
        var pc = Checks();
        pc.Children.Add(Cb("Site map pictures", maps, v => maps = v));
        pc.Children.Add(Cb("Networks", nets, v => nets = v));
        pc.Children.Add(Cb("Hosts", hosts, v => hosts = v));
        pc.Children.Add(Cb("Devices", devs, v => devs = v));
        pc.Children.Add(Cb("Connections", links, v => links = v));
        pc.Children.Add(Cb("VLANs", vlans, v => vlans = v));
        pc.Children.Add(Cb("Config backup list", cfgs, v => cfgs = v));
        pdf.Children.Add(pc);
        var pb = Ui.IconBtn("", "Create PDF report…", CreatePdf, "Primary"); pb.HorizontalAlignment = HorizontalAlignment.Left;
        pdf.Children.Add(pb);
        var pdfCard = Ui.Card(pdf, 20); pdfCard.Margin = new Thickness(0, 16, 0, 0);
        sp.Children.Add(pdfCard);

        // Excel
        var xl = new StackPanel();
        xl.Children.Add(Ui.Text("Excel workbook (.xlsx)", 18, FontWeights.Bold));
        xl.Children.Add(Ui.Muted("Sheets: Summary, Sites, Networks, Hosts, Devices, Connections, VLANs and Config backups — with filter buttons and the header row kept visible. The same choices as above are used.", 13.5));
        var xc = Checks();
        if (S.FullAccess) xc.Children.Add(Cb("Include device passwords and wireless keys", passwords, v => passwords = v));
        xl.Children.Add(xc);
        var xb = Ui.IconBtn("", "Create Excel workbook…", CreateExcel, "Primary"); xb.HorizontalAlignment = HorizontalAlignment.Left;
        xl.Children.Add(xb);
        var xlCard = Ui.Card(xl, 20); xlCard.Margin = new Thickness(0, 16, 0, 0);
        sp.Children.Add(xlCard);

        var more = Ui.Muted("Plain CSV files and a backup copy of the database are on the Backup & export page.", 13);
        more.Margin = new Thickness(0, 14, 0, 0);
        sp.Children.Add(more);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Padding = new Thickness(28, 22, 28, 22), Child = sp } };
    }

    Report.Options Options(bool forPdf)
    {
        var o = new Report.Options
        {
            SiteIds = scope == "" ? null : new HashSet<string> { scope },
            Networks = nets, Hosts = hosts, Devices = devs, Links = links, Vlans = vlans, Configs = cfgs,
            Passwords = !forPdf && passwords && S.FullAccess
        };
        if (forPdf && maps)
        {
            // pictures are drawn in the light theme, like paper
            var dark = Theme.Dark;
            if (dark) Theme.Apply(false);
            try
            {
                foreach (var s in S.Db.Sites.Where(s => o.SiteIds == null || o.SiteIds.Contains(s.Id)))
                    if (MapPage.RenderSite(s.Id) is { } img) o.Maps[s.Id] = img;
            }
            finally { if (dark) Theme.Apply(true); }
        }
        return o;
    }

    string ScopeName() => scope == "" ? "all-sites" : System.Text.RegularExpressions.Regex.Replace($"site-{S.SiteById(scope)?.SiteNumber}-{S.SiteById(scope)?.Name}", @"[^A-Za-z0-9._\-]+", "_");

    void CreatePdf()
    {
        if (S.Db.Sites.Count == 0) { Ui.Info("Add a site first.", "Reports"); return; }
        var f = new SaveFileDialog { FileName = $"IP-Monitor-report_{ScopeName()}_{DateTime.Now:yyyy-MM-dd}.pdf", Filter = "PDF document (*.pdf)|*.pdf" };
        if (f.ShowDialog(W) != true) return;
        Write(f.FileName, () => new Report(S, Options(true)).Pdf(S.Me?.Username));
    }

    void CreateExcel()
    {
        if (S.Db.Sites.Count == 0) { Ui.Info("Add a site first.", "Reports"); return; }
        if (passwords && S.FullAccess && !Ui.Ask("The workbook will contain device passwords and wireless keys in plain text. Keep it somewhere safe.", "Passwords", "Create")) return;
        var f = new SaveFileDialog { FileName = $"IP-Monitor_{ScopeName()}_{DateTime.Now:yyyy-MM-dd}.xlsx", Filter = "Excel workbook (*.xlsx)|*.xlsx" };
        if (f.ShowDialog(W) != true) return;
        Write(f.FileName, () => new Report(S, Options(false)).Excel(S.Me?.Username));
    }

    static void Write(string path, Func<byte[]> make)
    {
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            byte[] data;
            try { data = make(); } finally { Mouse.OverrideCursor = null; }
            File.WriteAllBytes(path, data);
            if (Ui.Ask($"{IOPath.GetFileName(path)} was saved ({Rsc.Size(data.Length)}).\n\nOpen it now?", "Report ready", "Open", false, "Close"))
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (IOException e) { Ui.Info("Couldn't save the file (is it open in another program?)\n\n" + e.Message, "Reports"); }
        catch (Exception e) when (e is UnauthorizedAccessException or System.ComponentModel.Win32Exception) { Ui.Info(e.Message, "Reports"); }
    }
}
