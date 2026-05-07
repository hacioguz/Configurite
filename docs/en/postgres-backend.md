# PostgreSQL backend

> Available since `Configurite.Postgres` 8.5 / 9.5 / 10.5.

When SQLite's per-host file model doesn't fit (multi-instance services, Kubernetes, centrally-managed config), swap in PostgreSQL. The same `IConfiguriteStore` API; same encryption, audit log, admin UI compatibility.

## Install

```bash
dotnet add package Configurite.Postgres
```

## Use

```csharp
using Configurite.Postgres;
using Configurite.Storage;

IConfiguriteStore store = new PostgresConfiguriteStore(
    connectionString: "Host=db.internal;Database=app;Username=svc;Password=…",
    schemaName: "configurite");
store.EnsureSchema();

// From here, the API is identical to the SQLite store.
store.Upsert("AppName", "Demo", isEncrypted: false, environment: null);
store.TryGet("AppName", null, out var entry);
```

## Schema

`EnsureSchema()` creates (idempotently):

```sql
CREATE SCHEMA IF NOT EXISTS configurite;

CREATE TABLE configurite.configuration (
    id           BIGSERIAL    PRIMARY KEY,
    key          TEXT         NOT NULL,
    value        TEXT         NOT NULL,
    is_encrypted BOOLEAN      NOT NULL DEFAULT FALSE,
    environment  TEXT,
    created_utc  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_utc  TIMESTAMPTZ  NOT NULL DEFAULT now(),
    UNIQUE (key, environment)
);

CREATE INDEX ix_configuration_environment ON configurite.configuration (environment);

CREATE TABLE configurite.metadata (
    key   TEXT PRIMARY KEY,
    value TEXT NOT NULL
);

CREATE TABLE configurite.audit_log (
    id          BIGSERIAL   PRIMARY KEY,
    operation   TEXT        NOT NULL,
    key         TEXT,
    environment TEXT,
    "user"      TEXT,
    timestamp   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ix_audit_log_timestamp ON configurite.audit_log (timestamp);
```

The schema mirrors SQLite: same column names (snake-cased to match PG conventions), same semantics, same `UNIQUE(key, environment)` for environment-override behaviour.

## Multi-tenant

Pass a custom `schemaName` to keep multiple apps isolated in one database:

```csharp
var tenantA = new PostgresConfiguriteStore(connStr, schemaName: "tenant_a");
var tenantB = new PostgresConfiguriteStore(connStr, schemaName: "tenant_b");
```

Each tenant gets its own `tenant_*.configuration`, `tenant_*.metadata`, `tenant_*.audit_log`.

## Provider integration

The provider currently constructs a `SqliteConfiguriteStore` directly. Wiring the Postgres store into `AddConfigurite` is on the v9.x roadmap (we'll expose a `Func<IConfiguriteStore>` factory in `ConfiguriteOptions`). Until then, use the Postgres store with **the public encryption helpers** (`ConfiguriteEncryption.CreateEncryptor`) and your own configuration loader.

## When to choose Postgres over SQLite

| Want | Pick |
|---|---|
| Single-host service, simple deploy | **SQLite** (zero-ops) |
| Multi-instance load-balanced service | **PostgreSQL** (shared state) |
| Strict centralised audit | **PostgreSQL** (one log, all instances) |
| Air-gapped / minimal dependencies | **SQLite** |
| Already running PostgreSQL | **PostgreSQL** (no new infra) |
| Data sovereignty constraints (file-based) | **SQLite** |

## Caveats

- The Postgres store **does not implement encryption itself** — use `ConfiguriteEncryption.CreateEncryptor` exactly as you would for SQLite. Encrypted bytes go in the `value` text column the same way.
- Audit log table exists; the `AuditingConfiguriteStore` decorator from `Configurite.Audit` works the same way against either backend.
- **Connection string is your responsibility**: SSL, pooling, failover all happen at the Npgsql layer. The Configurite layer is intentionally thin.
