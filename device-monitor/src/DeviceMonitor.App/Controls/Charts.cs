using System.Globalization;

namespace DeviceMonitor.App.Controls;

/// <summary>Base for the drawn charts: theme brushes, text helper, redraw when the theme changes.</summary>
public abstract class ChartBase : FrameworkElement
{
    protected ChartBase()
    {
        SnapsToDevicePixels = true;
        Loaded += (_, _) => Theme.Changed += InvalidateVisual;
        Unloaded += (_, _) => Theme.Changed -= InvalidateVisual;
    }

    protected static Brush B(string key) => Theme.B(key);
    protected static Pen P(string key, double w = 1, bool dashed = false)
    {
        var p = new Pen(B(key), w);
        if (dashed) p.DashStyle = new DashStyle(new double[] { 3, 3 }, 0);
        p.Freeze();
        return p;
    }
    protected FormattedText Txt(string s, double size, string brush = "Muted", bool bold = false) =>
        new(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, B(brush), VisualTreeHelper.GetDpi(this).PixelsPerDip);

    protected override Size MeasureOverride(Size available) =>
        new(double.IsInfinity(available.Width) ? 200 : available.Width, double.IsNaN(Height) ? (double.IsInfinity(available.Height) ? 120 : available.Height) : Height);

    public static SolidColorBrush Soft(Color c, byte alpha) { var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B)); b.Freeze(); return b; }
    protected static Color C(string key) => ((SolidColorBrush)B(key)).Color;
}

/// <summary>A small line of the recent reply times (device cards). Missed pings show as red marks at the bottom.</summary>
public class Sparkline : ChartBase
{
    Sample[] data = Array.Empty<Sample>();
    public string LineKey { get; set; } = "Acc";

    public void SetData(Sample[] samples) { data = samples ?? Array.Empty<Sample>(); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (data.Length < 2 || w < 4) return;
        var max = Math.Max(5, data.Where(s => s.Ms.HasValue).Select(s => (double)s.Ms.Value).DefaultIfEmpty(5).Max() * 1.15);
        double step = w / (data.Length - 1);
        var line = new StreamGeometry();
        var area = new StreamGeometry();
        using (var g = line.Open())
        using (var a = area.Open())
        {
            bool open = false; Point first = default, last = default;
            for (int i = 0; i < data.Length; i++)
            {
                var s = data[i];
                if (!s.Ms.HasValue)
                {
                    if (open) { a.LineTo(new Point(last.X, h), false, false); a.LineTo(new Point(first.X, h), false, false); }
                    open = false; continue;
                }
                var p = new Point(i * step, h - 2 - (h - 4) * s.Ms.Value / max);
                if (!open) { g.BeginFigure(p, false, false); a.BeginFigure(p, true, true); first = p; open = true; }
                else { g.LineTo(p, true, true); a.LineTo(p, false, true); }
                last = p;
            }
            if (open) { a.LineTo(new Point(last.X, h), false, false); a.LineTo(new Point(first.X, h), false, false); }
        }
        line.Freeze(); area.Freeze();
        var c = C(LineKey);
        var fill = new LinearGradientBrush(Color.FromArgb(90, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B), 90); fill.Freeze();
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, P(LineKey, 1.6), line);
        var sig = B("Sig");
        for (int i = 0; i < data.Length; i++)
            if (!data[i].Ms.HasValue) dc.DrawRectangle(sig, null, new Rect(Math.Max(0, i * step - 1), h - 4, 2.5, 4));
    }
}

/// <summary>
/// The big reply-time chart: time axis, ms axis with grid lines, filled line, red bands where the device did not answer,
/// a dashed "slow" line, and a tooltip-like read-out under the mouse.
/// </summary>
public class LatencyChart : ChartBase
{
    Sample[] data = Array.Empty<Sample>();
    DateTime from, to;
    Point? mouse;
    public double SlowMs { get; set; } = 150;

    public LatencyChart()
    {
        MouseMove += (_, e) => { mouse = e.GetPosition(this); InvalidateVisual(); };
        MouseLeave += (_, _) => { mouse = null; InvalidateVisual(); };
    }

