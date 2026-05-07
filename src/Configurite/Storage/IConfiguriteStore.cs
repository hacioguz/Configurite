namespace Configurite.Storage;

/// <summary>
/// EN: Public read/write API over a Configurite SQLite database. Implementations are stateless
///     between calls — every operation acquires its own connection so the caller controls
///     lifetime. Implementations must be safe to share across threads.
/// TR: Bir Configurite SQLite veritabanı üzerinde public okuma/yazma API'si. Uygulamalar çağrılar
///     arasında stateless'tır — her operasyon kendi bağlantısını alır; yaşam süresini çağıran
///     yönetir. Uygulamalar thread'ler arasında paylaşılabilir olmalıdır.
/// </summary>
public interface IConfiguriteStore
{
    /// <summary>
    /// EN: Ensures the Configurite schema exists on the underlying database (idempotent).
    /// TR: Configurite şemasının alttaki veritabanında bulunduğundan emin olur (idempotent).
    /// </summary>
    void EnsureSchema();

    /// <summary>
    /// EN: Reads all configuration entries applicable to <paramref name="environment"/>.
    ///     Rows with NULL environment are global; environment-specific rows override globals.
    /// TR: <paramref name="environment"/> için geçerli tüm yapılandırma satırlarını okur.
    ///     NULL ortamlı satırlar globaldir; ortama özgü satırlar globalleri ezer.
    /// </summary>
    IReadOnlyDictionary<string, ConfigEntry> ReadAll(string? environment);

    /// <summary>
    /// EN: Looks up a single row scoped to <paramref name="environment"/> (or global when null).
    ///     Returns <see langword="false"/> when no matching row exists.
    /// TR: <paramref name="environment"/> kapsamındaki tek bir satırı arar (null ise global).
    ///     Eşleşen satır yoksa <see langword="false"/> döner.
    /// </summary>
    bool TryGet(string key, string? environment, out ConfigEntry entry);

    /// <summary>
    /// EN: Inserts or updates a row. Encryption is the caller's responsibility — the value is
    ///     stored as-is. Set <paramref name="isEncrypted"/> so readers know to decrypt it.
    /// TR: Bir satır ekler veya günceller. Şifreleme çağıranın sorumluluğundadır — değer olduğu
    ///     gibi saklanır. <paramref name="isEncrypted"/>'i belirleyin ki okuyucular çözmesi gerektiğini bilsin.
    /// </summary>
    void Upsert(string key, string value, bool isEncrypted, string? environment);

    /// <summary>
    /// EN: Removes a single row. Returns the number of rows deleted (0 or 1).
    /// TR: Tek bir satırı kaldırır. Silinen satır sayısını döner (0 veya 1).
    /// </summary>
    int Delete(string key, string? environment);

    /// <summary>
    /// EN: Reads a single Metadata row, or <see langword="null"/> when absent.
    ///     Use this for known keys: <c>SchemaVersion</c>, <c>EncryptionSalt</c>, <c>EncryptionAlgorithm</c>.
    /// TR: Tek bir Metadata satırını okur; yoksa <see langword="null"/> döner.
    ///     Bilinen anahtarlar için kullanın: <c>SchemaVersion</c>, <c>EncryptionSalt</c>, <c>EncryptionAlgorithm</c>.
    /// </summary>
    string? ReadMetadata(string key);

    /// <summary>
    /// EN: Inserts or updates a Metadata row. Reserved for migration / rotation tools.
    /// TR: Bir Metadata satırı ekler veya günceller. Geçiş / rotasyon araçlarına ayrılmıştır.
    /// </summary>
    void WriteMetadata(string key, string value);
}

/// <summary>
/// EN: A single row read from the <c>Configuration</c> table.
/// TR: <c>Configuration</c> tablosundan okunan tek bir satır.
/// </summary>
/// <param name="Value">
/// EN: Stored value — plaintext when <see cref="IsEncrypted"/> is false, otherwise the
///     base64-encoded ciphertext payload (nonce ‖ ciphertext ‖ tag).
/// TR: Saklanan değer — <see cref="IsEncrypted"/> false ise düz metin, true ise
///     base64 şifreli yük (nonce ‖ ciphertext ‖ tag).
/// </param>
/// <param name="IsEncrypted">
/// EN: Indicates whether <see cref="Value"/> is encrypted.
/// TR: <see cref="Value"/> alanının şifrelenmiş olup olmadığını gösterir.
/// </param>
/// <param name="Environment">
/// EN: Environment scope of the row, or <see langword="null"/> for globals.
/// TR: Satırın ortam kapsamı veya globaller için <see langword="null"/>.
/// </param>
public readonly record struct ConfigEntry(string Value, bool IsEncrypted, string? Environment);
