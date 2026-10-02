using System.Globalization;
using System.Text;

namespace IpMonitor.Core;

/// <summary>
/// A small PDF writer (no extra libraries): pages, text in the standard fonts, rectangles, lines and JPEG pictures.
/// Coordinates are in points from the top-left corner of the page.
/// </summary>
public class Pdf
{
    public enum Font { Regular, Bold, Mono }
    public double PageW { get; }
    public double PageH { get; }
    readonly List<StringBuilder> pages = new();
    readonly List<(byte[] jpeg, int w, int h)> images = new();
    StringBuilder cur;
    public int PageCount => pages.Count;
    public int PageIndex => pages.Count - 1;
    public string Title = "IP Monitor";

    public Pdf(double w = 842, double h = 595) { PageW = w; PageH = h; }   // A4 landscape

    public void NewPage() { cur = new StringBuilder(); pages.Add(cur); }
    /// <summary>Draw on an earlier page (for "page n of m" footers).</summary>
    public void OnPage(int i) => cur = pages[i];

    static string N(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
    static string Rgb(string hex)
    {
        hex = hex.TrimStart('#');
        double C(int i) => Convert.ToInt32(hex.Substring(i, 2), 16) / 255.0;
        return $"{N(C(0))} {N(C(2))} {N(C(4))}";
    }

    public void Rect(double x, double y, double w, double h, string fill, string stroke = null, double lw = 0.6)
    {
        if (fill != null) cur.Append($"{Rgb(fill)} rg ");
        if (stroke != null) cur.Append($"{Rgb(stroke)} RG {N(lw)} w ");
        cur.Append($"{N(x)} {N(PageH - y - h)} {N(w)} {N(h)} re {(fill != null && stroke != null ? "B" : fill != null ? "f" : "S")}\n");
    }

    public void Line(double x1, double y1, double x2, double y2, string color, double lw = 0.6)
        => cur.Append($"{Rgb(color)} RG {N(lw)} w {N(x1)} {N(PageH - y1)} m {N(x2)} {N(PageH - y2)} l S\n");

    /// <summary>Text with its baseline at y.</summary>
    public void Text(double x, double y, string text, double size, Font f = Font.Regular, string color = "#1B2533")
    {
        if (string.IsNullOrEmpty(text)) return;
        cur.Append($"BT /F{(int)f + 1} {N(size)} Tf {Rgb(color)} rg {N(x)} {N(PageH - y)} Td (");
        foreach (var b in Encode(text))
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\') cur.Append('\\').Append((char)b);
            else if (b < 32 || b > 126) cur.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            else cur.Append((char)b);
        }
        cur.Append(") Tj ET\n");
    }

    public void TextRight(double right, double y, string text, double size, Font f = Font.Regular, string color = "#1B2533") => Text(right - Width(text, size, f), y, text, size, f, color);

    /// <summary>A JPEG picture drawn at (x, y) with the given size.</summary>
    public void Image(byte[] jpeg, int pxW, int pxH, double x, double y, double w, double h)
    {
        images.Add((jpeg, pxW, pxH));
        cur.Append($"q {N(w)} 0 0 {N(h)} {N(x)} {N(PageH - y - h)} cm /Im{images.Count} Do Q\n");
    }

