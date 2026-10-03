using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeviceMonitor.App.Pages;

namespace DeviceMonitor.App;

/// <summary>The main window: navigation on the left, a top bar with the monitor controls, and the current page.</summary>
public class MainWindow : Window
{
    readonly ContentControl content = new();
    readonly Dictionary<string, Button> nav = new();
    readonly Dictionary<string, IPage> pages = new();
    readonly TextBlock pageTitle = Ui.Text("", 22, FontWeights.Bold, "Ink", false);
    readonly TextBlock pageSub = Ui.Muted("", 13.5);
    readonly Button startStop;
    readonly ComboBox interval;
    readonly ComboBox siteFilter = new() { Width = 260, MaxDropDownHeight = 420, DisplayMemberPath = "Label", SelectedValuePath = "Value" };
    bool fillingSites;
    TextBlock siteLbl;
    int lastDown = -1;
    readonly Shapes.Ellipse liveDot = new() { Width = 12, Height = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock liveText = Ui.Text("", 14, FontWeights.SemiBold, "Ink", false);
    readonly TextBlock footer = Ui.Text("", 12.5, null, "NavMute");
    readonly Tray tray;
    readonly DispatcherTimer clock = new() { Interval = TimeSpan.FromSeconds(1) };
    string current;
    bool exiting, trayHintShown;

    public MainWindow(bool startHidden)
    {
        Title = App.Name; Width = 1360; Height = 860; MinWidth = 1000; MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 15;
        this.SetResourceReference(BackgroundProperty, "Bg");
        this.SetResourceReference(ForegroundProperty, "Ink");

        var root = new DockPanel();

        // ---------------- side bar
        var side = new DockPanel { Width = 240 }.Res(Panel.BackgroundProperty, "Nav");
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 22, 16, 26) };
        brand.Children.Add(Logo(40));
        var bt = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        bt.Children.Add(Ui.Text("Device Monitor", 18, FontWeights.Bold, "NavInk", false));
        bt.Children.Add(Ui.Text("MikroTik & network devices", 12, null, "NavMute", false));
        brand.Children.Add(bt);
        DockPanel.SetDock(brand, Dock.Top); side.Children.Add(brand);
        var foot = new Border { Padding = new Thickness(20, 12, 16, 16), Child = footer };
        DockPanel.SetDock(foot, Dock.Bottom); side.Children.Add(foot);
        var navList = new StackPanel();
        AddNav(navList, "dash", "\uE80F", "Dashboard");
        AddNav(navList, "sites", "\uE707", "Sites");
        AddNav(navList, "devices", "\uE839", "Devices");
        AddNav(navList, "events", "\uE81C", "Event log");
        AddNav(navList, "settings", "\uE713", "Settings");
        side.Children.Add(navList);
        DockPanel.SetDock(side, Dock.Left); root.Children.Add(side);

