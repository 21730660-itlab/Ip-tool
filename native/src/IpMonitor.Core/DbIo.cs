using System.Text.Encodings.Web;
using System.Text.Json;

namespace IpMonitor.Core;

/// <summary>Reads and writes ip-monitor-db.json (and the web app's JSON backups).</summary>
public static class DbIo
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static DbFile Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Accepts the database file, and the web app's backup where networks sit inside their site.</summary>
    public static DbFile Parse(string text)
    {
        text = (text ?? "").TrimStart('﻿');
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("sites", out var sitesEl) || sitesEl.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("This file isn't an IP Monitor database or backup.");
        if (root.TryGetProperty("vault", out _))
            throw new InvalidDataException("Device passwords in this file are locked with a master password. Open it in the web version and remove the master password first.");
        var nested = sitesEl.EnumerateArray().Any(s => s.ValueKind == JsonValueKind.Object && s.TryGetProperty("networks", out var n) && n.ValueKind == JsonValueKind.Array);
        var db = JsonSerializer.Deserialize<DbFile>(text, Options) ?? new DbFile();
        if (nested)
        {
            // backup layout: { sites: [ { …site, networks: [ … ] } ] }
            db.Networks = new List<Network>();
            foreach (var s in db.Sites)
            {
                if (s.Extra != null && s.Extra.TryGetValue("networks", out var nets))
                {
                    foreach (var n in nets.Deserialize<List<Network>>(Options) ?? new()) { n.SiteId = s.Id; db.Networks.Add(n); }
                    s.Extra.Remove("networks");
                }
            }
            db.Sites.RemoveAll(s => string.IsNullOrEmpty(s.Id) && s.Name == "Unassigned networks");
        }
        Normalize(db);
        return db;
    }

    static void Normalize(DbFile db)
    {
        db.Sites ??= new(); db.Networks ??= new(); db.Devices ??= new(); db.Links ??= new(); db.Vlans ??= new();
        db.Configs ??= new(); db.Changes ??= new(); db.Users ??= new();
        foreach (var n in db.Networks) { n.HostList ??= new(); }
        foreach (var d in db.Devices) { d.Ports ??= new(); d.Bridges ??= new(); d.Addrs ??= new(); foreach (var b in d.Bridges) b.Ports ??= new(); }
        foreach (var c in db.Changes) c.Lines ??= new();
        foreach (var s in db.Sites) if (string.IsNullOrEmpty(s.Id)) s.Id = Entity.NewId();
    }

    /// <summary>A deep copy, for editing in a dialog without touching the database until Save.</summary>
    public static T Clone<T>(T x) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(x, Options), Options);

    public static string Serialize(DbFile db) => JsonSerializer.Serialize(db, Options);

    /// <summary>Writes safely: to a temporary file first, then swaps it in, so a crash never leaves half a file.</summary>
    public static void Save(DbFile db, string path, string savedBy)
    {
        db.SavedAt = Entity.Now(); db.SavedBy = savedBy ?? "";
        var tmp = path + ".saving";
        File.WriteAllText(tmp, Serialize(db));
        File.Move(tmp, path, overwrite: true);
    }
}
