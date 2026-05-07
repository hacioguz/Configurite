using BenchmarkDotNet.Attributes;
using Configurite.Storage;
using Microsoft.Data.Sqlite;

namespace Configurite.Benchmarks;

/// <summary>
/// EN: Measures core SqliteConfiguriteStore operations — Upsert, ReadAll, TryGet — across
///     varying database sizes.
/// TR: Çekirdek SqliteConfiguriteStore işlemlerini (Upsert, ReadAll, TryGet) farklı veritabanı
///     boyutları boyunca ölçer.
/// </summary>
[MemoryDiagnoser]
public class StoreBenchmarks
{
    private string _dbPath = null!;
    private SqliteConfiguriteStore _store = null!;

    [Params(10, 100, 1000)]
    public int RowCount;

    [GlobalSetup]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"configurite-bench-{Guid.NewGuid():N}.db");
        _store = new SqliteConfiguriteStore(_dbPath);
        _store.EnsureSchema();
        for (int i = 0; i < RowCount; i++)
        {
            _store.Upsert($"Section:Key{i}", $"value-{i}", isEncrypted: false, environment: null);
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [Benchmark]
    public IReadOnlyDictionary<string, ConfigEntry> ReadAll()
        => _store.ReadAll(environment: null);

    [Benchmark]
    public bool TryGet_ExistingKey()
    {
        return _store.TryGet($"Section:Key{RowCount / 2}", null, out _);
    }

    [Benchmark]
    public void Upsert_NewKey()
    {
        // EN: Note: this leaves a row behind per iteration; with [MemoryDiagnoser] iteration count
        //     is bounded, so the database grows by hundreds of rows during the run — fine for timing.
        // TR: Not: Her iterasyonda bir satır bırakır; [MemoryDiagnoser] iterasyonu sınırlı olduğundan
        //     veritabanı yüzlerce satır büyür — zaman ölçümü için sorun yok.
        var key = $"Bench:Insert{Guid.NewGuid():N}";
        _store.Upsert(key, "value", isEncrypted: false, environment: null);
    }
}
