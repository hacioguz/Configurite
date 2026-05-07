using Configurite.Cli;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Configurite.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"configurite-cli-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }

    private static (int code, string stdout, string stderr) Run(params string[] args)
    {
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();
        var code = Dispatcher.Run(args, outWriter, errWriter);
        return (code, outWriter.ToString(), errWriter.ToString());
    }

    [Fact]
    public void Help_ExitsZeroAndPrintsUsage()
    {
        var (code, stdout, _) = Run("--help");
        code.Should().Be(0);
        stdout.Should().Contain("USAGE").And.Contain("COMMANDS");
    }

    [Fact]
    public void NoArgs_PrintsUsageAndExitsOne()
    {
        var (code, stdout, _) = Run();
        code.Should().Be(1);
        stdout.Should().Contain("USAGE");
    }

    [Fact]
    public void Init_CreatesSchema()
    {
        var (code, stdout, _) = Run("init", _dbPath);
        code.Should().Be(0);
        stdout.Should().Contain("initialized");
        File.Exists(_dbPath).Should().BeTrue();
    }

    [Fact]
    public void SetThenGet_PlainValue_Roundtrips()
    {
        Run("init", _dbPath);
        Run("set", _dbPath, "AppName", "Demo").code.Should().Be(0);

        var (code, stdout, _) = Run("get", _dbPath, "AppName");
        code.Should().Be(0);
        stdout.Trim().Should().Be("Demo");
    }

    [Fact]
    public void SetEncrypted_WithMasterKeyFlag_DecryptsTransparently()
    {
        Run("init", _dbPath);
        Run("set", _dbPath, "Auth:ApiKey", "abc-123", "--encrypt", "--master-key", "k").code.Should().Be(0);

        var (code, stdout, _) = Run("get", _dbPath, "Auth:ApiKey", "--master-key", "k");
        code.Should().Be(0);
        stdout.Trim().Should().Be("abc-123");
    }

    [Fact]
    public void List_HidesEncryptedByDefault_RevealsWithFlag()
    {
        Run("init", _dbPath);
        Run("set", _dbPath, "AppName", "Demo");
        Run("set", _dbPath, "Secret", "shh", "--encrypt", "--master-key", "k");

        var hidden = Run("list", _dbPath).stdout;
        hidden.Should().Contain("AppName = Demo");
        hidden.Should().Contain("Secret = <encrypted>");

        var revealed = Run("list", _dbPath, "--reveal", "--master-key", "k").stdout;
        revealed.Should().Contain("Secret = shh");
    }

    [Fact]
    public void Get_MissingKey_Exits5()
    {
        Run("init", _dbPath);
        var (code, _, stderr) = Run("get", _dbPath, "Nope");
        code.Should().Be(5);
        stderr.Should().Contain("not found");
    }

    [Fact]
    public void UnknownCommand_Exits2()
    {
        var (code, _, stderr) = Run("banana");
        code.Should().Be(2);
        stderr.Should().Contain("unknown command");
    }

    [Fact]
    public void MissingArgument_Exits2()
    {
        var (code, _, stderr) = Run("init");
        code.Should().Be(2);
        stderr.Should().Contain("missing required");
    }

    [Fact]
    public void Migrate_FromJsonFile_WithEncryptPattern_Works()
    {
        var json = Path.Combine(Path.GetTempPath(), $"app-{Guid.NewGuid():N}.json");
        File.WriteAllText(json, """{ "AppName": "Demo", "Auth": { "ApiKey": "abc" } }""");

        try
        {
            var (code, stdout, _) = Run("migrate", _dbPath, json, "--encrypt", "*:ApiKey", "--master-key", "k");
            code.Should().Be(0);
            stdout.Should().Contain("1 encrypted");

            Run("get", _dbPath, "AppName").stdout.Trim().Should().Be("Demo");
            Run("get", _dbPath, "Auth:ApiKey", "--master-key", "k").stdout.Trim().Should().Be("abc");
        }
        finally
        {
            File.Delete(json);
        }
    }

    [Fact]
    public void Rotate_ChangesMasterKey()
    {
        Run("init", _dbPath);
        Run("set", _dbPath, "Secret", "shh", "--encrypt", "--master-key", "old-key");

        var (code, stdout, _) = Run("rotate", _dbPath, "--old", "old-key", "--new", "new-key");
        code.Should().Be(0);
        stdout.Should().Contain("rotated 1");

        Run("get", _dbPath, "Secret", "--master-key", "new-key").stdout.Trim().Should().Be("shh");
    }

    [Fact]
    public void Audit_PrintsRecentEntries()
    {
        Run("init", _dbPath);

        var audit = new Configurite.Audit.SqliteConfiguriteAuditLog(_dbPath);
        audit.Record("Upsert", "Foo", null, "alice");
        audit.Record("Delete", "Bar", "Production", "bob");

        var (code, stdout, _) = Run("audit", _dbPath, "--limit", "10");
        code.Should().Be(0);
        stdout.Should().Contain("Upsert").And.Contain("Foo").And.Contain("alice");
        stdout.Should().Contain("Delete").And.Contain("Bar").And.Contain("[Production]").And.Contain("bob");
    }

    [Fact]
    public void Export_PlainDatabase_WritesHierarchicalJson()
    {
        Run("init", _dbPath);
        Run("set", _dbPath, "AppName", "Demo");
        Run("set", _dbPath, "Logging:LogLevel:Default", "Info");

        var outFile = Path.Combine(Path.GetTempPath(), $"export-{Guid.NewGuid():N}.json");
        try
        {
            var (code, stdout, _) = Run("export", _dbPath, outFile);
            code.Should().Be(0);
            stdout.Should().Contain("wrote");

            var content = File.ReadAllText(outFile);
            content.Should().Contain("\"AppName\": \"Demo\"");
            content.Should().Contain("\"LogLevel\":");
        }
        finally
        {
            if (File.Exists(outFile)) File.Delete(outFile);
        }
    }

    [Fact]
    public void Delete_RemovesRow()
    {
        Run("init", _dbPath);
        Run("set", _dbPath, "AppName", "Demo");

        Run("delete", _dbPath, "AppName").code.Should().Be(0);
        Run("get", _dbPath, "AppName").code.Should().Be(5);
    }
}
