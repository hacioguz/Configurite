using System.Security.Cryptography;
using System.Text;

namespace Configurite.Encryption;

/// <summary>
/// EN: AES-256-GCM authenticated encryption with PBKDF2-derived data key.
///     Each ciphertext payload carries its own 12-byte nonce, so the same plaintext
///     never produces the same output twice.
/// TR: PBKDF2 ile türetilmiş veri anahtarı kullanan AES-256-GCM kimlik doğrulamalı şifreleme.
///     Her şifreli yük kendi 12 byte'lık nonce değerini taşır; aynı düz metin asla aynı çıktıyı vermez.
/// </summary>
/// <remarks>
/// EN: Payload layout (base64-encoded): <c>nonce(12) ‖ ciphertext(n) ‖ tag(16)</c>.
/// TR: Yük düzeni (base64): <c>nonce(12) ‖ şifreli metin(n) ‖ tag(16)</c>.
/// </remarks>
public sealed class AesGcmConfigEncryptor : IConfigEncryptor, IDisposable
{
    private const int NonceSizeBytes = 12;   // GCM standard
    private const int TagSizeBytes = 16;     // GCM standard
    private const int KeySizeBytes = 32;     // AES-256
    private const int Pbkdf2Iterations = 200_000;

    private readonly byte[] _dataKey;
    private bool _disposed;

    /// <summary>
    /// EN: Derives an AES-256 data key from <paramref name="masterKey"/> and <paramref name="salt"/>
    ///     using PBKDF2 (HMAC-SHA-256, 200_000 iterations).
    /// TR: <paramref name="masterKey"/> ve <paramref name="salt"/> kullanarak PBKDF2
    ///     (HMAC-SHA-256, 200.000 iterasyon) ile bir AES-256 veri anahtarı türetir.
    /// </summary>
    /// <param name="masterKey">
    /// EN: User-supplied master key (e.g. environment variable, key file, vault secret).
    /// TR: Kullanıcı tarafından sağlanan ana anahtar (ortam değişkeni, anahtar dosyası, vault gizli değeri).
    /// </param>
    /// <param name="salt">
    /// EN: Per-database random salt (must be at least 16 bytes). Stored unencrypted in <c>Metadata</c>.
    /// TR: Veritabanı başına rastgele salt (en az 16 byte). <c>Metadata</c> tablosunda şifresiz saklanır.
    /// </param>
    public AesGcmConfigEncryptor(string masterKey, byte[] salt)
    {
        ArgumentException.ThrowIfNullOrEmpty(masterKey);
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length < 16)
        {
            throw new ArgumentException("Salt must be at least 16 bytes.", nameof(salt));
        }

        _dataKey = Rfc2898DeriveBytes.Pbkdf2(
            password: Encoding.UTF8.GetBytes(masterKey),
            salt: salt,
            iterations: Pbkdf2Iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: KeySizeBytes);
    }

    /// <inheritdoc />
    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var nonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSizeBytes];

        using var aes = new AesGcm(_dataKey, TagSizeBytes);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var payload = new byte[NonceSizeBytes + cipher.Length + TagSizeBytes];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSizeBytes);
        Buffer.BlockCopy(cipher, 0, payload, NonceSizeBytes, cipher.Length);
        Buffer.BlockCopy(tag, 0, payload, NonceSizeBytes + cipher.Length, TagSizeBytes);

        return Convert.ToBase64String(payload);
    }

    /// <inheritdoc />
    public string Decrypt(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrEmpty(ciphertext);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var payload = Convert.FromBase64String(ciphertext);
        if (payload.Length < NonceSizeBytes + TagSizeBytes)
        {
            throw new CryptographicException("Configurite ciphertext payload is shorter than the minimum length.");
        }

        var cipherLen = payload.Length - NonceSizeBytes - TagSizeBytes;
        var nonce = new byte[NonceSizeBytes];
        var cipher = new byte[cipherLen];
        var tag = new byte[TagSizeBytes];

        Buffer.BlockCopy(payload, 0, nonce, 0, NonceSizeBytes);
        Buffer.BlockCopy(payload, NonceSizeBytes, cipher, 0, cipherLen);
        Buffer.BlockCopy(payload, NonceSizeBytes + cipherLen, tag, 0, TagSizeBytes);

        var plainBytes = new byte[cipherLen];
        using var aes = new AesGcm(_dataKey, TagSizeBytes);
        aes.Decrypt(nonce, cipher, tag, plainBytes);

        return Encoding.UTF8.GetString(plainBytes);
    }

    /// <summary>
    /// EN: Generates a fresh 32-byte salt suitable for <see cref="AesGcmConfigEncryptor"/>.
    /// TR: <see cref="AesGcmConfigEncryptor"/> için uygun 32 byte'lık taze bir salt üretir.
    /// </summary>
    public static byte[] GenerateSalt() => RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// EN: Zero-fills the derived data key when no longer needed.
    /// TR: Türetilmiş veri anahtarını artık ihtiyaç kalmadığında sıfırlarla doldurur.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CryptographicOperations.ZeroMemory(_dataKey);
        _disposed = true;
    }
}
