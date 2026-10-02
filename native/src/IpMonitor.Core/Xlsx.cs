using System.IO.Compression;
using System.Security;
using System.Text;

namespace IpMonitor.Core;

/// <summary>
/// A small Excel (.xlsx) writer, no extra libraries: several sheets, a navy header row that stays visible,
/// filter buttons, column widths, group rows and numbers stored as numbers.
/// </summary>
public class Xlsx
{
    public enum Style { Normal = 0, Header = 1, Group = 2, Title = 3, Mono = 4, Muted = 5 }

    public class Sheet
    {
        public string Name;
        public double[] Widths = Array.Empty<double>();
        public readonly List<(object[] cells, Style style)> Rows = new();
        public int HeaderRow = -1;   // 0-based row index of the column headers (frozen + filter)
        public HashSet<int> MonoCols = new();
        public Sheet Add(Style s, params object[] cells) { Rows.Add((cells, s)); return this; }
        public Sheet Header(params object[] cells) { HeaderRow = Rows.Count; Rows.Add((cells, Style.Header)); return this; }
    }

    public readonly List<Sheet> Sheets = new();
    public Sheet AddSheet(string name, params double[] widths)
    {
        var clean = new string((name ?? "Sheet").Select(c => "[]:*?/\\".Contains(c) ? ' ' : c).ToArray()).Trim();
        if (clean.Length > 31) clean = clean[..31];
        var s = new Sheet { Name = clean, Widths = widths };
        Sheets.Add(s); return s;
    }

    static string Col(int i) { var s = ""; i++; while (i > 0) { int m = (i - 1) % 26; s = (char)('A' + m) + s; i = (i - 1) / 26; } return s; }
    static string X(string s) => SecurityElement.Escape(new string((s ?? "").Where(c => c == '\t' || c == '\n' || c == '\r' || c >= ' ').ToArray()));

    public byte[] Save()
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            void Put(string path, string xml)
            {
                var e = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(xml);
            }
            const string head = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n";
            Put("[Content_Types].xml", head + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
                "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                string.Concat(Sheets.Select((_, i) => $"<Override PartName=\"/xl/worksheets/sheet{i + 1}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>")) +
                "</Types>");
            Put("_rels/.rels", head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/></Relationships>");
            Put("docProps/core.xml", head + "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                $"<dc:title>IP Monitor</dc:title><dc:creator>IP Monitor</dc:creator><dcterms:created xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created></cp:coreProperties>");
            Put("xl/workbook.xml", head + "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
                string.Concat(Sheets.Select((s, i) => $"<sheet name=\"{X(s.Name)}\" sheetId=\"{i + 1}\" r:id=\"rId{i + 1}\"/>")) + "</sheets><definedNames>" +
                string.Concat(Sheets.Select((s, i) => (s, i)).Where(t => t.s.HeaderRow >= 0 && t.s.Rows.Count > t.s.HeaderRow + 0).Select(t =>
                    $"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{t.i}\" hidden=\"1\">'{X(t.s.Name.Replace("'", "''"))}'!${Col(0)}${t.s.HeaderRow + 1}:${Col(Math.Max(0, t.s.Rows[t.s.HeaderRow].cells.Length - 1))}${t.s.Rows.Count}</definedName>")) +
                "</definedNames></workbook>");
            Put("xl/_rels/workbook.xml.rels", head + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                string.Concat(Sheets.Select((_, i) => $"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{i + 1}.xml\"/>")) +
                $"<Relationship Id=\"rId{Sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            // styles: 0 normal, 1 header (white bold on navy), 2 group (bold on light blue), 3 title (bold 14), 4 mono, 5 muted
            Put("xl/styles.xml", head + "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                "<fonts count=\"6\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><color rgb=\"FFFFFFFF\"/><name val=\"Calibri\"/></font>" +
                "<font><b/><sz val=\"11\"/><color rgb=\"FF0B2240\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"15\"/><color rgb=\"FF0B2240\"/><name val=\"Calibri\"/></font>" +
                "<font><sz val=\"10\"/><name val=\"Consolas\"/></font><font><sz val=\"10\"/><color rgb=\"FF5E6B7B\"/><name val=\"Calibri\"/></font></fonts>" +
                "<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FF0B2240\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
                "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFEAF2FE\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
                "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border><border><left/><right/><top/><bottom style=\"thin\"><color rgb=\"FFE4E8ED\"/></bottom><diagonal/></border></borders>" +
                "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"6\">" +
                "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"><alignment vertical=\"center\"/></xf>" +
                "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
                "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf>" +
                "<xf numFmtId=\"0\" fontId=\"5\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
                "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
            for (int i = 0; i < Sheets.Count; i++) Put($"xl/worksheets/sheet{i + 1}.xml", SheetXml(Sheets[i]));
        }
        return ms.ToArray();
    }

    static string SheetXml(Sheet s)
    {
        var b = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\n<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">");
        b.Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr><sheetViews><sheetView workbookViewId=\"0\"");
        if (s.HeaderRow >= 0) b.Append($"><pane ySplit=\"{s.HeaderRow + 1}\" topLeftCell=\"A{s.HeaderRow + 2}\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView>");
        else b.Append("/>");
        b.Append("</sheetViews><sheetFormatPr defaultRowHeight=\"15\"/>");
        if (s.Widths.Length > 0) b.Append("<cols>" + string.Concat(s.Widths.Select((w, i) => $"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{w.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" customWidth=\"1\"/>")) + "</cols>");
        b.Append("<sheetData>");
        for (int r = 0; r < s.Rows.Count; r++)
        {
            var (cells, style) = s.Rows[r];
            b.Append($"<row r=\"{r + 1}\"{(style == Style.Header ? " ht=\"20\" customHeight=\"1\"" : style == Style.Title ? " ht=\"22\" customHeight=\"1\"" : "")}>");
            for (int c = 0; c < cells.Length; c++)
            {
                var v = cells[c]; if (v == null || v is string str0 && str0 == "") { if (style is Style.Header or Style.Group) b.Append($"<c r=\"{Col(c)}{r + 1}\" s=\"{(int)style}\"/>"); continue; }
                var st = style == Style.Normal && s.MonoCols.Contains(c) ? (int)Style.Mono : (int)style;
                var refc = $"{Col(c)}{r + 1}";
                if (v is int or long or double or decimal or float)
                    b.Append($"<c r=\"{refc}\" s=\"{st}\"><v>{Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                else
                {
                    var text = v.ToString();
                    if (text.Length > 32000) text = text[..32000];
                    b.Append($"<c r=\"{refc}\" s=\"{st}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{X(text)}</t></is></c>");
                }
            }
            b.Append("</row>");
        }
        b.Append("</sheetData>");
        if (s.HeaderRow >= 0 && s.Rows.Count > s.HeaderRow + 1)
            b.Append($"<autoFilter ref=\"A{s.HeaderRow + 1}:{Col(s.Rows[s.HeaderRow].cells.Length - 1)}{s.Rows.Count}\"/>");
        b.Append("<pageMargins left=\"0.5\" right=\"0.5\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/><pageSetup orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/></worksheet>");
        return b.ToString();
    }
}
