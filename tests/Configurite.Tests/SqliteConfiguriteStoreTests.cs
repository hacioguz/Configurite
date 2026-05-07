using Configurite.Storage;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Configurite.Tests;

public sealed class SqliteConfiguriteStoreTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"configurite-store-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void IConfiguriteStore_IsImplementedBySqliteConfiguriteStore()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Should().BeAssignableTo<IConfiguriteStore>();
    }

    [Fact]
    public void TryGet_ReturnsFalseWhenMissing_TrueWhenPresent()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        store.TryGet("Missing", null, out _).Should().BeFalse();

        store.Upsert("AppName", "Demo", isEncrypted: false, environment: null);
        store.TryGet("AppName", null, out var entry).Should().BeTrue();
        entry.Value.Should().Be("Demo");
        entry.IsEncrypted.Should().BeFalse();
        entry.Environment.Should().BeNull();
    }

    [Fact]
    public void TryGet_RespectsEnvironmentScope()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("Greeting", "global", isEncrypted: false, environment: null);
        store.Upsert("Greeting", "dev", isEncrypted: false, environment: "Development");

        store.TryGet("Greeting", "Development", out var dev).Should().BeTrue();
        dev.Value.Should().Be("dev");

        store.TryGet("Greeting", null, out var global).Should().BeTrue();
        global.Value.Should().Be("global");

        store.TryGet("Greeting", "Production", out _).Should().BeFalse(
            "TryGet only matches the exact environment, never falls back to global");
    }

    [Fact]
    public void Delete_RemovesOnlyTheRequestedRow()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("A", "1", false, null);
        store.Upsert("B", "2", false, null);
        store.Upsert("A", "1-dev", false, "Development");

        store.Delete("A", null).Should().Be(1);

        store.TryGet("A", null, out _).Should().BeFalse();
        store.TryGet("A", "Development", out _).Should().BeTrue("env-specific row must survive");
        store.TryGet("B", null, out _).Should().BeTrue();
    }

    [Fact]
    public void Metadata_RoundTripsArbitraryStrings()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        store.ReadMetadata("CustomThing").Should().BeNull();
        store.WriteMetadata("CustomThing", "value-1");
        store.ReadMetadata("CustomThing").Should().Be("value-1");

        store.WriteMetadata("CustomThing", "value-2");
        store.ReadMetadata("CustomThing").Should().Be("value-2");
    }

    [Fact]
    public void ReadAll_GlobalsAndEnvironment_MergeWithEnvWinning()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("Shared", "global", false, null);
        store.Upsert("OnlyGlobal", "g", false, null);
        store.Upsert("Shared", "dev", false, "Development");
        store.Upsert("OnlyDev", "d", false, "Development");

        var view = store.ReadAll("Development");
        view["Shared"].Value.Should().Be("dev");
        view["OnlyGlobal"].Value.Should().Be("g");
        view["OnlyDev"].Value.Should().Be("d");
        view.Should().HaveCount(3);
    }

    [Fact]
    public void Upsert_EmptyKey_Throws()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        store.Invoking(s => s.Upsert("", "x", false, null)).Should().Throw<ArgumentException>();
    }
}
