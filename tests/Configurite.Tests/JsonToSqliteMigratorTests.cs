using Configurite;
using Configurite.Encryption;
using Configurite.Migration;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace Configurite.Tests;

public sealed class JsonToSqliteMigratorTests : IDisposable
{
    private readonly string _workingDir;
    private readonly string _dbPath;

    public JsonToSqliteMigratorTests()
    {
        _workingDir = Path.Combine(Path.GetTempPath(), $"configurite-mig-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_workingDir);
        _dbPath = Path.Combine(_workingDir, "appsettings.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_workingDir))
        {
            Directory.Delete(_workingDir, recursive: true);
        }
    }

    private string WriteJson(string fileName, string content)
    {
        var path = Path.Combine(_workingDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void MigrateFile_FlattensNestedObjectsAndArrays()
    {
        var jsonPath = WriteJson("appsettings.json", """
            {
              "AppName": "Demo",
              "Logging": {
                "LogLevel": { "Default": "Information", "Microsoft": "Warning" }
              },
              "Forecast": { "Days": 7, "Enabled": true },
              "Hosts": [ "primary.local", "secondary.local" ]
            }
            """);

        using var migrator = new JsonToSqliteMigrator(_dbPath);
        var result = migrator.MigrateFile(jsonPath);

        result.FilesProcessed.Should().Be(1);
        result.KeysWritten.Should().Be(7);
        result.KeysEncrypted.Should().Be(0);

        var config = new ConfigurationBuilder().AddConfigurite(_dbPath).Build();
        config["AppName"].Should().Be("Demo");
        config["Logging:LogLevel:Default"].Should().Be("Information");
        config["Logging:LogLevel:Microsoft"].Should().Be("Warning");
        config["Forecast:Days"].Should().Be("7");
        config["Forecast:Enabled"].Should().Be("true");
        config["Hosts:0"].Should().Be("primary.local");
        config["Hosts:1"].Should().Be("secondary.local");
    }

    [Fact]
    public void MigrateFile_EncryptsKeysMatchingPatterns()
    {
        var jsonPath = WriteJson("appsettings.json", """
            {
              "AppName": "Demo",
              "ConnectionStrings": {
                "Default": "Server=db;Pwd=s3cr3t!",
                "ReadOnly": "Server=db-ro;Pwd=ro!"
              },
              "Auth": { "ApiKey": "abc-123" }
            }
            """);

        using var migrator = new JsonToSqliteMigrator(_dbPath, masterKey: "test-master-key");
        var result = migrator.MigrateFile(jsonPath, new MigrationOptions
        {
            EncryptKeyPatterns = { "ConnectionStrings:*", "*:ApiKey" },
        });

        result.KeysWritten.Should().Be(4);
        result.KeysEncrypted.Should().Be(3, "two ConnectionStrings + one ApiKey");

        var config = new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.MasterKey = "test-master-key";
            })
            .Build();

        config["AppName"].Should().Be("Demo");
        config["ConnectionStrings:Default"].Should().Be("Server=db;Pwd=s3cr3t!");
        config["ConnectionStrings:ReadOnly"].Should().Be("Server=db-ro;Pwd=ro!");
        config["Auth:ApiKey"].Should().Be("abc-123");
    }

    [Fact]
    public void MigrateFile_EncryptPattern_WithoutMasterKey_Throws()
    {
        var jsonPath = WriteJson("appsettings.json", """{ "Pwd": "hunter2" }""");

        Environment.SetEnvironmentVariable(MasterKeyResolver.EnvironmentVariableName, null);

        using var migrator = new JsonToSqliteMigrator(_dbPath);
        var act = () => migrator.MigrateFile(jsonPath, new MigrationOptions
        {
            EncryptKeyPatterns = { "Pwd" },
        });

        act.Should().Throw<InvalidOperationException>().WithMessage("*master key*");
    }

    [Fact]
    public void MigrateFile_InfersEnvironmentFromFileName()
    {
        WriteJson("appsettings.json", """{ "Greeting": "global" }""");
        var devPath = WriteJson("appsettings.Development.json", """{ "Greeting": "from-dev" }""");

        using var migrator = new JsonToSqliteMigrator(_dbPath);
        migrator.MigrateFile(Path.Combine(_workingDir, "appsettings.json"));
        migrator.MigrateFile(devPath);

        var devConfig = new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.Environment = "Development";
            })
            .Build();

        devConfig["Greeting"].Should().Be("from-dev");

        var prodConfig = new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.Environment = "Production";
            })
            .Build();

        prodConfig["Greeting"].Should().Be("global", "no Production override exists, so global wins");
    }

    [Fact]
    public void MigrateDirectory_PicksUpBaseAndEnvironmentFiles()
    {
        WriteJson("appsettings.json", """{ "AppName": "Demo", "Greeting": "global" }""");
        WriteJson("appsettings.Development.json", """{ "Greeting": "dev" }""");
        WriteJson("appsettings.Production.json", """{ "Greeting": "prod" }""");
        WriteJson("appsettings.local-secrets.json", """{ "Greeting": "local" }""");
        WriteJson("ignored.json", """{ "ShouldNotAppear": true }""");

        using var migrator = new JsonToSqliteMigrator(_dbPath);
        var result = migrator.MigrateDirectory(_workingDir);

        result.FilesProcessed.Should().Be(4, "appsettings.json + 3 environment-suffixed files; ignored.json filtered out");

        var devConfig = new ConfigurationBuilder()
            .AddConfigurite(opts =>
            {
                opts.DatabasePath = _dbPath;
                opts.Environment = "Development";
            })
            .Build();

        devConfig["AppName"].Should().Be("Demo");
        devConfig["Greeting"].Should().Be("dev");
        devConfig["ShouldNotAppear"].Should().BeNull();
    }

    [Fact]
    public void MigrateFile_OverwriteFalse_PreservesExistingKeys()
    {
        // Pre-seed an existing value.
        // Önceden mevcut bir değer ekle.
        var initial = WriteJson("appsettings.json", """{ "AppName": "Original" }""");
        using (var first = new JsonToSqliteMigrator(_dbPath))
        {
            first.MigrateFile(initial);
        }

        // Re-migrate with Overwrite=false; the original must survive.
        // Overwrite=false ile yeniden geçir; orijinal değer korunmalı.
        File.WriteAllText(initial, """{ "AppName": "Updated" }""");
        using var second = new JsonToSqliteMigrator(_dbPath);
        var result = second.MigrateFile(initial, new MigrationOptions { Overwrite = false });

        result.KeysSkipped.Should().Be(1);
        result.KeysWritten.Should().Be(0);

        var config = new ConfigurationBuilder().AddConfigurite(_dbPath).Build();
        config["AppName"].Should().Be("Original");
    }

    [Fact]
    public void KeyPatternMatcher_MatchesGlobsCaseInsensitively()
    {
        var matcher = new KeyPatternMatcher(new[] { "ConnectionStrings:*", "*:Password" });

        matcher.IsMatch("ConnectionStrings:Default").Should().BeTrue();
        matcher.IsMatch("connectionstrings:Default").Should().BeTrue();
        matcher.IsMatch("Auth:Password").Should().BeTrue();
        matcher.IsMatch("AppName").Should().BeFalse();
    }
}
