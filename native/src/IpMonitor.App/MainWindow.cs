using Microsoft.Win32;

namespace IpMonitor.App;

public class MainWindow : Window
{
    public static MainWindow Instance { get; private set; }
    public readonly Store Store = new();
    /// <summary>Live monitoring: pings the devices every few seconds and raises alerts.</summary>
    public readonly DeviceMonitor Monitor;
    readonly System.Windows.Threading.DispatcherTimer monTimer = new();
    readonly System.Windows.Threading.DispatcherTimer radioTimer = new();
    bool radioBusy;
    /// <summary>Result of the last automatic reading of the wireless connections (shown on the dashboard).</summary>
    public string RadioStatus = "";
    TextBlock monStatus; Border monPill;
    public readonly Settings Settings;
    /// <summary>The site chosen in the filter of the pages ("" = all sites).</summary>
    public string SiteFilter = "";

    readonly Grid root = new();
    readonly List<PageBase> pages;
    PageBase current;
    bool inShell, rebuilding;
    StackPanel nav;
    ContentControl content;
    TextBlock fileStatus, saveDot;
    Border banner;

    public MainWindow(Settings settings)
    {
        Instance = this; Settings = settings;
        Title = "IP Monitor"; Width = 1320; Height = 840; MinWidth = 960; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        this.SetResourceReference(BackgroundProperty, "Bg");
        this.SetResourceReference(ForegroundProperty, "Ink");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        Content = root;
        pages = new List<PageBase> { new DashboardPage(), new MapPage(), new SitesPage(), new NetworksPage(), new DevicesPage(), new LinksPage(), new VlansPage(), new CalcPage(), new ReportsPage(), new HistoryPage(), new UsersPage(), new BackupPage() };
        Store.Changed += OnChanged;
        Monitor = new DeviceMonitor(Store);
        Monitor.Changed += OnDeviceChanged;
        monTimer.Tick += async (_, _) => await RunMonitor();
        radioTimer.Tick += async (_, _) => await RunRadio();
        Activated += (_, _) => CheckOutside();
        Closing += (_, e) =>
        {
            if (Store.SaveError != null && !Ui.Ask("Your last change is NOT saved in the database file:\n\n" + Store.SaveError + "\n\nClose anyway and lose it?", "Not saved", "Close without saving", true))
                e.Cancel = true;
            else if (monTimer.IsEnabled) try { Monitor.Log?.Note("MONITORING OFF (app closed)"); } catch { }
        };
        Loaded += (_, _) => Start();
    }

    // ------------------------------------------------------------------ start: choose the database file
    void Start()
    {
        if (!string.IsNullOrEmpty(Settings.LastFile) && File.Exists(Settings.LastFile) && TryOpen(Settings.LastFile, out _)) ShowLogin();
        else ShowStart(null);
    }

    bool TryOpen(string path, out string error)
    {
        error = null;
        try
        {
            Store.Me = null; Store.Open(path);
            Settings.LastFile = path; Settings.Save();
            return true;
        }
        catch (Exception e) { error = $"Couldn't open {IOPath.GetFileName(path)}: {e.Message}"; return false; }
    }

