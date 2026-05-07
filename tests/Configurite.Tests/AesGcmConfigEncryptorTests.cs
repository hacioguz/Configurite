using System.Security.Cryptography;
using Configurite.Encryption;
using FluentAssertions;

namespace Configurite.Tests;

public sealed class AesGcmConfigEncryptorTests
{
    private static readonly byte[] Salt = AesGcmConfigEncryptor.GenerateSalt();
    private const string MasterKey = "correct-horse-battery-staple";

    [Fact]
    public void Roundtrip_ReturnsOriginalPlaintext()
    {
        using var enc = new AesGcmConfigEncryptor(MasterKey, Salt);

        var ciphertext = enc.Encrypt("Server=db;Pwd=s3cr3t!");
        var plaintext = enc.Decrypt(ciphertext);

        plaintext.Should().Be("Server=db;Pwd=s3cr3t!");
    }

    [Fact]
    public void Encrypt_SamePlaintext_ProducesDifferentCiphertext()
    {
        using var enc = new AesGcmConfigEncryptor(MasterKey, Salt);

        var a = enc.Encrypt("hello");
        var b = enc.Encrypt("hello");

        a.Should().NotBe(b, "each call uses a fresh random nonce");
    }

    [Fact]
    public void Decrypt_TamperedCiphertext_ThrowsCryptographicException()
    {
        using var enc = new AesGcmConfigEncryptor(MasterKey, Salt);
        var ciphertext = enc.Encrypt("important");

        var bytes = Convert.FromBase64String(ciphertext);
        bytes[^1] ^= 0xFF; // flip last byte (tag region)
        var tampered = Convert.ToBase64String(bytes);

        var act = () => enc.Decrypt(tampered);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithWrongMasterKey_ThrowsCryptographicException()
    {
        string ciphertext;
        using (var enc = new AesGcmConfigEncryptor("right-key", Salt))
        {
            ciphertext = enc.Encrypt("payload");
        }

        using var wrong = new AesGcmConfigEncryptor("wrong-key", Salt);
        var act = () => wrong.Decrypt(ciphertext);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithWrongSalt_ThrowsCryptographicException()
    {
        string ciphertext;
        using (var enc = new AesGcmConfigEncryptor(MasterKey, Salt))
        {
            ciphertext = enc.Encrypt("payload");
        }

        using var wrong = new AesGcmConfigEncryptor(MasterKey, AesGcmConfigEncryptor.GenerateSalt());
        var act = () => wrong.Decrypt(ciphertext);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Constructor_RejectsShortSalt()
    {
        var act = () => new AesGcmConfigEncryptor(MasterKey, new byte[8]);
        act.Should().Throw<ArgumentException>().WithParameterName("salt");
    }

    [Fact]
    public void Constructor_RejectsEmptyMasterKey()
    {
        var act = () => new AesGcmConfigEncryptor("", Salt);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Encrypt_AfterDispose_Throws()
    {
        var enc = new AesGcmConfigEncryptor(MasterKey, Salt);
        enc.Dispose();

        var act = () => enc.Encrypt("anything");
        act.Should().Throw<ObjectDisposedException>();
    }
}
