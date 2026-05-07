namespace Configurite.Encryption;

/// <summary>
/// EN: Encrypts and decrypts configuration values. Implementations are expected to be
///     thread-safe so a single instance can serve concurrent reads.
/// TR: Yapılandırma değerlerini şifreler ve çözer. Uygulamaların thread-safe olması beklenir;
///     tek bir örnek eşzamanlı okumalara hizmet edebilmelidir.
/// </summary>
public interface IConfigEncryptor
{
    /// <summary>
    /// EN: Encrypts <paramref name="plaintext"/> and returns a self-contained, base64-encoded payload
    ///     (nonce ‖ ciphertext ‖ tag) that can be stored as a single string column.
    /// TR: <paramref name="plaintext"/> değerini şifreler ve tek bir string sütuna saklanabilen,
    ///     kendi kendine yeten base64 yükü (nonce ‖ şifreli metin ‖ tag) döndürür.
    /// </summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// EN: Decrypts a payload produced by <see cref="Encrypt"/>. Throws if the payload was tampered
    ///     with or the wrong master key is configured.
    /// TR: <see cref="Encrypt"/> tarafından üretilen yükü çözer. Yük değiştirilmişse veya yanlış
    ///     ana anahtar yapılandırılmışsa hata fırlatır.
    /// </summary>
    string Decrypt(string ciphertext);
}
