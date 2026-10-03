namespace DeviceMonitor.App.Pages;

/// <summary>Add / edit a site (name, location, notes).</summary>
public static class SiteDialog
{
    /// <summary>Returns the new site, or null when cancelled.</summary>
    public static Site Add() => Open(new Site(), true);
    public static void Edit(Site s) => Open(s.Clone(), false);

    static Site Open(Site s, bool isNew)
    {
        var dlg = new Dlg(isNew ? "Add site" : "Edit site", 560);
        var name = Ui.Box(s.Name, tip: "For example \"Main office\", \"Branch Tripoli\", \"Tower 3\"");
        var loc = Ui.Box(s.Location, tip: "Address or description (optional)");
        var notes = Ui.Box(s.Notes, multi: true);
        if (isNew)
        {
            var hint = Ui.Muted("Create the site first, then add the IP addresses of its devices to it.", 13.5);
            hint.Margin = new Thickness(0, 0, 0, 14);
            dlg.Body.Children.Add(hint);
        }
        dlg.Body.Children.Add(Ui.Field("Site name", name));
        dlg.Body.Children.Add(Ui.Field("Location", loc));
        dlg.Body.Children.Add(Ui.Field("Notes", notes));
        if (!isNew)
            dlg.Extra("Delete site", () => { if (ConfirmDelete(s)) dlg.DialogResult = false; }, "Danger");
        dlg.Ok(isNew ? "Add site" : "Save", () =>
        {
            s.Name = name.Text; s.Location = loc.Text; s.Notes = notes.Text;
            App.SaveSite(s);
            return true;
        });
        dlg.Cancel();
        dlg.Loaded += (_, _) => name.Focus();
        return dlg.Open() && isNew ? s : null;
    }

    /// <summary>Asks, then deletes the site and its devices. True when deleted.</summary>
    public static bool ConfirmDelete(Site s)
    {
        var n = App.DevicesOf(s.Id).Count();
        var msg = n == 0 ? $"Delete the site \"{s.Name}\"?" : $"Delete the site \"{s.Name}\" and its {n} device(s)?\n\nThey will no longer be monitored. The log files keep their past events.";
        if (!Ui.Ask(msg, "Delete site", "Delete", true)) return false;
        App.DeleteSite(s.Id);
        return true;
    }
}
