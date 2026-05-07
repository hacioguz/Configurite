namespace Configurite.Migration;

/// <summary>
/// EN: Options that govern a JSON → SQLite migration.
/// TR: JSON → SQLite geçişini yöneten seçenekler.
/// </summary>
public sealed class MigrationOptions
{
    /// <summary>
    /// EN: Glob-style key patterns whose values are stored encrypted (AES-256-GCM).
    ///     Use <c>*</c> for any number of characters within a key segment, e.g.
    ///     <c>ConnectionStrings:*</c>, <c>*:Password</c>, <c>Auth:*Secret</c>.
    /// TR: Değerlerinin şifreli (AES-256-GCM) saklanacağı glob-stili anahtar desenleri.
    ///     Bir anahtar segmenti içinde herhangi sayıda karakter için <c>*</c> kullanın, örn.
    ///     <c>ConnectionStrings:*</c>, <c>*:Password</c>, <c>Auth:*Secret</c>.
    /// </summary>
    public IList<string> EncryptKeyPatterns { get; } = new List<string>();

    /// <summary>
    /// EN: When <see langword="true"/> (default), existing rows with the same key/environment
    ///     are overwritten. When <see langword="false"/>, existing rows are preserved.
    /// TR: <see langword="true"/> (varsayılan) ise aynı anahtar/ortam ile mevcut satırlar
    ///     üzerine yazılır. <see langword="false"/> ise mevcut satırlar korunur.
    /// </summary>
    public bool Overwrite { get; set; } = true;

    /// <summary>
    /// EN: Override the environment name applied to migrated rows. When <see langword="null"/>,
    ///     the migrator infers the environment from the file name (<c>appsettings.{Env}.json</c>).
    /// TR: Geçirilen satırlara uygulanacak ortam adını geçersiz kılar. <see langword="null"/> ise
    ///     migrator dosya adından (<c>appsettings.{Env}.json</c>) ortamı çıkarır.
    /// </summary>
    public string? EnvironmentOverride { get; set; }
}

/// <summary>
/// EN: Summary of a migration run.
/// TR: Bir geçiş işleminin özeti.
/// </summary>
/// <param name="FilesProcessed">
/// EN: Total number of JSON files migrated.
/// TR: Geçirilen toplam JSON dosyası sayısı.
/// </param>
/// <param name="KeysWritten">
/// EN: Total number of key/value rows written to the SQLite database.
/// TR: SQLite veritabanına yazılan toplam anahtar/değer satırı sayısı.
/// </param>
/// <param name="KeysEncrypted">
/// EN: Subset of <see cref="KeysWritten"/> that were encrypted.
/// TR: <see cref="KeysWritten"/> içinde şifrelenmiş olanların sayısı.
/// </param>
/// <param name="KeysSkipped">
/// EN: Number of keys skipped because <see cref="MigrationOptions.Overwrite"/> was disabled.
/// TR: <see cref="MigrationOptions.Overwrite"/> kapalı olduğu için atlanan anahtar sayısı.
/// </param>
public sealed record MigrationResult(int FilesProcessed, int KeysWritten, int KeysEncrypted, int KeysSkipped);
