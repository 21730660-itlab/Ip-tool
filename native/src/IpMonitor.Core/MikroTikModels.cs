namespace IpMonitor.Core;

/// <summary>
/// MikroTik models for the device form: name, kind (router / wireless / both) and its interfaces.
/// The ports are a starting point and can always be changed on the device.
/// </summary>
public static class MikroTikModels
{
    public record Model(string Name, string Code, string Type, List<string> Ports, string Family)
    {
        public string Label => Code == "" ? Name : $"{Name}  ({Code})";
    }

    static List<string> P(int eth, int sfp = 0, int sfpPlus = 0, int wlan = 0, int sfp28 = 0, int qsfp = 0, bool wlan60 = false)
    {
        var l = new List<string>();
        for (int i = 1; i <= eth; i++) l.Add("ether" + i);
        if (sfp == 1) l.Add("sfp1"); else for (int i = 1; i <= sfp; i++) l.Add("sfp" + i);
        for (int i = 1; i <= sfpPlus; i++) l.Add("sfp-sfpplus" + i);
        for (int i = 1; i <= sfp28; i++) l.Add("sfp28-" + i);
        for (int i = 1; i <= qsfp; i++) l.Add("qsfp28-" + i + "-1");
        for (int i = 1; i <= wlan; i++) l.Add("wlan" + i);
        if (wlan60) l.Add("wlan60-1");
        return l;
    }

