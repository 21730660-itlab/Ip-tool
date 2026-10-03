namespace DeviceMonitor.App;

/// <summary>Building blocks specific to this program.</summary>
public static class UiExtra
{
    /// <summary>
    /// The interval drop-down: pick 5 s … 10 min from the list, or type any value ("45", "90 s", "3 min").
    /// With a default text, the first item means "use the global interval" (value null).
    /// </summary>
    public static ComboBox IntervalBox(int? current, string defaultText = null, double width = 170)
    {
        var items = new List<string>();
        if (defaultText != null) items.Add(defaultText);
        items.AddRange(MonitorSettings.IntervalChoices.Select(MonitorSettings.IntervalText));
        var c = new ComboBox { IsEditable = true, IsTextSearchEnabled = false, Width = width, ItemsSource = items, ToolTip = "Choose from the list or type a number of seconds (for example 45), or \"2 min\"" };
        c.Text = current is int s ? MonitorSettings.IntervalText(s) : defaultText ?? "";
        return c;
    }

    /// <summary>Reads an interval box: (ok, seconds or null for "default").</summary>
    public static (bool ok, int? seconds) ReadInterval(ComboBox c, string defaultText = null)
    {
        var t = c.Text?.Trim() ?? "";
        if (defaultText != null && (t == "" || t == defaultText)) return (true, null);
        var s = MonitorSettings.ParseInterval(t);
        return s.HasValue ? (true, s) : (false, null);
    }

    /// <summary>Choices of a site drop-down: "All sites (n devices)", then "1. Main office (5 devices, 1 OFF)"…</summary>
    public static List<Opt> SiteOptions()
    {
        var items = new List<Opt> { new("", $"All sites ({App.Sites.Count} sites, {App.Devices.Count} devices)") };
        foreach (var s in App.Sites)
        {
            var devs = App.DevicesOf(s.Id).ToList();
            var off = devs.Count(d => d.Enabled && App.Engine.StateOf(d.Id).Status == DeviceStatus.Down);
            items.Add(new(s.Id, $"{s.Label}  ({devs.Count} device{(devs.Count == 1 ? "" : "s")}{(off > 0 ? $", {off} OFF" : "")})"));
        }
        return items;
    }

    /// <summary>The coloured square with the short name of the kind (MT, SW, AP...).</summary>
    public static Border KindBadge(DeviceKind k, double size = 40)
    {
        var col = Theme.KindColor(k);
        return new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.24),
            Background = new LinearGradientBrush(col, Color.FromArgb(255, (byte)(col.R * 0.75), (byte)(col.G * 0.75), (byte)(col.B * 0.75)), 45),
            Child = new TextBlock { Text = Theme.KindShort(k), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = size * (Theme.KindShort(k).Length > 2 ? 0.28 : 0.34), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>A pill such as "● ON".</summary>
    public static Border StatusPill(DeviceStatus s, double size = 12.5)
    {
        var dot = new Shapes.Ellipse { Width = size * 0.7, Height = size * 0.7, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.SetResourceReference(Shapes.Shape.FillProperty, Theme.StatusKey(s));
        var t = Ui.Text(Theme.StatusText(s), size, FontWeights.Bold, Theme.StatusKey(s), false);
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Children = { dot, t } };
        return new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(10, 3, 12, 3), Child = sp, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left }
            .Res(Border.BackgroundProperty, Theme.StatusSoftKey(s));
    }

    public static string Ms(long? ms) => ms.HasValue ? (ms.Value < 1 ? "<1 ms" : $"{ms} ms") : "—";
    public static string Pct(double? p) => p.HasValue ? (p.Value >= 99.995 ? "100%" : $"{p.Value:0.00}%") : "—";

    /// <summary>"5 s ago", "3 min ago".</summary>
    public static string Ago(DateTime? t)
    {
        if (t == null) return "never";
        var s = DateTime.Now - t.Value;
        return s.TotalSeconds < 60 ? $"{Math.Max(0, (int)s.TotalSeconds)} s ago" : s.TotalMinutes < 60 ? $"{(int)s.TotalMinutes} min ago" : s.TotalHours < 24 ? $"{(int)s.TotalHours} h ago" : t.Value.ToString("g");
    }

    /// <summary>A key figure tile: label, big value, small note, coloured accent.</summary>
    public static (Border tile, TextBlock value, TextBlock note) Kpi(string label, string accentKey, string glyph)
    {
        var value = Ui.Text("—", 30, FontWeights.Bold, "Ink", false);
        var note = Ui.Muted("", 13);
        var head = new DockPanel();
        var ic = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Child = new TextBlock { Text = Ui.HasIcons ? glyph : "•", FontFamily = Ui.Icons, FontSize = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }.Res(TextBlock.ForegroundProperty, accentKey) };
        ic.SetResourceReference(Border.BackgroundProperty, accentKey + "Soft");
        DockPanel.SetDock(ic, Dock.Right); head.Children.Add(ic);
        head.Children.Add(Ui.Text(label, 14, FontWeights.SemiBold, "Muted"));
        var sp = new StackPanel { Children = { head, value, note } };
        value.Margin = new Thickness(0, 2, 0, 0);
        var tile = Ui.Card(sp, 18);
        var strip = new Border { Height = 4, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 12, 0, 0) }.Res(Border.BackgroundProperty, accentKey);
        sp.Children.Add(strip);
        return (tile, value, note);
    }
}
