namespace Configurite.AdminUI;

/// <summary>
/// EN: Options for the Configurite Admin UI.
/// TR: Configurite Yönetim Arayüzü için seçenekler.
/// </summary>
public sealed class AdminUIOptions
{
    /// <summary>
    /// EN: Path to the SQLite database the UI will manage.
    /// TR: Arayüzün yöneteceği SQLite veritabanının yolu.
    /// </summary>
    public string DatabasePath { get; set; } = "appsettings.db";

    /// <summary>
    /// EN: Master key passed through to encryption-aware operations. Falls back to the standard
    ///     resolver chain (env var, key file) when null.
    /// TR: Şifreleme bilen işlemlere geçirilen ana anahtar. Null ise standart resolver zincirine
    ///     (env var, anahtar dosyası) düşer.
    /// </summary>
    public string? MasterKey { get; set; }

    /// <summary>
    /// EN: Default UI language when neither query string nor cookie selects one.
    ///     Supported values: <c>"en"</c>, <c>"tr"</c>.
    /// TR: Sorgu dizesi ve çerez bir dil seçmediğinde varsayılan arayüz dili.
    ///     Desteklenen değerler: <c>"en"</c>, <c>"tr"</c>.
    /// </summary>
    public string DefaultLanguage { get; set; } = "en";

    /// <summary>
    /// EN: When true (default), every mutation through the UI is recorded into the audit log.
    /// TR: True (varsayılan) ise, arayüz üzerinden yapılan her mutasyon denetim günlüğüne kaydedilir.
    /// </summary>
    public bool EnableAuditLog { get; set; } = true;
}
