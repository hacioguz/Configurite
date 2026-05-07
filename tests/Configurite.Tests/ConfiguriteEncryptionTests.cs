using Configurite.Encryption;
using Configurite.Storage;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Configurite.Tests;

[Collection("Environment")]
public sealed class ConfiguriteEncryptionTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"configurite-helper-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    [Fact]
    public void CreateEncryptor_WithExplicitKey_ReturnsWorkingEncryptor()
    {
        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        using var enc = ConfiguriteEncryption.CreateEncryptor(store, "test-key");
        var ciphertext = enc.Encrypt("payload");
        enc.Decrypt(ciphertext).Should().Be("payload");
    }

    [Fact]
    public void TryCreateEncryptor_NoKeyAvailable_ReturnsNull()
    {
        Environment.SetEnvironmentVariable(MasterKeyResolver.EnvironmentVariableName, null);

        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        var enc = ConfiguriteEncryption.TryCreateEncryptor(store);
        enc.Should().BeNull();
    }

    [Fact]
    public void CreateEncryptor_NoKeyAvailable_Throws()
    {
        Environment.SetEnvironmentVariable(MasterKeyResolver.EnvironmentVariableName, null);

        IConfiguriteStore store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();

        var act = () => ConfiguriteEncryption.CreateEncryptor(store);
        act.Should().Throw<InvalidOperationException>();
    }
}
