using BenchmarkDotNet.Attributes;
using Configurite;
using Configurite.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Configurite.Benchmarks;

/// <summary>
/// EN: Measures end-to-end provider loading: how long does AddConfigurite + Build + Load take
///     against databases of various sizes, with and without encrypted rows mixed in.
/// TR: Uçtan uca provider yükleme ölçümü: AddConfigurite + Build + Load çeşitli boyutlardaki
///     veritabanlarına karşı, şifreli satırlar karıştırılmış / karıştırılmamış olarak ne kadar sürüyor.
/// </summary>
[MemoryDiagnoser]
public class ProviderBenchmarks
{
    private string _plainDbPath = null!;
    private string _encryptedDbPath = null!;

    [Params(10, 100, 1000)]
    public int RowCount;

    [GlobalSetup]
    public void Setup()
    {
        _plainDbPath = Path.Combine(Path.GetTempPath(), $"configurite-prov-plain-{Guid.NewGuid():N}.db");
        _encryptedDbPath = Path.Combine(Path.GetTempPath(), $"configurite-prov-enc-{Guid.NewGuid():N}.db");

        var plain = new SqliteConfiguriteStore(_plainDbPath);
        plain.EnsureSchema();
        for (int i = 0; i < RowCount; i++)
        {
            plain.Upsert($"Section:Key{i}", $"value-{i}", isEncrypted: false, environment: null);
        }

        var encrypted = new SqliteConfiguriteStore(_encryptedDbPath);
        encrypted.EnsureSchema();
        using var enc = Configurite.Encryption.ConfiguriteEncryption.CreateEncryptor(encrypted, "bench-key");
        for (int i = 0; i < RowCount; i++)
        {
            // 50% encrypted, 50% plain — mirrors a realistic config.
            // %50 şifreli, %50 düz — gerçekçi bir yapılandırmayı yansıtır.
            if (i % 2 == 0)
            {
                encrypted.Upsert($"Section:Plain{i}", $"value-{i}", false, null);
            }
            else
            {
                encrypted.Upsert($"Section:Secret{i}", enc.Encrypt($"secret-{i}"), true, null);
            }
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in new[] { _plainDbPath, _encryptedDbPath })
        {
            if (File.Exists(p)) File.Delete(p);
        }
    }

    [Benchmark(Description = "Provider load — plain values only")]
    public IConfigurationRoot Load_PlainOnly()
    {
        var builder = new ConfigurationBuilder().AddConfigurite(_plainDbPath);
        return builder.Build();
    }

    [Benchmark(Description = "Provider load — 50% encrypted")]
    public IConfigurationRoot Load_HalfEncrypted()
    {
        var builder = new ConfigurationBuilder().AddConfigurite(opts =>
        {
            opts.DatabasePath = _encryptedDbPath;
            opts.MasterKey = "bench-key";
        });
        return builder.Build();
    }
}