    // ------------------------------------------------------------------ text measuring (Helvetica = Arial metrics)
    static readonly short[] WReg = Parse("278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,556,556,333,500,278,556,500,722,500,500,500,334,260,334,584,500,556,500,222,556,333,1000,556,556,333,1000,667,333,1000,500,611,500,500,222,222,333,333,350,556,1000,333,1000,500,333,944,500,500,667,278,333,556,556,556,556,260,556,333,737,370,556,584,333,737,552,400,549,333,333,333,576,537,333,333,333,365,556,834,834,834,611,667,667,667,667,667,667,1000,722,667,667,667,667,278,278,278,278,722,722,778,778,778,778,778,584,778,722,722,722,722,667,667,611,556,556,556,556,556,556,889,500,556,556,556,556,278,278,278,278,556,556,556,556,556,556,556,549,611,556,556,556,556,500,556,500");
    static readonly short[] WBold = Parse("278,333,474,556,556,889,722,238,333,333,389,584,278,333,278,278,556,556,556,556,556,556,556,556,556,556,333,333,584,584,584,611,975,722,722,722,722,667,611,778,722,278,556,722,611,833,722,778,667,778,722,667,611,722,667,944,667,667,611,333,278,333,584,556,333,556,611,556,611,556,333,611,611,278,278,556,278,889,611,611,611,611,389,556,333,611,556,778,556,556,500,389,280,389,584,500,556,500,278,556,500,1000,556,556,333,1000,667,333,1000,500,611,500,500,278,278,500,500,350,556,1000,333,1000,556,333,944,500,500,667,278,333,556,556,556,556,280,556,333,737,370,556,584,333,737,552,400,549,333,333,333,576,556,333,333,333,365,556,834,834,834,611,722,722,722,722,722,722,1000,722,667,667,667,667,278,278,278,278,722,722,778,778,778,778,778,584,778,722,722,722,722,667,667,611,556,556,556,556,556,556,889,556,556,556,556,556,278,278,278,278,611,611,611,611,611,611,611,549,611,611,611,611,611,556,611,556");
    static short[] Parse(string s) => s.Split(',').Select(short.Parse).ToArray();

    public static double Width(string text, double size, Font f = Font.Regular)
    {
        double w = 0;
        foreach (var b in Encode(text ?? "")) w += f == Font.Mono ? 600 : b < 32 ? 0 : (f == Font.Bold ? WBold : WReg)[b - 32];
        return w * size / 1000;
    }

    /// <summary>Shortens text with "…" so it fits in the width.</summary>
    public static string Fit(string text, double width, double size, Font f = Font.Regular)
    {
        text ??= "";
        if (Width(text, size, f) <= width) return text;
        while (text.Length > 0 && Width(text + "…", size, f) > width) text = text[..^1];
        return text.TrimEnd() + "…";
    }

