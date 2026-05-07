using System.Text.Json;
using Configurite.Migration;
using Configurite.Storage;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Configurite.Tests;

public sealed class SqliteToJsonExporterTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;

    public SqliteToJsonExporterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"configurite-export-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "appsettings.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    private void SeedPlain()
    {
        var s = new SqliteConfiguriteStore(_dbPath);
        s.EnsureSchema();
        s.Upsert("AppName", "Demo", false, null);
        s.Upsert("Logging:LogLevel:Default", "Information", false, null);
        s.Upsert("Logging:LogLevel:Microsoft", "Warning", false, null);
        s.Upsert("Greeting", "dev", false, "Development");
    }

    [Fact]
    public void ExportToFile_Globals_BuildsHierarchicalJson()
    {
        SeedPlain();
        var outPath = Path.Combine(_dir, "out.json");

        using var exporter = new SqliteToJsonExporter(_dbPath);
        var result = exporter.ExportToFile(outPath);

        result.Environment.Should().BeNull();
        File.Exists(outPath).Should().BeTrue();

        var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        doc.RootElement.GetProperty("AppName").GetString().Should().Be("Demo");
        doc.RootElement
            .GetProperty("Logging").GetProperty("LogLevel").GetProperty("Default")
            .GetString().Should().Be("Information");
    }

    [Fact]
    public void ExportToFile_EnvironmentScope_WritesOnlyEnvironmentRows()
    {
        SeedPlain();
        var outPath = Path.Combine(_dir, "dev.json");

        using var exporter = new SqliteToJsonExporter(_dbPath);
        exporter.ExportToFile(outPath, environment: "Development");

        var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        doc.RootElement.TryGetProperty("AppName", out _).Should().BeFalse("globals are excluded when env is specified");
        doc.RootElement.GetProperty("Greeting").GetString().Should().Be("dev");
    }

    [Fact]
    public void ExportPerEnvironment_EmitsOneFilePerEnvironment_PlusGlobal()
    {
        SeedPlain();
        var outDir = Path.Combine(_dir, "exported");

        using var exporter = new SqliteToJsonExporter(_dbPath);
        var results = exporter.ExportPerEnvironment(outDir);

        File.Exists(Path.Combine(outDir, "appsettings.json")).Should().BeTrue();
        File.Exists(Path.Combine(outDir, "appsettings.Development.json")).Should().BeTrue();
        results.Should().HaveCount(2);
        results.Should().Contain(r => r.Environment == null);
        results.Should().Contain(r => r.Environment == "Development");
    }

    [Fact]
    public void Export_EncryptedDefault_SkipsEncryptedRows()
    {
        var s = new SqliteConfiguriteStore(_dbPath);
        s.EnsureSchema();
        s.Upsert("AppName", "Demo", false, null);
        s.Upsert("Secret", "fake-ciphertext", true, null);

        var outPath = Path.Combine(_dir, "out.json");
        using var exporter = new SqliteToJsonExporter(_dbPath);
        exporter.ExportToFile(outPath);

        var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        doc.RootElement.GetProperty("AppName").GetString().Should().Be("Demo");
        doc.RootElement.TryGetProperty("Secret", out _).Should().BeFalse();
    }

    [Fact]
    public void Export_IncludeEncrypted_ReplacesValueWithPlaceholder()
    {
        var s = new SqliteConfiguriteStore(_dbPath);
        s.EnsureSchema();
        s.Upsert("Secret", "fake-ciphertext", true, null);

        var outPath = Path.Combine(_dir, "out.json");
        using var exporter = new SqliteToJsonExporter(_dbPath);
        exporter.ExportToFile(outPath, new ExportOptions { IncludeEncrypted = true });

        var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        doc.RootElement.GetProperty("Secret").GetString().Should().Be("(encrypted)");
    }

    [Fact]
    public void Export_DecryptWithMasterKey_RoundtripsThroughMigrator()
    {
        // Build an encrypted DB by going through the migrator with encrypt patterns.
        var jsonIn = Path.Combine(_dir, "appsettings.json");
        File.WriteAllText(jsonIn, """{ "AppName": "Demo", "Secret": "shh" }""");

        using (var migrator = new JsonToSqliteMigrator(_dbPath, masterKey: "k"))
        {
            migrator.MigrateFile(jsonIn, new MigrationOptions { EncryptKeyPatterns = { "Secret" } });
        }

        var outPath = Path.Combine(_dir, "out.json");
        using var exporter = new SqliteToJsonExporter(_dbPath);
        exporter.ExportToFile(outPath, new ExportOptions { DecryptWithMasterKey = true, MasterKey = "k" });

        var doc = JsonDocument.Parse(File.ReadAllText(outPath));
        doc.RootElement.GetProperty("AppName").GetString().Should().Be("Demo");
        doc.RootElement.GetProperty("Secret").GetString().Should().Be("shh");
    }
}