    static FrameworkElement Centered(FrameworkElement card, double width)
    {
        card.Width = width; card.HorizontalAlignment = HorizontalAlignment.Center; card.VerticalAlignment = VerticalAlignment.Center; card.Margin = new Thickness(20);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = card };
    }

    static StackPanel BrandHead(string subtitle)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 18) };
        head.Children.Add(Ui.Logo(52));
        var t = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        t.Children.Add(Ui.Text("IP Monitor", 26, FontWeights.Bold));
        t.Children.Add(Ui.Muted(subtitle, 13.5));
        head.Children.Add(t);
        return head;
    }

    void ShowStart(string error)
    {
        inShell = false; Title = "IP Monitor";
        var sp = new StackPanel();
        sp.Children.Add(BrandHead("Sites, IP addresses, devices and connections"));
        sp.Children.Add(Ui.Text("Choose the database file", 18, FontWeights.Bold));
        var p = Ui.Muted("All data (sites, networks, devices, accounts) is kept in one JSON file, ip-monitor-db.json — the same file the web version uses. Every change is saved to it automatically. Put it on a shared folder to use it from several computers.", 14);
        p.Margin = new Thickness(0, 6, 0, 18); sp.Children.Add(p);
        var err = Ui.Error(); err.Text = error ?? ""; err.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(err);

        var open = Ui.Btn("Open an existing database file…", () =>
        {
            var d = new OpenFileDialog { Title = "Open the IP Monitor database", Filter = "IP Monitor database (*.json)|*.json|All files|*.*" };
            if (d.ShowDialog(this) != true) return;
            if (TryOpen(d.FileName, out var e)) ShowLogin(); else err.Text = e;
        }, "Primary");
        open.HorizontalAlignment = HorizontalAlignment.Stretch; open.MinHeight = 44; open.Margin = new Thickness(0, 0, 0, 10);
        sp.Children.Add(open);
        var create = Ui.Btn("Create a new database file…", () =>
        {
            var d = new SaveFileDialog { Title = "Create a new IP Monitor database", FileName = "ip-monitor-db.json", Filter = "IP Monitor database (*.json)|*.json", OverwritePrompt = true };
            if (d.ShowDialog(this) != true) return;
            if (File.Exists(d.FileName) && !Ui.Ask($"{IOPath.GetFileName(d.FileName)} already exists. Replace it with an EMPTY database?\n\nEverything in that file will be lost.", "Replace file?", "Replace", true)) return;
            try { Store.CreateNew(d.FileName); Settings.LastFile = d.FileName; Settings.Save(); ShowLogin(); }
            catch (Exception e) { err.Text = "Couldn't create the file: " + e.Message; }
        });
        create.HorizontalAlignment = HorizontalAlignment.Stretch; create.MinHeight = 44;
        sp.Children.Add(create);
        if (!string.IsNullOrEmpty(Settings.LastFile))
        {
            var last = Ui.Muted("Last file: " + Settings.LastFile, 12.5); last.Margin = new Thickness(0, 14, 0, 0); sp.Children.Add(last);
        }
        root.Children.Clear();
        root.Children.Add(Centered(Ui.Card(sp, 30), 560));
    }

    // ------------------------------------------------------------------ sign in / create account
    void ShowLogin(string message = null)
    {
        inShell = false; Title = "IP Monitor";
        bool signup = Store.Db.Users.Count == 0;
        var sp = new StackPanel();
        sp.Children.Add(BrandHead("Sign in to continue"));

        var fileRow = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var change = Ui.Btn("Change file", () => ShowStart(null), "Link");
        DockPanel.SetDock(change, Dock.Right); fileRow.Children.Add(change);
        fileRow.Children.Add(Ui.Text("Database: " + Store.FilePath, 12.5, null, "Muted", false, true));
        sp.Children.Add(fileRow);

        var tabs = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        tabs.ColumnDefinitions.Add(new ColumnDefinition()); tabs.ColumnDefinitions.Add(new ColumnDefinition());
        var tIn = Ui.Btn("Sign in", () => { }, "NavItem"); var tUp = Ui.Btn("Create account", () => { }, "NavItem");
        foreach (var t in new[] { tIn, tUp }) { t.Margin = new Thickness(0); t.BorderThickness = new Thickness(0, 0, 0, 3); t.HorizontalContentAlignment = HorizontalAlignment.Center; }
        Grid.SetColumn(tUp, 1); tabs.Children.Add(tIn); tabs.Children.Add(tUp);
        sp.Children.Add(tabs);

        var form = new StackPanel(); sp.Children.Add(form);
        var err = Ui.Error(); err.Margin = new Thickness(0, 4, 0, 0);
        if (message != null) err.Text = message;

        void Build()
        {
            tIn.Tag = signup ? null : "on"; tUp.Tag = signup ? "on" : null;
            form.Children.Clear();
            if (!signup)
            {
                var user = Ui.Box(Settings.LastUser ?? ""); var pass = new SecretBox();
                form.Children.Add(Ui.Field("Username", user));
                form.Children.Add(Ui.Field("Password", pass));
                var go = Ui.Btn("Sign in", () =>
                {
                    var u = Store.Login(user.Text, pass.Password);
                    if (u == null) { err.Text = "Wrong username or password."; pass.Clear(); pass.Focus(); return; }
                    SignedIn(u);
                }, "Primary");
                go.IsDefault = true; go.HorizontalAlignment = HorizontalAlignment.Stretch; go.MinHeight = 42;
                form.Children.Add(go);
                Dispatcher.BeginInvoke(() => { if (user.Text == "") user.Focus(); else pass.Focus(); });
            }
            else
            {
                if (Store.Db.Users.Count == 0) { var n = Ui.Text("This database has no accounts yet. Create the first administrator account.", 13.5, FontWeights.SemiBold, "Acc"); n.Margin = new Thickness(0, 0, 0, 14); form.Children.Add(n); }
                var user = Ui.Box(); var p1 = new SecretBox(); var p2 = new SecretBox(); var key = new SecretBox();
                var role = Ui.Choice(new[] { ("admin", "Administrator — Full access"), ("guest", "Guest — View only") }, Store.Db.Users.Count == 0 ? "admin" : "guest");
                form.Children.Add(Ui.Field("Username", user, "3–32 characters: letters, numbers, dot, dash or underscore."));
                form.Children.Add(Ui.Cols(Ui.Field("Password", p1, "At least 8 characters."), Ui.Field("Confirm password", p2)));
                form.Children.Add(Ui.Field("Account type", role));
                form.Children.Add(Ui.Field("Pre-shared key", key, "Ask your administrator. Needed to create an account."));
                var go = Ui.Btn("Create account", () =>
                {
                    try { SignedIn(Store.SignUp(user.Text, p1.Password, p2.Password, key.Password, role.Val())); }
                    catch (RuleException e) { err.Text = e.Message; }
                }, "Primary");
                go.IsDefault = true; go.HorizontalAlignment = HorizontalAlignment.Stretch; go.MinHeight = 42;
                form.Children.Add(go);
                Dispatcher.BeginInvoke(() => user.Focus());
            }
            form.Children.Add(err);
        }
        tIn.Click += (_, _) => { signup = false; err.Text = ""; Build(); };
        tUp.Click += (_, _) => { signup = true; err.Text = ""; Build(); };
        Build();
        root.Children.Clear();
        root.Children.Add(Centered(Ui.Card(sp, 30), 520));
    }

    void SignedIn(User u)
    {
        Store.Me = u; Settings.LastUser = u.Username; Settings.Save();
        ShowShell();
        StartMonitor();
    }

    // ------------------------------------------------------------------ live monitoring
    public void StartMonitor()
    {
        monTimer.Stop();
        if (!Settings.MonitorOn || Store.Me == null) { UpdateMonitorStatus(); return; }
        Monitor.Log = new UptimeLog(UptimeLog.For(Store.FilePath), Store.Me.Username);   // next to the database file
        try { Monitor.Log.Note($"MONITORING ON (every {Settings.MonitorSeconds} s)"); } catch { }
        monTimer.Interval = TimeSpan.FromSeconds(Math.Max(15, Settings.MonitorSeconds));
        monTimer.Start();
        _ = RunMonitor();
        StartRadio();
    }

    /// <summary>Automatic reading of the wireless connections from the routers (while monitoring is on).</summary>
    public void StartRadio()
    {
        radioTimer.Stop();
        if (!Settings.MonitorOn || !Settings.RadioAuto || Store.Me == null || !Store.CanWrite) return;
        radioTimer.Interval = TimeSpan.FromMinutes(Math.Max(2, Settings.RadioMinutes));
        radioTimer.Start();
        var first = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };   // first reading shortly after sign-in
        first.Tick += async (_, _) => { first.Stop(); await RunRadio(); };
        first.Start();
    }

    public async Task RunRadio()
    {
        if (radioBusy || Store.Me == null || !Store.CanWrite) return;
        var links = Store.Db.Links.Where(l => l.Type == "wireless").ToList();
        if (links.Count == 0) { RadioStatus = ""; return; }
        radioBusy = true;
        try
        {
            var (ok, problems) = await LinksPage.ReadFromRouters(links);
            RadioStatus = $"last read {DateTime.Now:HH:mm} · {ok} of {links.Count} wireless connection{(links.Count == 1 ? "" : "s")} updated" + (problems.Count > 0 ? $" · {problems.Count} problem{(problems.Count == 1 ? "" : "s")}: {problems[0]}" : "");
        }
        catch (Exception e) { RadioStatus = "last try failed: " + e.Message; }
        finally { radioBusy = false; if (inShell && current is DashboardPage dp) dp.RefreshMonitorPanel(); }
    }
    public void StopMonitor()
    {
        if (monTimer.IsEnabled) try { Monitor.Log?.Note(Store.Me == null ? "MONITORING OFF (logged out)" : "MONITORING PAUSED"); } catch { }
        monTimer.Stop(); radioTimer.Stop(); UpdateMonitorStatus();
    }

    /// <summary>Pings all devices now; refreshes the dashboard, map and devices list when something changed.</summary>
    public async Task RunMonitor()
    {
        if (Store.Me == null || Monitor.Running) return;
        var before = Monitor.States.ToDictionary(k => k.Key, k => k.Value.State);
        UpdateMonitorStatus(true);
        try { await Monitor.CheckAsync(); }
        catch (Exception e) { Toast("Monitoring: " + e.Message); }
        foreach (var st in Monitor.States.Values)   // the Ping column of the lists shows the same result
            NetworksPage.Pings[st.Ip] = new Pinger.Result(st.Ip, st.State == DeviceMonitor.State.Up, st.Ms, st.Note);
        UpdateMonitorStatus();
        var changed = Monitor.States.Count != before.Count || Monitor.States.Any(k => !before.TryGetValue(k.Key, out var b) || b != k.Value.State);
        if (changed && inShell && !rebuilding && Mouse.LeftButton != MouseButtonState.Pressed && current is DashboardPage or MapPage or DevicesPage) Rebuild();
        else if (inShell && current is DashboardPage dp) dp.RefreshMonitorPanel();
    }

    void OnDeviceChanged(DeviceMonitor.Event e)
    {
        var site = Store.SiteById(e.Device.SiteId);
        var where = site == null ? "" : $"#{site.SiteNumber} {site.Name} · ";
        void Show()
        {
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            SiteFilter = e.Device.SiteId; Navigate<MapPage>();
        }
        if (e.Down) Notifier.Show("Device down", $"{e.Device.Name} is not answering ping", $"{where}{e.Ip} · {DateTime.Now:HH:mm:ss}{(string.IsNullOrEmpty(e.Note) || e.Note == "TimedOut" ? "" : " · " + e.Note)}", true, Show, Settings.MonitorSound);
        else Notifier.Show("Device back up", $"{e.Device.Name} answers ping again", $"{where}{e.Ip} · {DateTime.Now:HH:mm:ss}", false, Show, Settings.MonitorSound);
    }

    void UpdateMonitorStatus(bool checking = false)
    {
        if (monStatus == null) return;
        int up = Monitor.States.Values.Count(s => s.State == DeviceMonitor.State.Up), dn = Monitor.States.Values.Count(s => s.State == DeviceMonitor.State.Down);
        if (!Settings.MonitorOn) { monStatus.Text = "Monitoring paused"; monPill.SetResourceReference(Border.BackgroundProperty, "Nav2"); return; }
        monStatus.Text = checking && Monitor.LastRun == default ? "Monitoring: checking…" : $"{up} up · {dn} down";
        monPill.SetResourceReference(Border.BackgroundProperty, dn > 0 ? "Sig" : "Nav2");
        monPill.ToolTip = $"Live monitoring: every {Settings.MonitorSeconds} s{(Monitor.LastRun == default ? "" : $" · last check {Monitor.LastRun:HH:mm:ss}")}";
    }

    public void LogOut()
    {
        if (Store.SaveError != null && !Ui.Ask("Your last change is NOT saved in the database file:\n\n" + Store.SaveError + "\n\nLog out anyway?", "Not saved", "Log out", true)) return;
        Store.Me = null; StopMonitor();
        ShowLogin("You have logged out. Everything was saved to the database file.");
    }

    // ------------------------------------------------------------------ the app: top bar, side menu, page
    void ShowShell()
    {
        inShell = true;
        var dock = new DockPanel();

        // top bar
        var top = new Border { Height = 60, Padding = new Thickness(18, 0, 14, 0) }.Res(Border.BackgroundProperty, "Nav");
        var bar = new DockPanel { LastChildFill = true };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Ui.Logo(34));
        var name = Ui.Text("IP Monitor", 20, FontWeights.Bold, "NavInk"); name.Margin = new Thickness(10, 0, 0, 0); name.VerticalAlignment = VerticalAlignment.Center;
        brand.Children.Add(name);
        DockPanel.SetDock(brand, Dock.Left); bar.Children.Add(brand);

        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        monStatus = new TextBlock { FontWeight = FontWeights.SemiBold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        monStatus.SetResourceReference(TextBlock.ForegroundProperty, "NavInk");
        monPill = new Border { Child = monStatus, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(0, 0, 16, 0), Cursor = Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
        monPill.MouseLeftButtonUp += (_, _) => Navigate<DashboardPage>();
        right.Children.Add(monPill);
        UpdateMonitorStatus();
        var me = Store.Me;
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
        who.Children.Add(Ui.Text(me.Username, 14, FontWeights.SemiBold, "NavInk", false));
        who.Children.Add(Ui.Text(PermLabel(Store.Perm), 12, null, "NavMute", false));
        right.Children.Add(who);
        right.Children.Add(Ui.IconBtn(Theme.Dark ? "" : "", "", ToggleTheme, "Top", Theme.Dark ? "Light mode" : "Dark mode"));
        var pw = Ui.IconBtn("", "", ChangePassword, "Top", "Change my password"); pw.Margin = new Thickness(8, 0, 0, 0); right.Children.Add(pw);
        var lo = Ui.IconBtn("", "Log out", LogOut, "Top"); lo.Margin = new Thickness(8, 0, 0, 0); right.Children.Add(lo);
        DockPanel.SetDock(right, Dock.Right); bar.Children.Add(right);

        var status = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(28, 0, 16, 0) };
        saveDot = new TextBlock { Text = "●", FontSize = 14, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        fileStatus = Ui.Text("", 13, null, "NavMute", false);
        fileStatus.VerticalAlignment = VerticalAlignment.Center;
        status.Children.Add(saveDot); status.Children.Add(fileStatus);
        bar.Children.Add(status);
        top.Child = bar;
        DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);

        // warning when the file could not be written
        banner = new Border { Padding = new Thickness(18, 10, 18, 10), Visibility = Visibility.Collapsed, BorderThickness = new Thickness(0, 0, 0, 1) }
            .Res(Border.BackgroundProperty, "SigSoft").Res(Border.BorderBrushProperty, "Sig");
        DockPanel.SetDock(banner, Dock.Top); dock.Children.Add(banner);

        // side menu
        nav = new StackPanel { Margin = new Thickness(0, 14, 0, 14) };
        foreach (var p in pages)
        {
            var b = Ui.IconBtn(p.Glyph, p.Title, () => Navigate(p), "NavItem");
            b.DataContext = p; nav.Children.Add(b);
        }
        var side = new Border { Width = 230, BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = nav } }
            .Res(Border.BackgroundProperty, "Panel").Res(Border.BorderBrushProperty, "Line");
        DockPanel.SetDock(side, Dock.Left); dock.Children.Add(side);

        content = new ContentControl { Focusable = false };
        dock.Children.Add(content);
        root.Children.Clear(); root.Children.Add(dock);
        UpdateStatus();
        Navigate(current ?? pages[0]);
    }

    public static string PermLabel(string p) => p switch { "full" => "Full access", "write" => "Can edit", _ => "View only" };

    public void Navigate(PageBase p)
    {
        current = p;
        foreach (Button b in nav.Children) b.Tag = b.DataContext == p ? "on" : null;
        Title = $"{p.Title} — IP Monitor";
        Rebuild();
    }
    public void Navigate<T>() where T : PageBase => Navigate(pages.OfType<T>().First());
    public T Page<T>() where T : PageBase => pages.OfType<T>().First();

    void Rebuild()
    {
        if (!inShell || current == null || rebuilding) return;
        rebuilding = true;
        try { content.Content = current.Build(); }
        finally { rebuilding = false; }
    }

    void OnChanged()
    {
        if (!inShell) return;
        if (Store.Me == null) { ShowLogin("Your account no longer exists in the database file."); return; }
        UpdateStatus();
        Rebuild();
    }

    void UpdateStatus()
    {
        if (fileStatus == null) return;
        var ok = Store.SaveError == null;
        saveDot.SetResourceReference(TextBlock.ForegroundProperty, ok ? "Ok" : "Sig");
        fileStatus.Text = ok ? $"Saved automatically · {IOPath.GetFileName(Store.FilePath)}  ·  {Store.LastWrite.ToLocalTime():HH:mm:ss}" : "NOT SAVED to the database file";
        fileStatus.ToolTip = Store.FilePath;
        if (ok) { banner.Visibility = Visibility.Collapsed; return; }
        var sp = new DockPanel();
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        if (Store.Conflict)
        {
            btns.Children.Add(Ui.Btn("Reload the file (drop my last change)", () => { Store.Reload(); }, null));
            var ow = Ui.Btn("Keep mine (overwrite the file)", () => Store.SaveNow(true), "Danger"); ow.Margin = new Thickness(8, 0, 0, 0); btns.Children.Add(ow);
        }
        else btns.Children.Add(Ui.Btn("Try again", () => Store.SaveNow(), "Primary"));
        DockPanel.SetDock(btns, Dock.Right); sp.Children.Add(btns);
        var msg = Ui.Text(Store.SaveError, 14, FontWeights.SemiBold, "Sig"); msg.VerticalAlignment = VerticalAlignment.Center; msg.Margin = new Thickness(0, 0, 14, 0);
        sp.Children.Add(msg);
        banner.Child = sp; banner.Visibility = Visibility.Visible;
    }

    /// <summary>When the window comes back to the front: if another program changed the file and nothing here is unsaved, load the new data.</summary>
    void CheckOutside()
    {
        if (Store.FilePath == null || Store.SaveError != null) return;
        try
        {
            if (!Store.ChangedOutside) return;
            Store.Reload();
            if (inShell && Store.Me != null) Toast("The database file was changed by another program — the new data was loaded.");
        }
        catch (Exception e) { Toast("Couldn't reload the database file: " + e.Message); }
    }

    /// <summary>A short message at the bottom of the window.</summary>
    public void Toast(string text)
    {
        var t = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 10, 16, 10), Margin = new Thickness(0, 0, 0, 24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, MaxWidth = 700, IsHitTestVisible = false }
            .Res(Border.BackgroundProperty, "Nav");
        t.Child = Ui.Text(text, 14, FontWeights.SemiBold, "NavInk");
        root.Children.Add(t);
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += (_, _) => { timer.Stop(); root.Children.Remove(t); };
        timer.Start();
    }

    void ToggleTheme()
    {
        Theme.Apply(!Theme.Dark);
        Settings.Theme = Theme.Dark ? "dark" : "light"; Settings.Save();
        ShowShell();
    }

    void ChangePassword()
    {
        var d = new Dlg("Change my password", 460);
        var cur = new SecretBox(); var p1 = new SecretBox(); var p2 = new SecretBox();
        d.Body.Children.Add(Ui.Field("Current password", cur));
        d.Body.Children.Add(Ui.Field("New password", p1, "At least 8 characters."));
        d.Body.Children.Add(Ui.Field("Confirm new password", p2));
        d.Ok("Change password", () => { Store.ChangeMyPassword(cur.Password, p1.Password, p2.Password); return true; });
        d.Cancel();
        if (d.Open()) Toast("Your password was changed.");
    }
}

