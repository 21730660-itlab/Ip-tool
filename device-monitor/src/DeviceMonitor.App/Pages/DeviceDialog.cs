namespace DeviceMonitor.App.Pages;

/// <summary>Add / edit a device: name, IP, site, kind, own interval, notes, with a "Test ping" button.</summary>
public static class DeviceDialog
{
    /// <summary>Adds a device to a site (default: the site chosen in the top bar). With no site yet, a site is created first.</summary>
    public static void Add(string siteId = null)
    {
        if (App.Sites.Count == 0)
        {
            if (!Ui.Ask("Devices belong to a site, and there is no site yet.\n\nAdd a site first (for example \"Main office\"), then add its IP addresses.", "Add a site first", "Add site")) return;
            var s = SiteDialog.Add();
            if (s == null) return;
            siteId = s.Id;
        }
        siteId ??= App.CurrentSiteId;
        Open(new Device { SiteId = App.FindSite(siteId) != null ? siteId : App.Sites[0].Id }, true);
    }
    public static void Edit(Device d) => Open(d.Clone(), false);

    static void Open(Device d, bool isNew)
    {
        var dlg = new Dlg(isNew ? "Add device (IP) to a site" : "Edit device", 660);
        var name = Ui.Box(d.Name, tip: "A name you recognise, for example \"Core router\" or \"AP floor 2\"");
        var addr = Ui.Box(d.Address, mono: true, tip: "IPv4, IPv6 or host name");
        var kind = Ui.Choice(Enum.GetValues<DeviceKind>().Select(k => (k.ToString(), Theme.KindText(k))), d.Kind.ToString());
        var site = new ComboBox { DisplayMemberPath = "Label", SelectedValuePath = "Id", ItemsSource = App.Sites, SelectedValue = d.SiteId, MaxDropDownHeight = 360 };
        var newSite = Ui.IconBtn("\uE710", "New site…", () =>
        {
            var s = SiteDialog.Add();
            if (s == null) return;
            site.ItemsSource = App.Sites; site.SelectedValue = s.Id;
        });
        newSite.Margin = new Thickness(8, 0, 0, 0);
        var siteRow = new DockPanel();
        DockPanel.SetDock(newSite, Dock.Right); siteRow.Children.Add(newSite); siteRow.Children.Add(site);
        var defText = $"Default ({MonitorSettings.IntervalText(App.Settings.IntervalSeconds)})";
        var interval = UiExtra.IntervalBox(d.IntervalSeconds, defText, 260);
        var notes = Ui.Box(d.Notes, multi: true);
        var enabled = new CheckBox { Content = "Monitor this device (untick to pause it)", IsChecked = d.Enabled, FontSize = 14.5 };

        var test = Ui.Muted("", 13.5);
        test.Margin = new Thickness(12, 0, 0, 0); test.VerticalAlignment = VerticalAlignment.Center;
        var testBtn = Ui.IconBtn("\uE72C", "Test ping", () => { });
        testBtn.Click += async (_, _) =>
        {
            var a = addr.Text.Trim();
            if (!Validation.IsAddress(a)) { test.Text = "Enter a valid address first."; test.Res(TextBlock.ForegroundProperty, "Sig"); return; }
            testBtn.IsEnabled = false; test.Text = "Pinging " + a + "…"; test.Res(TextBlock.ForegroundProperty, "Muted");
            var r = await new PingProbe().CheckAsync(a, Math.Max(App.Settings.TimeoutMs, 1000), CancellationToken.None);
            testBtn.IsEnabled = true;
            test.Text = r.Ok ? $"✔ Reply in {r.Ms} ms — the device is ON" : $"✖ {r.Error}";
            test.Res(TextBlock.ForegroundProperty, r.Ok ? "Ok" : "Sig");
        };

        dlg.Body.Children.Add(Ui.Field("Site", siteRow, "The site this device belongs to"));
        dlg.Body.Children.Add(Ui.Cols(Ui.Field("Name", name), Ui.Field("IP address", addr, "For example 192.168.88.1 (MikroTik default)")));
        dlg.Body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -4, 0, 16), Children = { testBtn, test } });
        dlg.Body.Children.Add(Ui.Cols(Ui.Field("Kind", kind), Ui.Field("Ping interval", interval, "Default, a value from the list, or seconds (e.g. 45)")));
        dlg.Body.Children.Add(Ui.Field("Notes", notes));
        dlg.Body.Children.Add(enabled);

        if (!isNew)
            dlg.Extra("Delete", () =>
            {
                if (!Ui.Ask($"Delete \"{d.Name}\" ({d.Address}) from the monitor?", "Delete device", "Delete", true)) return;
                App.Delete(new[] { d.Id });
                dlg.DialogResult = false;
            }, "Danger");

        dlg.Ok(isNew ? "Add device" : "Save", () =>
        {
            var (ok, secs) = UiExtra.ReadInterval(interval, defText);
            if (!ok) throw new RuleException($"The interval must be a number of seconds between {MonitorSettings.MinInterval} and {MonitorSettings.MaxInterval} (or for example \"2 min\").");
            d.Name = name.Text; d.Address = addr.Text;
            d.Kind = Enum.Parse<DeviceKind>(kind.Val());
            d.SiteId = site.SelectedValue as string ?? ""; d.Notes = notes.Text;
            d.IntervalSeconds = secs; d.Enabled = enabled.IsChecked == true;
            App.Save(d);
            return true;
        });
        dlg.Cancel();
        dlg.Loaded += (_, _) => (isNew ? addr : name).Focus();
        dlg.Open();
    }
}
