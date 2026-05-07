using System.Globalization;
using Configurite.Audit;
using Configurite.Encryption;
using Configurite.Migration;
using Configurite.Storage;

namespace Configurite.Cli.Commands;

internal static class InitCommand
{
    public static int Execute(string[] argv, TextWriter stdout, TextWriter stderr)
    {
        _ = stderr;
        var args = Args.Parse(argv, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var dbPath = args.RequirePositional(0, "<db>");

        var store = new SqliteConfiguriteStore(dbPath);
        store.EnsureSchema();

        stdout.WriteLine($"initialized {dbPath}");
        return 0;
    }
}

internal static class MigrateCommand
{
    public static int Execute(string[] argv, TextWriter stdout, TextWriter stderr)
    {
        _ = stderr;
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "no-overwrite" };
        var args = Args.Parse(argv, flags);

        var dbPath = args.RequirePositional(0, "<db>");
        var source = args.RequirePositional(1, "<json-or-dir>");

        var options = new MigrationOptions { Overwrite = !args.HasFlag("no-overwrite") };
        foreach (var pattern in args.Multi("encrypt"))
        {
            options.EncryptKeyPatterns.Add(pattern);
        }
        if (args.Single("env") is { } env)
        {
            options.EnvironmentOverride = env;
        }

        using var migrator = new JsonToSqliteMigrator(dbPath, args.Single("master-key"));

        MigrationResult result;
        if (Directory.Exists(source))
        {
            var baseFileName = args.Single("base-name") ?? "appsettings";
            result = migrator.MigrateDirectory(source, baseFileName, options);
        }
        else
        {
            result = migrator.MigrateFile(source, options);
        }

        stdout.WriteLine(
            $"migrated {result.FilesProcessed} file(s): " +
            $"{result.KeysWritten} written, {result.KeysEncrypted} encrypted, {result.KeysSkipped} skipped");
        return 0;
    }
}

internal static class RotateCommand
{
    public static int Execute(string[] argv, TextWriter stdout, TextWriter stderr)
    {
        _ = stderr;
        var args = Args.Parse(argv, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var dbPath = args.RequirePositional(0, "<db>");
        var oldKey = args.Require("old");
        var newKey = args.Require("new");

        var rotator = new ConfiguriteKeyRotator(dbPath);
        var result = rotator.Rotate(oldKey, newKey);

        stdout.WriteLine($"rotated {result.RowsRotated} encrypted row(s)");
        return 0;
    }
}

internal static class GetCommand
{
    public static int Execute(string[] argv, TextWriter stdout, TextWriter stderr)
    {
        var args = Args.Parse(argv, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var dbPath = args.RequirePositional(0, "<db>");
        var key = args.RequirePositional(1, "<key>");
        var environment = args.Single("env");

        var store = new SqliteConfiguriteStore(dbPath);
        store.EnsureSchema();

        var entries = store.ReadAll(environment);
        if (!entries.TryGetValue(key, out var entry))
        {
            stderr.WriteLine($"key not found: {key}");
            return 5;
        }

        if (entry.IsEncrypted)
        {
            using var enc = BuildEncryptor(store, args.Single("master-key"));
            stdout.WriteLine(enc.Decrypt(entry.Value));
        }
        else
        {
            stdout.WriteLine(entry.Value);
        }

        return 0;
    }

    internal static AesGcmConfigEncryptor BuildEncryptor(SqliteConfiguriteStore store, string? explicitKey)
        => ConfiguriteEncryption.CreateEncryptor(store, explicitKey);
}

internal static class SetCommand
{
    public static int Execute(string[] argv, TextWriter stdout, TextWriter stderr)
    {
        _ = stderr;
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "encrypt" };
        var args = Args.Parse(argv, flags);

        var dbPath = args.RequirePositional(0, "<db>");
        var key = args.RequirePositional(1, "<key>");
        var value = args.RequirePositional(2, "<value>");
        var environment = args.Single("env");
        var encrypt = args.HasFlag("encrypt");

        var store = new SqliteConfiguriteStore(dbPath);
        store.EnsureSchema();

        if (encrypt)
        {
            using var enc = GetCommand.BuildEncryptor(store, args.Single("master-key"));
            store.Upsert(key, enc.Encrypt(value), isEncrypted: true, environment);
        }
        else
        {
            store.Upsert(key, value, isEncrypted: false, environment);
        }

        stdout.WriteLine($"set {key}{(environment is null ? "" : $" [{environment}]")}{(encrypt ? " (encrypted)" : "")}");
        return 0;
    }
}

internal static class ListCommand
{
    public static int Execute(string[] argv, TextWriter stdout, TextWriter stderr)
    {
        _ = stderr;
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "reveal" };
        var args = Args.Parse(argv, flags);