    /// <summary>Splits text into lines that fit the width (breaks at spaces and commas, long words are cut).</summary>
    public static List<string> Wrap(string text, double width, double size, Font f = Font.Regular, int maxLines = 6)
    {
        var lines = new List<string>();
        foreach (var para in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in System.Text.RegularExpressions.Regex.Split(para, @"(?<=[ ,])"))
            {
                if (word == "") continue;
                if (Width(line + word, size, f) <= width) { line += word; continue; }
                if (line.Trim() != "") { lines.Add(line.TrimEnd()); line = ""; }
                var w = word;
                while (Width(w, size, f) > width && w.Length > 1)
                {
                    int k = w.Length; while (k > 1 && Width(w[..k], size, f) > width) k--;
                    lines.Add(w[..k]); w = w[k..];
                }
                line = w;
            }
            lines.Add(line.TrimEnd());
        }
        if (lines.Count > maxLines) { lines = lines.Take(maxLines).ToList(); lines[^1] = Fit(lines[^1] + "…", width, size, f); }
        return lines;
    }

    // ------------------------------------------------------------------ characters (WinAnsi)
    static readonly Dictionary<char, byte> Ansi = new()
    {
        ['€'] = 0x80, ['‚'] = 0x82, ['ƒ'] = 0x83, ['„'] = 0x84, ['…'] = 0x85, ['†'] = 0x86, ['‡'] = 0x87, ['ˆ'] = 0x88, ['‰'] = 0x89, ['Š'] = 0x8A, ['‹'] = 0x8B, ['Œ'] = 0x8C, ['Ž'] = 0x8E,
        ['‘'] = 0x91, ['’'] = 0x92, ['“'] = 0x93, ['”'] = 0x94, ['•'] = 0x95, ['–'] = 0x96, ['—'] = 0x97, ['˜'] = 0x98, ['™'] = 0x99, ['š'] = 0x9A, ['›'] = 0x9B, ['œ'] = 0x9C, ['ž'] = 0x9E, ['Ÿ'] = 0x9F,
    };
    static readonly Dictionary<char, string> Fallback = new() { ['→'] = "->", ['←'] = "<-", ['↔'] = "<->", ['−'] = "-", ['✓'] = "v", ['✕'] = "x", ['⋯'] = "...", ['≡'] = "=", ['⚠'] = "!", ['\t'] = "  " };

    static IEnumerable<byte> Encode(string s)
    {
        foreach (var ch in s)
        {
            if (ch >= 32 && ch < 127 || ch >= 160 && ch <= 255) yield return (byte)ch;
            else if (Ansi.TryGetValue(ch, out var b)) yield return b;
            else if (Fallback.TryGetValue(ch, out var fb)) foreach (var c in fb) yield return (byte)c;
            else if (ch >= 32) yield return (byte)'?';
        }
    }

    // ------------------------------------------------------------------ file
    public byte[] Save()
    {
        var ms = new MemoryStream();
        var offsets = new List<long>();
        void W(string s) { var b = Encoding.Latin1.GetBytes(s); ms.Write(b, 0, b.Length); }
        int objNo = 0;
        int Obj() { offsets.Add(ms.Position); objNo++; return objNo; }

        W("%PDF-1.4\n%âãÏÓ\n");
        // 1 catalog, 2 pages, 3-5 fonts, 6 info; then images, then page + content pairs
        int nImg = images.Count, firstImg = 7, firstPage = firstImg + nImg;
        var kids = string.Join(" ", Enumerable.Range(0, pages.Count).Select(i => $"{firstPage + i * 2} 0 R"));
        Obj(); W($"1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\n");
        Obj(); W($"2 0 obj << /Type /Pages /Kids [{kids}] /Count {pages.Count} >> endobj\n");
        foreach (var (n, name) in new[] { (3, "Helvetica"), (4, "Helvetica-Bold"), (5, "Courier") })
        { Obj(); W($"{n} 0 obj << /Type /Font /Subtype /Type1 /BaseFont /{name} /Encoding /WinAnsiEncoding >> endobj\n"); }
        Obj(); W($"6 0 obj << /Title ({Esc(Title)}) /Producer (IP Monitor) /CreationDate (D:{DateTime.Now:yyyyMMddHHmmss}) >> endobj\n");
        var imgRes = new StringBuilder();
        for (int i = 0; i < nImg; i++)
        {
            var (jpg, w, h) = images[i]; int n = Obj();
            W($"{n} 0 obj << /Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpg.Length} >>\nstream\n");
            ms.Write(jpg, 0, jpg.Length); W("\nendstream endobj\n");
            imgRes.Append($"/Im{i + 1} {n} 0 R ");
        }
        for (int i = 0; i < pages.Count; i++)
        {
            int pn = Obj();
            W($"{pn} 0 obj << /Type /Page /Parent 2 0 R /MediaBox [0 0 {N(PageW)} {N(PageH)}] /Resources << /Font << /F1 3 0 R /F2 4 0 R /F3 5 0 R >> /XObject << {imgRes}>> >> /Contents {pn + 1} 0 R >> endobj\n");
            var data = Encoding.Latin1.GetBytes(pages[i].ToString());
            var z = new MemoryStream();
            using (var zl = new System.IO.Compression.ZLibStream(z, System.IO.Compression.CompressionLevel.Optimal, true)) zl.Write(data, 0, data.Length);
            int cn = Obj();
            W($"{cn} 0 obj << /Length {z.Length} /Filter /FlateDecode >>\nstream\n"); z.Position = 0; z.CopyTo(ms); W("\nendstream endobj\n");
        }
        long xref = ms.Position;
        W($"xref\n0 {objNo + 1}\n0000000000 65535 f \n");
        foreach (var o in offsets) W($"{o:0000000000} 00000 n \n");
        W($"trailer << /Size {objNo + 1} /Root 1 0 R /Info 6 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return ms.ToArray();
    }

    static string Esc(string s) => new string(Encode(s ?? "").Select(b => (char)b).ToArray()).Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
}
