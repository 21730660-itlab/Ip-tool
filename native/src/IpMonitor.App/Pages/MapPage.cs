using System.Text.Json;

namespace IpMonitor.App;

/// <summary>
/// The site map, as in the web version: every site is a box with its devices (routers on top, wireless in the middle,
/// end devices at the bottom), cables and wireless links between them. Drag a device or a site title to arrange it
/// (saved in the database file, shared with the web version), drag the background to move around, wheel to zoom.
/// To connect two devices: "Add connection", click the first device, then the second.
/// </summary>
public class MapPage : PageBase
{
    public override string Title => "Site map";
    public override string Glyph => "";

    const double NW = 132, NH = 76, CW = 252, CH = 160, PAD = 20, HDR = 34, GAP = 130, MAXW = 1100;
    static readonly string[] TierLabel = { "CORE · ROUTING", "WIRELESS", "END DEVICES" };

    // view state (kept while the app is open)
    double zoom = 1, tx = 20, ty = 20;
    bool needFit = true;
    string lastFilter;

    // what is on the canvas now
    MapLayout L;
    Canvas canvas;
    Border host;
    readonly ScaleTransform scale = new();
    readonly TranslateTransform move = new();

    // mouse
    string downTag; Point downScreen, downWorld; bool moved, panning;
    double panTx, panTy;
    (string kind, string id, double dx, double dy)? drag;

    // connect mode
    bool connecting; string connectA; Point mouseWorld;
    Shapes.Line rubber;
    Border cnBar; TextBlock cnMsg;

    record P(double X, double Y, Device D, bool Ghost);

    // drawing a site for a report (no window): which site, and no grid
    string renderFilter; bool print;
    string Filter => renderFilter ?? W.SiteFilter;

    /// <summary>A picture of one site's map (JPEG, twice the screen size), or null when the site has no devices.</summary>
    public static (byte[] jpeg, int w, int h)? RenderSite(string siteId)
    {
        var mp = new MapPage { renderFilter = siteId, print = true, canvas = new Canvas() };
        mp.L = mp.Compute();
        if (mp.L.ShownDevs.Count == 0) return null;
        mp.Draw();
        double w = mp.L.W, h = mp.L.H;
        var root = new Border { Width = w, Height = h, Background = Brushes.White, ClipToBounds = true, Child = mp.canvas };
        root.Measure(new Size(w, h)); root.Arrange(new Rect(0, 0, w, h)); root.UpdateLayout();
        double scale = Math.Min(2, 4000 / Math.Max(w, h));
        int pw = (int)Math.Ceiling(w * scale), ph = (int)Math.Ceiling(h * scale);
        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(pw, ph, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        rtb.Render(root);
        var enc = new System.Windows.Media.Imaging.JpegBitmapEncoder { QualityLevel = 92 };
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(new System.Windows.Media.Imaging.FormatConvertedBitmap(rtb, PixelFormats.Bgr24, null, 0)));
        using var ms = new MemoryStream(); enc.Save(ms);
        return (ms.ToArray(), pw, ph);
    }
    class MapLayout
    {
        public double W, H;
        public readonly Dictionary<string, Rect> Sites = new();
        public readonly Dictionary<string, (double rx, double ry, string site)> Devs = new();
        public readonly Dictionary<string, P> Pos = new();
        public List<Site> ShownSites = new();
        public List<Device> ShownDevs = new();
        public List<Link> ShownLinks = new();
    }

    static Brush TypeBrush(string type) => Theme.B(type switch
    {
        "router" => "Acc", "both" => "Ok", "pc" or "phone" => "Acc", "server" or "dbserver" => "CSrv", "nvr" or "camera" => "CCam", _ => "Sig"
    });
    static int TierOf(Device d) => d.Type is "router" or "both" ? 0 : d.Type == "wireless" ? 1 : 2;

