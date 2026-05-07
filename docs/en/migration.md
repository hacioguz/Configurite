# Migrating from `appsettings.json`

## One-shot migration

```csharp
using Configurite.Migration;

using var migrator = new JsonToSqliteMigrator("appsettings.db");

var result = migrator.MigrateDirectory(
    AppContext.BaseDirectory,
    baseFileName: "appsettings",
    options: new MigrationOptions
    {
        EncryptKeyPatterns =
        {
            "ConnectionStrings:*",
            "*:Password",
            "*:ApiKey",
        },
    });

Console.WriteLine($"{result.FilesProcessed} files / {result.KeysWritten} keys / {result.KeysEncrypted} encrypted");
```

## File-name conventions

| File | Environment column |
|---|---|
| `appsettings.json` | `NULL` (global) |
| `appsettings.Development.json` | `Development` |
| `appsettings.Production.json` | `Production` |
| `appsettings.Anything.json` | `Anything` |

You can override with `MigrationOptions.EnvironmentOverride`.

## How JSON becomes rows

| JSON | SQLite key | Value |
|---|---|---|
| `"AppName": "Demo"` | `AppName` | `Demo` |
| `"Logging": { "LogLevel": { "Default": "Info" } }` | `Logging:LogLevel:Default` | `Info` |
| `"Hosts": [ "a.local", "b.local" ]` | `Hosts:0`, `Hosts:1` | `a.local`, `b.local` |
| `"Forecast": { "Days": 7, "Enabled": true }` | `Forecast:Days`, `Forecast:Enabled` | `7`, `true` |

This matches the .NET configuration system, so existing `IOptions<T>` bindings continue to work unchanged.

## Encrypt-key patterns

`EncryptKeyPatterns` is a list of glob expressions. `*` matches any number of characters across segment boundaries:

| Pattern | Matches |
|---|---|
| `ConnectionStrings:*` | `ConnectionStrings:Default`, `ConnectionStrings:ReadOnly` |
| `*:Password` | `Auth:Password`, `Database:Admin:Password` |
| `Auth:*Token` | `Auth:AccessToken`, `Auth:RefreshToken` |

Matching is case-insensitive.

## Idempotency

By default, `Overwrite = true` — re-running the migration replaces matching rows. Set `Overwrite = false` to preserve hand-edited values; the result reports `KeysSkipped`.

## Recommended workflow

1. Land Configurite in a feature branch with the migration call gated behind `if (!File.Exists(dbPath))`.
2. Run once locally; commit the resulting `appsettings.db` if it's safe (it is — encrypted secrets are useless without the master key).
3. Delete the old `appsettings*.json` files (or keep them for emergency rollback).
4. Add the `*.db-journal`, `*.db-wal`, `*.db-shm` patterns to `.gitignore`.
