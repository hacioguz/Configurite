using Configurite.Storage;

namespace Configurite.Encryption;

/// <summary>
/// EN: Reads and writes encryption-related metadata (per-database salt) into the Configurite store.
/// TR: Şifreleme ile ilgili meta verileri (veritabanı başına salt) Configurite store'una okur/yazar.
/// </summary>
internal static class EncryptionMetadata
{
    internal const string SaltMetadataKey = "EncryptionSalt";
    internal const string AlgorithmMetadataKey = "EncryptionAlgorithm";
    internal const string AlgorithmName = "AES-256-GCM/PBKDF2-SHA256";

    /// <summary>
    /// EN: Returns the existing salt if present; otherwise generates and persists a new one.
    /// TR: Mevcut salt varsa döndürür; yoksa yeni bir salt üretir ve kaydeder.
    /// </summary>
    public static byte[] GetOrCreateSalt(IConfiguriteStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        var existing = store.ReadMetadata(SaltMetadataKey);
        if (!string.IsNullOrEmpty(existing))
        {
            return Convert.FromBase64String(existing);
        }

        var salt = AesGcmConfigEncryptor.GenerateSalt();
        store.WriteMetadata(SaltMetadataKey, Convert.ToBase64String(salt));
        store.WriteMetadata(AlgorithmMetadataKey, AlgorithmName);
        return salt;
    }
}
