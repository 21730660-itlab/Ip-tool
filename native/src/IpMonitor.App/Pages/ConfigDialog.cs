using Microsoft.Win32;

namespace IpMonitor.App;

/// <summary>Config backups of one device: add a RouterOS export (.rsc file or pasted /export), view, compare versions, save, delete.</summary>
public class ConfigDialog
{
    static Store S => MainWindow.Instance.Store;
    readonly Device dev;
    readonly Dlg dlg;
    string viewId; bool diffMode;

    public class LineItem
    {
        public string Text { get; set; }
        public Brush Fg { get; set; }
        public Brush Bg { get; set; }
    }
    public class CfgRow
    {
        public string Id { get; set; }
        public string When { get; set; }
        public string At { get; set; }
        public string By { get; set; }
        public int Lines { get; set; }
        public string Size { get; set; }
        public string Ros { get; set; }
        public string Note { get; set; }
        public string Flags { get; set; }
    }

    // add-a-backup controls
    TextBox text, note; CheckBox mask, updRos; TextBlock state; Button save;
    // list + viewer
    DataGrid list; TextBlock viewTitle, viewInfo; ListBox viewer; Button bView, bDiff, bSaveAs, bDel;

    ConfigDialog(Device d)
    {
        dev = d;
        dlg = new Dlg($"Config backups · {d.Name}", 1040);
        dlg.AllowDrop = true;
        dlg.Drop += (_, e) => { if (e.Data.GetData(DataFormats.FileDrop) is string[] f && f.Length > 0) LoadFile(f[0]); };
        Build();
        dlg.Cancel("Close");
    }

    public static void Open(Device d)
    {
        if (d == null) return;
        var x = new ConfigDialog(d);
        x.ShowLatest();
        x.dlg.Open();
    }

