using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Configurite.Audit;
using Configurite.Encryption;
using Configurite.Storage;
using Microsoft.AspNetCore.Http;

namespace Configurite.AdminUI.Internal;

internal static class AdminEndpoints
{
    private static readonly HtmlEncoder Enc = HtmlEncoder.Default;

    public static IResult Dashboard(HttpContext ctx, AdminUIOptions options, string basePath)
    {
        var lang = Localization.Resolve(ctx, options.DefaultLanguage);
        var store = new SqliteConfiguriteStore(options.DatabasePath);
        store.EnsureSchema();

        var entries = ReadAllAcrossEnvironments(options.DatabasePath);
        var encryptedCount = entries.Count(e => e.IsEncrypted);
        var environments = entries.Where(e => e.Environment is not null)
                                  .Select(e => e.Environment!)
                                  .Distinct(StringComparer.OrdinalIgnoreCase)
                                  .Count();
        var schemaVer = store.ReadMetadata("SchemaVersion") ?? "?";
        var auditCount = new SqliteConfiguriteAuditLog(options.DatabasePath).Count();

        var body = new StringBuilder();
        body.Append("<h2>").Append(Enc.Encode(Localization.Get("DashTitle", lang))).Append("</h2><div class=\"stats\">");
        Stat(body, Localization.Get("StatRows", lang), entries.Count.ToString(CultureInfo.InvariantCulture));
        Stat(body, Localization.Get("StatEncrypted", lang), encryptedCount.ToString(CultureInfo.InvariantCulture));
        Stat(body, Localization.Get("StatEnvironments", lang), environments.ToString(CultureInfo.InvariantCulture));
        Stat(body, Localization.Get("StatSchema", lang), schemaVer);
        Stat(body, Localization.Get("StatAuditCount", lang), auditCount.ToString(CultureInfo.InvariantCulture));
        body.Append("</div>");

        return Results.Content(HtmlLayout.Render("Dashboard", "", lang, basePath, body.ToString()), "text/html; charset=utf-8");
    }

