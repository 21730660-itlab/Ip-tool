namespace IpMonitor.App;

public class HistoryPage : PageBase
{
    public override string Title => "History";
    public override string Glyph => "";
    string search = "";

    public class ChangeRow
    {
        public string When { get; set; }
        public string At { get; set; }
        public string By { get; set; }
        public string Action { get; set; }
        public string Kind { get; set; }
        public string What { get; set; }
        public string Site { get; set; }
        public string Details { get; set; }
        public Brush Fg { get; set; }
        public Brush Bg { get; set; }
    }

    public static ChangeRow ToRow(Change c)
    {
        var (fg, bg) = c.Act switch
        {
            "add" => ("Ok", "OkSoft"), "delete" => ("Sig", "SigSoft"), "edit" => ("Acc", "AccSoft"), _ => ("Warn", "WarnSoft")
        };
        var when = DateTime.TryParse(c.At, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : c.At;
        var act = c.Act switch { "add" => "Added", "edit" => "Changed", "delete" => "Deleted", "import" => "Imported", "clear" => "Cleared", _ => c.Act };
        return new ChangeRow
        {
            When = when, At = c.At, By = c.By, Action = act, Kind = c.Kind, What = $"{c.Kind} · {c.Label}",
            Site = S.SiteById(c.SiteId)?.ToString() ?? "", Details = string.Join("  ·  ", c.Lines ?? new()),
            Fg = Theme.B(fg), Bg = Theme.B(bg)
        };
    }

    public override FrameworkElement Build()
    {
        var g = Ui.Table();
        g.Columns.Add(Ui.Col("When", nameof(ChangeRow.When), 150, false, nameof(ChangeRow.At)));
        g.Columns.Add(Ui.Col("Who", nameof(ChangeRow.By), 120));
        g.Columns.Add(Ui.BadgeCol("Action", nameof(ChangeRow.Action), nameof(ChangeRow.Fg), nameof(ChangeRow.Bg)));
        g.Columns.Add(Ui.Col("What", nameof(ChangeRow.What), -1.6));
        g.Columns.Add(Ui.Col("Site", nameof(ChangeRow.Site), -1));
        g.Columns.Add(Ui.Col("Details", nameof(ChangeRow.Details), -2.4, false, null, true));
        var empty = Ui.Muted("", 15);
        var count = Ui.Muted("", 13);
        List<Change> shown = new();
        void Refresh()
        {
            shown = S.Db.Changes.Where(c => InSite(c.SiteId) || W.SiteFilter == "")
                .Where(c => W.SiteFilter == "" || c.SiteId == W.SiteFilter)
                .Where(c => Match(search, c.By, c.Label, c.Kind, c.Act, string.Join(" ", c.Lines ?? new())))
                .OrderByDescending(c => c.At, StringComparer.Ordinal).Take(1500).ToList();
            var rows = shown.Select(ToRow).ToList();
            g.ItemsSource = rows;
            count.Text = $"{rows.Count} of {S.Db.Changes.Count} changes";
            empty.Text = rows.Count > 0 ? "" : "No changes recorded.";
        }
        Refresh();
        Ui.OnRowDoubleClick(g, r =>
        {
            var c = (ChangeRow)r;
            Ui.Info($"{c.When} · {c.By}\n{c.Action} {c.What}{(c.Site == "" ? "" : "\nSite: " + c.Site)}\n\n" + (c.Details == "" ? "No details." : c.Details.Replace("  ·  ", "\n")), "Change");
        });
        var clear = Ui.IconBtn("", "Clear history", () => Confirm($"Delete all {S.Db.Changes.Count} history entries? The data itself is not changed.", S.ClearHistory), "Danger");
        clear.IsEnabled = S.FullAccess && S.Db.Changes.Count > 0;
        var pdf = Ui.IconBtn("\uEA90", "Export PDF…", () =>
        {
            if (shown.Count == 0) { Ui.Info("There is nothing to export with this filter.", "History"); return; }
            var scope = W.SiteFilter == "" ? "All sites" : $"Site {S.SiteById(W.SiteFilter)}";
            if (!string.IsNullOrWhiteSpace(search)) scope += $" · search “{search.Trim()}”";
            var f = new Microsoft.Win32.SaveFileDialog { FileName = $"IP-Monitor-history_{DateTime.Now:yyyy-MM-dd}.pdf", Filter = "PDF document (*.pdf)|*.pdf" };
            if (f.ShowDialog(W) != true) return;
            try
            {
                File.WriteAllBytes(f.FileName, new Report(S, null).HistoryPdf(shown, scope, S.Me?.Username));
                if (Ui.Ask($"{IOPath.GetFileName(f.FileName)} was saved ({shown.Count} changes).\n\nOpen it now?", "History exported", "Open", false, "Close"))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(f.FileName) { UseShellExecute = true });
            }
            catch (Exception e) { Ui.Info("Couldn't save the PDF: " + e.Message, "History"); }
        }, "Primary", "Save the history shown here (with the site filter and search) as a PDF");
        var bar = FilterBar(() => search, v => search = v, Refresh, true, count);
        return Layout(Header("History", "Who changed what, and when. Double-click an entry for its details. Passwords and keys are never written here.", pdf, clear), bar, TableWithEmpty(g, empty));
    }
}