    public void SetData(Sample[] samples, DateTime start, DateTime end)
    {
        data = samples ?? Array.Empty<Sample>(); from = start; to = end <= start ? start.AddMinutes(1) : end;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        const double left = 52, right = 12, top = 12, bottom = 28;
        var plot = new Rect(left, top, Math.Max(10, w - left - right), Math.Max(10, h - top - bottom));

        var values = data.Where(s => s.Ms.HasValue).Select(s => (double)s.Ms.Value).ToList();
        var maxV = NiceMax(Math.Max(values.DefaultIfEmpty(0).Max() * 1.15, 10));
        double X(DateTime t) => plot.Left + plot.Width * (t - from).TotalSeconds / Math.Max(1, (to - from).TotalSeconds);
        double Y(double v) => plot.Bottom - plot.Height * Math.Min(v, maxV) / maxV;

        // grid + ms axis
        var grid = P("Grid", 1);
        for (int i = 0; i <= 4; i++)
        {
            var v = maxV * i / 4; var y = Y(v);
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Txt($"{v:0} ms", 11.5);
            dc.DrawText(t, new Point(plot.Left - t.Width - 8, y - t.Height / 2));
        }
        // time axis
        var span = to - from;
        var tick = span.TotalMinutes <= 20 ? TimeSpan.FromMinutes(2) : span.TotalMinutes <= 70 ? TimeSpan.FromMinutes(10) : span.TotalHours <= 7 ? TimeSpan.FromHours(1) : TimeSpan.FromHours(4);
        var first = new DateTime(from.Ticks / tick.Ticks * tick.Ticks).Add(tick);
        for (var t = first; t < to; t = t.Add(tick))
        {
            var x = X(t);
            dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
            var lbl = Txt(t.ToString("HH:mm"), 11.5);
            dc.DrawText(lbl, new Point(x - lbl.Width / 2, plot.Bottom + 6));
        }
        dc.DrawLine(P("Frame"), new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        if (data.Length == 0)
        {
            var t = Txt("Waiting for the first ping…", 14);
            dc.DrawText(t, new Point(plot.Left + (plot.Width - t.Width) / 2, plot.Top + (plot.Height - t.Height) / 2));
            return;
        }

        // red bands: no reply
        var sigSoft = Soft(C("Sig"), 60);
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i].Ms.HasValue) continue;
            int j = i; while (j + 1 < data.Length && !data[j + 1].Ms.HasValue) j++;
            var x1 = X(data[i].At); var x2 = j + 1 < data.Length ? X(data[j + 1].At) : Math.Max(x1 + 3, X(data[j].At));
            dc.DrawRectangle(sigSoft, null, new Rect(Math.Max(plot.Left, x1 - 1), plot.Top, Math.Max(3, x2 - x1), plot.Height));
            i = j;
        }

        // slow line
        if (SlowMs < maxV)
        {
            var y = Y(SlowMs);
            dc.DrawLine(P("Warn", 1, true), new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Txt("slow", 11, "Warn");
            dc.DrawText(t, new Point(plot.Right - t.Width - 2, y - t.Height - 1));
        }

        // line + area (broken where a ping was missed)
        var line = new StreamGeometry(); var area = new StreamGeometry();
        using (var g = line.Open())
        using (var a = area.Open())
        {
            bool open = false; Point f0 = default, last = default;
            foreach (var s in data)
            {
                if (s.At < from) continue;
                if (!s.Ms.HasValue)
                {
                    if (open) { a.LineTo(new Point(last.X, plot.Bottom), false, false); a.LineTo(new Point(f0.X, plot.Bottom), false, false); }
                    open = false; continue;
                }
                var p = new Point(X(s.At), Y(s.Ms.Value));
                if (!open) { g.BeginFigure(p, false, false); a.BeginFigure(p, true, true); f0 = p; open = true; }
                else { g.LineTo(p, true, true); a.LineTo(p, false, true); }
                last = p;
            }
            if (open) { a.LineTo(new Point(last.X, plot.Bottom), false, false); a.LineTo(new Point(f0.X, plot.Bottom), false, false); }
        }
        line.Freeze(); area.Freeze();
        var c = C("Acc");
        var fill = new LinearGradientBrush(Color.FromArgb(110, c.R, c.G, c.B), Color.FromArgb(5, c.R, c.G, c.B), 90); fill.Freeze();
        dc.PushClip(new RectangleGeometry(plot));
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, P("Acc", 2), line);
        dc.Pop();

        // read-out under the mouse
        if (mouse is Point m && plot.Contains(m))
        {
            var at = from + TimeSpan.FromSeconds((m.X - plot.Left) / plot.Width * (to - from).TotalSeconds);
            var near = data.Where(s => s.At >= from).OrderBy(s => Math.Abs((s.At - at).Ticks)).FirstOrDefault();
            if (near.At != default)
            {
                var x = X(near.At);
                dc.DrawLine(P("Muted", 1, true), new Point(x, plot.Top), new Point(x, plot.Bottom));
                var label = $"{near.At:HH:mm:ss}   " + (near.Ms.HasValue ? $"{near.Ms} ms" : "no reply");
                var t = Txt(label, 12.5, "Ink", true);
                var bx = Math.Min(Math.Max(plot.Left, x + 8), plot.Right - t.Width - 16);
                var box = new Rect(bx, plot.Top + 4, t.Width + 16, t.Height + 10);
                dc.DrawRoundedRectangle(B("Panel"), P("Frame"), box, 5, 5);
                dc.DrawText(t, new Point(box.Left + 8, box.Top + 5));
                if (near.Ms.HasValue) dc.DrawEllipse(B("Acc"), new Pen(B("Panel"), 2), new Point(x, Y(near.Ms.Value)), 4.5, 4.5);
            }
        }
    }

    static double NiceMax(double v)
    {
        var mag = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var m in new[] { 1, 2, 2.5, 5, 10 }) if (m * mag >= v) return m * mag;
        return 10 * mag;
    }
}

