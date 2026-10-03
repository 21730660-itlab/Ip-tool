using DeviceMonitor.App.Controls;

namespace DeviceMonitor.App.Pages;

/// <summary>Every event (OFF / ON / notes) with filters, two charts, and buttons to open the log files.</summary>
public class EventsPage : IPage
{
    public string Title => "Event log";
    public string Subtitle => "Saved in the log folder: events.csv (Excel) and one text file per day";
    public FrameworkElement View { get; }

    readonly DataGrid grid = Ui.Table();
    readonly TextBox search = Ui.Box(tip: "Search by device, address or text");
    readonly ComboBox kind = Ui.Choice(new[] { ("all", "All events"), ("down", "OFF only"), ("up", "ON only"), ("info", "Notes only") }, "all");
    readonly ComboBox period = Ui.Choice(new[] { ("1", "Last 24 hours"), ("7", "Last 7 days"), ("30", "Last 30 days"), ("0", "Everything") }, "7");
    readonly ColumnChart perHour = new() { Height = 150 };
    readonly BarChart outages = new() { Height = 150, Empty = "No outages in this period" };
    readonly TextBlock count = Ui.Muted("", 13.5);
    readonly TextBlock folder = Ui.Muted("", 13);
    bool dirty = true;

    public EventsPage()
    {
        var root = new DockPanel();

        var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        search.Width = 260; kind.Width = 160; period.Width = 170;
        kind.Margin = new Thickness(10, 0, 0, 0); period.Margin = new Thickness(10, 0, 0, 0);
        left.Children.Add(search); left.Children.Add(kind); left.Children.Add(period);
        DockPanel.SetDock(left, Dock.Left); bar.Children.Add(left);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var openCsv = Ui.IconBtn("", "Open in Excel", () => { if (File.Exists(App.Log.CsvPath)) App.OpenPath(App.Log.CsvPath); else Ui.Info("No events have been written yet."); });
        var openFolder = Ui.IconBtn("", "Open log folder", () => App.OpenPath(App.Log.Folder)); openFolder.Margin = new Thickness(8, 0, 0, 0);
        right.Children.Add(openCsv); right.Children.Add(openFolder);
        bar.Children.Add(right);
        DockPanel.SetDock(bar, Dock.Top); root.Children.Add(bar);

        var charts = new Grid { Margin = new Thickness(0, 0, 0, 14) };
        charts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.4, GridUnitType.Star) });
        charts.ColumnDefinitions.Add(new ColumnDefinition());
        var legend = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        void L(string key, string text)
        {
            legend.Children.Add(new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(2), Margin = new Thickness(12, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center }.Res(Border.BackgroundProperty, key));
            legend.Children.Add(Ui.Muted(text, 12.5));
        }
        L("Sig", "OFF"); L("Ok", "ON");
        var h1 = new DockPanel(); DockPanel.SetDock(legend, Dock.Right); h1.Children.Add(legend); h1.Children.Add(Ui.Section("Events over time"));
        var c1 = Ui.Card(new StackPanel { Children = { h1, perHour } }, 16); c1.Margin = new Thickness(0, 0, 7, 0);
        var c2 = Ui.Card(new StackPanel { Children = { Ui.Section("Most outages"), outages } }, 16); c2.Margin = new Thickness(7, 0, 0, 0);
        Grid.SetColumn(c2, 1); charts.Children.Add(c1); charts.Children.Add(c2);
        DockPanel.SetDock(charts, Dock.Top); root.Children.Add(charts);

        var foot = new DockPanel { Margin = new Thickness(2, 10, 0, 0) };
        DockPanel.SetDock(folder, Dock.Right); foot.Children.Add(folder); foot.Children.Add(count);
        DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot);

        grid.Columns.Add(Ui.Col("Date", nameof(EventRow.Date), 110, sortPath: nameof(EventRow.Sort)));
        grid.Columns.Add(Ui.Col("Time", nameof(EventRow.Time), 90, sortPath: nameof(EventRow.Sort)));
        grid.Columns.Add(Ui.BadgeCol("Event", nameof(EventRow.Kind), nameof(EventRow.Fg), nameof(EventRow.Bg)));
        grid.Columns.Add(Ui.Col("Device", nameof(EventRow.Device), -1.4));
        grid.Columns.Add(Ui.Col("IP address", nameof(EventRow.Address), -1, true));
        grid.Columns.Add(Ui.Col("Group", nameof(EventRow.Group), -0.9));
        grid.Columns.Add(Ui.Col("Detail", nameof(EventRow.Detail), -2.2));
        grid.Columns.Add(Ui.Col("Was OFF for", nameof(EventRow.Duration), 120, sortPath: nameof(EventRow.DurationKey)));
        Ui.OnRowDoubleClick(grid, o =>
        {
            var r = (EventRow)o;
            var d = App.Devices.FirstOrDefault(x => x.Id == r.DeviceId) ?? App.Devices.FirstOrDefault(x => x.Address == r.Address && r.Address != "");
            if (d != null) DeviceWindow.Open(d.Id);
        });
        root.Children.Add(grid);

        search.TextChanged += (_, _) => Fill();
        kind.SelectionChanged += (_, _) => Fill();
        period.SelectionChanged += (_, _) => Fill();
        App.EventAdded += _ => dirty = true;
        View = root;
    }

    public void Shown() => Fill();
    public void Hidden() { }
    public void Refresh() { if (dirty) Fill(); }

    void Fill()
    {
        dirty = false;
        var days = int.Parse(period.Val() == "" ? "7" : period.Val());
        var since = days == 0 ? DateTime.MinValue : DateTime.Now.AddDays(-days);
        var q = search.Text.Trim();
        var k = kind.Val();
        var inPeriod = App.Log.Recent.Where(e => e.At >= since).ToList();
        var list = inPeriod
            .Where(e => k switch { "down" => e.Kind == EventKind.Down, "up" => e.Kind == EventKind.Up, "info" => e.Kind == EventKind.Info, _ => true })
            .Where(e => q == "" || e.DeviceName.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Address.Contains(q, StringComparison.OrdinalIgnoreCase)
                        || e.Detail.Contains(q, StringComparison.OrdinalIgnoreCase) || e.Group.Contains(q, StringComparison.OrdinalIgnoreCase))
            .Select(e => new EventRow(e)).ToList();
        grid.ItemsSource = list;
        count.Text = $"{list.Count} events shown" + (App.Log.Recent.Count >= EventLog.Keep ? $" (the newest {EventLog.Keep}; everything is in events.csv)" : "");
        folder.Text = "Log folder: " + App.Log.Folder;

        // events per hour (24 h) or per day (longer)
        var dev = inPeriod.Where(e => e.Kind != EventKind.Info).ToList();
        if (days == 1)
        {
            var start = DateTime.Now.AddHours(-23); start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0);
            perHour.SetData(Enumerable.Range(0, 24).Select(i =>
            {
                var a = start.AddHours(i); var b = a.AddHours(1);
                return (a.ToString("HH"), dev.Count(e => e.Kind == EventKind.Down && e.At >= a && e.At < b), dev.Count(e => e.Kind == EventKind.Up && e.At >= a && e.At < b));
            }));
        }
        else
        {
            var n = days == 0 ? Math.Clamp((int)Math.Ceiling((DateTime.Today - (dev.Count > 0 ? dev.Min(e => e.At).Date : DateTime.Today)).TotalDays) + 1, 7, 60) : days;
            var start = DateTime.Today.AddDays(-(n - 1));
            perHour.SetData(Enumerable.Range(0, n).Select(i =>
            {
                var a = start.AddDays(i); var b = a.AddDays(1);
                return (a.ToString("dd/MM"), dev.Count(e => e.Kind == EventKind.Down && e.At >= a && e.At < b), dev.Count(e => e.Kind == EventKind.Up && e.At >= a && e.At < b));
            }));
        }
        outages.SetData(dev.Where(e => e.Kind == EventKind.Down).GroupBy(e => e.DeviceName + " (" + e.Address + ")")
            .Select(g => (g.Key, (double)g.Count(), g.Count() + "×", "Sig")).OrderByDescending(x => x.Item2).Take(5));
    }
}

public class EventRow
{
    public EventRow(MonitorEvent e)
    {
        Date = e.At.ToString("yyyy-MM-dd"); Time = e.At.ToString("HH:mm:ss"); Sort = e.At;
        Kind = EventLog.KindText(e.Kind);
        var key = e.Kind switch { EventKind.Down => "Sig", EventKind.Up => "Ok", _ => "Acc" };
        Fg = Theme.B(key); Bg = Theme.B(key + "Soft");
        Device = e.DeviceName; Address = e.Address; Group = e.Group; Detail = e.Detail; DeviceId = e.DeviceId;
        Duration = e.Duration is TimeSpan t ? EventLog.Duration(t) : "";
        DurationKey = e.Duration?.TotalSeconds ?? -1;
    }
    public string Date { get; } public string Time { get; } public DateTime Sort { get; }
    public string Kind { get; } public Brush Fg { get; } public Brush Bg { get; }
    public string Device { get; } public string Address { get; } public string Group { get; } public string Detail { get; }
    public string Duration { get; } public double DurationKey { get; } public string DeviceId { get; }
}
