---
title: Introducing Configurite — encrypted SQLite-backed configuration for ASP.NET Core
date: 2026-04-29
tags: [.NET, ASP.NET Core, configuration, security, SQLite]
---

# Introducing Configurite

> *Drop-in replacement for `appsettings.json`. AES-256-GCM encryption at rest, hot reload, audit log, web admin UI, and a dotnet tool — all 4 packages ship as one coordinated release.*

If you've ever opened a teammate's pull request and noticed `appsettings.Production.json` carrying real database passwords, this post is for you.

## The problem with `appsettings.json`

`appsettings.json` is .NET's defacto runtime config. It's wonderful for non-secrets — log levels, feature flags, tunables — and dangerous for everything else:

- **Plaintext on disk.** Anyone with read access to the file (CI logs, container images, backup tapes, accidental git pushes) sees your secrets.
- **No audit.** Who changed `ConnectionStrings:Default` last Tuesday? Nobody knows.
- **One file per environment.** Override semantics are crisp but the file proliferation is real.
- **No editing without redeploying.** Operations runbooks mention "edit `appsettings.json` then restart the pod"... in 2026.

The cloud answer is "use a secrets manager" — Vault, KMS, Key Vault. They work. They're also another piece of infrastructure to run, secure, observe, and pay for. For services that don't need a global secret backbone, the cloud answer is overkill.