/// <summary>A ring chart with a big number in the middle (ON / OFF / paused share).</summary>
public class DonutChart : ChartBase
{
    (double value, string key)[] parts = Array.Empty<(double, string)>();
    string center = "", caption = "";

    public void SetData(IEnumerable<(double value, string key)> values, string centerText, string captionText)
    {
        parts = values.ToArray(); center = centerText; caption = captionText;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var r = Math.Min(w, h) / 2 - 4; if (r < 10) return;
        var c = new Point(w / 2, h / 2);
        double thick = Math.Max(10, r * 0.22);
        dc.DrawEllipse(null, new Pen(B("Sunk"), thick), c, r - thick / 2, r - thick / 2);
        var total = parts.Sum(p => p.value);
        if (total > 0)
        {
            double a = -90;
            foreach (var (v, key) in parts)
            {
                if (v <= 0) continue;
                var sweep = 360 * v / total;
                DrawArc(dc, c, r - thick / 2, a, Math.Min(sweep, 359.99), new Pen(B(key), thick) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat });
                a += sweep;
            }
        }
        var t = Txt(center, Math.Max(14, r * 0.38), "Ink", true);
        var cap = Txt(caption, Math.Max(10, r * 0.13));
        dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2 - cap.Height / 2));
        dc.DrawText(cap, new Point(c.X - cap.Width / 2, c.Y + t.Height / 2 - cap.Height / 2));
    }

    static void DrawArc(DrawingContext dc, Point c, double r, double startDeg, double sweepDeg, Pen pen)
    {
        Point At(double deg) { var rad = deg * Math.PI / 180; return new Point(c.X + r * Math.Cos(rad), c.Y + r * Math.Sin(rad)); }
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(startDeg), false, false);
            ctx.ArcTo(At(startDeg + sweepDeg), new Size(r, r), 0, sweepDeg > 180, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>Horizontal bars with a label and a value (slowest devices, most outages).</summary>
public class BarChart : ChartBase
{
    (string label, double value, string text, string key)[] rows = Array.Empty<(string, double, string, string)>();
    public string Empty { get; set; } = "Nothing to show yet";

    public void SetData(IEnumerable<(string label, double value, string text, string key)> data) { rows = data.ToArray(); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (rows.Length == 0)
        {
            var e = Txt(Empty, 13.5);
            dc.DrawText(e, new Point((w - e.Width) / 2, (h - e.Height) / 2));
            return;
        }
        double rowH = Math.Min(34, h / rows.Length);
        double labelW = Math.Min(w * 0.38, rows.Max(r => Txt(r.label, 13).Width) + 10);
        double valueW = rows.Max(r => Txt(r.text, 13, "Ink", true).Width) + 10;
        if (w < 60 || h < 10) return;
        double barW = Math.Max(10, w - labelW - valueW);
        var max = Math.Max(1e-9, rows.Max(r => r.value));
        for (int i = 0; i < rows.Length; i++)
        {
            var (label, value, text, key) = rows[i];
            double y = i * rowH;
            var lt = Txt(label, 13, "Ink2"); lt.MaxTextWidth = Math.Max(10, labelW - 8); lt.MaxLineCount = 1; lt.Trimming = TextTrimming.CharacterEllipsis;
            dc.DrawText(lt, new Point(0, y + (rowH - lt.Height) / 2));
            var bh = Math.Max(6, rowH * 0.5);
            dc.DrawRoundedRectangle(B("Sunk"), null, new Rect(labelW, y + (rowH - bh) / 2, barW, bh), bh / 2, bh / 2);
            dc.DrawRoundedRectangle(B(key), null, new Rect(labelW, y + (rowH - bh) / 2, Math.Max(bh, barW * value / max), bh), bh / 2, bh / 2);
            var vt = Txt(text, 13, "Ink", true);
            dc.DrawText(vt, new Point(labelW + barW + 8, y + (rowH - vt.Height) / 2));
        }
    }
}

/// <summary>Columns per time slot (events per hour), stacked OFF (red) over ON (green).</summary>
public class ColumnChart : ChartBase
{
    (string label, int a, int b)[] cols = Array.Empty<(string, int, int)>();
    public string KeyA { get; set; } = "Sig";
    public string KeyB { get; set; } = "Ok";

    public void SetData(IEnumerable<(string label, int a, int b)> data) { cols = data.ToArray(); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (cols.Length == 0) return;
        const double bottom = 22, top = 6, left = 28;
        var plot = new Rect(left, top, w - left - 4, h - top - bottom);
        var max = Math.Max(1, cols.Max(c => c.a + c.b));
        var grid = P("Grid");
        foreach (var v in new[] { 0, max / 2.0, max })
        {
            var y = plot.Bottom - plot.Height * v / max;
            dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
            var t = Txt(v.ToString("0"), 11);
            dc.DrawText(t, new Point(plot.Left - t.Width - 6, y - t.Height / 2));
        }
        double cw = plot.Width / cols.Length;
        for (int i = 0; i < cols.Length; i++)
        {
            var (label, a, b) = cols[i];
            double x = plot.Left + i * cw + cw * 0.18, bw = cw * 0.64;
            double hb = plot.Height * b / max, ha = plot.Height * a / max;
            if (hb > 0) dc.DrawRectangle(B(KeyB), null, new Rect(x, plot.Bottom - hb, bw, hb));
            if (ha > 0) dc.DrawRectangle(B(KeyA), null, new Rect(x, plot.Bottom - hb - ha, bw, ha));
            if (i % Math.Max(1, (int)Math.Ceiling(cols.Length / (plot.Width / 38))) == 0)
            {
                var t = Txt(label, 11);
                dc.DrawText(t, new Point(x + bw / 2 - t.Width / 2, plot.Bottom + 4));
            }
        }
    }
}

/// <summary>A bar from left (oldest) to right (now), green while the device answered, red while it did not.</summary>
public class StatusStrip : ChartBase
{
    Sample[] data = Array.Empty<Sample>();
    DateTime from, to;

    public void SetData(Sample[] samples, DateTime start, DateTime end) { data = samples ?? Array.Empty<Sample>(); from = start; to = end; InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        var all = new Rect(0, 0, w, h);
        dc.PushClip(new RectangleGeometry(all, 4, 4));
        dc.DrawRectangle(B("Sunk"), null, all);
        if (data.Length > 0 && to > from)
        {
            double X(DateTime t) => w * Math.Clamp((t - from).TotalSeconds / (to - from).TotalSeconds, 0, 1);
            for (int i = 0; i < data.Length; i++)
            {
                var x1 = X(data[i].At);
                var x2 = i + 1 < data.Length ? X(data[i + 1].At) : X(to);
                if (x2 - x1 < 0.5) x2 = x1 + 0.5;
                dc.DrawRectangle(B(data[i].Ms.HasValue ? "Ok" : "Sig"), null, new Rect(x1, 0, x2 - x1 + 0.3, h));
            }
        }
        dc.Pop();
    }
}
