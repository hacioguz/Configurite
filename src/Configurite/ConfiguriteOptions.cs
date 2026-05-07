using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Configurite;

/// <summary>
/// EN: Options for configuring the Configurite SQLite-backed configuration provider.
/// TR: Configurite SQLite tabanlı yapılandırma sağlayıcısı için seçenekler.
/// </summary>
public sealed class ConfiguriteOptions
{
    /// <summary>
    /// EN: Path to the SQLite database file (e.g. <c>appsettings.db</c>). Relative paths
    ///     resolve against <see cref="AppContext.BaseDirectory"/>.
    /// TR: SQLite veritabanı dosyasının yolu (örn. <c>appsettings.db</c>). Göreli yollar
    ///     <see cref="AppContext.BaseDirectory"/> baz alınarak çözülür.
    /// </summary>
    public string DatabasePath { get; set; } = "appsettings.db";

    /// <summary>
    /// EN: Optional environment name (e.g. <c>Development</c>, <c>Production</c>). When set,
    ///     only rows matching this environment plus rows with NULL environment are loaded.
    /// TR: İsteğe bağlı ortam adı (örn. <c>Development</c>, <c>Production</c>). Belirtilirse
    ///     yalnızca bu ortama ait veya NULL ortamlı satırlar yüklenir.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// EN: If <see langword="true"/>, the database file and schema are created automatically when missing.
    /// TR: <see langword="true"/> ise, veritabanı dosyası ve şeması yoksa otomatik oluşturulur.
    /// </summary>
    public bool CreateIfMissing { get; set; } = true;

    /// <summary>
    /// EN: Master key used to encrypt/decrypt values. Reserved for Phase 3.
    /// TR: Değerleri şifrelemek/çözmek için kullanılan ana anahtar. Phase 3 için ayrılmıştır.
    /// </summary>
    public string? MasterKey { get; set; }

    /// <summary>
    /// EN: Throw when the database file is missing instead of returning an empty configuration.
    /// TR: Veritabanı dosyası yoksa boş yapılandırma döndürmek yerine hata fırlat.
    /// </summary>
    public bool Optional { get; set; }

    /// <summary>
    /// EN: Reload configuration when the underlying database file changes. Reserved for Phase 4.
    /// TR: Veritabanı dosyası değiştiğinde yapılandırmayı yeniden yükle. Phase 4 için ayrılmıştır.
    /// </summary>
    public bool ReloadOnChange { get; set; }

    /// <summary>
    /// EN: Optional logger for diagnostic events (load, reload, decryption, watcher errors).
    ///     Defaults to <see cref="NullLogger.Instance"/> (no output) when null.
    /// TR: Tanılama olayları için isteğe bağlı logger (yükleme, yeniden yükleme, şifre çözme,
    ///     izleyici hataları). Null ise <see cref="NullLogger.Instance"/> (çıktı yok).
    /// </summary>
    public ILogger? Logger { get; set; }

    internal ILogger ResolveLogger() => Logger ?? NullLogger.Instance;

    /// <summary>
    /// EN: Resolves <see cref="DatabasePath"/> to an absolute path.
    /// TR: <see cref="DatabasePath"/> değerini mutlak yola dönüştürür.
    /// </summary>
    internal string ResolveDatabasePath()
    {
        if (Path.IsPathRooted(DatabasePath))
        {
            return DatabasePath;
        }

        return Path.Combine(AppContext.BaseDirectory, DatabasePath);
    }
}