    static string When(string at) => DateTime.TryParse(at, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : at;

    void Build()
    {
        var b = dlg.Body;
        b.Children.Add(Ui.Muted($"{S.SiteById(dev.SiteId)} · {dev.Model}{(string.IsNullOrEmpty(dev.Ros) ? "" : " · RouterOS " + dev.Ros)}", 13.5));

        if (S.CanWrite)
        {
            var add = new StackPanel();
            add.Children.Add(Ui.Text("Add a backup", 16, FontWeights.Bold));
            var how = Ui.Muted("Load the router's .rsc file (on the router: /export file=backup, then download backup.rsc from Files), drop the file on this window, or paste the text of /export below.", 13);
            how.Margin = new Thickness(0, 4, 0, 10); add.Children.Add(how);
            var load = Ui.IconBtn("", "Load .rsc file…", PickFile, "Primary");
            load.HorizontalAlignment = HorizontalAlignment.Left; load.Margin = new Thickness(0, 0, 0, 10);
            add.Children.Add(load);
            text = Ui.Box("", true, true); text.MinHeight = 130; text.MaxHeight = 200; text.FontSize = 12.5;
            text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto; text.TextWrapping = TextWrapping.NoWrap; text.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            text.TextChanged += (_, _) => Check();
            add.Children.Add(text);
            state = Ui.Text("", 13.5, FontWeights.SemiBold, "Muted"); state.Margin = new Thickness(0, 8, 0, 8);
            add.Children.Add(state);
            note = Ui.Box(); note.MaxLength = 120;
            add.Children.Add(Ui.Field("Note", note, "e.g. before firmware upgrade"));
            mask = new CheckBox { Content = "Hide passwords and keys before saving (recommended)", IsChecked = true, Margin = new Thickness(0, 0, 0, 6) };
            mask.Checked += (_, _) => Check(); mask.Unchecked += (_, _) => Check();
            updRos = new CheckBox { IsChecked = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 6) };
            add.Children.Add(mask); add.Children.Add(updRos);
            save = Ui.IconBtn("", "Save backup", Save, "Primary"); save.IsEnabled = false; save.HorizontalAlignment = HorizontalAlignment.Left; save.Margin = new Thickness(0, 6, 0, 0);
            add.Children.Add(save);
            var card = Ui.Card(add, 16); card.Margin = new Thickness(0, 12, 0, 16);
            b.Children.Add(card);
        }

        b.Children.Add(Ui.Text("Saved versions", 16, FontWeights.Bold));
        list = Ui.Table(); list.MaxHeight = 220; list.CanUserSortColumns = false; list.Margin = new Thickness(0, 8, 0, 8);
        list.Columns.Add(Ui.Col("Saved", nameof(CfgRow.When), 140, false, nameof(CfgRow.At)));
        list.Columns.Add(Ui.Col("By", nameof(CfgRow.By), 100));
        list.Columns.Add(Ui.Col("Lines", nameof(CfgRow.Lines)));
        list.Columns.Add(Ui.Col("Size", nameof(CfgRow.Size)));
        list.Columns.Add(Ui.Col("RouterOS", nameof(CfgRow.Ros)));
        list.Columns.Add(Ui.Col("Note", nameof(CfgRow.Note), -1));
        list.Columns.Add(Ui.Col("", nameof(CfgRow.Flags), 0));
        list.SelectionChanged += (_, _) => Buttons();
        Ui.OnRowDoubleClick(list, r => { viewId = ((CfgRow)r).Id; diffMode = false; ShowAndScroll(); });
        b.Children.Add(list);

        bView = Ui.Btn("View", () => { if (Sel() is ConfigBackup c) { viewId = c.Id; diffMode = false; ShowAndScroll(); } });
        bDiff = Ui.Btn("What changed", () => { if (Sel() is ConfigBackup c) { viewId = c.Id; diffMode = true; ShowAndScroll(); } }, null, "Compare with the version before it");
        bSaveAs = Ui.IconBtn("", "Save as .rsc…", () => { if (Sel() is ConfigBackup c) SaveAs(c); });
        bDel = Ui.Btn("Delete", () =>
        {
            if (Sel() is not ConfigBackup c) return;
            if (!Ui.Ask($"Delete the config saved {When(c.At)}{(string.IsNullOrEmpty(c.By) ? "" : " by " + c.By)}?", "Delete backup", "Delete", true)) return;
            try { S.DeleteConfig(c.Id); if (viewId == c.Id) viewId = null; Refresh(); ShowView(); } catch (RuleException e) { Ui.Info(e.Message); }
        }, "Danger");
        b.Children.Add(Ui.Row(8, bView, bDiff, bSaveAs, bDel));

        var vh = new StackPanel { Margin = new Thickness(0, 16, 0, 6) };
        viewTitle = Ui.Text("", 16, FontWeights.Bold); viewInfo = Ui.Muted("", 13);
        vh.Children.Add(viewTitle); vh.Children.Add(viewInfo);
        b.Children.Add(vh);
        viewer = new ListBox { Height = 360, FontFamily = new FontFamily(Ui.Mono), FontSize = 12.5, BorderThickness = new Thickness(1) };
        viewer.SetResourceReference(Control.BackgroundProperty, "Panel");
        viewer.SetResourceReference(Control.BorderBrushProperty, "Line");
        VirtualizingPanel.SetIsVirtualizing(viewer, true);
        var tb = new FrameworkElementFactory(typeof(TextBlock));
        tb.SetBinding(TextBlock.TextProperty, new Binding(nameof(LineItem.Text)));
        tb.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(LineItem.Fg)));
        tb.SetBinding(TextBlock.BackgroundProperty, new Binding(nameof(LineItem.Bg)));
        tb.SetValue(TextBlock.PaddingProperty, new Thickness(4, 0, 4, 0));
        viewer.ItemTemplate = new DataTemplate { VisualTree = tb };
        b.Children.Add(viewer);
        Refresh();
    }

    ConfigBackup Sel() => list.SelectedItem is CfgRow r ? S.Db.Configs.FirstOrDefault(c => c.Id == r.Id) : null;

    void Refresh()
    {
        var cs = S.ConfigsOf(dev.Id);
        list.ItemsSource = cs.Select((c, i) => new CfgRow
        {
            Id = c.Id, When = When(c.At), At = c.At, By = c.By, Lines = c.Lines, Size = Rsc.Size(c.Size), Ros = c.Ros, Note = c.Note,
            Flags = string.Join(" · ", new[] { i == 0 ? "LATEST" : "", c.Masked == false ? "HAS PASSWORDS" : "" }.Where(t => t != ""))
        }).ToList();
        if (viewId != null && list.ItemsSource is List<CfgRow> rows && rows.FirstOrDefault(r => r.Id == viewId) is CfgRow sel) list.SelectedItem = sel;
        Buttons();
    }

    void Buttons()
    {
        var c = Sel(); var cs = S.ConfigsOf(dev.Id);
        bView.IsEnabled = c != null; bSaveAs.IsEnabled = c != null;
        bDiff.IsEnabled = c != null && cs.IndexOf(c) < cs.Count - 1;
        bDel.IsEnabled = c != null && S.FullAccess;
    }

    void ShowLatest()
    {
        var cs = S.ConfigsOf(dev.Id);
        viewId = cs.FirstOrDefault()?.Id; diffMode = false; Refresh(); ShowView();
    }

    /// <summary>Shows the selected version and scrolls the window down to it.</summary>
    void ShowAndScroll() { ShowView(); dlg.Dispatcher.BeginInvoke(() => viewTitle.BringIntoView(new Rect(0, 0, 10, 420)), System.Windows.Threading.DispatcherPriority.Loaded); }

    void ShowView()
    {
        var cs = S.ConfigsOf(dev.Id);
        var i = cs.FindIndex(c => c.Id == viewId);
        if (i < 0) { viewTitle.Text = cs.Count == 0 ? "No backups saved for this device yet." : ""; viewInfo.Text = ""; viewer.ItemsSource = null; viewer.Visibility = cs.Count == 0 ? Visibility.Collapsed : Visibility.Visible; return; }
        viewer.Visibility = Visibility.Visible;
        var c = cs[i]; var prev = i + 1 < cs.Count ? cs[i + 1] : null;
        Brush ink = Theme.B("Ink"), muted = Theme.B("Muted");
        if (diffMode && prev != null)
        {
            var ops = Rsc.Diff(prev.Text, c.Text);
            int add = ops.Count(o => o.op == '+'), del = ops.Count(o => o.op == '-');
            viewTitle.Text = "What changed";
            viewInfo.Text = $"{When(prev.At)} → {When(c.At)} · +{add} added · −{del} removed";
            viewer.ItemsSource = add + del == 0
                ? new List<LineItem> { new() { Text = "No differences apart from the export date.", Fg = muted, Bg = Brushes.Transparent } }
                : Rsc.DiffContext(ops).Select(o => new LineItem
                {
                    Text = o.op == '~' ? o.line : $"{(o.op == ' ' ? " " : o.op.ToString())} {o.line}",
                    Fg = o.op == '+' ? Theme.B("Ok") : o.op == '-' ? Theme.B("Sig") : o.op == '~' ? muted : ink,
                    Bg = o.op == '+' ? Theme.B("OkSoft") : o.op == '-' ? Theme.B("SigSoft") : Brushes.Transparent
                }).ToList();
        }
        else
        {
            viewTitle.Text = $"Saved {When(c.At)}";
            viewInfo.Text = string.Join(" · ", new[] { string.IsNullOrEmpty(c.By) ? "" : "by " + c.By, $"{c.Lines} lines", Rsc.Size(c.Size), string.IsNullOrEmpty(c.Ros) ? "" : "RouterOS " + c.Ros, c.Model, c.Note }.Where(t => !string.IsNullOrEmpty(t)));
            viewer.ItemsSource = c.Text.Replace("\r\n", "\n").Split('\n').Select(l => new LineItem
            {
                Text = l, Fg = l.StartsWith('#') ? muted : l.StartsWith('/') ? Theme.B("Acc") : ink, Bg = Brushes.Transparent
            }).ToList();
        }
    }

    // ------------------------------------------------------------------ adding
    void PickFile()
    {
        var f = new OpenFileDialog { Title = $"Config backup of {dev.Name}", Filter = "RouterOS export (*.rsc)|*.rsc|Text files (*.txt)|*.txt|All files|*.*" };
        if (f.ShowDialog(dlg) == true) LoadFile(f.FileName);
    }

    void LoadFile(string path)
    {
        if (!S.CanWrite) return;
        try
        {
            var info = new FileInfo(path);
            if (info.Length > Rsc.MaxBytes * 2) { Ui.Info($"{info.Name} is {Rsc.Size(info.Length)}. A normal /export is much smaller.", "File too big"); return; }
            var raw = File.ReadAllText(path);
            if (raw.Contains('\0'))
            {
                Ui.Info($"{info.Name} is a binary .backup file. IP Monitor keeps text exports, so you can read and compare them.\n\nOn the router run:  /export file=backup\nthen download backup.rsc from Files and load that.", "Not a text export");
                return;
            }
            text.Text = raw;
            if (string.IsNullOrWhiteSpace(note.Text)) note.Text = info.Name;
        }
        catch (Exception e) { Ui.Info("Couldn't read the file: " + e.Message); }
    }

    bool Check()
    {
        updRos.Visibility = Visibility.Collapsed;
        if (string.IsNullOrWhiteSpace(text.Text)) { state.Text = ""; save.IsEnabled = false; return false; }
        try
        {
            var (t, info, msg, warn) = S.CheckConfig(dev.Id, text.Text, mask.IsChecked == true);
            state.Text = "✓ " + msg + (warn.Count > 0 ? "\n⚠ " + string.Join("\n⚠ ", warn) : "");
            state.SetResourceReference(TextBlock.ForegroundProperty, warn.Count > 0 ? "Warn" : "Ok");
            if (info.Ros != "" && info.Ros != dev.Ros) { updRos.Content = $"Also update the device's RouterOS version: {(string.IsNullOrEmpty(dev.Ros) ? "—" : dev.Ros)} → {info.Ros}"; updRos.Visibility = Visibility.Visible; }
            save.IsEnabled = true;
            return true;
        }
        catch (RuleException e)
        {
            state.Text = "✕ " + e.Message; state.SetResourceReference(TextBlock.ForegroundProperty, "Sig");
            save.IsEnabled = false;
            return false;
        }
    }

    void Save()
    {
        if (!Check()) return;
        try
        {
            var hadPrev = S.ConfigsOf(dev.Id).Count > 0;
            var c = S.SaveConfig(dev.Id, text.Text, note.Text, mask.IsChecked == true, updRos.Visibility == Visibility.Visible && updRos.IsChecked == true);
            text.Text = ""; note.Text = "";
            viewId = c.Id; diffMode = hadPrev;
            Refresh(); ShowAndScroll();
            MainWindow.Instance.Toast(hadPrev ? "Backup saved — showing what changed since the previous one." : "Backup saved.");
        }
        catch (RuleException e) { state.Text = "✕ " + e.Message; state.SetResourceReference(TextBlock.ForegroundProperty, "Sig"); }
    }

    void SaveAs(ConfigBackup c)
    {
        var day = DateTime.TryParse(c.At, out var t) ? t.ToLocalTime().ToString("yyyy-MM-dd") : "backup";
        var name = System.Text.RegularExpressions.Regex.Replace($"{dev.Name}-{day}", @"[^A-Za-z0-9._\-]+", "_") + ".rsc";
        var f = new SaveFileDialog { FileName = name, Filter = "RouterOS export (*.rsc)|*.rsc" };
        if (f.ShowDialog(dlg) != true) return;
        try { File.WriteAllText(f.FileName, c.Text); MainWindow.Instance.Toast("Saved " + IOPath.GetFileName(f.FileName)); }
        catch (Exception e) { Ui.Info("Couldn't save: " + e.Message); }
    }
}