    public static readonly List<Model> All = new()
    {
        // ---------------- home and office routers
        new("hEX", "RB750Gr3", "router", P(5), "Router"),
        new("hEX S", "RB760iGS", "router", P(5, sfp: 1), "Router"),
        new("hEX lite", "RB750r2", "router", P(5), "Router"),
        new("hEX PoE", "RB960PGS", "router", P(5, sfp: 1), "Router"),
        new("hEX refresh", "E50UG", "router", P(5), "Router"),
        new("hAP lite", "RB941-2nD", "both", P(4, wlan: 1), "Router + Wi-Fi"),
        new("hAP", "RB951Ui-2nD", "both", P(5, wlan: 1), "Router + Wi-Fi"),
        new("hAP ac lite", "RB952Ui-5ac2nD", "both", P(5, wlan: 2), "Router + Wi-Fi"),
        new("hAP ac²", "RBD52G-5HacD2HnD", "both", P(5, wlan: 2), "Router + Wi-Fi"),
        new("hAP ac³", "RBD53iG-5HacD2HnD", "both", P(5, wlan: 2), "Router + Wi-Fi"),
        new("hAP ax lite", "L41G-2axD", "both", P(4, wlan: 1), "Router + Wi-Fi"),
        new("hAP ax²", "C52iG-5HaxD2HaxD", "both", P(5, wlan: 2), "Router + Wi-Fi"),
        new("hAP ax³", "C53UiG+5HPaxD2HPaxD", "both", P(5, wlan: 2), "Router + Wi-Fi"),
        new("Chateau ax", "S53UG+5HaxD2HaxD", "both", P(5, wlan: 2), "Router + Wi-Fi"),
        new("Chateau LTE12", "RBD53G-5HacD2HnD-TC&EG12-EA", "both", P(5, wlan: 2), "Router + Wi-Fi"),
        new("RB951Ui-2HnD", "RB951Ui-2HnD", "both", P(5, wlan: 1), "Router + Wi-Fi"),
        new("RB2011UiAS-RM", "RB2011UiAS-RM", "router", P(10, sfp: 1), "Router"),
        new("RB2011UiAS-2HnD-IN", "RB2011UiAS-2HnD-IN", "both", P(10, sfp: 1, wlan: 1), "Router + Wi-Fi"),
        new("RB3011UiAS-RM", "RB3011UiAS-RM", "router", P(10, sfp: 1), "Router"),
        new("RB4011iGS+RM", "RB4011iGS+RM", "router", P(10, sfpPlus: 1), "Router"),
        new("RB4011iGS+5HacQ2HnD-IN", "RB4011iGS+5HacQ2HnD-IN", "both", P(10, sfpPlus: 1, wlan: 2), "Router + Wi-Fi"),
        new("RB5009UG+S+IN", "RB5009UG+S+IN", "router", P(8, sfpPlus: 1), "Router"),
        new("RB5009UPr+S+IN", "RB5009UPr+S+IN", "router", P(8, sfpPlus: 1), "Router"),
        new("L009UiGS-RM", "L009UiGS-RM", "router", P(9, sfp: 1), "Router"),
        new("L009UiGS-2HaxD-IN", "L009UiGS-2HaxD-IN", "both", P(9, sfp: 1, wlan: 1), "Router + Wi-Fi"),
        new("RB1100AHx4", "RB1100Dx4", "router", P(13), "Router"),
        // ---------------- cloud core routers
        new("CCR1009-7G-1C-1S+", "CCR1009-7G-1C-1S+", "router", P(8, sfp: 1, sfpPlus: 1), "Cloud Core Router"),
        new("CCR1016-12G", "CCR1016-12G", "router", P(12), "Cloud Core Router"),
        new("CCR1036-8G-2S+", "CCR1036-8G-2S+", "router", P(8, sfpPlus: 2), "Cloud Core Router"),
        new("CCR1072-1G-8S+", "CCR1072-1G-8S+", "router", P(1, sfpPlus: 8), "Cloud Core Router"),
        new("CCR2004-16G-2S+", "CCR2004-16G-2S+", "router", P(16, sfpPlus: 2), "Cloud Core Router"),
        new("CCR2004-1G-12S+2XS", "CCR2004-1G-12S+2XS", "router", P(1, sfpPlus: 12, sfp28: 2), "Cloud Core Router"),
        new("CCR2116-12G-4S+", "CCR2116-12G-4S+", "router", P(13, sfpPlus: 4), "Cloud Core Router"),
        new("CCR2216-1G-12XS-2XQ", "CCR2216-1G-12XS-2XQ", "router", P(1, sfp28: 12, qsfp: 2), "Cloud Core Router"),
        // ---------------- switches (RouterOS)
        new("CRS112-8P-4S-IN", "CRS112-8P-4S-IN", "router", P(8, sfp: 4), "Switch"),
        new("CRS305-1G-4S+IN", "CRS305-1G-4S+IN", "router", P(1, sfpPlus: 4), "Switch"),
        new("CRS309-1G-8S+IN", "CRS309-1G-8S+IN", "router", P(1, sfpPlus: 8), "Switch"),
        new("CRS310-1G-5S-4S+IN", "CRS310-1G-5S-4S+IN", "router", P(1, sfp: 5, sfpPlus: 4), "Switch"),
        new("CRS317-1G-16S+RM", "CRS317-1G-16S+RM", "router", P(1, sfpPlus: 16), "Switch"),
        new("CRS326-24G-2S+RM", "CRS326-24G-2S+RM", "router", P(24, sfpPlus: 2), "Switch"),
        new("CRS328-24P-4S+RM", "CRS328-24P-4S+RM", "router", P(24, sfpPlus: 4), "Switch"),
        new("CRS354-48G-4S+2Q+RM", "CRS354-48G-4S+2Q+RM", "router", P(49, sfpPlus: 4), "Switch"),
        // ---------------- indoor access points
        new("cAP ac", "RBcAPGi-5acD2nD", "wireless", P(2, wlan: 2), "Access point"),
        new("cAP ax", "cAPGi-5HaxD2HaxD", "wireless", P(2, wlan: 2), "Access point"),
        new("cAP lite", "RBcAPL-2nD", "wireless", P(1, wlan: 1), "Access point"),
        new("wAP ac", "RBwAPG-5HacD2HnD", "wireless", P(1, wlan: 2), "Access point"),
        new("wAP ax", "wAPG-5HaxD2HaxD", "wireless", P(1, wlan: 2), "Access point"),
        new("wAP", "RBwAP2nD", "wireless", P(1, wlan: 1), "Access point"),
        new("Audience", "RBD25G-5HPacQD2HPnD", "wireless", P(2, wlan: 3), "Access point"),
        // ---------------- outdoor point-to-point / point-to-multipoint
        new("SXTsq 5 ac", "RBSXTsqG-5acD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("SXTsq 5 High Power", "RBSXTsq5HPnD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("SXTsq Lite5", "RBSXTsq5nD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("SXTsq Lite2", "RBSXTsq2nD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("SXT 5 ac", "RBSXTG-5HPacD-SA", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("SXT Lite5", "RBSXT5nDr2", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("SXT LTE6 kit", "RBSXTR&R11e-LTE6", "wireless", P(1), "Outdoor wireless"),
        new("LHG 5 ac", "RBLHGG-5acD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("LHG XL 5 ac", "RBLHGG-5acD-XL", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("LHG XL 52 ac", "RBLHGG-5HPacD2HPnD-XL", "wireless", P(1, wlan: 2), "Outdoor wireless"),
        new("LHG 5", "RBLHG-5nD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("LHG HP5", "RBLHG-5HPnD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("LHG XL HP5", "RBLHG-5HPnD-XL", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("LHG 2", "RBLHG-2nD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("LDF 5 ac", "RBLDFG-5acD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("LDF 5", "RBLDF-5nD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("Disc Lite5 ac", "RBDiscG-5acD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("Disc Lite5", "RBDisc-5nD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("DynaDish 5", "RBDynaDishG-5HacD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("QRT 5 ac", "RBQRTG-5HacD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("QRT 5", "RB911G-5HPnD-QRT", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("mANTBox 15s", "RB921GS-5HPacD-15S", "wireless", P(1, sfp: 1, wlan: 1), "Outdoor wireless"),
        new("mANTBox 19s", "RB921GS-5HPacD-19S", "wireless", P(1, sfp: 1, wlan: 1), "Outdoor wireless"),
        new("mANTBox 52 15s", "RB921GS-5HPacD2HPnD-15S", "wireless", P(1, sfp: 1, wlan: 2), "Outdoor wireless"),
        new("mANTBox ax 15s", "L22UGS-5HaxD2HaxD-15S", "wireless", P(1, sfp: 1, wlan: 2), "Outdoor wireless"),
        new("mANTBox 2 12s", "RB911G-2HPnD-12S", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("NetBox 5", "RB911G-5HPacD-NB", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("NetBox 5 ax", "L23UGSR-5HaxD2HaxD-NB", "wireless", P(1, sfp: 1, wlan: 2), "Outdoor wireless"),
        new("NetMetal ac²", "RB921UAGS-5SHPacD-NM", "wireless", P(1, sfp: 1, wlan: 1), "Outdoor wireless"),
        new("NetMetal 5", "RB921GS-5HPacD-NM", "wireless", P(1, sfp: 1, wlan: 1), "Outdoor wireless"),
        new("Groove 52 ac", "RBGrooveGA-52HPacn", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("Metal 52 ac", "RBMetalG-52SHPacn", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("BaseBox 5", "RB912UAG-5HPnD-OUT", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("BaseBox 2", "RB912UAG-2HPnD-OUT", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("OmniTIK 5 ac", "RBOmniTikG-5HacD", "wireless", P(5, wlan: 1), "Outdoor wireless"),
        new("OmniTIK 5 PoE ac", "RBOmniTikPG-5HacD", "wireless", P(5, wlan: 1), "Outdoor wireless"),
        new("RB911G-5HPacD", "RB911G-5HPacD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        new("RB912UAG-5HPnD", "RB912UAG-5HPnD", "wireless", P(1, wlan: 1), "Outdoor wireless"),
        // ---------------- 60 GHz
        new("Wireless Wire", "RBwAPG-60adkit", "wireless", P(1, wlan60: true), "60 GHz"),
        new("Wireless Wire Cube", "CubeG-5ac60adpair", "wireless", P(1, wlan: 1, wlan60: true), "60 GHz"),
        new("Wireless Wire nRAY", "nRAYG-60adpair", "wireless", P(1, wlan60: true), "60 GHz"),
        new("Wireless Wire Dish", "RBLHGG-60adkit", "wireless", P(1, wlan60: true), "60 GHz"),
        new("Cube 60Pro ac", "CubeG-5ac60adpair", "wireless", P(1, wlan: 1, wlan60: true), "60 GHz"),
        new("Cube Lite60", "CubeSA-60adr2", "wireless", P(1, wlan60: true), "60 GHz"),
        new("LHG 60G", "RBLHGG-60ad", "wireless", P(1, wlan60: true), "60 GHz"),
        new("wAP 60G", "RBwAPG-60ad", "wireless", P(1, wlan60: true), "60 GHz"),
        new("wAP 60G AP", "RBwAPG-60ad-A", "wireless", P(1, wlan60: true), "60 GHz"),
    };

    static string Key(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>Models whose name or code contains what was typed (spaces and dashes ignored): "sq" → SXTsq…, "4011" → RB4011….</summary>
    public static List<Model> Search(string typed)
    {
        var k = Key(typed);
        if (k == "") return All.ToList();
        return All.Where(m => Key(m.Name).Contains(k) || Key(m.Code).Contains(k))
                  .OrderBy(m => Key(m.Name).StartsWith(k) ? 0 : Key(m.Name).Contains(k) ? 1 : 2).ThenBy(m => m.Name).ToList();
    }

    /// <summary>The catalogue entry for a model written on a device (by name or code).</summary>
    public static Model Find(string model)
    {
        var k = Key(model); if (k == "") return null;
        return All.FirstOrDefault(m => Key(m.Name) == k || Key(m.Code) == k);
    }
}