    public static IResult KeysPage(HttpContext ctx, AdminUIOptions options, string basePath)
    {
        var lang = Localization.Resolve(ctx, options.DefaultLanguage);
        var store = new SqliteConfiguriteStore(options.DatabasePath);
        store.EnsureSchema();

        var envFilter = ctx.Request.Query["env"].ToString();
        var revealKey = ctx.Request.Query["reveal"].ToString();
        var revealEnv = ctx.Request.Query["revealEnv"].ToString();
        var nullableEnv = string.IsNullOrEmpty(envFilter) ? null
            : envFilter == "__global__" ? string.Empty // sentinel for global-only
            : envFilter;

        var allEntries = ReadAllAcrossEnvironments(options.DatabasePath);
        var environments = allEntries.Where(e => e.Environment is not null)
                                     .Select(e => e.Environment!)
                                     .Distinct(StringComparer.OrdinalIgnoreCase)
                                     .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
                                     .ToList();

        IEnumerable<RawEntry> filtered = allEntries;
        if (nullableEnv == string.Empty)
        {
            filtered = allEntries.Where(e => e.Environment is null);
        }
        else if (nullableEnv is not null)
        {
            filtered = allEntries.Where(e => string.Equals(e.Environment, nullableEnv, StringComparison.OrdinalIgnoreCase));
        }

        // Build optional encryptor for reveal.
        AesGcmConfigEncryptor? enc = ConfiguriteEncryption.TryCreateEncryptor(store, options.MasterKey);

        var body = new StringBuilder();
        body.Append("<h2>").Append(Enc.Encode(Localization.Get("KeysTitle", lang))).Append("</h2>");

        // Filter form
        body.Append("<form method=\"get\" style=\"display:flex;gap:12px;align-items:end;\">");
        body.Append("<label style=\"margin:0;flex:1;max-width:280px;\">").Append(Enc.Encode(Localization.Get("FilterEnv", lang)));
        body.Append("<select name=\"env\">");
        body.Append("<option value=\"\">").Append(Enc.Encode(Localization.Get("FilterAll", lang))).Append("</option>");
        body.Append("<option value=\"__global__\"").Append(envFilter == "__global__" ? " selected" : "").Append('>')
            .Append(Enc.Encode(Localization.Get("FilterGlobal", lang))).Append("</option>");
        foreach (var env in environments)
        {
            body.Append("<option value=\"").Append(Enc.Encode(env)).Append('"')
                .Append(string.Equals(envFilter, env, StringComparison.OrdinalIgnoreCase) ? " selected" : "")
                .Append('>').Append(Enc.Encode(env)).Append("</option>");
        }
        body.Append("</select></label>");
        body.Append("<button type=\"submit\">↻</button>");
        body.Append("</form>");

        // Add-new form
        body.Append("<details style=\"margin:16px 0;\"><summary>+ ").Append(Enc.Encode(Localization.Get("ActionAdd", lang))).Append("</summary>");
        body.Append("<form method=\"post\" action=\"").Append(Enc.Encode(basePath + "/keys")).Append("\">");
        body.Append("<label>").Append(Enc.Encode(Localization.Get("ColKey", lang))).Append("<input type=\"text\" name=\"key\" required></label>");
        body.Append("<label>").Append(Enc.Encode(Localization.Get("ColValue", lang))).Append("<textarea name=\"value\"></textarea></label>");
        body.Append("<label>").Append(Enc.Encode(Localization.Get("ColEnv", lang)))
            .Append("<input type=\"text\" name=\"env\" placeholder=\"(global)\"></label>");
        body.Append("<label><input type=\"checkbox\" name=\"encrypt\" value=\"1\"> ").Append(Enc.Encode(Localization.Get("FieldEncrypt", lang))).Append("</label>");
        body.Append("<button type=\"submit\">").Append(Enc.Encode(Localization.Get("ActionSave", lang))).Append("</button>");
        body.Append("</form></details>");

        if (enc is null && filtered.Any(e => e.IsEncrypted))
        {
            body.Append("<div class=\"notice warn\">").Append(Enc.Encode(Localization.Get("NoMasterKey", lang))).Append("</div>");
        }

        // Table
        var sorted = filtered.OrderBy(e => e.Environment is null ? "" : e.Environment, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(e => e.Key, StringComparer.OrdinalIgnoreCase).ToList();
        if (sorted.Count == 0)
        {
            body.Append("<div class=\"notice\">").Append(Enc.Encode(Localization.Get("EmptyKeys", lang))).Append("</div>");
        }
        else
        {
            body.Append("<table><thead><tr>");
            body.Append("<th>").Append(Enc.Encode(Localization.Get("ColKey", lang))).Append("</th>");
            body.Append("<th>").Append(Enc.Encode(Localization.Get("ColValue", lang))).Append("</th>");
            body.Append("<th>").Append(Enc.Encode(Localization.Get("ColEnv", lang))).Append("</th>");
            body.Append("<th>").Append(Enc.Encode(Localization.Get("ColEncrypted", lang))).Append("</th>");
            body.Append("<th>").Append(Enc.Encode(Localization.Get("ColActions", lang))).Append("</th>");
            body.Append("</tr></thead><tbody>");

            foreach (var entry in sorted)
            {
                body.Append("<tr>");
                body.Append("<td>").Append(Enc.Encode(entry.Key)).Append("</td>");
                body.Append("<td class=\"value\">");
                if (entry.IsEncrypted)
                {
                    bool isThisRow = string.Equals(revealKey, entry.Key, StringComparison.Ordinal)
                                  && string.Equals(revealEnv, entry.Environment ?? "", StringComparison.Ordinal);
                    if (isThisRow && enc is not null)
                    {
                        body.Append(Enc.Encode(enc.Decrypt(entry.Value)));
                    }
                    else
                    {
                        body.Append(Enc.Encode(Localization.Get("EncryptedHidden", lang)));
                        if (enc is not null)
                        {
                            body.Append(" <a href=\"?reveal=").Append(Enc.Encode(entry.Key))
                                .Append("&revealEnv=").Append(Enc.Encode(entry.Environment ?? ""));
                            if (!string.IsNullOrEmpty(envFilter)) body.Append("&env=").Append(Enc.Encode(envFilter));
                            body.Append("\">").Append(Enc.Encode(Localization.Get("ActionReveal", lang))).Append("</a>");
                        }
                    }
                }
                else
                {
                    body.Append(Enc.Encode(entry.Value));
                }
                body.Append("</td>");
                body.Append("<td>");
                if (entry.Environment is not null)
                    body.Append("<span class=\"pill env\">").Append(Enc.Encode(entry.Environment)).Append("</span>");
                body.Append("</td>");
                body.Append("<td>");
                if (entry.IsEncrypted) body.Append("<span class=\"pill enc\">🔒</span>");
                body.Append("</td>");
                body.Append("<td><div class=\"row-actions\">");
                body.Append("<form method=\"post\" action=\"").Append(Enc.Encode(basePath + "/keys/delete")).Append("\" class=\"inline\" onsubmit=\"return confirm('").Append(Enc.Encode(Localization.Get("DeleteConfirm", lang)).Replace("'", "\\'", StringComparison.Ordinal)).Append("');\">");
                body.Append("<input type=\"hidden\" name=\"key\" value=\"").Append(Enc.Encode(entry.Key)).Append("\">");
                body.Append("<input type=\"hidden\" name=\"env\" value=\"").Append(Enc.Encode(entry.Environment ?? "")).Append("\">");
                body.Append("<button type=\"submit\" class=\"danger\">").Append(Enc.Encode(Localization.Get("ActionDelete", lang))).Append("</button>");
                body.Append("</form>");
                body.Append("</div></td>");
                body.Append("</tr>");
            }
            body.Append("</tbody></table>");
        }

        enc?.Dispose();

        return Results.Content(HtmlLayout.Render("Keys", "/keys", lang, basePath, body.ToString()), "text/html; charset=utf-8");
    }

    public static IResult UpsertKey(HttpContext ctx, AdminUIOptions options, IConfiguriteStore writeStore, string basePath)
    {
        var form = ctx.Request.Form;
        var key = form["key"].ToString();
        var value = form["value"].ToString();
        var envInput = form["env"].ToString();
        var environment = string.IsNullOrWhiteSpace(envInput) ? null : envInput;
        var encrypt = form["encrypt"].ToString() == "1";

        if (string.IsNullOrEmpty(key))
        {
            return Results.BadRequest("key is required");
        }

        if (encrypt)
        {
            var s = new SqliteConfiguriteStore(options.DatabasePath);
            s.EnsureSchema();
            using var enc = ConfiguriteEncryption.CreateEncryptor(s, options.MasterKey);
            writeStore.Upsert(key, enc.Encrypt(value), isEncrypted: true, environment);
        }
        else
        {
            writeStore.Upsert(key, value, isEncrypted: false, environment);
        }

        return Results.Redirect(basePath + "/keys");
    }

    public static IResult DeleteKey(HttpContext ctx, IConfiguriteStore writeStore, string basePath)
    {
        var form = ctx.Request.Form;
        var key = form["key"].ToString();
        var envInput = form["env"].ToString();
        var environment = string.IsNullOrEmpty(envInput) ? null : envInput;

        if (!string.IsNullOrEmpty(key))
        {
            writeStore.Delete(key, environment);
        }

        return Results.Redirect(basePath + "/keys");
    }

    public static IResult AuditPage(HttpContext ctx, AdminUIOptions options, string basePath)
    {
        var lang = Localization.Resolve(ctx, options.DefaultLanguage);
        var store = new SqliteConfiguriteStore(options.DatabasePath);
        store.EnsureSchema();
        var audit = new SqliteConfiguriteAuditLog(options.DatabasePath);

        var entries = audit.ReadRecent(limit: 200);

        var body = new StringBuilder();
        body.Append("<h2>").Append(Enc.Encode(Localization.Get("AuditTitle", lang))).Append("</h2>");
        if (entries.Count == 0)
        {
            body.Append("<div class=\"notice\">").Append(Enc.Encode(Localization.Get("EmptyAudit", lang))).Append("</div>");
        }
        else
        {
            body.Append("<table><thead><tr>");
            body.Append("<th>").Append(Enc.Encode(Localization.Get("AuditWhen", lang))).Append("</th>");
            body.Append("<th>").Append(Enc.Encode(Localization.Get("AuditOp", lang))).Append("</th>");
            body.Append("<th>").Append(Enc.Encode(Localization.Get("ColKey", lang))).Append("</th>");
            body.Append("<th>").Append(Enc.Encode(Localization.Get("ColEnv", lang))).Append("</th>");
            body.Append("<th>").Append(Enc.Encode(Localization.Get("AuditUser", lang))).Append("</th>");
            body.Append("</tr></thead><tbody>");
            foreach (var e in entries)
            {
                body.Append("<tr>");
                body.Append("<td>").Append(Enc.Encode(e.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append("</td>");
                body.Append("<td>").Append(Enc.Encode(e.Operation)).Append("</td>");
                body.Append("<td>").Append(Enc.Encode(e.Key ?? "")).Append("</td>");
                body.Append("<td>");
                if (e.Environment is not null)
                    body.Append("<span class=\"pill env\">").Append(Enc.Encode(e.Environment)).Append("</span>");
                body.Append("</td>");
                body.Append("<td>").Append(Enc.Encode(e.User ?? "")).Append("</td>");
                body.Append("</tr>");
            }
            body.Append("</tbody></table>");
        }

        return Results.Content(HtmlLayout.Render("Audit", "/audit", lang, basePath, body.ToString()), "text/html; charset=utf-8");
    }

    private static void Stat(StringBuilder sb, string label, string value)
    {
        sb.Append("<div class=\"stat\"><div class=\"label\">").Append(Enc.Encode(label))
          .Append("</div><div class=\"value\">").Append(Enc.Encode(value)).Append("</div></div>");
    }

    // Helper: read every row across all environments (no filter), used for the dashboard / keys page.
    // Yardımcı: tüm ortamlardaki her satırı (filtresiz) oku — dashboard / anahtarlar sayfası için.
    private static List<RawEntry> ReadAllAcrossEnvironments(string dbPath)
    {
        var rows = new List<RawEntry>();
        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Key, Value, IsEncrypted, Environment FROM Configuration ORDER BY Environment, Key;";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new RawEntry(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt64(2) != 0,
                reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return rows;
    }

    private readonly record struct RawEntry(string Key, string Value, bool IsEncrypted, string? Environment);
}
