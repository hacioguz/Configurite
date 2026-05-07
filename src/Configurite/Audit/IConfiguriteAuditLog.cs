namespace Configurite.Audit;

/// <summary>
/// EN: Records mutating operations on a Configurite database (creates, updates, deletes,
///     rotations) for forensic and compliance purposes.
/// TR: Bir Configurite veritabanındaki değiştirici işlemleri (oluştur, güncelle, sil, rotasyon)
///     adli analiz ve uyumluluk amacıyla kaydeder.
/// </summary>
public interface IConfiguriteAuditLog
{
    /// <summary>
    /// EN: Appends a new audit entry with the current UTC timestamp.
    /// TR: Geçerli UTC zaman damgasıyla yeni bir denetim kaydı ekler.
    /// </summary>
    /// <param name="operation">
    /// EN: Operation type — typically <c>Upsert</c>, <c>Delete</c>, or <c>Rotate</c>.
    /// TR: İşlem türü — genellikle <c>Upsert</c>, <c>Delete</c> veya <c>Rotate</c>.
    /// </param>
    /// <param name="key">
    /// EN: Configuration key affected, or <see langword="null"/> for global operations like rotation.
    /// TR: Etkilenen yapılandırma anahtarı veya rotasyon gibi global işlemler için <see langword="null"/>.
    /// </param>
    /// <param name="environment">
    /// EN: Environment scope, or <see langword="null"/> when global.
    /// TR: Ortam kapsamı, global ise <see langword="null"/>.
    /// </param>
    /// <param name="user">
    /// EN: Identifier of the actor making the change (e.g. <c>HttpContext.User.Identity.Name</c>).
    /// TR: Değişikliği yapan aktörün tanımlayıcısı (örn. <c>HttpContext.User.Identity.Name</c>).
    /// </param>
    void Record(string operation, string? key, string? environment, string? user);

    /// <summary>
    /// EN: Returns recent entries in reverse chronological order (newest first).
    /// TR: En son girdileri ters kronolojik sırayla döner (en yeni önce).
    /// </summary>
    IReadOnlyList<AuditEntry> ReadRecent(int limit, int offset = 0);

    /// <summary>
    /// EN: Total number of entries in the audit log.
    /// TR: Denetim günlüğündeki toplam giriş sayısı.
    /// </summary>
    long Count();
}

/// <summary>
/// EN: A single audit log row.
/// TR: Tek bir denetim günlüğü satırı.
/// </summary>
public sealed record AuditEntry(
    long Id,
    string Operation,
    string? Key,
    string? Environment,
    string? User,
    DateTime TimestampUtc);
