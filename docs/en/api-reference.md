# API reference

> Versions referenced are stable in `Configurite` 1.0.

## `Configurite` namespace

### `ConfigurationBuilderExtensions.AddConfigurite`

```csharp
IConfigurationBuilder AddConfigurite(this IConfigurationBuilder builder, string databasePath);
IConfigurationBuilder AddConfigurite(this IConfigurationBuilder builder, Action<ConfiguriteOptions> configure);
```

### `ConfiguriteOptions`

| Property | Default | Purpose |
|---|---|---|
| `DatabasePath` | `"appsettings.db"` | Absolute or relative path. Relatives resolve under `AppContext.BaseDirectory`. |
| `Environment` | `null` | Limits the load to rows with `Environment IS NULL` or `Environment = …`. |
| `CreateIfMissing` | `true` | Creates the file + schema if absent. |
| `Optional` | `false` | When the file is missing and `CreateIfMissing = false`, return empty instead of throwing. |
| `MasterKey` | `null` | Highest-priority master key source. |
| `ReloadOnChange` | `false` | Enable hot reload via `FileSystemWatcher`. |

### `SqliteConfigurationProvider`

`IConfigurationProvider, IDisposable`. Public for advanced scenarios — most users go through `AddConfigurite`. Disposing zeros the derived data key and stops the watcher.

### `SqliteConfigurationSource`

`IConfigurationSource`. Carries `ConfiguriteOptions Options { get; set; }`.

## `Configurite.Storage` namespace *(since 8.3 / 9.3 / 10.3)*

### `IConfiguriteStore`

Stable read/write API over a Configurite database. Use it to build admin UIs, custom CLIs, migration scripts, etc.

```csharp
void EnsureSchema();
IReadOnlyDictionary<string, ConfigEntry> ReadAll(string? environment);
bool TryGet(string key, string? environment, out ConfigEntry entry);
void Upsert(string key, string value, bool isEncrypted, string? environment);
int  Delete(string key, string? environment);
string? ReadMetadata(string key);
void WriteMetadata(string key, string value);
```

### `SqliteConfiguriteStore`

`IConfiguriteStore` implementation. Constructor: `(string databasePath)`. Stateless across calls — every method opens its own connection.

### `ConfigEntry`

```csharp
public readonly record struct ConfigEntry(string Value, bool IsEncrypted, string? Environment);
```

`Value` is plaintext when `IsEncrypted = false`, otherwise the base64 ciphertext payload.

## `Configurite.Encryption` namespace

### `IConfigEncryptor`

```csharp
string Encrypt(string plaintext);
string Decrypt(string ciphertext);
```

### `AesGcmConfigEncryptor`

`IConfigEncryptor, IDisposable`. Constructor: `(string masterKey, byte[] salt)`. Static helper: `GenerateSalt()` returns 32 random bytes.

### `ConfiguriteEncryption` *(since 8.3 / 9.3 / 10.3)*

```csharp
static AesGcmConfigEncryptor  CreateEncryptor(IConfiguriteStore store, string? explicitMasterKey = null);
static AesGcmConfigEncryptor? TryCreateEncryptor(IConfiguriteStore store, string? explicitMasterKey = null);
```

High-level helper: resolves the master key (explicit → env var → key file), reads/creates the per-database salt, and returns a ready-to-use `AesGcmConfigEncryptor`. `Create` throws when no key is found; `Try` returns `null`.

### `ConfiguriteKeyRotator` *(since 1.1)*

```csharp
ConfiguriteKeyRotator(string databasePath);
KeyRotationResult Rotate(string oldMasterKey, string newMasterKey);
```

Atomic — see [Key rotation](key-rotation.md). Returns `KeyRotationResult(int RowsRotated)`.

### `MasterKeyResolver`

```csharp
const string EnvironmentVariableName = "CONFIGURITE_MASTER_KEY";

static string? Resolve(string? explicitKey = null);
static string  Require(string? explicitKey = null); // throws if no source available
static string  DefaultKeyFilePath();                // ~/.configurite/master.key
```

## `Configurite.Migration` namespace

### `JsonToSqliteMigrator`

```csharp
JsonToSqliteMigrator(string databasePath, string? masterKey = null);

MigrationResult MigrateFile(string jsonPath, MigrationOptions? options = null);
MigrationResult MigrateDirectory(string directory, string baseFileName = "appsettings", MigrationOptions? options = null);
```

`IDisposable` — disposes the underlying encryptor.

### `MigrationOptions`

| Property | Default | Purpose |
|---|---|---|
| `EncryptKeyPatterns` | empty | Glob patterns whose values are encrypted. |
| `Overwrite` | `true` | When false, existing rows are preserved and counted as `KeysSkipped`. |
| `EnvironmentOverride` | `null` | Forces the environment column instead of inferring from the filename. |

### `MigrationResult`

```csharp
record MigrationResult(int FilesProcessed, int KeysWritten, int KeysEncrypted, int KeysSkipped);
```

## SQLite schema (Configurite 1.0)

```sql
CREATE TABLE Configuration (
    Id          INTEGER PRIMARY KEY AUTOINCREMENT,
    Key         TEXT    NOT NULL,
    Value       TEXT    NOT NULL,
    IsEncrypted INTEGER NOT NULL DEFAULT 0,
    Environment TEXT,
    CreatedUtc  TEXT    NOT NULL DEFAULT (datetime('now')),
    UpdatedUtc  TEXT    NOT NULL DEFAULT (datetime('now')),
    UNIQUE(Key, Environment)
);

CREATE INDEX IX_Configuration_Environment ON Configuration(Environment);

CREATE TABLE Metadata (
    Key   TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
-- Known keys: SchemaVersion, EncryptionSalt (base64), EncryptionAlgorithm.
```
