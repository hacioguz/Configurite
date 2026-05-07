using Microsoft.AspNetCore.Http;

namespace Configurite.AdminUI.Internal;

/// <summary>
/// EN: Tiny EN/TR string table. Resolves the active language from <c>?lang=…</c>, the
///     <c>configurite_lang</c> cookie, or <see cref="AdminUIOptions.DefaultLanguage"/>.
/// TR: Küçük EN/TR dizgi tablosu. Aktif dili <c>?lang=…</c>, <c>configurite_lang</c> çerezinden
///     veya <see cref="AdminUIOptions.DefaultLanguage"/>'den çözer.
/// </summary>
internal static class Localization
{
    private static readonly Dictionary<string, (string En, string Tr)> Strings = new(StringComparer.Ordinal)
    {
        ["AppTitle"] = ("Configurite Admin", "Configurite Yönetim"),
        ["NavDashboard"] = ("Dashboard", "Panel"),
        ["NavKeys"] = ("Keys", "Anahtarlar"),
        ["NavAudit"] = ("Audit log", "Denetim günlüğü"),
        ["LangToggle"] = ("Türkçe", "English"),
        ["DashTitle"] = ("Dashboard", "Panel"),
        ["StatRows"] = ("Total rows", "Toplam satır"),
        ["StatEncrypted"] = ("Encrypted", "Şifreli"),
        ["StatEnvironments"] = ("Environments", "Ortamlar"),
        ["StatSchema"] = ("Schema version", "Şema sürümü"),
        ["StatAuditCount"] = ("Audit entries", "Denetim girdileri"),
        ["KeysTitle"] = ("Keys", "Anahtarlar"),
        ["FilterEnv"] = ("Environment", "Ortam"),
        ["FilterAll"] = ("(all)", "(tümü)"),
        ["FilterGlobal"] = ("(global only)", "(yalnız global)"),
        ["ColKey"] = ("Key", "Anahtar"),
        ["ColValue"] = ("Value", "Değer"),
        ["ColEnv"] = ("Env", "Ortam"),
        ["ColEncrypted"] = ("Enc", "Şfr"),
        ["ColActions"] = ("Actions", "İşlemler"),
        ["ActionEdit"] = ("Edit", "Düzenle"),
        ["ActionDelete"] = ("Delete", "Sil"),
        ["ActionSave"] = ("Save", "Kaydet"),
        ["ActionCancel"] = ("Cancel", "İptal"),
        ["ActionAdd"] = ("Add new", "Yeni ekle"),
        ["ActionReveal"] = ("Show", "Göster"),
        ["EncryptedHidden"] = ("(encrypted)", "(şifreli)"),
        ["NewKeyTitle"] = ("New value", "Yeni değer"),
        ["EditKeyTitle"] = ("Edit value", "Değeri düzenle"),
        ["FieldEncrypt"] = ("Encrypt this value", "Bu değeri şifrele"),
        ["AuditTitle"] = ("Audit log", "Denetim günlüğü"),
        ["AuditOp"] = ("Operation", "İşlem"),
        ["AuditUser"] = ("User", "Kullanıcı"),
        ["AuditWhen"] = ("When (UTC)", "Zaman (UTC)"),
        ["EmptyAudit"] = ("No audit entries yet.", "Henüz denetim kaydı yok."),
        ["EmptyKeys"] = ("No matching keys.", "Eşleşen anahtar yok."),
        ["NoMasterKey"] = ("No master key available — encrypted values cannot be displayed.",
                               "Ana anahtar yok — şifreli değerler görüntülenemez."),
        ["DeleteConfirm"] = ("Delete this key? This cannot be undone.",
                               "Bu anahtar silinsin mi? Geri alınamaz."),
        ["LangCookie"] = ("configurite_lang", "configurite_lang"),
    };

    public static string Resolve(HttpContext ctx, string defaultLang)
    {
        if (ctx.Request.Query.TryGetValue("lang", out var q) && IsSupported(q!))
        {
            ctx.Response.Cookies.Append("configurite_lang", q!, new CookieOptions
            {
                HttpOnly = false,
                SameSite = SameSiteMode.Lax,
                Expires = DateTimeOffset.UtcNow.AddYears(1),
            });
            return q!;
        }

        if (ctx.Request.Cookies.TryGetValue("configurite_lang", out var c) && IsSupported(c))
        {
            return c;
        }

        return IsSupported(defaultLang) ? defaultLang : "en";
    }

    public static string Get(string key, string lang)
    {
        if (!Strings.TryGetValue(key, out var entry))
        {
            return key; // fail open: show the missing-key marker rather than crashing.
        }

        return string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase) ? entry.Tr : entry.En;
    }

    private static bool IsSupported(string? lang)
        => string.Equals(lang, "en", StringComparison.OrdinalIgnoreCase)
        || string.Equals(lang, "tr", StringComparison.OrdinalIgnoreCase);
}