**Configurite is the in-between.** A single SQLite file replaces `appsettings.json`. The values you mark as encrypted ride AES-256-GCM with PBKDF2-HMAC-SHA256 key derivation. Plain values stay plain (your `Logging:LogLevel:Default` doesn't need encryption). The whole thing keeps the standard `IConfiguration` API, hot reload, and dotnet ecosystem niceties.

## What you actually write

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

That's it. The first run creates the schema, every subsequent run loads from SQLite. `IConfiguration["ConnectionStrings:Default"]` returns a decrypted string transparently. `IOptionsMonitor<T>` reacts to file changes through the standard hot reload pipeline.

## What you get out of the box

The 8.5 / 9.5 / 10.5 release ships **four NuGet packages** in lockstep:

| Package | Purpose | Size |
|---|---|---|
| `Configurite` | The configuration provider library. | 40 KB |
| `Configurite.Cli` | `dotnet configurite` global tool — migrate, rotate, audit, export. | 13 MB |
| `Configurite.AdminUI` | Drop-in web admin panel for any ASP.NET Core app. | 20 KB |
| `Configurite.Postgres` | `IConfiguriteStore` implementation backed by PostgreSQL. | 10 KB |

You install one, two, or all four. They version together: install `Configurite 9.x` and you can pull `Configurite.AdminUI 9.x` knowing they're source-compatible.

## Migration: stop shipping JSON, start shipping a database

```bash
dotnet configurite migrate ./appsettings.db ./config-folder \
    --encrypt "ConnectionStrings:*" \
    --encrypt "*:Password" \
    --encrypt "*:ApiKey"
```

That single command:

1. Reads every `appsettings.json` and `appsettings.{Env}.json` in `./config-folder`.
2. Flattens nested keys into `:`-separated paths the .NET configuration system uses.
3. Detects the environment from each file name (`.Production.json` → `Environment = "Production"`).
4. Writes plain values directly; encrypts values matching any of the glob patterns.
5. Reports a one-line summary: `migrated 2 file(s): 9 keys written, 2 encrypted`.

You commit the resulting `appsettings.db` if you want — without the master key, the encrypted bytes are computationally infeasible to recover (AES-256, PBKDF2 200 K iterations, fresh per-database salt). Plain values are visible by design; only sensitive paths should be encrypted.

A reverse exporter is shipped too:

```bash
dotnet configurite export ./appsettings.db ./out --per-env --decrypt --master-key "$KEY"
```

So you can roll back to JSON if you ever want to. (Or compare versions of your config across time; the audit log makes that easier still.)

## The encryption story

Crypto layers, and why each:

- **AES-256-GCM** for the data cipher. Authenticated encryption — tampering throws on read. NIST-approved, ships in the .NET BCL since .NET 8 (`AesGcm`).
- **PBKDF2-HMAC-SHA256** with 200 000 iterations for key derivation. OWASP minimum for SHA-256, BCL-native. We considered Argon2id — it's the modern winner against GPU attacks, but adding a third-party crypto package would expand the trust surface, and PBKDF2 with a generous iteration count is plenty for a config-secret use case. We'll re-evaluate on .NET 11.
- **Per-database 32-byte salt**, persisted in a `Metadata` table, never stored alongside the master key.
- **Per-encryption 12-byte nonce** generated via `RandomNumberGenerator.GetBytes`. Identical plaintexts never produce identical ciphertexts.
- **Master key** resolved from a fallback chain: option → env var → key file at `~/.configurite/master.key`. Never persisted in the database. We document the operational story in detail; in short: don't co-locate the key with the data.

The whole thing is documented in [docs/en/security.md](https://github.com/hacioguz/configurite/blob/main/docs/en/security.md) — threat model, five concrete attack scenarios, and a six-point production checklist.

## Hot reload that actually works

`ReloadOnChange = true` and you're done:

```csharp
public sealed class GreetingService(IOptionsMonitor<GreetingOptions> opts)
{
    public string CurrentMessage => opts.CurrentValue.Message;
    public void StartListening() => opts.OnChange(o => Console.WriteLine($"new: {o.Message}"));
}
```

Under the hood:

1. `FileSystemWatcher` on the database directory, filtering for the .db file plus its `-journal` / `-wal` / `-shm` siblings.
2. 250 ms debounce — a single SQLite commit fires multiple file events, all coalesced into one reload.
3. Provider re-reads, decrypts, signals `OnReload()` via `IChangeToken`. `IOptionsMonitor<T>` propagates.
4. Failures inside the callback (transient file lock during a write) are swallowed; the watcher stays alive.

Tested under all three .NET versions (8 / 9 / 10) on Linux, macOS, and Windows.

## Atomic key rotation

A built-in helper rotates the master key without downtime:

```csharp
using var rotator = new ConfiguriteKeyRotator("appsettings.db");
var result = rotator.Rotate(oldKey, newKey);
Console.WriteLine($"{result.RowsRotated} rows re-encrypted under the new key.");
```

Or from the CLI:

```bash
configurite rotate ./appsettings.db --old "$OLD_KEY" --new "$NEW_KEY"
```

The whole rotation runs inside one SQLite transaction:

1. Decrypt every `IsEncrypted = 1` row with the old key into memory.
2. **Begin transaction.** Generate a fresh salt, write it to `Metadata`. Build a new encryptor. Replace every row's ciphertext.
3. **Commit.** Or, on any error, roll back fully — the database stays on the **old** key. There is no partial state.

Wrong old key throws `CryptographicException` *before* any write happens. We have a test that captures the original ciphertext byte-for-byte and verifies it's untouched after a failed rotation.

## The admin UI

Sometimes you need to edit a value. Sometimes you want to see what's there.

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddConfiguriteAdmin(opts =>
{
    opts.DatabasePath    = "appsettings.db";
    opts.DefaultLanguage = "en";   // or "tr"
});

app.MapConfiguriteAdmin("/configurite-admin")
   .RequireAuthorization("ConfiguriteAdmin");
```

You get three pages at `/configurite-admin`:

- **Dashboard** — total rows, encrypted count, environments, schema version, audit count.
- **Keys** — browse, filter by environment, add, delete, reveal encrypted values on demand.
- **Audit log** — last 200 entries, reverse-chronological, who changed what when.

It's `~20 KB` of dependency-free server-rendered HTML — no JS framework, no Razor compilation, no Blazor runtime. It's intentionally minimal because **you own the auth** (`RequireAuthorization`), and because admin UI complexity is a footgun in production.

The audit log table sits in the same SQLite database (`AuditLog`); it's automatically populated whenever changes go through the admin UI. Programmatic `IConfiguriteAuditLog` lets you write to it from your own code too.

## Observability

Pass an `ILogger`:

```csharp
opts.Logger = loggerFactory.CreateLogger("Configurite");
```

You get five `EventId`-tagged messages: load completed, optional missing, required missing, reload completed, reload failed. All compiled via `LoggerMessage` source generators — zero allocations when the level is disabled.

Subscribe to the OpenTelemetry `ActivitySource` and `Meter`:

```csharp
.WithTracing(t => t.AddSource(ConfiguriteTelemetry.SourceName))
.WithMetrics(m => m.AddMeter(ConfiguriteTelemetry.MeterName))
```

Activities and metrics:

- `Configurite.Load` activity with `database`, `environment`, `rows.total`, `rows.decrypted` tags.
- `configurite.loads.total`, `configurite.reloads.total`, `configurite.decryptions.total`, `configurite.watcher.errors.total` counters.
- `configurite.load.duration` histogram (p50/p99 ready).

Standard .NET tools, standard wiring, zero proprietary telemetry pipeline.

## Performance

The numbers below are from BenchmarkDotNet `--job short` on macOS / Apple Silicon. Reproduce with `dotnet run --project benchmarks/Configurite.Benchmarks -c Release`.

| Operation | Cost |
|---|---|
| AES-GCM encrypt 16 B | 2.0 µs |
| AES-GCM encrypt 4 KB | 4.2 µs |
| PBKDF2 derive (200 K iter) | 30.5 ms (once per process) |
| `TryGet` | 6.6 µs constant |
| `Upsert` (after WAL+synchronous=NORMAL) | 63 µs |
| Provider load 1 000 plain rows | ~700 µs |
| Provider load 1 000 rows, 50 % encrypted | ~35 ms |

The headline: an app with encrypted secrets pays ~30 ms once at startup for PBKDF2; thereafter the per-row decrypt cost is negligible. An app without encryption pays ~700 µs to load 1 000 keys. Configurite never sits on the request hot path.

## Versioning that actually means something

Configurite uses **TFM-major versioning** — the major component of the package version mirrors the .NET target framework. Library, CLI, AdminUI, and Postgres ship in **lockstep**.

| .NET | Package line |
|---|---|
| `net8.0` | `Configurite 8.x.y` |
| `net9.0` | `Configurite 9.x.y` |
| `net10.0` | `Configurite 10.x.y` |

You pin to a line via floating ranges:

```xml
<PackageReference Include="Configurite" Version="8.*" />
```

You upgrade to .NET 9 by switching to `9.*`. **No surprise major bumps** in between. Each release produces three separate `.nupkg` files from one source tree — same code, three TFMs, three versions.

## What's next

We're committed to the public surface that landed in 8.x. Expect:

- **8.6 / 9.6 / 10.6**: `ConfiguriteOptions.StoreFactory` so the provider can use any `IConfiguriteStore` (Postgres, your own backend) directly instead of constructing the SQLite store hard-coded.
- **8.7 / 9.7 / 10.7**: Append-only audit log with hash chaining for tamper evidence (the current audit log is in the same DB and editable by anyone with write access — we document this; the next minor closes it).
- **9.x**: Argon2id key derivation re-evaluation when .NET 11 ships.
- **2.0**: To-be-determined. We're listening.

## Try it

```bash
# Library
dotnet add package Configurite

# CLI tool
dotnet tool install -g Configurite.Cli

# Admin UI
dotnet add package Configurite.AdminUI

# PostgreSQL backend
dotnet add package Configurite.Postgres
```

Source, full bilingual documentation (English + Turkish), and benchmarks at the repo. The site is auto-published to GitHub Pages on every main push.

## Why?

Because you should be able to ship a .NET service without a separate secrets manager *or* plaintext secrets in your repo. Because `appsettings.json` solved the wrong half of the problem. Because crypto is hard, and people will pick the wrong primitive (or none) if it's not the easy default.

Configurite is the easy default we wanted. We hope it's the easy default for you too.

— *Configurite contributors, 2026-04-29*
