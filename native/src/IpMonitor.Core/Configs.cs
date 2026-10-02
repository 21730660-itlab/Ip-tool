using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace IpMonitor.Core;

/// <summary>A saved RouterOS configuration (/export, .rsc file) of a device — same fields as the web version.</summary>
public class ConfigBackup
{
    [JsonPropertyName("id")] public string Id { get; set; } = Entity.NewId();
    [JsonPropertyName("deviceId")] public string DeviceId { get; set; } = "";
    [JsonPropertyName("siteId")] public string SiteId { get; set; } = "";
    [JsonPropertyName("at")] public string At { get; set; } = Entity.Now();
    [JsonPropertyName("by")] public string By { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
    [JsonPropertyName("size"), JsonConverter(typeof(LooseInt))] public int Size { get; set; }
    [JsonPropertyName("lines"), JsonConverter(typeof(LooseInt))] public int Lines { get; set; }
    [JsonPropertyName("ros")] public string Ros { get; set; } = "";
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("identity")] public string Identity { get; set; } = "";
    [JsonPropertyName("masked")] public bool? Masked { get; set; }
    [JsonPropertyName("createdAt"), JsonConverter(typeof(LooseString))] public string CreatedAt { get; set; }
    [JsonPropertyName("updatedAt"), JsonConverter(typeof(LooseString))] public string UpdatedAt { get; set; }
    [JsonExtensionData] public Dictionary<string, System.Text.Json.JsonElement> Extra { get; set; }
}

/// <summary>RouterOS export helpers: recognise, describe, hide secrets, compare.</summary>
public static class Rsc
{
    public const int MaxBytes = 200 * 1024;

    public record Info(string Ros, string Model, string Identity, int Lines, bool Looks);

    static readonly Regex SecretRe = new(
        @"(^|[\s/])((?:password|secret|passphrase|wpa-pre-shared-key|wpa2-pre-shared-key|pre-shared-key|authentication-key|auth-key|private-key|preshared-key|psk|shared-secret|auth-password|encryption-password|md5-key)=)(""(?:[^""\\]|\\.)*""|[^\s""]+)",
        RegexOptions.IgnoreCase | RegexOptions.Multiline);
    static bool IsHidden(string v) => Regex.IsMatch(v, @"^""?\*+""?$") || v == "\"\"";

    public static Info Parse(string t)
    {
        t ??= "";
        var ros = Regex.Match(t, @"by RouterOS\s+([0-9][0-9A-Za-z.\-]*)").Groups[1].Value;
        var model = Regex.Match(t, @"^#\s*model\s*=\s*(.+)$", RegexOptions.Multiline).Groups[1].Value.Trim();
        var idm = Regex.Match(t, @"/system identity\s*\r?\n\s*set name=(?:""((?:[^""\\]|\\.)*)""|(\S+))");
        var identity = idm.Success ? (idm.Groups[1].Success ? idm.Groups[1].Value : idm.Groups[2].Value) : "";
        return new Info(ros, model, identity, t.Split('\n').Length, Regex.IsMatch(t, @"^\s*/[a-z]", RegexOptions.IgnoreCase | RegexOptions.Multiline));
    }

    public static string MaskSecrets(string t) => SecretRe.Replace(t, m => IsHidden(m.Groups[3].Value) ? m.Value : m.Groups[1].Value + m.Groups[2].Value + "\"***\"");
    public static int CountSecrets(string t) => SecretRe.Matches(t ?? "").Count(m => !IsHidden(m.Groups[3].Value));

    /// <summary>Without the export's own date line and trailing spaces, so two exports of the same config compare equal.</summary>
    public static string Norm(string t)
    {
        t = (t ?? "").Replace("\r\n", "\n");
        t = new Regex(@"^#.*by RouterOS.*\n", RegexOptions.Multiline).Replace(t, "", 1);
        return Regex.Replace(t, @"[ \t]+$", "", RegexOptions.Multiline).Trim();
    }

    public static int Bytes(string t) => Encoding.UTF8.GetByteCount(t ?? "");
    public static string Size(long n) => n < 1024 ? $"{n} B" : n < 1024 * 1024 ? $"{n / 1024.0:0.#} KB" : $"{n / 1048576.0:0.#} MB";

    /// <summary>Line by line comparison: ' ' same, '-' only in the old one, '+' only in the new one.</summary>
    public static List<(char op, string line)> Diff(string a, string b)
    {
        var A = Norm(a).Split('\n'); var B = Norm(b).Split('\n');
        int s = 0; while (s < A.Length && s < B.Length && A[s] == B[s]) s++;
        int e = 0; while (e < A.Length - s && e < B.Length - s && A[A.Length - 1 - e] == B[B.Length - 1 - e]) e++;
        var a2 = A[s..(A.Length - e)]; var b2 = B[s..(B.Length - e)];
        int n = a2.Length, m = b2.Length;
        var mid = new List<(char, string)>();
        if ((long)n * m > 4_000_000) { mid.AddRange(a2.Select(t => ('-', t))); mid.AddRange(b2.Select(t => ('+', t))); }
        else
        {
            var L = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--) for (int j = m - 1; j >= 0; j--) L[i, j] = a2[i] == b2[j] ? L[i + 1, j + 1] + 1 : Math.Max(L[i + 1, j], L[i, j + 1]);
            int x = 0, y = 0;
            while (x < n && y < m)
            {
                if (a2[x] == b2[y]) { mid.Add((' ', a2[x])); x++; y++; }
                else if (L[x + 1, y] >= L[x, y + 1]) mid.Add(('-', a2[x++]));
                else mid.Add(('+', b2[y++]));
            }
            while (x < n) mid.Add(('-', a2[x++]));
            while (y < m) mid.Add(('+', b2[y++]));
        }
        return A[..s].Select(t => (' ', t)).Concat(mid).Concat(A[(A.Length - e)..].Select(t => (' ', t))).ToList();
    }

    /// <summary>The changed lines with 3 lines around them and the "/section" header above each change; gaps become "⋯ n unchanged lines".</summary>
    public static List<(char op, string line)> DiffContext(List<(char op, string line)> ops, int ctx = 3)
    {
        var keep = new bool[ops.Count];
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops[i].op == ' ') continue;
            for (int k = Math.Max(0, i - ctx); k <= Math.Min(ops.Count - 1, i + ctx); k++) keep[k] = true;
            for (int k = i; k >= 0; k--) if (ops[k].line.StartsWith('/')) { keep[k] = true; break; }
        }
        var res = new List<(char, string)>(); int skip = 0;
        for (int i = 0; i < ops.Count; i++)
        {
            if (!keep[i]) { skip++; continue; }
            if (skip > 0) { res.Add(('~', $"⋯ {skip} unchanged line{(skip == 1 ? "" : "s")}")); skip = 0; }
            res.Add(ops[i]);
        }
        if (skip > 0) res.Add(('~', $"⋯ {skip} unchanged line{(skip == 1 ? "" : "s")}"));
        return res;
    }
}
