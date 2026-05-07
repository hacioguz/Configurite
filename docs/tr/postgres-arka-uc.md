# PostgreSQL arka ucu

> `Configurite.Postgres` 8.5 / 9.5 / 10.5'ten itibaren mevcut.

SQLite'ın host başına dosya modeli sığmadığında (çok-instance servisler, Kubernetes, merkezî yönetim) PostgreSQL'e geçin. Aynı `IConfiguriteStore` API'si; aynı şifreleme, denetim günlüğü, yönetim paneli uyumluluğu.

## Kurulum

```bash
dotnet add package Configurite.Postgres
```

## Kullanım

```csharp
using Configurite.Postgres;
using Configurite.Storage;

IConfiguriteStore store = new PostgresConfiguriteStore(
    connectionString: "Host=db.internal;Database=app;Username=svc;Password=…",
    schemaName: "configurite");
store.EnsureSchema();

// Buradan sonra API, SQLite store'la birebir aynı.
store.Upsert("AppName", "Demo", isEncrypted: false, environment: null);
store.TryGet("AppName", null, out var entry);
```

## Şema

`EnsureSchema()` idempotent olarak oluşturur:

```sql
CREATE SCHEMA IF NOT EXISTS configurite;

CREATE TABLE configurite.configuration (...);
CREATE TABLE configurite.metadata (...);
CREATE TABLE configurite.audit_log (...);
```

Şema SQLite'ı yansıtır: aynı sütun adları (PG kurallarına uymak için snake-case), aynı semantik, ortam-override davranışı için aynı `UNIQUE(key, environment)`.

## Çok-tenant

Bir veritabanında birden çok uygulamayı izole etmek için özel `schemaName` geçin:

```csharp
var tenantA = new PostgresConfiguriteStore(connStr, schemaName: "tenant_a");
var tenantB = new PostgresConfiguriteStore(connStr, schemaName: "tenant_b");
```

Her tenant kendi `tenant_*.configuration`, `tenant_*.metadata`, `tenant_*.audit_log` tablolarına sahip olur.

## Provider entegrasyonu

Provider şu an doğrudan `SqliteConfiguriteStore` kuruyor. `AddConfigurite`'a Postgres store'u bağlamak v9.x yol haritasında (`ConfiguriteOptions`'a `Func<IConfiguriteStore>` factory geleceğiz). O zamana kadar Postgres store'u **public şifreleme yardımcıları** (`ConfiguriteEncryption.CreateEncryptor`) ve kendi yapılandırma yükleyicinizle kullanın.

## SQLite vs Postgres ne zaman?

| İstek | Tercih |
|---|---|
| Tek host servis, basit deploy | **SQLite** (sıfır-ops) |
| Çok-instance load-balanced servis | **PostgreSQL** (paylaşılan state) |
| Sıkı merkezî denetim | **PostgreSQL** (tek log, tüm instance'lar) |
| Air-gapped / minimum bağımlılık | **SQLite** |
| Zaten PostgreSQL çalışıyor | **PostgreSQL** (yeni altyapı yok) |
| Veri egemenliği kısıtları (dosya bazlı) | **SQLite** |

## Dikkat edilecekler

- Postgres store **şifrelemeyi kendi başına yapmaz** — SQLite'da olduğu gibi `ConfiguriteEncryption.CreateEncryptor` kullanın. Şifreli byte'lar `value` text sütununa aynı şekilde girer.
- Denetim günlüğü tablosu mevcut; `Configurite.Audit`'in `AuditingConfiguriteStore` decorator'ı her iki arka uçla aynı şekilde çalışır.
- **Bağlantı dizesi sizin sorumluluğunuzda**: SSL, pooling, failover hepsi Npgsql katmanında. Configurite katmanı bilerek incedir.
