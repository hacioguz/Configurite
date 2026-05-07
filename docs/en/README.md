# Configurite — English Documentation

> Secure, encrypted SQLite-backed configuration provider for ASP.NET Core / .NET 8+.

## Why Configurite?

`appsettings.json` ships secrets in plain text. Configurite replaces it with an encrypted SQLite file, keeps the standard `IConfiguration` API, and adds:

- **AES-256-GCM** encryption per value (PBKDF2-HMAC-SHA256, 200K iterations).
- **Hot reload** via `FileSystemWatcher` and `IChangeToken`.
- **Environment-aware** overrides (`Development`, `Production`, …) in a single file.
- **One-shot migration** from any number of `appsettings*.json` files.

## Table of contents

1. [Getting started](getting-started.md)
2. [Encryption model](encryption.md)
3. [Migrating from appsettings.json](migration.md)
4. [Hot reload](hot-reload.md)
5. [Key rotation](key-rotation.md)
6. [CLI tool](cli.md)
7. [Admin UI](admin-ui.md)
8. [Benchmarks](benchmarks.md)
9. [Security architecture](security.md)
10. [Observability](observability.md)
11. [PostgreSQL backend](postgres-backend.md)
12. [Versioning policy](versioning.md)
13. [API reference](api-reference.md)

## 30-second quick start

```csharp
using Configurite;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfigurite(opts =>
{
    opts.DatabasePath   = "appsettings.db";
    opts.Environment    = builder.Environment.EnvironmentName;
    opts.ReloadOnChange = true;
});
```

Anywhere downstream:

```csharp
public sealed class HomeController(IConfiguration cfg) : Controller
{
    public string ApiKey => cfg["Auth:ApiKey"]!; // decrypted on Load
}
```

## License

MIT — see [LICENSE](../../LICENSE).