        var dbPath = args.RequirePositional(0, "<db>");
        var environment = args.Single("env");
        var reveal = args.HasFlag("reveal");

        var store = new SqliteConfiguriteStore(dbPath);
        store.EnsureSchema();

        var entries = store.ReadAll(environment);
        if (entries.Count == 0)
        {
            stdout.WriteLine("(no rows)");
            return 0;
        }

        AesGcmConfigEncryptor? encryptor = null;
        try
        {
            foreach (var (key, entry) in entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                string display;
                if (entry.IsEncrypted)
                {
                    if (reveal)
                    {
                        encryptor ??= GetCommand.BuildEncryptor(store, args.Single("master-key"));
                        display = encryptor.Decrypt(entry.Value);
                    }
                    else
                    {
                        display = "<encrypted>";
                    }
                }
                else
                {
                    display = entry.Value;
                }

                var envSuffix = entry.Environment is null ? "" : $" [{entry.Environment}]";
                stdout.WriteLine($"{key}{envSuffix} = {display}");
            }
        }
        finally
        {
            encryptor?.Dispose();
        }

        return 0;
    }
}

internal static class AuditCommand
{
    public static int Execute(string[] argv, TextWriter stdout, TextWriter stderr)
    {
        _ = stderr;
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "follow" };
        var args = Args.Parse(argv, flags);
        var dbPath = args.RequirePositional(0, "<db>");
        var limit = int.Parse(args.Single("limit") ?? "20", CultureInfo.InvariantCulture);
        var follow = args.HasFlag("follow");

        var store = new SqliteConfiguriteStore(dbPath);
        store.EnsureSchema();
        var audit = new SqliteConfiguriteAuditLog(dbPath);

        long lastSeen = 0;
        var initial = audit.ReadRecent(limit).Reverse().ToList();
        foreach (var entry in initial)
        {
            WriteEntry(stdout, entry);
            if (entry.Id > lastSeen) lastSeen = entry.Id;
        }

        if (!follow)
        {
            return 0;
        }

        // Naive polling tail: 500 ms cadence. Ctrl+C to stop.
        // Naif polling tail: 500 ms aralıkla. Durdurmak için Ctrl+C.
        while (true)
        {
            Thread.Sleep(500);
            var batch = audit.ReadRecent(limit).Reverse().Where(e => e.Id > lastSeen).ToList();
            foreach (var entry in batch)
            {
                WriteEntry(stdout, entry);
                if (entry.Id > lastSeen) lastSeen = entry.Id;
            }
        }
    }

    private static void WriteEntry(TextWriter stdout, AuditEntry e)
    {
        var ts = e.TimestampUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        var env = e.Environment is null ? "" : $" [{e.Environment}]";
        var user = string.IsNullOrEmpty(e.User) ? "" : $" by {e.User}";
        stdout.WriteLine($"{ts}  {e.Operation,-7} {e.Key}{env}{user}");
    }
}

internal static class ExportCommand
{
    public static int Execute(string[] argv, TextWriter stdout, TextWriter stderr)
    {
        _ = stderr;
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "decrypt", "include-encrypted", "per-env" };
        var args = Args.Parse(argv, flags);
        var dbPath = args.RequirePositional(0, "<db>");
        var output = args.RequirePositional(1, "<out>");
        var environment = args.Single("env");
        var perEnv = args.HasFlag("per-env");

        var options = new ExportOptions
        {
            DecryptWithMasterKey = args.HasFlag("decrypt"),
            IncludeEncrypted = args.HasFlag("include-encrypted"),
            MasterKey = args.Single("master-key"),
        };

        using var exporter = new SqliteToJsonExporter(dbPath);

        if (perEnv)
        {
            var results = exporter.ExportPerEnvironment(output, args.Single("base-name") ?? "appsettings", options);
            foreach (var r in results)
            {
                stdout.WriteLine($"wrote {r.BytesWritten,7} bytes for env={r.Environment ?? "(global)"}");
            }
        }
        else
        {
            var result = exporter.ExportToFile(output, options, environment);
            stdout.WriteLine($"wrote {result.BytesWritten} bytes to {output}");
        }

        return 0;
    }
}

internal static class DeleteCommand
{
    public static int Execute(string[] argv, TextWriter stdout, TextWriter stderr)
    {
        var args = Args.Parse(argv, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var dbPath = args.RequirePositional(0, "<db>");
        var key = args.RequirePositional(1, "<key>");
        var environment = args.Single("env");

        var store = new SqliteConfiguriteStore(dbPath);
        store.EnsureSchema();

        var deleted = store.Delete(key, environment);
        if (deleted == 0)
        {
            stderr.WriteLine($"key not found: {key}");
            return 5;
        }

        stdout.WriteLine($"deleted {key}{(environment is null ? "" : $" [{environment}]")}");
        return 0;
    }
}
