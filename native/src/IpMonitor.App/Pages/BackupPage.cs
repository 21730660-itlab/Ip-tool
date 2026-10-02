using Microsoft.Win32;

namespace IpMonitor.App;

public class BackupPage : PageBase
{
    public override string Title => "Backup & export";
    public override string Glyph => "";

    public override FrameworkElement Build()
    {
        var sp = new StackPanel();
        sp.Children.Add(Header("Backup & export", "Your data is saved automatically to the database file after every change."));

        // database file
        var f = new StackPanel();
        f.Children.Add(Ui.Text("Database file", 17, FontWeights.Bold));
        var path = Ui.Text(S.FilePath, 14, FontWeights.SemiBold, "Ink2", true, true); path.Margin = new Thickness(0, 6, 0, 4); f.Children.Add(path);
        var fi = new FileInfo(S.FilePath);
        f.Children.Add(Ui.Muted($"{(fi.Exists ? $"{fi.Length / 1024.0:N1} KB · last written {fi.LastWriteTime:yyyy-MM-dd HH:mm:ss}" : "missing")} · {S.Db.Sites.Count} sites, {S.Db.Networks.Count} networks, {S.Db.Devices.Count} devices, {S.Db.Links.Count} connections, {S.Db.Users.Count} accounts", 13));
        var fb = Ui.Row(10,
            Ui.IconBtn("", "Show in folder", () => System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{S.FilePath}\"")),
            Ui.IconBtn("", "Reload from file", () => { try { S.Reload(); W.Toast("Reloaded from the file."); } catch (Exception e) { Ui.Info(e.Message); } }),
            Ui.IconBtn("", "Open another database…", OpenOther));
        fb.Margin = new Thickness(0, 14, 0, 0); f.Children.Add(fb);
        sp.Children.Add(Ui.Card(f));

        // backup copy
        var b = new StackPanel();
        b.Children.Add(Ui.Text("Backup copy", 17, FontWeights.Bold));
        b.Children.Add(Ui.Muted(S.FullAccess ? "A complete copy of the database (with accounts and device passwords). It can be opened here or in the web version." : "A copy without device passwords, wireless keys and accounts (your account doesn't have Full access).", 13.5));
        var bb = Ui.Row(10, Ui.IconBtn("", "Save a backup copy…", SaveCopy, "Primary"));
        if (S.FullAccess) bb.Children.Add(Ui.IconBtn("", "Restore from a backup…", Restore, "Danger"));
        ((FrameworkElement)bb.Children[^1]).Margin = new Thickness(bb.Children.Count > 1 ? 10 : 0, 0, 0, 0);
        bb.Margin = new Thickness(0, 14, 0, 0); b.Children.Add(bb);
        var bc = Ui.Card(b); bc.Margin = new Thickness(0, 16, 0, 0); sp.Children.Add(bc);

        // spreadsheets
        var x = new StackPanel();
        x.Children.Add(Ui.Text("Export to Excel (CSV)", 17, FontWeights.Bold));
        x.Children.Add(Ui.Muted("CSV files open directly in Excel.", 13.5));
        var xb = Ui.Row(10,
            Ui.IconBtn("", "Networks & hosts", () => Export("ip-monitor-networks.csv", S.NetworksCsv())),
            Ui.IconBtn("", "Devices", () => Export("ip-monitor-devices.csv", S.DevicesCsv(false))),
            Ui.IconBtn("", "Connections", () => Export("ip-monitor-connections.csv", S.LinksCsv())));
        if (S.FullAccess) xb.Children.Add(Ui.IconBtn("", "Devices with passwords", () =>
        {
            if (Ui.Ask("This file contains device passwords and wireless keys in plain text. Keep it somewhere safe.", "Passwords", "Export"))
                Export("ip-monitor-devices-credentials.csv", S.DevicesCsv(true));
        }, "Danger"));
        ((FrameworkElement)xb.Children[^1]).Margin = new Thickness(10, 0, 0, 0);
        xb.Margin = new Thickness(0, 14, 0, 0); x.Children.Add(xb);
        var xc = Ui.Card(x); xc.Margin = new Thickness(0, 16, 0, 0); sp.Children.Add(xc);

        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Padding = new Thickness(28, 22, 28, 22), Child = sp } };
    }

    static string Stamp => DateTime.Now.ToString("yyyy-MM-dd_HHmm");

    static void Export(string name, string text)
    {
        var d = new SaveFileDialog { FileName = name.Replace(".csv", "_" + Stamp + ".csv"), Filter = "CSV for Excel (*.csv)|*.csv" };
        if (d.ShowDialog(W) != true) return;
        try { File.WriteAllText(d.FileName, text, new System.Text.UTF8Encoding(false)); W.Toast("Saved " + IOPath.GetFileName(d.FileName)); }
        catch (Exception e) { Ui.Info("Couldn't save: " + e.Message); }
    }

    static void SaveCopy()
    {
        var d = new SaveFileDialog { FileName = $"ip-monitor-backup_{Stamp}.json", Filter = "IP Monitor backup (*.json)|*.json" };
        if (d.ShowDialog(W) != true) return;
        if (string.Equals(IOPath.GetFullPath(d.FileName), IOPath.GetFullPath(S.FilePath), StringComparison.OrdinalIgnoreCase)) { Ui.Info("Choose another name: that is the database file itself."); return; }
        try
        {
            var copy = DbIo.Clone(S.Db);
            copy.Kind = "backup";
            if (!S.FullAccess)
            {
                foreach (var x in copy.Devices) { x.Pass = ""; x.Psk = ""; }
                copy.Users.Clear(); copy.Configs.Clear();
            }
            File.WriteAllText(d.FileName, DbIo.Serialize(copy));
            W.Toast("Backup saved: " + IOPath.GetFileName(d.FileName));
        }
        catch (Exception e) { Ui.Info("Couldn't save: " + e.Message); }
    }

    static void Restore()
    {
        var d = new OpenFileDialog { Title = "Restore from a backup", Filter = "IP Monitor backup or database (*.json)|*.json" };
        if (d.ShowDialog(W) != true) return;
        if (!Ui.Ask($"Replace ALL data in the database with the contents of {IOPath.GetFileName(d.FileName)}?\n\nTip: save a backup copy first.", "Restore", "Restore", true)) return;
        try { Ui.Info(S.RestoreFrom(d.FileName), "Restored"); }
        catch (Exception e) when (e is RuleException or InvalidDataException or System.Text.Json.JsonException or IOException) { Ui.Info("Couldn't restore: " + e.Message, "Restore"); }
    }

    static void OpenOther()
    {
        var d = new OpenFileDialog { Title = "Open another IP Monitor database", Filter = "IP Monitor database (*.json)|*.json" };
        if (d.ShowDialog(W) != true) return;
        try
        {
            var db = DbIo.Load(d.FileName);
            if (!Ui.Ask($"Switch to {IOPath.GetFileName(d.FileName)}? You will sign in again with an account of that database.", "Open database", "Open")) return;
            W.Store.Me = null; W.Store.Open(d.FileName);
            W.Settings.LastFile = d.FileName; W.Settings.Save();
            W.LogOut();
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or IOException) { Ui.Info("Couldn't open: " + e.Message); }
    }
}
