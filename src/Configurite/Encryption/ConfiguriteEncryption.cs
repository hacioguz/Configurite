using Configurite.Storage;

namespace Configurite.Encryption;

/// <summary>
/// EN: High-level helpers that wire <see cref="IConfiguriteStore"/> together with the AES-GCM
///     encryptor. Use these when building admin tools, custom CLIs, or migration scripts.
/// TR: <see cref="IConfiguriteStore"/> ile AES-GCM şifreleyiciyi birleştiren üst seviye yardımcılar.
///     Yönetim araçları, özel CLI'lar veya geçiş script'leri yazarken kullanın.
/// </summary>
public static class ConfiguriteEncryption
{
    /// <summary>
    /// EN: Resolves a master key (explicit, env var, or key file) and returns an
    ///     <see cref="AesGcmConfigEncryptor"/> bound to <paramref name="store"/>'s salt
    ///     (a fresh salt is created if one does not exist yet). Caller owns the returned
    ///     encryptor and is responsible for disposing it.
    /// TR: Bir ana anahtar çözer (açık, env var veya anahtar dosyası) ve <paramref name="store"/>'un
    ///     saltına bağlı bir <see cref="AesGcmConfigEncryptor"/> döndürür (salt yoksa yenisi
    ///     üretilir). Çağıran encryptor'a sahiptir ve dispose etmekten sorumludur.
    /// </summary>
    public static AesGcmConfigEncryptor CreateEncryptor(IConfiguriteStore store, string? explicitMasterKey = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        var masterKey = MasterKeyResolver.Require(explicitMasterKey);
        var salt = EncryptionMetadata.GetOrCreateSalt(store);
        return new AesGcmConfigEncryptor(masterKey, salt);
    }

    /// <summary>
    /// EN: Like <see cref="CreateEncryptor"/> but returns <see langword="null"/> instead of
    ///     throwing when no master key is available.
    /// TR: <see cref="CreateEncryptor"/> gibi ama ana anahtar yoksa hata fırlatmak yerine
    ///     <see langword="null"/> döner.
    /// </summary>
    public static AesGcmConfigEncryptor? TryCreateEncryptor(IConfiguriteStore store, string? explicitMasterKey = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        var masterKey = MasterKeyResolver.Resolve(explicitMasterKey);
        if (string.IsNullOrEmpty(masterKey))
        {
            return null;
        }

        var salt = EncryptionMetadata.GetOrCreateSalt(store);
        return new AesGcmConfigEncryptor(masterKey, salt);
    }
}
