using BenchmarkDotNet.Attributes;
using Configurite.Encryption;

namespace Configurite.Benchmarks;

/// <summary>
/// EN: Measures the cost of a single AES-256-GCM encrypt/decrypt round-trip and PBKDF2
///     key derivation. Provides a baseline for capacity planning.
/// TR: Tek bir AES-256-GCM şifrele/çöz roundtrip'i ve PBKDF2 anahtar türetiminin maliyetini ölçer.
///     Kapasite planlaması için bir baseline sağlar.
/// </summary>
[MemoryDiagnoser]
public class EncryptionBenchmarks
{
    private AesGcmConfigEncryptor _enc = null!;
    private string _ciphertext = null!;
    private byte[] _salt = null!;

    [Params(16, 256, 4096)]
    public int PayloadSizeBytes;

    private string _payload = null!;

    [GlobalSetup]
    public void Setup()
    {
        _salt = AesGcmConfigEncryptor.GenerateSalt();
        _enc = new AesGcmConfigEncryptor("benchmark-master-key", _salt);
        _payload = new string('x', PayloadSizeBytes);
        _ciphertext = _enc.Encrypt(_payload);
    }

    [GlobalCleanup]
    public void Cleanup() => _enc.Dispose();

    [Benchmark]
    public string Encrypt() => _enc.Encrypt(_payload);

    [Benchmark]
    public string Decrypt() => _enc.Decrypt(_ciphertext);

    [Benchmark(Description = "PBKDF2 key derivation (200K iter)")]
    public AesGcmConfigEncryptor DeriveKey()
    {
        // EN: Construction includes the PBKDF2 cost — this is the one-time price per process.
        // TR: Constructor PBKDF2 maliyetini içerir — bu süreç başına tek seferlik bedeldir.
        var enc = new AesGcmConfigEncryptor("benchmark-master-key", _salt);
        enc.Dispose();
        return enc;
    }
}
