# Getting started

## 1. Install

```bash
dotnet add package Configurite
```

## 2. Provide a master key

Configurite never stores the master key. Pick **one** source — the resolver tries them in order:

| Priority | Source | Example |
|---|---|---|
| 1 | `ConfiguriteOptions.MasterKey` | `opts.MasterKey = "…"` |
| 2 | Environment variable | `export CONFIGURITE_MASTER_KEY="…"` |
| 3 | Key file | `~/.configurite/master.key` (single line) |

If you never write encrypted values, no master key is required.

## 3. Wire up the provider

```csharp
using Configurite;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfigurite(opts =>
{
    opts.DatabasePath    = "appsettings.db";
    opts.Environment     = builder.Environment.EnvironmentName;
    opts.CreateIfMissing = true;   // create schema on first run
    opts.ReloadOnChange  = true;   // pick up live edits
});
```

## 4. Read configuration as usual

```csharp
public sealed class WeatherSettings
{
    public int Days { get; set; }
}

builder.Services.Configure<WeatherSettings>(builder.Configuration.GetSection("Forecast"));
```

```csharp
app.MapGet("/", (IConfiguration cfg) => new
{
    Greeting       = cfg["Greeting"],
    ConnString     = cfg["ConnectionStrings:Default"], // decrypted on read
    DefaultLogLevel = cfg["Logging:LogLevel:Default"],
});
```

## 5. (Optional) Migrate existing JSON

See [Migrating from appsettings.json](migration.md). One call replaces all your JSON files:

```csharp
using var migrator = new JsonToSqliteMigrator("appsettings.db");
migrator.MigrateDirectory(AppContext.BaseDirectory, "appsettings", new MigrationOptions
{
    EncryptKeyPatterns = { "ConnectionStrings:*", "*:Password", "*:ApiKey" }
});
```