/// <summary>One screen of the app. Build() is called again after every change, so it always shows the saved data.</summary>
public abstract class PageBase
{
    protected static MainWindow W => MainWindow.Instance;
    protected static Store S => W.Store;
    public abstract string Title { get; }
    public abstract string Glyph { get; }
    public abstract FrameworkElement Build();

    /// <summary>Title, subtitle and the buttons on the right.</summary>
    protected static FrameworkElement Header(string title, string subtitle, params UIElement[] actions)
    {
        var d = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var a in actions) { if (a is FrameworkElement fe) fe.Margin = new Thickness(10, 0, 0, 0); right.Children.Add(a); }
        DockPanel.SetDock(right, Dock.Right); d.Children.Add(right);
        var t = new StackPanel();
        t.Children.Add(Ui.Text(title, 26, FontWeights.Bold));
        if (!string.IsNullOrEmpty(subtitle)) t.Children.Add(Ui.Muted(subtitle, 14));
        d.Children.Add(t);
        return d;
    }

    /// <summary>The page area: header on top, the rest fills the window.</summary>
    protected static FrameworkElement Layout(FrameworkElement header, FrameworkElement toolbar, FrameworkElement body)
    {
        var d = new DockPanel { Margin = new Thickness(28, 22, 28, 22) };
        DockPanel.SetDock(header, Dock.Top); d.Children.Add(header);
        if (toolbar != null) { toolbar.Margin = new Thickness(0, 0, 0, 14); DockPanel.SetDock(toolbar, Dock.Top); d.Children.Add(toolbar); }
        d.Children.Add(body);
        return d;
    }

    /// <summary>Search box + site filter; onChange refreshes the table only, so typing keeps the focus.</summary>
    protected static FrameworkElement FilterBar(Func<string> getSearch, Action<string> setSearch, Action onChange, bool withSite = true, params UIElement[] extra)
    {
        var sp = new WrapPanel();
        var search = Ui.Box(getSearch(), tip: "Search"); search.Width = 260;
        search.TextChanged += (_, _) => { setSearch(search.Text); onChange(); };
        sp.Children.Add(Ui.Field("Search", search));
        if (withSite)
        {
            var items = new List<(string, string)> { ("", "All sites") };
            items.AddRange(S.Db.Sites.OrderBy(s => s.SiteNumber, StringComparer.OrdinalIgnoreCase).Select(s => (s.Id, $"#{s.SiteNumber} · {s.Name}")));
            if (W.SiteFilter != "" && S.SiteById(W.SiteFilter) == null) W.SiteFilter = "";
            var site = Ui.Choice(items, W.SiteFilter); site.Width = 250;
            site.SelectionChanged += (_, _) => { W.SiteFilter = site.Val(); onChange(); };
            var f = Ui.Field("Site", site); ((FrameworkElement)f).Margin = new Thickness(12, 0, 0, 0);
            sp.Children.Add(f);
        }
        foreach (var e in extra) { if (e is FrameworkElement fe) { fe.Margin = new Thickness(12, 0, 0, 14); fe.VerticalAlignment = VerticalAlignment.Bottom; } sp.Children.Add(e); }
        return sp;
    }

    protected static bool Match(string search, params string[] fields)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        foreach (var word in search.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (!fields.Any(f => f != null && f.Contains(word, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }
    protected static bool InSite(string siteId) => W.SiteFilter == "" || siteId == W.SiteFilter;

    /// <summary>A table that fills the page, with a message when it is empty.</summary>
    protected static FrameworkElement TableWithEmpty(DataGrid g, TextBlock empty)
    {
        var grid = new Grid();
        grid.Children.Add(g);
        empty.HorizontalAlignment = HorizontalAlignment.Center; empty.VerticalAlignment = VerticalAlignment.Center; empty.TextAlignment = TextAlignment.Center;
        empty.IsHitTestVisible = false; empty.Margin = new Thickness(30, 50, 30, 0);
        grid.Children.Add(empty);
        return grid;
    }

    protected static Button AddBtn(string text, Action a)
    {
        var b = Ui.IconBtn("", text, a, "Primary");
        b.IsEnabled = S.CanWrite;
        if (!S.CanWrite) b.ToolTip = "Your account can only view.";
        return b;
    }

    /// <summary>Delete with a confirmation; rule errors are shown in a message.</summary>
    protected static void Confirm(string question, Action act)
    {
        if (!Ui.Ask(question, "Delete", "Delete", true)) return;
        try { act(); } catch (RuleException e) { Ui.Info(e.Message, "Can't delete"); }
    }
}