        // ---------------- top bar
        var top = new Grid { Margin = new Thickness(28, 18, 28, 14) };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        // the site drop-down: "All sites" or one site; every page shows only the chosen site's devices
        siteLbl = Ui.Text("Site", 14, FontWeights.SemiBold, "Muted", false); siteLbl.VerticalAlignment = VerticalAlignment.Center; siteLbl.Margin = new Thickness(0, 0, 8, 0);
        pageSub.VerticalAlignment = VerticalAlignment.Center; pageSub.Margin = new Thickness(16, 0, 0, 0);
        var siteRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { siteLbl, siteFilter, pageSub } };
        siteFilter.ToolTip = "Show all devices, or only the devices of one site";
        siteFilter.SelectionChanged += (_, _) => { if (!fillingSites && siteFilter.SelectedValue is string v) App.CurrentSiteId = v; };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { pageTitle, siteRow } };
        top.Children.Add(titles);

        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var live = new Border { CornerRadius = new CornerRadius(20), Padding = new Thickness(12, 6, 14, 6), Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1) }
            .Res(Border.BackgroundProperty, "Panel").Res(Border.BorderBrushProperty, "Line");
        liveDot.Margin = new Thickness(0, 0, 8, 0);
        live.Child = new StackPanel { Orientation = Orientation.Horizontal, Children = { liveDot, liveText } };
        controls.Children.Add(live);
        var il = Ui.Text("Ping every", 14, FontWeights.SemiBold, "Muted", false); il.VerticalAlignment = VerticalAlignment.Center; il.Margin = new Thickness(0, 0, 8, 0);
        controls.Children.Add(il);
        interval = UiExtra.IntervalBox(App.Settings.IntervalSeconds, null, 160);
        interval.SelectionChanged += (_, _) => Dispatcher.BeginInvoke(ApplyInterval, DispatcherPriority.Background);
        interval.LostKeyboardFocus += (_, _) => ApplyInterval();
        interval.KeyDown += (_, e) => { if (e.Key == Key.Enter) { ApplyInterval(); e.Handled = true; } };
        controls.Children.Add(interval);
        var checkAll = Ui.IconBtn("\uE72C", "Check now", () => _ = App.Engine.CheckNowAsync(), null, "Ping every device right away");
        checkAll.Margin = new Thickness(10, 0, 0, 0);
        controls.Children.Add(checkAll);
        startStop = Ui.IconBtn("\uE769", "Pause", ToggleMonitoring, "Primary");
        startStop.Margin = new Thickness(10, 0, 0, 0); startStop.MinWidth = 120;
        controls.Children.Add(startStop);
        Grid.SetColumn(controls, 1); top.Children.Add(controls);
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

        content.Margin = new Thickness(28, 0, 28, 24);
        root.Children.Add(content);
        Content = root;

        // ---------------- tray, events, timers
        tray = new Tray();
        tray.OpenRequested += ShowFromTray;
        tray.ToggleRequested += ToggleMonitoring;
        tray.ExitRequested += () => _ = ExitAsync();
        App.MonitorChanged += UpdateMonitorUi;
        App.DevicesChanged += FillSites;
        App.SiteFilterChanged += () => { FillSites(); if (current != null && pages.TryGetValue(current, out var pg)) pg.Shown(); };
        FillSites();
        App.ShowDeviceRequested += id => { ShowFromTray(); if (App.Find(id) != null) DeviceWindow.Open(id); };
        clock.Tick += (_, _) => Tick();
        clock.Start();

        Closing += (_, e) =>
        {
            if (exiting) return;
            e.Cancel = true;
            if (!App.Settings.CloseToTray) { _ = ExitAsync(); return; }
            Hide();
            if (!trayHintShown) { trayHintShown = true; tray.Balloon(App.Name, "Still monitoring in the background. Right-click this icon to exit."); }
        };

        Go("dash");
        if (App.Settings.StartMonitoringOnLaunch) App.Start();
        UpdateMonitorUi();
        if (!startHidden) Show();
        Application.Current.MainWindow = this;
    }

    /// <summary>Fills the site drop-down: "All sites", then each site with its device count and how many are OFF.</summary>
    void FillSites()
    {
        fillingSites = true;
        siteFilter.ItemsSource = UiExtra.SiteOptions();
        siteFilter.SelectedValue = App.CurrentSiteId;
        fillingSites = false;
    }

    void AddNav(Panel list, string key, string glyph, string text)
    {
        var b = Ui.IconBtn(glyph, text, () => Go(key), "NavItem");
        b.SetResourceReference(Control.ForegroundProperty, "NavInk");
        nav[key] = b; list.Children.Add(b);
    }

    public void Go(string key)
    {
        if (!pages.TryGetValue(key, out var p))
        {
            p = key switch { "sites" => new SitesPage(), "devices" => new DevicesPage(), "events" => new EventsPage(), "settings" => new SettingsPage(), _ => new DashboardPage() };
            pages[key] = p;
        }
        if (current != null && pages.TryGetValue(current, out var old)) old.Hidden();
        current = key;
        foreach (var (k, b) in nav) b.Tag = k == key ? "on" : null;
        pageTitle.Text = p.Title; pageSub.Text = p.Subtitle;
        // the Dashboard has its own big site drop-down; the top one is for the Devices and Event log pages
        var topSites = key is "devices" or "events" ? Visibility.Visible : Visibility.Collapsed;
        siteFilter.Visibility = topSites; siteLbl.Visibility = topSites;
        pageSub.Margin = new Thickness(topSites == Visibility.Visible ? 16 : 0, 0, 0, 0);
        content.Content = p.View;
        p.Shown();
    }

    void ApplyInterval()
    {
        var (ok, s) = UiExtra.ReadInterval(interval);
        if (!ok || s == null) { interval.Text = MonitorSettings.IntervalText(App.Settings.IntervalSeconds); return; }
        App.SetInterval(s.Value);
        var text = MonitorSettings.IntervalText(s.Value);
        if (interval.Text != text) interval.Text = text;
    }

    async void ToggleMonitoring()
    {
        if (App.Engine.IsRunning) await App.StopAsync(); else App.Start();
    }

    void UpdateMonitorUi()
    {
        var run = App.Engine.IsRunning;
        startStop.Content = null;
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        if (Ui.HasIcons) sp.Children.Add(new TextBlock { Text = run ? "\uE769" : "\uE768", FontFamily = Ui.Icons, FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        sp.Children.Add(new TextBlock { Text = run ? "Pause" : "Start", VerticalAlignment = VerticalAlignment.Center });
        startStop.Content = sp;
        startStop.Style = run ? null : (Style)Application.Current.Resources["Primary"];
        liveDot.SetResourceReference(Shapes.Shape.FillProperty, run ? "Ok" : "Idle");
        if (run) liveDot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromSeconds(0.9)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        else { liveDot.BeginAnimation(OpacityProperty, null); liveDot.Opacity = 1; }
        var text = MonitorSettings.IntervalText(App.Settings.IntervalSeconds);
        if (!interval.IsKeyboardFocusWithin && interval.Text != text) interval.Text = text;
        Tick();
    }

    void Tick()
    {
        var (total, up, down, _, _) = App.Engine.Counts();
        liveText.Text = App.Engine.IsRunning ? (down > 0 ? $"Monitoring · {down} OFF" : "Monitoring") : "Paused";
        tray.Update(up, down, App.Engine.IsRunning);
        footer.Text = $"{total} devices · {up} ON · {down} OFF\nVersion {App.Version}";
        Title = down > 0 ? $"({down} OFF) {App.Name}" : App.Name;
        if (down != lastDown && !siteFilter.IsDropDownOpen) { lastDown = down; FillSites(); }   // keep the "n OFF" in the site list current
        if (current != null && IsVisible && pages.TryGetValue(current, out var p)) p.Refresh();
    }

    public void ShowFromTray()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate(); Topmost = true; Topmost = false; Focus();
    }

    async Task ExitAsync()
    {
        exiting = true;
        clock.Stop();
        Toast.CloseAll();
        await App.StopAsync();
        tray.Dispose();
        Application.Current.Shutdown();
    }

    /// <summary>The program's logo: a rounded square with a pulse line.</summary>
    public static Viewbox Logo(double size)
    {
        var c = new Canvas { Width = 40, Height = 40 };
        var grad = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#3B82F6"), (Color)ColorConverter.ConvertFromString("#1E40AF"), new Point(0, 0), new Point(1, 1));
        c.Children.Add(new Shapes.Rectangle { Width = 40, Height = 40, RadiusX = 10, RadiusY = 10, Fill = grad });
        c.Children.Add(new Shapes.Path
        {
            Data = Geometry.Parse("M6,22 L13,22 L16.5,13 L21.5,30 L25,18 L27.5,22 L34,22"), Stroke = Brushes.White, StrokeThickness = 2.8,
            StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round
        });
        var dot = new Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#34D399")), Stroke = Brushes.White, StrokeThickness = 1.6 };
        Canvas.SetLeft(dot, 28); Canvas.SetTop(dot, 4); c.Children.Add(dot);
        return new Viewbox { Width = size, Height = size, Child = c };
    }
}

/// <summary>A screen of the main window.</summary>
public interface IPage
{
    string Title { get; }
    string Subtitle { get; }
    FrameworkElement View { get; }
    /// <summary>The page became visible.</summary>
    void Shown();
    void Hidden();
    /// <summary>Called every second while visible: update live values.</summary>
    void Refresh();
}
