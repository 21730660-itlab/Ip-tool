using System.Numerics;

namespace IpMonitor.App;

/// <summary>Subnet calculator (IPv4 and IPv6): analysis, bit map, check against the registered networks, size for hosts, subnet splitter.</summary>
public class CalcPage : PageBase
{
    public override string Title => "Subnet calculator";
    public override string Glyph => "";

    string input = "192.168.1.10/24";
    int? splitTo;
    TextBox box; Slider slide; TextBlock pfxLabel, err; WrapPanel chips; StackPanel output;
    bool busy;

    public class SubRow
    {
        public int N { get; set; }
        public string Subnet { get; set; }
        public string Range { get; set; }
        public string Usable { get; set; }
        public string InUse { get; set; }
    }

    public override FrameworkElement Build()
    {
        var sp = new StackPanel();
        sp.Children.Add(Header("Subnet calculator", "IPv4 and IPv6 · type an address with /prefix or a mask, e.g. 10.0.0.5 255.255.255.0"));

        var inCard = new StackPanel();
        box = new TextBox { Text = input, FontFamily = new FontFamily(Ui.Mono), FontSize = 22, MinHeight = 48, Padding = new Thickness(12, 6, 12, 6) };
        box.TextChanged += (_, _) => { if (busy) return; input = box.Text; splitTo = null; Calc(); };
        inCard.Children.Add(box);
        var sl = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        pfxLabel = Ui.Text("/24", 18, FontWeights.Bold, "Acc", false, true); pfxLabel.Width = 70; pfxLabel.TextAlignment = TextAlignment.Right;
        DockPanel.SetDock(pfxLabel, Dock.Right); sl.Children.Add(pfxLabel);
        slide = new Slider { Minimum = 0, Maximum = 32, Value = 24, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        slide.ValueChanged += (_, _) => { if (busy) return; SetPrefix((int)slide.Value); };
        sl.Children.Add(slide);
        inCard.Children.Add(sl);
        chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        inCard.Children.Add(chips);
        err = Ui.Error(); err.Margin = new Thickness(0, 8, 0, 0);
        inCard.Children.Add(err);
        sp.Children.Add(Ui.Card(inCard));

        output = new StackPanel();
        sp.Children.Add(output);
        Calc();
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new Border { Padding = new Thickness(28, 22, 28, 22), Child = sp } };
    }