    // ------------------------------------------------------------------ page
    public override FrameworkElement Build()
    {
        if (W.SiteFilter != "" && S.SiteById(W.SiteFilter) == null) W.SiteFilter = "";
        if (lastFilter != W.SiteFilter) { needFit = true; lastFilter = W.SiteFilter; }

        // site chips
        var chips = new WrapPanel();
        void Chip(string id, string text)
        {
            var b = Ui.Btn(text, () => { W.SiteFilter = id; ExitConnect(); W.Navigate(this); }, W.SiteFilter == id ? "Primary" : null);
            b.Margin = new Thickness(0, 0, 8, 8); b.MinHeight = 32; b.Padding = new Thickness(12, 4, 12, 4);
            chips.Children.Add(b);
        }
        Chip("", "All sites");
        foreach (var s in S.Db.Sites.OrderBy(s => s.SiteNumber?.PadLeft(10, '0'))) Chip(s.Id, $"#{s.SiteNumber}  {s.Name}");

        L = Compute();

        // readouts
        var inter = L.ShownLinks.Count(l => S.DevById(l.A) is Device a && S.DevById(l.B) is Device b && a.SiteId != b.SiteId);
        var ro = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 12) };
        foreach (var (v, t) in new[] { (L.ShownDevs.Count, "Devices"), (L.ShownLinks.Count, "Connections"), (L.ShownLinks.Count(l => l.Type == "wireless"), "Wireless links"), (inter, "Site-to-site") })
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(Ui.Text(v.ToString(), 18, FontWeights.Bold));
            var tt = Ui.Muted(t, 13); tt.Margin = new Thickness(8, 0, 0, 0); tt.VerticalAlignment = VerticalAlignment.Center; sp.Children.Add(tt);
            var c = Ui.Card(sp, 10); c.Margin = new Thickness(0, 0, 10, 0); c.Padding = new Thickness(14, 6, 14, 6);
            ro.Children.Add(c);
        }

        // connect bar
        cnMsg = Ui.Text("", 14, FontWeights.SemiBold, "Acc");
        cnMsg.VerticalAlignment = VerticalAlignment.Center;
        var form = Ui.Btn("Use the form instead", () => { ExitConnect(); W.Page<LinksPage>().Edit(null); });
        var cancel = Ui.Btn("Cancel", ExitConnect, "Primary"); cancel.Margin = new Thickness(8, 0, 0, 0);
        var cnDock = new DockPanel();
        var cnBtns = Ui.Row(0, form, cancel); DockPanel.SetDock(cnBtns, Dock.Right); cnDock.Children.Add(cnBtns);
        cnDock.Children.Add(cnMsg);
        cnBar = new Border { Child = cnDock, Padding = new Thickness(14, 8, 10, 8), CornerRadius = new CornerRadius(6), BorderThickness = new Thickness(1), Margin = new Thickness(10, 10, 10, 0), VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 520, Visibility = Visibility.Collapsed }
            .Res(Border.BackgroundProperty, "AccSoft").Res(Border.BorderBrushProperty, "AccLine");

        // the drawing
        canvas = new Canvas { RenderTransform = new TransformGroup { Children = { scale, move } } };
        host = new Border { ClipToBounds = true, Child = canvas, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Focusable = true, Cursor = Cursors.Arrow }
            .Res(Border.BackgroundProperty, "Panel").Res(Border.BorderBrushProperty, "Line");
        host.MouseLeftButtonDown += OnDown;
        host.MouseMove += OnMove;
        host.MouseLeftButtonUp += OnUp;
        host.MouseWheel += (_, e) => { var p = e.GetPosition(host); ZoomAt(e.Delta > 0 ? 1.15 : 1 / 1.15, p); e.Handled = true; };
        host.KeyDown += (_, e) => { if (e.Key == Key.Escape && connecting) { ExitConnect(); e.Handled = true; } };
        host.SizeChanged += (_, _) => { if (needFit) Fit(); };
        Draw();
        ApplyView();

        // zoom tools over the map
        var tools = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 10, 0) };
        foreach (var (t, a, tip) in new (string, Action, string)[] { ("+", () => ZoomCentre(1.25), "Zoom in"), ("−", () => ZoomCentre(0.8), "Zoom out"), ("Fit", Fit, "Fit to screen") })
        {
            var b = Ui.Btn(t, a, null, tip); b.MinWidth = 40; b.Margin = new Thickness(6, 0, 0, 0); b.Padding = new Thickness(10, 4, 10, 4); tools.Children.Add(b);
        }
        var mapArea = new Grid();
        mapArea.Children.Add(host); mapArea.Children.Add(tools); mapArea.Children.Add(cnBar);

        var legend = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        void Leg(UIElement sample, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 18, 0) };
            sp.Children.Add(sample); var tb = Ui.Text(text, 13, FontWeights.SemiBold, "Muted"); tb.Margin = new Thickness(6, 0, 0, 0); tb.VerticalAlignment = VerticalAlignment.Center; sp.Children.Add(tb);
            legend.Children.Add(sp);
        }
        Shapes.Line LegLine(string brush, double th, DoubleCollection dash) { var l = new Shapes.Line { X1 = 0, Y1 = 6, X2 = 34, Y2 = 6, StrokeThickness = th, StrokeDashArray = dash, VerticalAlignment = VerticalAlignment.Center }; l.SetResourceReference(Shapes.Shape.StrokeProperty, brush); return l; }
        Leg(LegLine("Ink", 3, null), "Wired");
        Leg(LegLine("Sig", 3, new DoubleCollection { 3, 2 }), "Wireless");
        Leg(LegLine("Muted", 1.6, new DoubleCollection { 1, 2.5 }), "IP from bridge");
        foreach (var (t, k) in new[] { ("Router", "router"), ("Wireless", "wireless"), ("Router + Wi-Fi", "both"), ("PC / IP phone", "pc"), ("Servers", "server"), ("Cameras / NVR", "camera") })
            Leg(new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(2), Background = TypeBrush(k) }, t);
        var hint = Ui.Muted("Drag the background to move around · mouse wheel or +/− to zoom · drag a device, or a site by its title bar, to arrange it (saved automatically) · click a device or a line to open it · to connect two devices click “Add connection”, then the first device, then the second.", 12.5);
        hint.Margin = new Thickness(0, 6, 0, 0);
        var bottom = new StackPanel(); bottom.Children.Add(legend); bottom.Children.Add(hint);

        var addDev = AddBtn("Add device", () => DeviceDialog.Edit(null, null));
        var addLink = Ui.IconBtn("", "Add connection", StartConnect); addLink.IsEnabled = S.CanWrite && S.Db.Devices.Count >= 2;
        var reset = Ui.Btn("Reset layout", () =>
        {
            if (!Ui.Ask("Sites and devices in this view go back to the automatic arrangement.", "Reset layout?", "Reset")) return;
            S.ResetLayout(W.SiteFilter == "" ? L.ShownSites.Select(s => s.Id) : null, L.ShownDevs.Select(d => d.Id));
            needFit = true; W.Navigate(this);
        });
        reset.IsEnabled = S.CanWrite;

        var top = new StackPanel();
        top.Children.Add(chips); top.Children.Add(ro);
        var page = new DockPanel { Margin = new Thickness(28, 22, 28, 22) };
        var header = Header("Site map", "All sites and devices, with their cables and wireless links.", reset, addLink, addDev);
        DockPanel.SetDock(header, Dock.Top); page.Children.Add(header);
        DockPanel.SetDock(top, Dock.Top); page.Children.Add(top);
        DockPanel.SetDock(bottom, Dock.Bottom); page.Children.Add(bottom);
        page.Children.Add(mapArea);
        if (connecting) ShowConnectBar();
        return page;
    }

    // ------------------------------------------------------------------ layout (same rules as the web version)
    MapLayout Compute()
    {
        var l = new MapLayout();
        var single = Filter != "";
        var sorted = S.Db.Sites.OrderBy(s => s.SiteNumber?.PadLeft(10, '0'), StringComparer.OrdinalIgnoreCase).ToList();
        l.ShownSites = single ? sorted.Where(s => s.Id == Filter).ToList() : sorted;
        var siteIds = l.ShownSites.Select(s => s.Id).ToHashSet();
        l.ShownDevs = S.Db.Devices.Where(d => siteIds.Contains(d.SiteId)).ToList();
        var devIds = l.ShownDevs.Select(d => d.Id).ToHashSet();
        l.ShownLinks = S.Db.Links.Where(k => devIds.Contains(k.A) || devIds.Contains(k.B)).ToList();

        double x = 20, y = 20, rowH = 0, maxX = 0, maxY = 0;
        Point PlaceBox(double w, double h)
        {
            if (x > 20 && x + w > MAXW) { x = 20; y += rowH + GAP; rowH = 0; }
            var at = new Point(x, y); x += w + GAP; rowH = Math.Max(rowH, h); return at;
        }
        var siteIdx = l.ShownSites.Select((s, i) => (s.Id, i)).ToDictionary(t => t.Id, t => t.i);
        double DirScore(Device d)
        {
            var own = siteIdx.GetValueOrDefault(d.SiteId); var v = new List<int>();
            foreach (var k in S.Db.Links.Where(k => k.A == d.Id || k.B == d.Id))
            {
                var o = S.DevById(k.A == d.Id ? k.B : k.A); if (o == null || o.SiteId == d.SiteId) continue;
                v.Add(Math.Sign((siteIdx.TryGetValue(o.SiteId, out var oi) ? oi : l.ShownSites.Count) - own));
            }
            return v.Count > 0 ? v.Average() : 0;
        }

        foreach (var s in l.ShownSites)
        {
            var devs = l.ShownDevs.Where(d => d.SiteId == s.Id).OrderBy(TierOf).ThenBy(DirScore).ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
            var tiers = new[] { 0, 1, 2 }.Select(t => devs.Where(d => TierOf(d) == t).ToList()).Where(g => g.Count > 0).ToList();
            int cols = Math.Min(4, Math.Max(1, tiers.Count == 0 ? 1 : tiers.Max(g => g.Count)));
            bool auto = !devs.Any(d => Store.MapX(d) != null);
            double TOP = HDR + PAD + (auto && devs.Count > 0 ? 16 : 0);
            var rel = new Dictionary<string, (double rx, double ry)>();
            int row = 0; var tierRows = new List<(int t, double y, int n)>();
            foreach (var g in tiers)
            {
                tierRows.Add((TierOf(g[0]), TOP + row * CH, g.Count));
                for (int i = 0; i < g.Count; i++)
                {
                    var d = g[i];
                    int rowCount = Math.Min(cols, g.Count - i / cols * cols); double off = (cols - rowCount) * CW / 2;
                    double rx = Store.MapX(d) ?? PAD + off + i % cols * CW + NW / 2;
                    double ry = Store.MapY(d) ?? TOP + (row + i / cols) * CH + NH / 2;
                    if (drag is { kind: "dev" } dr && dr.id == d.Id) { rx += dr.dx; ry += dr.dy; }
                    rel[d.Id] = (Math.Max(PAD + NW / 2, rx), Math.Max(HDR + PAD + NH / 2, ry));
                }
                row += (g.Count + cols - 1) / cols;
            }
            double w = 210, h = HDR + PAD + 36 + PAD;
            foreach (var r in rel.Values) { w = Math.Max(w, r.rx + NW / 2 + PAD); h = Math.Max(h, r.ry + NH / 2 + PAD + (devs.Count > 2 ? 26 : 0)); }
            var at = single ? new Point(20, 20) : Store.MapX(s) is double sx && Store.MapY(s) is double sy ? new Point(sx, sy) : PlaceBox(w, h);
            if (drag is { kind: "site" } ds && ds.id == s.Id) at = new Point(at.X + ds.dx, at.Y + ds.dy);
            at = new Point(Math.Max(0, at.X), Math.Max(0, at.Y));
            l.Sites[s.Id] = new Rect(at.X, at.Y, w, h);
            maxX = Math.Max(maxX, at.X + w); maxY = Math.Max(maxY, at.Y + h);
            siteTiers[s.Id] = (auto, tierRows, devs.Count);
            foreach (var d in devs) { var r = rel[d.Id]; l.Pos[d.Id] = new P(at.X + r.rx, at.Y + r.ry, d, false); l.Devs[d.Id] = (r.rx, r.ry, s.Id); }
        }

        // one site shown: the other ends of its links, in dashed boxes below
        ghostBoxes.Clear();
        if (single)
        {
            var remote = new Dictionary<string, (Dictionary<string, Device> devs, List<double> xs)>();
            foreach (var k in l.ShownLinks)
                foreach (var (id, other) in new[] { (k.A, k.B), (k.B, k.A) })
                {
                    if (l.Pos.ContainsKey(id) || S.DevById(id) is not Device d) continue;
                    if (!remote.ContainsKey(d.SiteId)) remote[d.SiteId] = (new(), new());
                    remote[d.SiteId].devs[d.Id] = d;
                    if (l.Pos.TryGetValue(other, out var lp) && !lp.Ghost) remote[d.SiteId].xs.Add(lp.X);
                }
            double gx = 20, gy = maxY + GAP, gH = 0;
            foreach (var (sid, g) in remote.Select(kv => (kv.Key, kv.Value)).OrderBy(t => t.Value.xs.Count > 0 ? t.Value.xs.Average() : 0))
            {
                var devs = g.devs.Values.ToList(); int n = devs.Count, cols = Math.Min(3, n);
                double w = Math.Max(170, cols * CW - (CW - NW) + PAD * 2), h = HDR + PAD + (n + cols - 1) / cols * CH - (CH - NH) + PAD;
                double mx = g.xs.Count > 0 ? g.xs.Average() : 0;
                var at = new Point(Math.Max(gx, Math.Round(mx - w / 2)), gy);
                ghostBoxes.Add((sid, new Rect(at.X, at.Y, w, h)));
                for (int i = 0; i < n; i++) l.Pos[devs[i].Id] = new P(at.X + PAD + i % cols * CW + NW / 2, at.Y + HDR + PAD + i / cols * CH + NH / 2, devs[i], true);
                gx = at.X + w + 40; gH = Math.Max(gH, h); maxX = Math.Max(maxX, at.X + w);
            }
            if (remote.Count > 0) maxY = Math.Max(maxY, gy + gH);
        }
        l.W = Math.Max(maxX + 20, 340); l.H = maxY + 20;
        return l;
    }
    readonly Dictionary<string, (bool auto, List<(int t, double y, int n)> tiers, int count)> siteTiers = new();
    readonly List<(string siteId, Rect r)> ghostBoxes = new();

    // ------------------------------------------------------------------ drawing
    static T At<T>(T e, double x, double y) where T : UIElement { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); return e; }
    void Add(UIElement e) => canvas.Children.Add(e);
    static TextBlock T(string text, double size, string brush, FontWeight? w = null, bool mono = false)
    {
        var t = new TextBlock { Text = text, FontSize = size, FontWeight = w ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        if (mono) t.FontFamily = new FontFamily(Ui.Mono);
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        return t;
    }

    void Draw()
    {
        canvas.Children.Clear();
        if (L.ShownSites.Count == 0 || (L.ShownDevs.Count == 0 && L.ShownLinks.Count == 0))
        {
            var msg = L.ShownSites.Count == 0 ? "No sites yet. Create a site on the Sites page first." : $"No devices {(Filter == "" ? "yet" : "in this site")}. Click “Add device”.";
            Add(At(T(msg, 16, "Muted", FontWeights.SemiBold), 30, 30));
            if (L.ShownSites.Count == 0) return;
        }
        // grid
        if (!print) AddGrid();
        void AddGrid()
        {
        var grid = new DrawingBrush
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 24, 24), ViewportUnits = BrushMappingMode.Absolute,
            Drawing = new GeometryDrawing(null, new Pen(Theme.B("Grid"), 1), Geometry.Parse("M24,0 H0 V24"))
        };
        Add(At(new Shapes.Rectangle { Width = L.W * 3 + 2000, Height = L.H * 3 + 2000, Fill = grid, IsHitTestVisible = false }, -L.W - 1000, -L.H - 1000));
        }

        // site boxes
        foreach (var s in L.ShownSites)
        {
            var r = L.Sites[s.Id];
            Add(At(new Shapes.Rectangle { Width = r.Width, Height = r.Height, RadiusX = 4, RadiusY = 4, StrokeThickness = 1.5, Fill = Theme.B("Panel2"), Stroke = Theme.B("Frame") }, r.X, r.Y));
            var hdr = new Border { Width = r.Width, Height = HDR, CornerRadius = new CornerRadius(4, 4, 0, 0), Tag = "site:" + s.Id, Cursor = Filter == "" && S.CanWrite ? Cursors.SizeAll : Cursors.Hand };
            hdr.SetResourceReference(Border.BackgroundProperty, "Nav");
            var hp = new DockPanel { Margin = new Thickness(12, 0, 12, 0) };
            if (Filter == "" && S.CanWrite) { var g = T("≡", 16, "NavMute"); g.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(g, Dock.Right); hp.Children.Add(g); }
            var n = T($"#{s.SiteNumber}", 14, "NavHi", FontWeights.Bold, true); n.VerticalAlignment = VerticalAlignment.Center; n.Margin = new Thickness(0, 0, 8, 0); hp.Children.Add(n);
            var nm = T(s.Name, 14, "NavInk", FontWeights.Bold); nm.VerticalAlignment = VerticalAlignment.Center; hp.Children.Add(nm);
            hdr.Child = hp;
            Add(At(hdr, r.X, r.Y));
            var info = siteTiers[s.Id];
            if (info.count == 0) Add(At(T("No devices", 13, "Muted"), r.X + r.Width / 2 - 34, r.Y + HDR + PAD + 8));
            if (info.auto)
                foreach (var (t, ty0, cnt) in info.tiers)
                {
                    Add(At(T($"{TierLabel[t]} · {cnt}", 10.5, "Muted", FontWeights.Bold), r.X + 12, r.Y + ty0 - 18));
                    Add(new Shapes.Line { X1 = r.X + 12, Y1 = r.Y + ty0 - 2, X2 = r.X + r.Width - 12, Y2 = r.Y + ty0 - 2, Stroke = Theme.B("Line"), StrokeThickness = 1 });
                }
        }
        foreach (var (sid, r) in ghostBoxes)
        {
            var s = S.SiteById(sid);
            var box = new Border { Width = r.Width, Height = r.Height, Tag = "goto:" + sid, Cursor = Cursors.Hand, Background = Brushes.Transparent };
            var rect = new Shapes.Rectangle { Stroke = Theme.B("Muted"), StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 3 }, RadiusX = 4, RadiusY = 4, IsHitTestVisible = false };
            var gg = new Grid(); gg.Children.Add(rect);
            var lbl = T(s == null ? "Other site ›" : $"#{s.SiteNumber} {s.Name} ›", 13, "Muted", FontWeights.Bold); lbl.Margin = new Thickness(10, 8, 10, 0); lbl.VerticalAlignment = VerticalAlignment.Top;
            gg.Children.Add(lbl); box.Child = gg;
            box.ToolTip = "Show this site";
            Add(At(box, r.X, r.Y));
        }

        DrawUplinks();
        DrawLinks();
        foreach (var (id, p) in L.Pos) DrawNode(id, p);
        rubber = null;
        if (connecting && connectA != null && L.Pos.TryGetValue(connectA, out var a))
        {
            rubber = new Shapes.Line { X1 = a.X, Y1 = a.Y, X2 = mouseWorld.X, Y2 = mouseWorld.Y, StrokeThickness = 2.5, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false, Stroke = Theme.B("Acc") };
            canvas.Children.Insert(canvas.Children.Count, rubber);
        }
    }

    void DrawUplinks()
    {
        // other-brand devices that take their IP from a MikroTik bridge: a thin dotted line to the router
        var labelled = new HashSet<string>();
        foreach (var (id, p) in L.Pos)
        {
            var d = p.D; if (p.Ghost || d.IsMikroTik || d.Uplink == null || !L.Pos.TryGetValue(d.Uplink.DeviceId, out var up) || up.Ghost) continue;
            if (L.ShownLinks.Any(k => (k.A == id && k.B == d.Uplink.DeviceId) || (k.B == id && k.A == d.Uplink.DeviceId))) continue;
            Add(new Shapes.Line { X1 = up.X, Y1 = up.Y, X2 = p.X, Y2 = p.Y, StrokeThickness = 1.6, StrokeDashArray = new DoubleCollection { 1, 2.5 }, StrokeDashCap = PenLineCap.Round, Stroke = Theme.B("Muted"), IsHitTestVisible = false });
            var key = d.Uplink.DeviceId + "|" + d.Uplink.Bridge;
            if (labelled.Add(key)) Pill(d.Uplink.Bridge, (up.X + p.X) / 2, (up.Y + p.Y) / 2, "Muted", "Panel", null, 11);
        }
    }

    static string LinkText(Link l)
    {
        string X(string k) => l.Extra != null && l.Extra.TryGetValue(k, out var v) && v.ValueKind is JsonValueKind.String or JsonValueKind.Number ? v.ToString() : "";
        if (l.Type == "wireless")
        {
            var p = new List<string>();
            if (X("freq") != "") p.Add(X("freq")); else if (X("band") != "") p.Add(X("band") + "G");
            if (X("dist") != "") p.Add(X("dist") + "km");
            if (X("signal") != "") p.Add(X("signal"));
            return "((·)) " + (p.Count > 0 ? string.Join(" · ", p) : "Wi-Fi");
        }
        return string.Join(" · ", new[] { X("speed"), X("cable") == "Fiber" ? "fiber" : "" }.Where(t => t != ""));
    }

    void DrawLinks()
    {
        var pairCount = new Dictionary<string, int>();
        var labels = new List<Action>();
        foreach (var l in L.ShownLinks)
        {
            if (!L.Pos.TryGetValue(l.A, out var A) || !L.Pos.TryGetValue(l.B, out var B)) continue;
            var key = string.CompareOrdinal(l.A, l.B) < 0 ? l.A + "|" + l.B : l.B + "|" + l.A;
            int k = pairCount.GetValueOrDefault(key); pairCount[key] = k + 1;
            double mx = (A.X + B.X) / 2, my = (A.Y + B.Y) / 2, dx = B.X - A.X, dy = B.Y - A.Y, len = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
            double off = k == 0 ? 0 : (k % 2 == 1 ? 1 : -1) * Math.Ceiling(k / 2.0) * 34;
            bool sameBox = A.D.SiteId == B.D.SiteId && !A.Ghost && !B.Ghost;
            double bend = off != 0 ? off : sameBox && Math.Abs(dy) < 1 && Math.Abs(dx) > CW + 1 ? 116 : 0;
            if (bend != 0 && off == 0 && dx < 0) bend = -bend;
            double cx = mx - dy / len * bend, cy = my + dx / len * bend;
            Geometry geo = bend != 0
                ? new PathGeometry(new[] { new PathFigure(new Point(A.X, A.Y), new[] { new QuadraticBezierSegment(new Point(cx, cy), new Point(B.X, B.Y), true) }, false) })
                : new LineGeometry(new Point(A.X, A.Y), new Point(B.X, B.Y));
            bool wl = l.Type == "wireless";
            Add(new Shapes.Path { Data = geo, StrokeThickness = 3, Stroke = Theme.B(wl ? "Sig" : "Ink"), StrokeDashArray = wl ? new DoubleCollection { 3, 2 } : null, IsHitTestVisible = false });
            Add(new Shapes.Path { Data = geo, StrokeThickness = 14, Stroke = Brushes.Transparent, Tag = "link:" + l.Id, Cursor = Cursors.Hand, ToolTip = $"{S.LinkLabel(l)} · {(wl ? "wireless" : "wired")}" });

            // interface + IP next to each end, the link label in the middle
            var lx = bend != 0 ? (A.X + 2 * cx + B.X) / 4 : mx; var ly = bend != 0 ? (A.Y + 2 * cy + B.Y) / 4 : my;
            List<string> Lines(Device d, string port, string ip)
            {
                var br = d.Bridges.FirstOrDefault(b => b.Ports.Contains(port ?? ""));
                return new[] { string.IsNullOrEmpty(port) ? "" : br != null ? $"{port} · {br.Name}" : port, br != null ? "" : ip ?? "" }.Where(t => t != "").ToList();
            }
            var ends = new[] { (A, Lines(A.D, l.PortA, l.IpA), bend != 0 ? new Vector(cx - A.X, cy - A.Y) : new Vector(dx, dy)),
                               (B, Lines(B.D, l.PortB, l.IpB), bend != 0 ? new Vector(cx - B.X, cy - B.Y) : new Vector(-dx, -dy)) };
            var lid = l.Id;
            labels.Add(() =>
            {
                var tags = new List<(P P, List<string> lines, Vector u, double exit, double w, double h, double along)>();
                foreach (var (P, lines, v) in ends)
                {
                    if (lines.Count == 0) continue;
                    var u = v; u.Normalize();
                    double exit = Math.Min(Math.Abs(u.X) > 1e-6 ? NW / 2 / Math.Abs(u.X) : 1e9, Math.Abs(u.Y) > 1e-6 ? NH / 2 / Math.Abs(u.Y) : 1e9);
                    double w = lines.Max(s => s.Length) * 6.6 + 12, h = lines.Count * 14 + 6;
                    tags.Add((P, lines, u, exit, w, h, Math.Abs(u.X) * w / 2 + Math.Abs(u.Y) * h / 2 + 5));
                }
                // not enough room on a short line: the two tags go on opposite sides of it
                var free = len - tags.Sum(z => z.exit);
                bool split = tags.Count > 1 && free < tags.Sum(z => 2 * z.along) + 6;
                foreach (var tg in tags)
                {
                    double x0 = tg.P.X + tg.u.X * (tg.exit + tg.along), y0 = tg.P.Y + tg.u.Y * (tg.exit + tg.along);
                    if (split) { var sh = Math.Abs(tg.u.X) * tg.h / 2 + Math.Abs(tg.u.Y) * tg.w / 2 + 5; x0 += -tg.u.Y * sh; y0 += tg.u.X * sh; }
                    Tag(tg.lines, x0, y0, tg.w, tg.h, wl, lid);
                }
                var t = LinkText(l);
                if (t != "") Pill(t, lx, ly, wl ? "Sig" : "Ink2", "Panel", "link:" + lid, 11.5);
            });
        }
        foreach (var a in labels) a();   // labels on top of all lines
    }

    void Tag(List<string> lines, double cx, double cy, double w, double h, bool wireless, string linkId)
    {
        var sp = new StackPanel();
        foreach (var s in lines) { var t = T(s, 11, s.Contains('.') || s.Contains(':') ? "Ink" : "Ink2", FontWeights.SemiBold, true); t.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(t); }
        var b = new Border { Child = sp, Width = w, Height = h, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Tag = "link:" + linkId, Cursor = Cursors.Hand, Padding = new Thickness(2, 2, 2, 2) };
        b.SetResourceReference(Border.BackgroundProperty, "Panel");
        b.SetResourceReference(Border.BorderBrushProperty, wireless ? "Sig" : "Frame");
        Add(At(b, cx - w / 2, cy - h / 2));
    }

    void Pill(string text, double cx, double cy, string fg, string bg, string tag, double size)
    {
        var t = T(text, size, fg, FontWeights.SemiBold);
        var b = new Border { Child = t, CornerRadius = new CornerRadius(9), Padding = new Thickness(7, 1, 7, 1), BorderThickness = new Thickness(1), Tag = tag, Cursor = tag != null ? Cursors.Hand : null, IsHitTestVisible = tag != null };
        b.SetResourceReference(Border.BackgroundProperty, bg);
        b.SetResourceReference(Border.BorderBrushProperty, "Line");
        b.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Add(At(b, cx - b.DesiredSize.Width / 2, cy - b.DesiredSize.Height / 2));
    }

    void DrawNode(string id, P p)
    {
        var d = p.D; var color = p.Ghost ? Theme.B("Muted") : TypeBrush(d.Type);
        var g = new Grid { Width = NW, Height = NH };
        var body = new Border { CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(connectA == id ? 3 : 2), BorderBrush = connectA == id ? Theme.B("Acc") : color, Background = Theme.B("Panel") };
        if (p.Ghost) body.Opacity = 0.75;
        g.Children.Add(body);
        g.Children.Add(new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = color, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 2, 2, 0) });
        var c = new Canvas();
        var icon = new Shapes.Path { Data = Icon(d.Type), Stroke = color, StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round };
        c.Children.Add(At(icon, 8, 8));
        var led = new Shapes.Ellipse { Width = 8, Height = 8, Fill = Theme.Status(d.Status).fg, ToolTip = Store.StatusLabel[Store.StatusOf(d.Status)] };
        c.Children.Add(At(led, NW - 14, 10));
        var nm = T(d.Name, 13.5, "Ink", FontWeights.Bold); nm.Width = NW - 52; c.Children.Add(At(nm, 34, 9));
        var md = T(d.Model, 12, "Muted"); md.Width = NW - 18; c.Children.Add(At(md, 10, 32));
        var ip = DevicesPage.MainIp(d); var third = ip != "" ? ip : Store.RoleLabel.GetValueOrDefault(d.Role ?? "", "");
        if (third == "—") third = "";
        var ipt = T(third, 12, "Ink2", FontWeights.SemiBold, ip != ""); ipt.Width = NW - 18; c.Children.Add(At(ipt, 10, 51));
        g.Children.Add(c);
        var wrap = new Border { Child = g, Tag = "dev:" + id, Cursor = connecting ? Cursors.Hand : p.Ghost || !S.CanWrite ? Cursors.Hand : Cursors.SizeAll, Background = Brushes.Transparent };
        wrap.ToolTip = $"{d.Name} · {Store.TypeLabel.GetValueOrDefault(d.Type, d.Type)} · {d.Model}" + (ip != "" ? "\n" + ip : "") + (p.Ghost ? $"\n{S.SiteById(d.SiteId)}" : "");
        Add(At(wrap, p.X - NW / 2, p.Y - NH / 2));
    }

    /// <summary>The device icons of the web version (20×20, stroked).</summary>
    static Geometry Icon(string type)
    {
        const string wifi = "M2,7 q8,-8 16,0 M5,10 q5,-5 10,0 M10,13 m-1.2,0 a1.2,1.2 0 1,0 2.4,0 a1.2,1.2 0 1,0 -2.4,0";
        const string router = "M2,4 h16 a2,2 0 0 1 2,2 v6 a2,2 0 0 1 -2,2 h-16 a2,2 0 0 1 -2,-2 v-6 a2,2 0 0 1 2,-2 z M5,9 h0.5 M10,9 h0.5 M15,9 h0.5";
        string s = type switch
        {
            "router" => router,
            "pc" => "M2.5,1 h15 a1.5,1.5 0 0 1 1.5,1.5 v9 a1.5,1.5 0 0 1 -1.5,1.5 h-15 a1.5,1.5 0 0 1 -1.5,-1.5 v-9 a1.5,1.5 0 0 1 1.5,-1.5 z M7,18 h6 M10,13 v5",
            "phone" => "M4,1 h12 a2,2 0 0 1 2,2 v13 a2,2 0 0 1 -2,2 h-12 a2,2 0 0 1 -2,-2 v-13 a2,2 0 0 1 2,-2 z M5,5 h10 M6,10 h0.5 M10,10 h0.5 M14,10 h0.5 M6,14 h0.5 M10,14 h0.5 M14,14 h0.5",
            "server" => "M3.5,1 h13 a1.5,1.5 0 0 1 1.5,1.5 v4 a1.5,1.5 0 0 1 -1.5,1.5 h-13 a1.5,1.5 0 0 1 -1.5,-1.5 v-4 a1.5,1.5 0 0 1 1.5,-1.5 z M3.5,10 h13 a1.5,1.5 0 0 1 1.5,1.5 v4 a1.5,1.5 0 0 1 -1.5,1.5 h-13 a1.5,1.5 0 0 1 -1.5,-1.5 v-4 a1.5,1.5 0 0 1 1.5,-1.5 z M5,4.5 h0.5 M5,13.5 h0.5",
            "dbserver" => "M2,4 a8,3 0 1 0 16,0 a8,3 0 1 0 -16,0 M2,4 v11 c0,1.7 3.6,3 8,3 s8,-1.3 8,-3 V4 M2,9.5 c0,1.7 3.6,3 8,3 s8,-1.3 8,-3",
            "nvr" => "M1.5,5 h17 a1.5,1.5 0 0 1 1.5,1.5 v6 a1.5,1.5 0 0 1 -1.5,1.5 h-17 a1.5,1.5 0 0 1 -1.5,-1.5 v-6 a1.5,1.5 0 0 1 1.5,-1.5 z M13,9.5 a2,2 0 1 0 4,0 a2,2 0 1 0 -4,0 M4,9.5 h5",
            "camera" => "M3,4 h9 a2,2 0 0 1 2,2 v5 a2,2 0 0 1 -2,2 h-9 a2,2 0 0 1 -2,-2 v-5 a2,2 0 0 1 2,-2 z M14,7 l5,-3 v9 l-5,-3 M6,13 v5 M3,18 h6",
            "both" => "M2,2 q8,-8 16,0 M5,5 q5,-5 10,0 M2,9 h16 a2,2 0 0 1 2,2 v6 a2,2 0 0 1 -2,2 h-16 a2,2 0 0 1 -2,-2 v-6 a2,2 0 0 1 2,-2 z M5,14 h0.5 M10,14 h0.5 M15,14 h0.5",
            _ => wifi
        };
        return Geometry.Parse(s);
    }

    // ------------------------------------------------------------------ view
    void ApplyView() { scale.ScaleX = scale.ScaleY = zoom; move.X = tx; move.Y = ty; }
    void Fit()
    {
        if (host == null || host.ActualWidth < 10 || L == null) return;
        zoom = Math.Clamp(Math.Min(host.ActualWidth / L.W, host.ActualHeight / L.H) * 0.96, 0.15, 1.5);
        tx = (host.ActualWidth - L.W * zoom) / 2; ty = Math.Max(10, (host.ActualHeight - L.H * zoom) / 2);
        needFit = false; ApplyView();
    }
    void ZoomAt(double f, Point screen)
    {
        var nz = Math.Clamp(zoom * f, 0.15, 3);
        var wx = (screen.X - tx) / zoom; var wy = (screen.Y - ty) / zoom;
        zoom = nz; tx = screen.X - wx * zoom; ty = screen.Y - wy * zoom; ApplyView();
    }
    void ZoomCentre(double f) => ZoomAt(f, new Point(host.ActualWidth / 2, host.ActualHeight / 2));
    Point ToWorld(Point screen) => new((screen.X - tx) / zoom, (screen.Y - ty) / zoom);

    string TagAt(object source)
    {
        for (var d = source as DependencyObject; d != null && d != host; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is FrameworkElement fe && fe.Tag is string t && t.Contains(':')) return t;
        return null;
    }

    // ------------------------------------------------------------------ mouse
    void OnDown(object sender, MouseButtonEventArgs e)
    {
        host.Focus();
        downTag = TagAt(e.OriginalSource); downScreen = e.GetPosition(host); downWorld = ToWorld(downScreen); moved = false;
        panning = downTag == null || downTag.StartsWith("link:") || downTag.StartsWith("goto:");
        panTx = tx; panTy = ty;
        host.CaptureMouse();
        e.Handled = true;
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(host);
        mouseWorld = ToWorld(p);
        if (rubber != null) { rubber.X2 = mouseWorld.X; rubber.Y2 = mouseWorld.Y; }
        if (!host.IsMouseCaptured) return;
        if (!moved && (Math.Abs(p.X - downScreen.X) > 4 || Math.Abs(p.Y - downScreen.Y) > 4)) moved = true;
        if (!moved) return;
        if (panning) { tx = panTx + p.X - downScreen.X; ty = panTy + p.Y - downScreen.Y; ApplyView(); host.Cursor = Cursors.SizeAll; return; }
        if (connecting || !S.CanWrite || downTag == null) return;
        var dx = mouseWorld.X - downWorld.X; var dy = mouseWorld.Y - downWorld.Y;
        if (downTag.StartsWith("dev:") && L.Pos.TryGetValue(downTag[4..], out var dp) && !dp.Ghost) drag = ("dev", downTag[4..], dx, dy);
        else if (downTag.StartsWith("site:") && W.SiteFilter == "") drag = ("site", downTag[5..], dx, dy);
        else return;
        L = Compute(); Draw();
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!host.IsMouseCaptured) return;
        host.ReleaseMouseCapture(); host.Cursor = Cursors.Arrow;
        var tag = downTag; downTag = null;
        if (drag is { } dr)
        {
            drag = null;
            var final = L;   // computed with the drag applied
            try
            {
                if (dr.kind == "dev")
                {
                    var site = final.Devs[dr.id].site;
                    S.SaveLayout(final.Devs.Where(kv => kv.Value.site == site).ToDictionary(kv => kv.Key, kv => (kv.Value.rx, kv.Value.ry)), null);
                }
                else S.SaveLayout(null, final.Sites.ToDictionary(kv => kv.Key, kv => (kv.Value.X, kv.Value.Y)));
            }
            catch (RuleException ex) { Ui.Info(ex.Message); W.Navigate(this); }
            return;
        }
        if (moved) return;
        Click(tag);
    }

    void Click(string tag)
    {
        if (tag == null) return;
        var (kind, id) = (tag[..tag.IndexOf(':')], tag[(tag.IndexOf(':') + 1)..]);
        if (connecting)
        {
            if (kind != "dev") return;
            if (connectA == null) { connectA = id; mouseWorld = new Point(L.Pos[id].X, L.Pos[id].Y); ShowConnectBar(); Draw(); return; }
            if (id == connectA) return;
            var a = S.DevById(connectA); var b = S.DevById(id);
            ExitConnect();
            if (a == null || b == null) return;
            var type = a.SiteId != b.SiteId || (a.Type == "wireless" && b.Type == "wireless") ? "wireless" : "wired";
            W.Page<LinksPage>().Edit(null, new Link { A = a.Id, B = b.Id, Type = type });
            return;
        }
        switch (kind)
        {
            case "dev": if (S.DevById(id) is Device d) DeviceDialog.Edit(d, null); break;
            case "link": if (S.Db.Links.FirstOrDefault(l => l.Id == id) is Link l) W.Page<LinksPage>().Edit(l); break;
            case "goto": case "site" when W.SiteFilter == "" && !S.CanWrite: W.SiteFilter = id; W.Navigate(this); break;
            case "site": if (W.SiteFilter == "") { W.SiteFilter = id; W.Navigate(this); } break;
        }
    }

    // ------------------------------------------------------------------ connect mode
    void StartConnect()
    {
        connecting = true; connectA = null;
        ShowConnectBar(); Draw(); host.Focus();
    }
    void ShowConnectBar()
    {
        if (cnBar == null) return;
        cnBar.Visibility = Visibility.Visible;
        cnMsg.Text = connectA == null ? "Click the first device." : $"{S.DevById(connectA)?.Name} selected — now click the second device.  (Esc to cancel)";
    }
    void ExitConnect()
    {
        connecting = false; connectA = null;
        if (cnMsg != null) cnMsg.Text = "";                 // nothing left behind after cancelling
        if (cnBar != null) cnBar.Visibility = Visibility.Collapsed;
        if (canvas != null && L != null) Draw();
    }
}
