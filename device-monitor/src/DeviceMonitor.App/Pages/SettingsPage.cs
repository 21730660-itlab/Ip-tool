using Microsoft.Win32;

namespace DeviceMonitor.App.Pages;

/// <summary>Interval, timeouts, alarm rule, pop-ups, look, log folder, starting with Windows.</summary>
public class SettingsPage : IPage
{
    public string Title => "Settings";
    public string Subtitle => "How often to ping, when to warn you, and where to keep the logs";
    public FrameworkElement View { get; }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RunName = "DeviceMonitor";

    ComboBox interval;
    TextBox timeout, failures, slow, popupSecs, logFolder, keepDays;
    CheckBox popDown, popUp, sound, tray, autoStart, withWindows;
    RadioButton dark, light;
    readonly TextBlock saved = Ui.Text("", 14, FontWeights.SemiBold, "Ok");

    public SettingsPage()
    {
        var root = new StackPanel { MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        Build(root);
        var save = Ui.IconBtn("", "Save settings", Save, "Primary");
        var reset = Ui.Btn("Undo changes", Load); reset.Margin = new Thickness(10, 0, 0, 0);
        saved.Margin = new Thickness(14, 0, 0, 0); saved.VerticalAlignment = VerticalAlignment.Center;
        root.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 10), Children = { save, reset, saved } });
        View = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Load();
    }

    static CheckBox Check(string text) => new() { Content = text, FontSize = 14.5, Margin = new Thickness(0, 0, 0, 12) };

    void Build(StackPanel root)
    {
        // monitoring
        interval = UiExtra.IntervalBox(App.Settings.IntervalSeconds, null, 220);
        timeout = Ui.Box(); timeout.Width = 120;
        failures = Ui.Box(); failures.Width = 120;
        slow = Ui.Box(); slow.Width = 120;
        var mon = new StackPanel();
        mon.Children.Add(Ui.Section("Monitoring"));
        mon.Children.Add(Ui.Cols(
            Ui.Field("Ping every", interval, "Pick from the list or type any value: 20, 45 s, 2 min…"),
            Ui.Field("Wait for a reply (ms)", timeout, "100 – 10000. 1000 ms suits most networks")));
        mon.Children.Add(Ui.Cols(
            Ui.Field("Missed pings before OFF", failures, "1 = warn at the first miss. 2 or 3 avoids false alarms; a miss is re-checked after 2 s"),
            Ui.Field("Slow above (ms)", slow, "Reply times above this show in yellow")));
        autoStart = Check("Start monitoring when the program opens");
        mon.Children.Add(autoStart);
        root.Children.Add(Card(mon));

        // pop-ups
        popDown = Check("Pop-up when a device turns OFF");
        popUp = Check("Pop-up when a device is back ON");
        sound = Check("Play a sound with the pop-up");
        popupSecs = Ui.Box(); popupSecs.Width = 120;
        var pop = new StackPanel();
        pop.Children.Add(Ui.Section("Alerts"));
        pop.Children.Add(popDown); pop.Children.Add(popUp); pop.Children.Add(sound);
        pop.Children.Add(Ui.Field("Close a pop-up after (seconds)", popupSecs, "0 = it stays until you close it"));
        var testRow = new StackPanel { Orientation = Orientation.Horizontal };
        testRow.Children.Add(Ui.Btn("Test OFF pop-up", () => Toast.Show(new MonitorEvent(DateTime.Now, EventKind.Down, "", "Test router", "192.168.88.1", "Example", "No reply (timed out)"), true, sound.IsChecked == true, ParseInt(popupSecs.Text, 10))));
        var tu = Ui.Btn("Test ON pop-up", () => Toast.Show(new MonitorEvent(DateTime.Now, EventKind.Up, "", "Test router", "192.168.88.1", "Example", "Reply in 2 ms", TimeSpan.FromMinutes(3.5)), false, sound.IsChecked == true, ParseInt(popupSecs.Text, 10)));
        tu.Margin = new Thickness(10, 0, 0, 0); testRow.Children.Add(tu);
        pop.Children.Add(testRow);
        root.Children.Add(Card(pop));

        // program
        dark = new RadioButton { Content = "Dark", GroupName = "theme", FontSize = 14.5 };
        light = new RadioButton { Content = "Light", GroupName = "theme", FontSize = 14.5 };
        dark.Checked += (_, _) => Theme.Apply(true);
        light.Checked += (_, _) => Theme.Apply(false);
        tray = Check("Closing the window keeps monitoring in the background (icon next to the clock)");
        withWindows = Check("Start Device Monitor when I sign in to Windows (in the background)");
        var prog = new StackPanel();
        prog.Children.Add(Ui.Section("Program"));
        prog.Children.Add(Ui.Field("Look", new StackPanel { Orientation = Orientation.Horizontal, Children = { dark, light } }));
        prog.Children.Add(tray); prog.Children.Add(withWindows);
        root.Children.Add(Card(prog));

        // logs
        logFolder = Ui.Box(); logFolder.IsReadOnly = true;
        keepDays = Ui.Box(); keepDays.Width = 120;
        var browse = Ui.Btn("Change…", () =>
        {
            var d = new OpenFolderDialog { Title = "Folder for the log files", InitialDirectory = Directory.Exists(logFolder.Text) ? logFolder.Text : "" };
            if (d.ShowDialog() == true) logFolder.Text = d.FolderName;
        });
        browse.Margin = new Thickness(8, 0, 0, 0);
        var def = Ui.Btn("Default", () => logFolder.Text = Storage.DefaultLogFolder); def.Margin = new Thickness(8, 0, 0, 0);
        var open = Ui.Btn("Open", () => App.OpenPath(App.Log.Folder)); open.Margin = new Thickness(8, 0, 0, 0);
        var folderRow = new DockPanel();
        var btns = new StackPanel { Orientation = Orientation.Horizontal, Children = { browse, def, open } };
        DockPanel.SetDock(btns, Dock.Right); folderRow.Children.Add(btns); folderRow.Children.Add(logFolder);
        var logs = new StackPanel();
        logs.Children.Add(Ui.Section("Log files"));
        logs.Children.Add(Ui.Field("Log folder", folderRow, "events.csv (all events, opens in Excel) and DeviceMonitor-YYYY-MM-DD.log (one readable file per day)"));
        logs.Children.Add(Ui.Field("Keep daily log files for (days)", keepDays, "Older daily files are deleted at start-up; 0 = keep all. events.csv is always kept"));
        logs.Children.Add(Ui.Muted("Device list and settings are stored in: " + App.Storage.DataFolder, 13));
        root.Children.Add(Card(logs));
    }

    static Border Card(UIElement body) { var c = Ui.Card(body, 20); c.Margin = new Thickness(0, 0, 0, 16); return c; }

    static int ParseInt(string s, int fallback) => int.TryParse(s?.Trim(), out var v) ? v : fallback;

    void Load()
    {
        var s = App.Settings;
        interval.Text = MonitorSettings.IntervalText(s.IntervalSeconds);
        timeout.Text = s.TimeoutMs.ToString(); failures.Text = s.FailuresBeforeDown.ToString(); slow.Text = s.SlowMs.ToString();
        autoStart.IsChecked = s.StartMonitoringOnLaunch;
        popDown.IsChecked = s.PopupOnDown; popUp.IsChecked = s.PopupOnUp; sound.IsChecked = s.Sound; popupSecs.Text = s.PopupSeconds.ToString();
        if (s.DarkTheme) dark.IsChecked = true; else light.IsChecked = true;
        tray.IsChecked = s.CloseToTray;
        withWindows.IsChecked = StartsWithWindows();
        logFolder.Text = string.IsNullOrWhiteSpace(s.LogFolder) ? Storage.DefaultLogFolder : s.LogFolder;
        keepDays.Text = s.KeepLogDays.ToString();
        saved.Text = "";
    }

    void Save()
    {
        saved.Text = "";
        var (ok, secs) = UiExtra.ReadInterval(interval);
        string err = null;
        if (!ok || secs == null) err = "The interval must be a number of seconds (for example 15) or a value like \"2 min\".";
        else if (!int.TryParse(timeout.Text.Trim(), out var to) || to < 100 || to > 10000) err = "The reply wait must be between 100 and 10000 ms.";
        else if (!int.TryParse(failures.Text.Trim(), out var f) || f < 1 || f > 10) err = "Missed pings before OFF must be between 1 and 10.";
        else if (!int.TryParse(slow.Text.Trim(), out var sl) || sl < 1) err = "\"Slow above\" must be a number of ms.";
        else if (!int.TryParse(popupSecs.Text.Trim(), out var ps) || ps < 0) err = "Pop-up seconds must be 0 or more.";
        else if (!int.TryParse(keepDays.Text.Trim(), out var kd) || kd < 0) err = "Days to keep logs must be 0 or more.";
        else
        {
            try { Directory.CreateDirectory(logFolder.Text); }
            catch (Exception e) { err = "The log folder cannot be used: " + e.Message; }
        }
        if (err != null) { saved.Text = err; saved.Res(TextBlock.ForegroundProperty, "Sig"); return; }

        var s = App.Settings;
        var oldInterval = s.IntervalSeconds;
        s.TimeoutMs = int.Parse(timeout.Text.Trim()); s.FailuresBeforeDown = int.Parse(failures.Text.Trim()); s.SlowMs = int.Parse(slow.Text.Trim());
        s.StartMonitoringOnLaunch = autoStart.IsChecked == true;
        s.PopupOnDown = popDown.IsChecked == true; s.PopupOnUp = popUp.IsChecked == true; s.Sound = sound.IsChecked == true;
        s.PopupSeconds = int.Parse(popupSecs.Text.Trim());
        s.DarkTheme = dark.IsChecked == true; s.CloseToTray = tray.IsChecked == true;
        s.LogFolder = logFolder.Text.Trim() == Storage.DefaultLogFolder ? "" : logFolder.Text.Trim();
        s.KeepLogDays = int.Parse(keepDays.Text.Trim());
        s.IntervalSeconds = secs.Value;
        try
        {
            App.SaveSettings();
            if (oldInterval != secs.Value) { App.Log.Info($"Ping interval set to {MonitorSettings.IntervalText(secs.Value)}"); App.Engine.RescheduleAll(); }
            SetStartWithWindows(withWindows.IsChecked == true);
            interval.Text = MonitorSettings.IntervalText(s.IntervalSeconds);
            saved.Text = "✔ Saved"; saved.Res(TextBlock.ForegroundProperty, "Ok");
        }
        catch (RuleException e) { saved.Text = e.Message; saved.Res(TextBlock.ForegroundProperty, "Sig"); }
    }

    static bool StartsWithWindows()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue(RunName) != null; }
        catch (Exception) { return false; }
    }

    static void SetStartWithWindows(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue(RunName, $"\"{Environment.ProcessPath}\" --minimized");
            else if (k.GetValue(RunName) != null) k.DeleteValue(RunName);
        }
        catch (Exception) { /* not allowed by policy: leave as is */ }
    }

    public void Shown() => Load();
    public void Hidden() { if (Theme.Dark != App.Settings.DarkTheme) Theme.Apply(App.Settings.DarkTheme); }
    public void Refresh() { }
}
