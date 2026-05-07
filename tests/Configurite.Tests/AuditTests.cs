using Configurite.Audit;
using Configurite.Storage;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Configurite.Tests;

public sealed class AuditTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"configurite-audit-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void Schema_CreatesAuditLogTable_OnEnsureSchema()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        IConfiguriteAuditLog audit = new SqliteConfiguriteAuditLog(_dbPath);
        audit.Count().Should().Be(0);
    }

    [Fact]
    public void Record_AppendsEntries_InReverseChronologicalOrder()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        IConfiguriteAuditLog audit = new SqliteConfiguriteAuditLog(_dbPath);
        audit.Record("Upsert", "AppName", null, "alice");
        audit.Record("Upsert", "Greeting", "Development", "bob");
        audit.Record("Delete", "Stale", null, "carol");

        audit.Count().Should().Be(3);

        var recent = audit.ReadRecent(limit: 10);
        recent.Should().HaveCount(3);
        recent[0].Operation.Should().Be("Delete");
        recent[0].Key.Should().Be("Stale");
        recent[0].User.Should().Be("carol");
        recent[2].Key.Should().Be("AppName");
    }

    [Fact]
    public void ReadRecent_RespectsLimitAndOffset()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        IConfiguriteAuditLog audit = new SqliteConfiguriteAuditLog(_dbPath);
        for (int i = 0; i < 5; i++)
        {
            audit.Record("Upsert", $"Key{i}", null, "user");
        }

        var page1 = audit.ReadRecent(limit: 2, offset: 0);
        page1.Should().HaveCount(2);
        page1[0].Key.Should().Be("Key4");
        page1[1].Key.Should().Be("Key3");

        var page2 = audit.ReadRecent(limit: 2, offset: 2);
        page2[0].Key.Should().Be("Key2");
        page2[1].Key.Should().Be("Key1");
    }

    [Fact]
    public void AuditingStore_RecordsUpsertsAndDeletes_NotReads()
    {
        IConfiguriteStore inner = new SqliteConfiguriteStore(_dbPath);
        inner.EnsureSchema();
        IConfiguriteAuditLog audit = new SqliteConfiguriteAuditLog(_dbPath);

        var decorated = new AuditingConfiguriteStore(inner, audit, () => "test-user");

        decorated.Upsert("AppName", "Demo", false, null);
        decorated.TryGet("AppName", null, out _);   // read — must NOT be audited
        decorated.ReadAll(null);                     // read — must NOT be audited
        decorated.Delete("AppName", null);

        var entries = audit.ReadRecent(10);
        entries.Should().HaveCount(2);
        entries[0].Operation.Should().Be("Delete");
        entries[0].User.Should().Be("test-user");
        entries[1].Operation.Should().Be("Upsert");
    }

    [Fact]
    public void AuditingStore_DeleteOfMissingKey_DoesNotRecord()
    {
        IConfiguriteStore inner = new SqliteConfiguriteStore(_dbPath);
        inner.EnsureSchema();
        IConfiguriteAuditLog audit = new SqliteConfiguriteAuditLog(_dbPath);

        var decorated = new AuditingConfiguriteStore(inner, audit);
        decorated.Delete("Nope", null).Should().Be(0);

        audit.Count().Should().Be(0);
    }

    [Fact]
    public void Schema_V1Database_UpgradesToV2_WithoutDataLoss()
    {
        // Simulate a v1 database (no AuditLog table) by creating the legacy schema by hand.
        // v1 veritabanını taklit et (AuditLog yok) — eski şemayı elle oluştur.
        using (var conn = new SqliteConnection($"Data Source={_dbPath}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Configuration (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Key TEXT NOT NULL, Value TEXT NOT NULL,
                    IsEncrypted INTEGER NOT NULL DEFAULT 0,
                    Environment TEXT,
                    CreatedUtc TEXT NOT NULL DEFAULT (datetime('now')),
                    UpdatedUtc TEXT NOT NULL DEFAULT (datetime('now')),
                    UNIQUE(Key, Environment));
                CREATE TABLE Metadata (Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
                INSERT INTO Configuration(Key, Value) VALUES ('AppName', 'Demo');
                INSERT INTO Metadata(Key, Value) VALUES ('SchemaVersion', '1');
                """;
            cmd.ExecuteNonQuery();
        }

        // Run EnsureSchema, which should add the AuditLog table and bump the version.
        // EnsureSchema'yı çalıştır; AuditLog'u eklemeli ve sürümü yükseltmeli.
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        store.TryGet("AppName", null, out var entry).Should().BeTrue();
        entry.Value.Should().Be("Demo");

        IConfiguriteAuditLog audit = new SqliteConfiguriteAuditLog(_dbPath);
        audit.Count().Should().Be(0);

        store.ReadMetadata("SchemaVersion").Should().Be("2");
    }
}
