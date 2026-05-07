---
_layout: landing
title: Configurite
---

# Configurite

> **Drop-in replacement for `appsettings.json`** — encrypted SQLite-backed configuration for ASP.NET Core / .NET 8+.

[![CI](https://github.com/hacioguz/configurite/actions/workflows/ci.yml/badge.svg)](https://github.com/hacioguz/configurite/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET 8 / 9 / 10](https://img.shields.io/badge/.NET-8%20%2F%209%20%2F%2010-512BD4)](https://dotnet.microsoft.com/)

## Three packages, one ecosystem

| Package | Purpose | Latest |
|---|---|---|
| **Configurite** | The configuration provider library. | [`8.4.0` / `9.4.0` / `10.4.0`](https://www.nuget.org/packages/Configurite/) |
| **Configurite.Cli** | `dotnet configurite` global tool — migrate, rotate, audit, export. | [`8.4.0` / `9.4.0` / `10.4.0`](https://www.nuget.org/packages/Configurite.Cli/) |
| **Configurite.AdminUI** | Drop-in web admin panel for any ASP.NET Core app. | [`8.4.0` / `9.4.0` / `10.4.0`](https://www.nuget.org/packages/Configurite.AdminUI/) |

## Read the docs

- 🇬🇧 [English documentation](en/README.md)
- 🇹🇷 [Türkçe dokümantasyon](tr/README.md)
- 🔧 [API reference](api/index.md) (auto-generated)

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

var app = builder.Build();
app.Run();
```

That's it. The first run creates the schema, every subsequent run loads from SQLite.

## Why Configurite?

| Problem with `appsettings.json` | Configurite's answer |
|---|---|
| Secrets in plaintext | AES-256-GCM at rest, transparent decrypt at read time |
| One file per environment | One database, environment column with override semantics |
| Editing means redeploy | Hot reload via `FileSystemWatcher` + `IChangeToken` |
| No audit trail | Optional audit log, viewable in the admin UI |
| Manual migration scripts | Built-in `JsonToSqliteMigrator` + reverse `SqliteToJsonExporter` |

## License

MIT — see [LICENSE](https://github.com/hacioguz/configurite/blob/main/LICENSE).