    /// <summary>Puts the prefix into the text box (keeps the address).</summary>
    void SetPrefix(int p)
    {
        var baseIp = input.Trim().Split('/')[0].Split((char[])null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.TrimEnd('.') ?? "";
        input = $"{baseIp}/{p}"; splitTo = null;
        busy = true; box.Text = input; busy = false;
        Calc();
    }

    IpEntry Current()
    {
        var p = IpMath.ParseRaw(input);
        if (p == null) return null;
        busy = true;
        slide.Maximum = p.Bits;
        if (p.Prefix != null) slide.Value = p.Prefix.Value; else if (slide.Value > p.Bits) slide.Value = p.Bits;
        busy = false;
        return IpMath.Analyze(p.V, p.Addr, (int)slide.Value);
    }

    void Calc()
    {
        var e = Current();
        pfxLabel.Text = "/" + (int)slide.Value;
        err.Text = input.Trim() != "" && e == null ? "✕ Not a valid IPv4/IPv6 address, CIDR or mask" : "";
        chips.Children.Clear();
        foreach (var p in e?.V == 6 ? new[] { 128, 64, 56, 48, 32 } : new[] { 32, 30, 29, 28, 27, 26, 25, 24, 22, 20, 16, 8 })
        {
            var b = Ui.Btn("/" + p, () => SetPrefix(p), e != null && e.Prefix == p ? "Primary" : null);
            b.MinHeight = 30; b.Padding = new Thickness(10, 3, 10, 3); b.Margin = new Thickness(0, 0, 6, 6); b.FontFamily = new FontFamily(Ui.Mono);
            chips.Children.Add(b);
        }
        output.Children.Clear();
        if (e == null) return;

        // analysis + bit map side by side
        var rows = e.V == 4 ? new List<(string, string, bool)>
        {
            ("Address", IpMath.Format(4, e.Addr), false), ("Network", e.Text.Contains('/') ? e.Text : $"{e.Network}/{e.Prefix}", false), ("Netmask", e.Mask, false), ("Wildcard", IpMath.Wildcard(e), false),
            ("Broadcast", e.Prefix < 31 ? IpMath.Format(4, e.End) : "n/a (RFC 3021)", false), ("First host", e.FirstU, false), ("Last host", e.LastU, false),
            ("Total addresses", IpMath.CountText(e.Size), false), ("Usable hosts", IpMath.CountText(e.Usable), true),
            ("Class", IpMath.V4Class(e.Addr), false), ("Scope", IpMath.Scope(e), false),
            ("Integer", e.Addr.ToString(), false), ("Hex", "0x" + e.Addr.ToString("X8").TrimStart('0').PadLeft(8, '0'), false), ("Binary", IpMath.Binary(4, e.Addr), false)
        } : new List<(string, string, bool)>
        {
            ("Address", IpMath.Format(6, e.Addr), false), ("Expanded", IpMath.Expanded6(e.Addr), false), ("Network", $"{e.Network}/{e.Prefix}", false),
            ("First", IpMath.Format(6, e.Start), false), ("Last", IpMath.Format(6, e.End), false), ("Total addresses", IpMath.CountText(e.Size), true),
            ("/64 networks inside", e.Prefix <= 64 ? IpMath.CountText(BigInteger.One << (64 - e.Prefix)) : "—", false), ("Scope", IpMath.Scope(e), false)
        };
        var kv = new Grid();
        kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        kv.ColumnDefinitions.Add(new ColumnDefinition());
        for (int i = 0; i < rows.Count; i++)
        {
            kv.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var k = Ui.Text(rows[i].Item1, 13.5, FontWeights.SemiBold, "Muted"); k.Margin = new Thickness(0, 5, 10, 5);
            var v = Ui.Text(rows[i].Item2, rows[i].Item3 ? 20 : 14, rows[i].Item3 ? FontWeights.Bold : FontWeights.SemiBold, rows[i].Item3 ? "Acc" : "Ink", true, true); v.Margin = new Thickness(0, 4, 0, 4);
            Grid.SetRow(k, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            kv.Children.Add(k); kv.Children.Add(v);
        }
        var hb = e.Bits - e.Prefix;
        var formula = Ui.Muted(e.V == 4 ? (e.Prefix < 31 ? $"usable = 2^(32−{e.Prefix}) − 2 = 2^{hb} − 2 = {IpMath.CountText(e.Usable)}" : $"usable = 2^{hb} = {IpMath.CountText(e.Usable)} (point-to-point / host route)") : $"total = 2^(128−{e.Prefix}) = 2^{hb}", 13);
        formula.Margin = new Thickness(0, 10, 0, 0);
        var an = new StackPanel(); an.Children.Add(Ui.Text($"Analysis · IPv{e.V}", 16, FontWeights.Bold)); kv.Margin = new Thickness(0, 8, 0, 0); an.Children.Add(kv); an.Children.Add(formula);

        var bm = new StackPanel(); bm.Children.Add(Ui.Text($"Bit map · {e.Bits}-bit · /{e.Prefix}", 16, FontWeights.Bold));
        bm.Children.Add(BitMap(e));
        var grid2 = Ui.Cols(Ui.Card(an), Ui.Card(bm));
        grid2.Margin = new Thickness(0, 16, 0, 0);
        output.Children.Add(grid2);

        // check against the registered networks
        var chk = new StackPanel(); chk.Children.Add(Ui.Text("Network check · against all sites", 16, FontWeights.Bold));
        var found = S.NetConflicts(e);
        if (found.Count > 0)
        {
            var t = Ui.Text($"✕ Overlaps {found.Count} registered network{(found.Count == 1 ? "" : "s")}:", 14, FontWeights.SemiBold, "Sig"); t.Margin = new Thickness(0, 8, 0, 4); chk.Children.Add(t);
            foreach (var f in found.Take(8)) chk.Children.Add(Ui.Text("• " + f, 13.5, null, "Ink2"));
            if (found.Count > 8) chk.Children.Add(Ui.Muted($"…and {found.Count - 8} more", 13));
        }
        else
        {
            var t = Ui.Text($"✓ {e.Text} is free. No overlap with any site.", 14, FontWeights.SemiBold, "Ok"); t.Margin = new Thickness(0, 8, 0, 8); chk.Children.Add(t);
            if (S.CanWrite && S.Db.Sites.Count > 0) { var add = Ui.IconBtn("", $"Add {e.Text} as a network…", () => AddNetwork(e.Text), "Primary"); add.HorizontalAlignment = HorizontalAlignment.Left; chk.Children.Add(add); }
        }
        // size for hosts
        var need = new StackPanel(); need.Children.Add(Ui.Text("Size for hosts · smallest fitting prefix", 16, FontWeights.Bold));
        var nb = Ui.Box(); nb.Width = 160; nb.HorizontalAlignment = HorizontalAlignment.Left; var nOut = Ui.Text("", 14, FontWeights.SemiBold, "Ink"); nOut.Margin = new Thickness(0, 8, 0, 0);
        var apply = Ui.Btn("", () => { }); apply.Visibility = Visibility.Collapsed; apply.HorizontalAlignment = HorizontalAlignment.Left; apply.Margin = new Thickness(0, 8, 0, 0);
        int? applyP = null;
        apply.Click += (_, _) => { if (applyP is int p) SetPrefix(p); };
        nb.TextChanged += (_, _) =>
        {
            apply.Visibility = Visibility.Collapsed;
            if (!BigInteger.TryParse(nb.Text.Trim(), out var n) || n <= 0) { nOut.Text = ""; return; }
            var p = IpMath.PrefixForHosts(e.V, n);
            if (p == null) { nOut.Text = "Too many hosts for this address family."; return; }
            var a = IpMath.Analyze(e.V, e.Start, p.Value);
            nOut.Text = $"Needs /{p} → {IpMath.CountText(a.Usable)} usable · {IpMath.CountText(a.Usable - n)} spare";
            applyP = p; apply.Content = $"Apply /{p}"; apply.Visibility = Visibility.Visible;
        };
        var nf = Ui.Field("Hosts needed", nb); ((FrameworkElement)nf).Margin = new Thickness(0, 8, 0, 0);
        need.Children.Add(nf); need.Children.Add(nOut); need.Children.Add(apply);
        var grid3 = Ui.Cols(Ui.Card(chk), Ui.Card(need));
        grid3.Margin = new Thickness(0, 16, 0, 0);
        output.Children.Add(grid3);

        // splitter
        if (e.Prefix < e.Bits)
        {
            int maxSplit = Math.Min(e.Bits, e.Prefix + 12);
            if (splitTo == null || splitTo <= e.Prefix || splitTo > maxSplit) splitTo = Math.Min(e.Bits, e.Prefix + 2);
            var sp = new StackPanel(); sp.Children.Add(Ui.Text($"Subnet splitter · {e.Text}", 16, FontWeights.Bold));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
            var lbl = Ui.Text("Divide into", 14, FontWeights.SemiBold, "Ink2"); lbl.VerticalAlignment = VerticalAlignment.Center; lbl.Margin = new Thickness(0, 0, 10, 0);
            var combo = Ui.Choice(Enumerable.Range(e.Prefix + 1, maxSplit - e.Prefix).Select(p => (p.ToString(), "/" + p)), splitTo.ToString()); combo.Width = 110;
            combo.SelectionChanged += (_, _) => { splitTo = int.Parse(combo.Val()); Dispatcher().BeginInvoke(Calc); };
            var count = BigInteger.One << (splitTo.Value - e.Prefix); var step = BigInteger.One << (e.Bits - splitTo.Value);
            var per = IpMath.Analyze(e.V, e.Start, splitTo.Value).Usable;
            var sum = Ui.Text($"{IpMath.CountText(count)} subnets × {IpMath.CountText(per)} usable each", 14, FontWeights.SemiBold, "Ink"); sum.VerticalAlignment = VerticalAlignment.Center; sum.Margin = new Thickness(16, 0, 0, 0);
            row.Children.Add(lbl); row.Children.Add(combo); row.Children.Add(sum);
            sp.Children.Add(row);
            var g = Ui.Table(); g.MaxHeight = 420;
            g.Columns.Add(Ui.Col("#", nameof(SubRow.N), 60));
            g.Columns.Add(Ui.Col("Subnet", nameof(SubRow.Subnet), -1.2, true));
            g.Columns.Add(Ui.Col("Usable range", nameof(SubRow.Range), -2, true));
            g.Columns.Add(Ui.Col("Usable", nameof(SubRow.Usable)));
            g.Columns.Add(Ui.Col("In use", nameof(SubRow.InUse), -2));
            int show = count > 256 ? 256 : (int)count;
            g.ItemsSource = Enumerable.Range(0, show).Select(i =>
            {
                var sub = IpMath.Analyze(e.V, e.Start + i * step, splitTo.Value); var c = S.NetConflicts(sub);
                return new SubRow { N = i + 1, Subnet = sub.Text, Range = $"{sub.FirstU} – {sub.LastU}", Usable = IpMath.CountText(sub.Usable), InUse = c.FirstOrDefault() ?? "" };
            }).ToList();
            var use = Ui.IconBtn("", "Add selected as a network…", () => { if (g.SelectedItem is SubRow r) AddNetwork(r.Subnet); });
            use.IsEnabled = false; use.Margin = new Thickness(0, 10, 0, 0); use.HorizontalAlignment = HorizontalAlignment.Left;
            g.SelectionChanged += (_, _) => use.IsEnabled = S.CanWrite && g.SelectedItem is SubRow r && r.InUse == "";
            Ui.OnRowDoubleClick(g, r => { if (S.CanWrite && ((SubRow)r).InUse == "") AddNetwork(((SubRow)r).Subnet); });
            sp.Children.Add(g);
            if (count > 256) sp.Children.Add(Ui.Muted($"Showing the first 256 of {IpMath.CountText(count)}", 13));
            sp.Children.Add(use);
            var card = Ui.Card(sp); card.Margin = new Thickness(0, 16, 0, 0);
            output.Children.Add(card);
        }
    }

    static System.Windows.Threading.Dispatcher Dispatcher() => Application.Current.Dispatcher;

    /// <summary>Network bits blue, host bits plain, one row per octet (IPv4) or per 16 bits (IPv6).</summary>
    static FrameworkElement BitMap(IpEntry e)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        int gs = e.V == 4 ? 8 : 16;
        double cell = e.V == 4 ? 30 : 19;
        for (int g = 0; g < e.Bits / gs; g++)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            int val = 0;
            var cells = new List<UIElement>();
            for (int i = 0; i < gs; i++)
            {
                int bit = g * gs + i;
                int b = (int)((e.Addr >> (e.Bits - 1 - bit)) & 1);
                val = val * 2 + b;
                bool net = bit < e.Prefix;
                var c = new Border { Width = cell, Height = cell, Margin = new Thickness(0, 0, i % 4 == 3 ? 5 : 2, 0), CornerRadius = new CornerRadius(3),
                    Background = Theme.B(net ? "Acc" : b == 1 ? "AccSoft" : "Sunk"),
                    Child = new TextBlock { Text = b.ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily(Ui.Mono), FontSize = e.V == 4 ? 13 : 10.5, FontWeight = FontWeights.Bold, Foreground = Theme.B(net ? "AccInk" : "Ink") } };
                cells.Add(c);
            }
            var lab = Ui.Text(e.V == 4 ? val.ToString() : val.ToString("x4"), 14, FontWeights.Bold, "Ink2", false, true); lab.Width = e.V == 4 ? 44 : 48; lab.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(lab);
            foreach (var c in cells) row.Children.Add(c);
            sp.Children.Add(row);
        }
        var leg = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        leg.Children.Add(Ui.Badge($"Network bits · {e.Prefix}", Theme.B("AccInk"), Theme.B("Acc")));
        var h = Ui.Badge($"Host bits · {e.Bits - e.Prefix}", Theme.B("Ink"), Theme.B("Sunk")); h.Margin = new Thickness(8, 0, 0, 0); leg.Children.Add(h);
        sp.Children.Add(leg);
        return sp;
    }

    void AddNetwork(string subnet) => W.Page<NetworksPage>().Edit(null, subnet);
}
