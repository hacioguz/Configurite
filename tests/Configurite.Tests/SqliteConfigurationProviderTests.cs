using Configurite;
using Configurite.Storage;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Configurite.Tests;

public sealed class SqliteConfigurationProviderTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"configurite-test-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void Build_WithMissingDatabase_AndCreateIfMissing_CreatesSchema()
    {
        var builder = new ConfigurationBuilder().AddConfigurite(_dbPath);

        var config = builder.Build();

        config.AsEnumerable().Should().BeEmpty();
        File.Exists(_dbPath).Should().BeTrue();
    }

    [Fact]
    public void Build_WithMissingDatabase_AndOptional_ReturnsEmpty()
    {
        var builder = new ConfigurationBuilder().AddConfigurite(opts =>
        {
            opts.DatabasePath = _dbPath;
            opts.CreateIfMissing = false;
            opts.Optional = true;
        });

        var config = builder.Build();

        config.AsEnumerable().Should().BeEmpty();
    }

    [Fact]
    public void Build_WithMissingDatabase_AndNotOptional_Throws()
    {
        var act = () => new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.CreateIfMissing = false;
                opts.Optional = false;
            })
            .Build();

        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Load_ReadsValuesFromDatabase()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("AppName", "TestApp", isEncrypted: false, environment: null);
        store.Upsert("Logging:LogLevel:Default", "Information", isEncrypted: false, environment: null);

        var config = new ConfigurationBuilder().AddConfigurite(_dbPath).Build();

        config["AppName"].Should().Be("TestApp");
        config["Logging:LogLevel:Default"].Should().Be("Information");
    }

    [Fact]
    public void Load_HierarchicalKeys_ExposeAsSection()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("Logging:LogLevel:Default", "Information", isEncrypted: false, environment: null);
        store.Upsert("Logging:LogLevel:Microsoft", "Warning", isEncrypted: false, environment: null);

        var config = new ConfigurationBuilder().AddConfigurite(_dbPath).Build();

        var section = config.GetSection("Logging:LogLevel");
        section["Default"].Should().Be("Information");
        section["Microsoft"].Should().Be("Warning");
    }

    [Fact]
    public void Load_EnvironmentSpecific_OverridesGlobal()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("ApiUrl", "https://global.example.com", isEncrypted: false, environment: null);
        store.Upsert("ApiUrl", "https://dev.example.com", isEncrypted: false, environment: "Development");

        var config = new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.Environment = "Development";
            })
            .Build();

        config["ApiUrl"].Should().Be("https://dev.example.com");
    }

    [Fact]
    public void Load_EncryptedRow_WithoutMasterKey_Throws()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        // Write a non-decryptable placeholder; we only assert the missing-key guard fires.
        store.Upsert("Secret", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", isEncrypted: true, environment: null);

        // Make sure no env-var bleeds in.
        Environment.SetEnvironmentVariable(Configurite.Encryption.MasterKeyResolver.EnvironmentVariableName, null);

        var act = () => new ConfigurationBuilder()
            .AddConfigurite(opts => opts.DatabasePath = _dbPath)
            .Build();

        act.Should().Throw<InvalidOperationException>().WithMessage("*master key*");
    }

    [Fact]
    public void EncryptedValue_RoundtripsThroughProvider()
    {
        // Arrange: seed an encrypted value using the same master key the provider will resolve.
        const string masterKey = "test-master-key-do-not-use";

        var provider = new SqliteConfigurationProvider(new ConfiguriteOptions
        {
            DatabasePath = _dbPath,
            MasterKey = masterKey,
        });

        var encryptor = provider.Encryptor!;
        var ciphertext = encryptor.Encrypt("Server=db;Password=s3cr3t!");
        provider.Store.Upsert("ConnectionStrings:Default", ciphertext, isEncrypted: true, environment: null);
        provider.Store.Upsert("AppName", "PlainApp", isEncrypted: false, environment: null);

        // Act: build configuration via the public API.
        var config = new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.MasterKey = masterKey;
            })
            .Build();

        // Assert
        config["ConnectionStrings:Default"].Should().Be("Server=db;Password=s3cr3t!");
        config["AppName"].Should().Be("PlainApp");

        provider.Dispose();
    }

    [Fact]
    public void Upsert_SameKey_ReplacesValue()
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("AppName", "Original", isEncrypted: false, environment: null);
        store.Upsert("AppName", "Updated", isEncrypted: false, environment: null);

        var config = new ConfigurationBuilder().AddConfigurite(_dbPath).Build();

        config["AppName"].Should().Be("Updated");
    }
}
