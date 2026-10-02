namespace IpMonitor.App;

public class UsersPage : PageBase
{
    public override string Title => "Users";
    public override string Glyph => "";

    public class UserRow
    {
        public string Id { get; set; }
        public string Username { get; set; }
        public string Perm { get; set; }
        public string Created { get; set; }
        public string Me { get; set; }
        public Brush Fg { get; set; }
        public Brush Bg { get; set; }
    }

    public override FrameworkElement Build()
    {
        var g = Ui.Table();
        g.Columns.Add(Ui.Col("Username", nameof(UserRow.Username), -1.5));
        g.Columns.Add(Ui.BadgeCol("Permission", nameof(UserRow.Perm), nameof(UserRow.Fg), nameof(UserRow.Bg)));
        g.Columns.Add(Ui.Col("Created", nameof(UserRow.Created), -1));
        g.Columns.Add(Ui.Col("", nameof(UserRow.Me), -1));
        g.ItemsSource = S.Db.Users.OrderBy(u => u.Username, StringComparer.OrdinalIgnoreCase).Select(u =>
        {
            var p = Auth.Perms.Contains(u.Perm) ? u.Perm : "read";
            var (fg, bg) = p switch { "full" => ("Sig", "SigSoft"), "write" => ("Acc", "AccSoft"), _ => ("Muted", "Sunk") };
            return new UserRow
            {
                Id = u.Id, Username = u.Username, Perm = MainWindow.PermLabel(p), Fg = Theme.B(fg), Bg = Theme.B(bg),
                Created = DateTime.TryParse(u.CreatedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("yyyy-MM-dd") : "",
                Me = u.Id == S.Me.Id ? "you" : ""
            };
        }).ToList();
        UserRow Sel() => g.SelectedItem as UserRow;

        var perm = Ui.Choice(new[] { ("read", "View only"), ("write", "Can edit"), ("full", "Full access") }, "read"); perm.Width = 170;
        var set = Ui.Btn("Set permission", () =>
        {
            if (Sel() is not UserRow r) return;
            try { S.SetPermission(r.Id, perm.Val()); W.Toast($"{r.Username}: {MainWindow.PermLabel(perm.Val())}"); }
            catch (RuleException e) { Ui.Info(e.Message, "Permission"); }
        });
        var del = Ui.IconBtn("", "Delete account", () => { if (Sel() is UserRow r) Confirm($"Delete the account {r.Username}?", () => S.DeleteUser(r.Id)); }, "Danger");
        void Buttons() { var on = Sel() != null && S.FullAccess && Sel().Id != S.Me.Id; perm.IsEnabled = on; set.IsEnabled = on; del.IsEnabled = on; }
        g.SelectionChanged += (_, _) => { if (Sel() is UserRow r) perm.SelectedValue = S.Db.Users.First(u => u.Id == r.Id).Perm; Buttons(); };
        Buttons();

        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(perm);
        set.Margin = new Thickness(8, 0, 0, 0); tools.Children.Add(set);
        del.Margin = new Thickness(8, 0, 0, 0); tools.Children.Add(del);

        var info = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        info.Children.Add(Ui.Section("Permissions"));
        info.Children.Add(Ui.Text("View only — sees everything except device passwords and wireless keys.\nCan edit — adds and changes sites, networks, devices, connections and VLANs.\nFull access — also deletes, manages accounts, restores backups and exports passwords.", 14, null, "Ink2"));
        var body = new DockPanel();
        DockPanel.SetDock(info, Dock.Bottom); body.Children.Add(info);
        body.Children.Add(g);

        var add = Ui.IconBtn("", "Add account", AddAccount, "Primary");
        add.IsEnabled = S.FullAccess;
        return Layout(Header("Users", $"{S.Db.Users.Count} accounts. You are signed in as {S.Me.Username} ({MainWindow.PermLabel(S.Perm)}).", add), S.FullAccess ? tools : Ui.Muted("Only accounts with Full access can manage accounts.", 14), body);
    }

    static void AddAccount()
    {
        var d = new Dlg("Add account", 520);
        var user = Ui.Box(); var p1 = new PasswordBox(); var p2 = new PasswordBox();
        var perm = Ui.Choice(new[] { ("read", "View only"), ("write", "Can edit"), ("full", "Full access") }, "read");
        d.Body.Children.Add(Ui.Field("Username", user, "3–32 characters: letters, numbers, dot, dash or underscore."));
        d.Body.Children.Add(Ui.Cols(Ui.Field("Password", p1, "At least 8 characters."), Ui.Field("Confirm", p2)));
        d.Body.Children.Add(Ui.Field("Permission", perm));
        d.Ok("Add account", () => { S.AddUser(user.Text, p1.Password, p2.Password, perm.Val()); return true; });
        d.Cancel();
        d.Loaded += (_, _) => user.Focus();
        d.Open();
    }
}
