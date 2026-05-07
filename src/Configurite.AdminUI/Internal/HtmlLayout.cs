using System.Text;
using System.Text.Encodings.Web;

namespace Configurite.AdminUI.Internal;

/// <summary>
/// EN: Renders a minimal, dependency-free HTML shell shared by every admin page.
/// TR: Her yönetim sayfasının paylaştığı, bağımlılıksız minimal HTML kabuğunu üretir.
/// </summary>
internal static class HtmlLayout
{
    private const string Css = """
        :root { --bg:#0d1117; --panel:#161b22; --border:#30363d; --text:#e6edf3; --muted:#8b949e; --accent:#2f81f7; --danger:#f85149; --good:#3fb950; }
        * { box-sizing:border-box; }
        body { margin:0; font:14px/1.5 -apple-system,BlinkMacSystemFont,Segoe UI,sans-serif; background:var(--bg); color:var(--text); }
        a { color:var(--accent); text-decoration:none; }
        a:hover { text-decoration:underline; }
        header { background:var(--panel); border-bottom:1px solid var(--border); padding:12px 24px; display:flex; align-items:center; gap:24px; }
        header h1 { margin:0; font-size:16px; }
        nav { display:flex; gap:18px; flex:1; }
        nav a { color:var(--muted); padding:6px 0; }
        nav a.active { color:var(--text); border-bottom:2px solid var(--accent); }
        main { padding:24px; max-width:1100px; margin:0 auto; }
        h2 { margin:0 0 16px; font-size:20px; }
        .stats { display:grid; grid-template-columns:repeat(auto-fit, minmax(170px, 1fr)); gap:12px; margin-bottom:24px; }
        .stat { background:var(--panel); border:1px solid var(--border); border-radius:8px; padding:14px 16px; }
        .stat .label { color:var(--muted); font-size:12px; text-transform:uppercase; letter-spacing:0.05em; }
        .stat .value { font-size:24px; font-weight:600; margin-top:4px; }
        table { width:100%; border-collapse:collapse; background:var(--panel); border:1px solid var(--border); border-radius:8px; overflow:hidden; }
        th, td { padding:10px 14px; text-align:left; border-bottom:1px solid var(--border); }
        th { background:#1c2128; font-size:12px; text-transform:uppercase; letter-spacing:0.05em; color:var(--muted); }
        tr:last-child td { border-bottom:none; }
        td.value { font-family:ui-monospace,Menlo,monospace; word-break:break-all; }
        .pill { display:inline-block; padding:2px 8px; border-radius:10px; font-size:11px; }
        .pill.enc { background:#1f3a5b; color:#7fb6f4; }
        .pill.env { background:#2a3a26; color:#9ed18b; }
        form { background:var(--panel); border:1px solid var(--border); border-radius:8px; padding:18px; margin-bottom:18px; }
        form.inline { display:inline; padding:0; border:none; background:transparent; }
        label { display:block; margin-bottom:12px; }
        label .hint { color:var(--muted); font-size:12px; margin-left:6px; }
        input[type=text], input[type=password], select, textarea { width:100%; background:#0d1117; border:1px solid var(--border); color:var(--text); padding:8px 10px; border-radius:6px; font:inherit; }
        textarea { min-height:80px; font-family:ui-monospace,Menlo,monospace; }
        button, .btn { background:var(--accent); color:#fff; border:0; padding:8px 14px; border-radius:6px; cursor:pointer; font:inherit; }
        button.secondary, .btn.secondary { background:#21262d; }
        button.danger, .btn.danger { background:var(--danger); }
        .row-actions { display:flex; gap:6px; }
        .notice { padding:12px 16px; border-radius:6px; background:#1c2128; border:1px solid var(--border); margin-bottom:16px; color:var(--muted); }
        .notice.warn { background:#3a261a; border-color:#7d4f3b; color:#f0b070; }
        footer { color:var(--muted); padding:24px; text-align:center; font-size:12px; }
        """;

    public static string Render(string title, string activeTab, string lang, string basePath, string body)
    {
        var sb = new StringBuilder(4096);
        var enc = HtmlEncoder.Default;
        var otherLang = lang == "tr" ? "en" : "tr";

        sb.Append("<!doctype html><html lang=\"").Append(enc.Encode(lang)).Append("\"><head><meta charset=\"utf-8\">");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.Append("<title>").Append(enc.Encode(title)).Append("</title><style>").Append(Css).Append("</style></head><body>");
        sb.Append("<header><h1>").Append(enc.Encode(Localization.Get("AppTitle", lang))).Append("</h1><nav>");
        AppendNav(sb, basePath, "", "NavDashboard", activeTab, lang);
        AppendNav(sb, basePath, "/keys", "NavKeys", activeTab, lang);
        AppendNav(sb, basePath, "/audit", "NavAudit", activeTab, lang);
        sb.Append("</nav>");
        sb.Append("<a href=\"?lang=").Append(otherLang).Append("\">").Append(enc.Encode(Localization.Get("LangToggle", lang))).Append("</a>");
        sb.Append("</header><main>").Append(body).Append("</main>");
        sb.Append("<footer>Configurite Admin · v8.4 / 9.4 / 10.4</footer></body></html>");

        return sb.ToString();
    }

    private static void AppendNav(StringBuilder sb, string basePath, string suffix, string labelKey, string activeTab, string lang)
    {
        var enc = HtmlEncoder.Default;
        var href = basePath + suffix;
        var cssClass = string.Equals(activeTab, suffix, StringComparison.Ordinal) ? " class=\"active\"" : string.Empty;
        sb.Append("<a href=\"").Append(enc.Encode(href)).Append('"').Append(cssClass).Append('>')
          .Append(enc.Encode(Localization.Get(labelKey, lang)))
          .Append("</a>");
    }
}
