using Configurite.Storage;

namespace Configurite.Audit;

/// <summary>
/// EN: Wraps an <see cref="IConfiguriteStore"/> so that every <see cref="IConfiguriteStore.Upsert"/>
///     and <see cref="IConfiguriteStore.Delete"/> call is recorded into an
///     <see cref="IConfiguriteAuditLog"/>. Reads pass through untouched.
/// TR: Bir <see cref="IConfiguriteStore"/>'u sarmalar; her <see cref="IConfiguriteStore.Upsert"/>
///     ve <see cref="IConfiguriteStore.Delete"/> çağrısı bir <see cref="IConfiguriteAuditLog"/>'a
///     kaydedilir. Okumalar dokunulmadan geçer.
/// </summary>
public sealed class AuditingConfiguriteStore : IConfiguriteStore
{
    private readonly IConfiguriteStore _inner;
    private readonly IConfiguriteAuditLog _audit;
    private readonly Func<string?> _userResolver;

    /// <summary>
    /// EN: Decorates <paramref name="inner"/> with audit logging. The optional
    ///     <paramref name="userResolver"/> is invoked on every mutation to identify the actor.
    /// TR: <paramref name="inner"/>'i denetim günlüğüyle sarar. İsteğe bağlı
    ///     <paramref name="userResolver"/>, aktörü tespit etmek için her mutasyonda çağrılır.
    /// </summary>
    public AuditingConfiguriteStore(
        IConfiguriteStore inner,
        IConfiguriteAuditLog audit,
        Func<string?>? userResolver = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(audit);

        _inner = inner;
        _audit = audit;
        _userResolver = userResolver ?? (() => null);
    }

    /// <inheritdoc />
    public void EnsureSchema() => _inner.EnsureSchema();

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ConfigEntry> ReadAll(string? environment) => _inner.ReadAll(environment);

    /// <inheritdoc />
    public bool TryGet(string key, string? environment, out ConfigEntry entry) => _inner.TryGet(key, environment, out entry);

    /// <inheritdoc />
    public void Upsert(string key, string value, bool isEncrypted, string? environment)
    {
        _inner.Upsert(key, value, isEncrypted, environment);
        _audit.Record("Upsert", key, environment, _userResolver());
    }

    /// <inheritdoc />
    public int Delete(string key, string? environment)
    {
        var deleted = _inner.Delete(key, environment);
        if (deleted > 0)
        {
            _audit.Record("Delete", key, environment, _userResolver());
        }
        return deleted;
    }

    /// <inheritdoc />
    public string? ReadMetadata(string key) => _inner.ReadMetadata(key);

    /// <inheritdoc />
    public void WriteMetadata(string key, string value) => _inner.WriteMetadata(key, value);
}
