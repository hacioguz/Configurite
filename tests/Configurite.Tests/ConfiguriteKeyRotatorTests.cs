using System.Security.Cryptography;
using Configurite;
using Configurite.Encryption;
using Configurite.Storage;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Configurite.Tests;

public sealed class ConfiguriteKeyRotatorTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"configurite-rotate-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private void SeedDatabase(string masterKey, params (string key, string value, bool encrypt)[] rows)
    {
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        var salt = EncryptionMetadata.GetOrCreateSalt(store);
        using var enc = new AesGcmConfigEncryptor(masterKey, salt);

        foreach (var (key, value, encrypt) in rows)
        {
            if (encrypt)
            {
                store.Upsert(key, enc.Encrypt(value), isEncrypted: true, environment: null);
            }
            else
            {
                store.Upsert(key, value, isEncrypted: false, environment: null);
            }
        }
    }

    [Fact]
    public void Rotate_ReencryptsAllRows_AndConfigStillReadable()
    {
        const string oldKey = "old-master-key";
        const string newKey = "brand-new-master-key";

        SeedDatabase(oldKey,
            ("AppName", "Demo", false),
            ("ConnectionStrings:Default", "Server=db;Pwd=s3cr3t!", true),
            ("Auth:ApiKey", "abc-123", true));

        var rotator = new ConfiguriteKeyRotator(_dbPath);
        var result = rotator.Rotate(oldKey, newKey);

        result.RowsRotated.Should().Be(2);

        var config = new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.MasterKey = newKey;
            })
            .Build();

        config["AppName"].Should().Be("Demo");
        config["ConnectionStrings:Default"].Should().Be("Server=db;Pwd=s3cr3t!");
        config["Auth:ApiKey"].Should().Be("abc-123");
    }

    [Fact]
    public void Rotate_OldKeyNoLongerWorks()
    {
        const string oldKey = "old-master-key";
        const string newKey = "brand-new-master-key";

        SeedDatabase(oldKey, ("Secret", "shh", true));

        new ConfiguriteKeyRotator(_dbPath).Rotate(oldKey, newKey);

        var act = () => new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.MasterKey = oldKey;
            })
            .Build();

        act.Should().Throw<CryptographicException>("the old key cannot decrypt rows re-encrypted with the new key");
    }

    [Fact]
    public void Rotate_WrongOldKey_RollsBack()
    {
        const string oldKey = "real-old-key";
        const string newKey = "new-key";

        SeedDatabase(oldKey, ("Secret", "shh", true));

        // Capture the original ciphertext so we can prove nothing changed.
        // Orijinal ciphertext'i kaydet ki hiçbir şeyin değişmediğini kanıtlayalım.
        string OriginalCiphertext()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM Configuration WHERE Key='Secret';";
            return (string)cmd.ExecuteScalar()!;
        }

        var before = OriginalCiphertext();

        var rotator = new ConfiguriteKeyRotator(_dbPath);
        var act = () => rotator.Rotate(oldMasterKey: "WRONG", newMasterKey: newKey);
        act.Should().Throw<CryptographicException>("decrypt with the wrong key must fail before any write happens");

        OriginalCiphertext().Should().Be(before, "the row must be untouched after a failed rotation");

        // The original key must still work.
        // Orijinal anahtar hâlâ çalışmalı.
        var config = new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.MasterKey = oldKey;
            })
            .Build();
        config["Secret"].Should().Be("shh");
    }

    [Fact]
    public void Rotate_NoEncryptedRows_StillRefreshesSalt()
    {
        const string oldKey = "old";
        const string newKey = "new";

        SeedDatabase(oldKey, ("AppName", "Demo", false));

        var store = new SqliteConfiguriteStore(_dbPath);
        var oldSalt = store.ReadMetadata(EncryptionMetadata.SaltMetadataKey);
        oldSalt.Should().NotBeNull();

        var result = new ConfiguriteKeyRotator(_dbPath).Rotate(oldKey, newKey);

        result.RowsRotated.Should().Be(0);

        var newSalt = store.ReadMetadata(EncryptionMetadata.SaltMetadataKey);
        newSalt.Should().NotBe(oldSalt, "the salt must be refreshed even when no rows need rotation");
    }

    [Fact]
    public void Rotate_RejectsEmptyKeys()
    {
        SeedDatabase("k", ("Secret", "shh", true));

        var rotator = new ConfiguriteKeyRotator(_dbPath);
        rotator.Invoking(r => r.Rotate("", "new")).Should().Throw<ArgumentException>();
        rotator.Invoking(r => r.Rotate("old", "")).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rotate_OnDatabaseWithNoSalt_Throws()
    {
        // Create an unencrypted-only database (never had a salt written).
        // Hiç salt yazılmamış, sadece düz değer içeren bir DB oluştur.
        var store = new SqliteConfiguriteStore(_dbPath);
        store.EnsureSchema();
        store.Upsert("AppName", "Demo", isEncrypted: false, environment: null);

        var rotator = new ConfiguriteKeyRotator(_dbPath);
        var act = () => rotator.Rotate("a", "b");
        act.Should().Throw<InvalidOperationException>().WithMessage("*salt*");
    }
}
