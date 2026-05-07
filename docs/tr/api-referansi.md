# API referansı

> Belirtilen sürümler `Configurite` 1.0'da kararlıdır.

## `Configurite` namespace

### `ConfigurationBuilderExtensions.AddConfigurite`

```csharp
IConfigurationBuilder AddConfigurite(this IConfigurationBuilder builder, string databasePath);
IConfigurationBuilder AddConfigurite(this IConfigurationBuilder builder, Action<ConfiguriteOptions> configure);
```

### `ConfiguriteOptions`

| Özellik | Varsayılan | Amaç |
|---|---|---|
| `DatabasePath` | `"appsettings.db"` | Mutlak veya göreli yol. Göreli olanlar `AppContext.BaseDirectory` altında çözülür. |
| `Environment` | `null` | Yüklemeyi `Environment IS NULL` veya `Environment = …` olan satırlara sınırlar. |
| `CreateIfMissing` | `true` | Yoksa dosyayı + şemayı oluşturur. |
| `Optional` | `false` | Dosya yok ve `CreateIfMissing = false` ise hata yerine boş döndür. |
| `MasterKey` | `null` | En yüksek öncelikli ana anahtar kaynağı. |
| `ReloadOnChange` | `false` | `FileSystemWatcher` ile hot reload'u etkinleştir. |

### `SqliteConfigurationProvider`

`IConfigurationProvider, IDisposable`. İleri senaryolar için public — çoğu kullanıcı `AddConfigurite` üzerinden gider. Dispose, türetilmiş veri anahtarını sıfırlar ve izleyiciyi durdurur.

### `SqliteConfigurationSource`

`IConfigurationSource`. `ConfiguriteOptions Options { get; set; }` taşır.

## `Configurite.Storage` namespace *(8.3 / 9.3 / 10.3'ten itibaren)*

### `IConfiguriteStore`

Bir Configurite veritabanı üzerinde stabil okuma/yazma API'si. Yönetim UI'ları, özel CLI'lar, geçiş script'leri yazmak için kullanın.

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

`IConfiguriteStore` uygulaması. Constructor: `(string databasePath)`. Çağrılar arasında stateless — her metot kendi bağlantısını açar.

### `ConfigEntry`

```csharp
public readonly record struct ConfigEntry(string Value, bool IsEncrypted, string? Environment);
```

`IsEncrypted = false` ise `Value` düz metindir, true ise base64 şifreli yüktür.

## `Configurite.Encryption` namespace

### `IConfigEncryptor`

```csharp
string Encrypt(string plaintext);
string Decrypt(string ciphertext);
```

### `AesGcmConfigEncryptor`

`IConfigEncryptor, IDisposable`. Constructor: `(string masterKey, byte[] salt)`. Static helper: `GenerateSalt()` 32 rastgele byte döndürür.

### `ConfiguriteEncryption` *(8.3 / 9.3 / 10.3'ten itibaren)*

```csharp
static AesGcmConfigEncryptor  CreateEncryptor(IConfiguriteStore store, string? explicitMasterKey = null);
static AesGcmConfigEncryptor? TryCreateEncryptor(IConfiguriteStore store, string? explicitMasterKey = null);
```

Üst seviye yardımcı: ana anahtarı çözer (açık → env var → anahtar dosyası), DB başına saltı okur/oluşturur ve hazır bir `AesGcmConfigEncryptor` döndürür. `Create` anahtar yoksa hata fırlatır; `Try` `null` döner.

### `ConfiguriteKeyRotator` *(1.1'den itibaren)*

```csharp
ConfiguriteKeyRotator(string databasePath);
KeyRotationResult Rotate(string oldMasterKey, string newMasterKey);
```

Atomik — bkz. [Anahtar rotasyonu](anahtar-rotasyonu.md). `KeyRotationResult(int RowsRotated)` döner.

### `MasterKeyResolver`

```csharp
const string EnvironmentVariableName = "CONFIGURITE_MASTER_KEY";

static string? Resolve(string? explicitKey = null);
static string  Require(string? explicitKey = null); // kaynak yoksa hata
static string  DefaultKeyFilePath();                // ~/.configurite/master.key
```

## `Configurite.Migration` namespace

### `JsonToSqliteMigrator`

```csharp
JsonToSqliteMigrator(string databasePath, string? masterKey = null);

MigrationResult MigrateFile(string jsonPath, MigrationOptions? options = null);
MigrationResult MigrateDirectory(string directory, string baseFileName = "appsettings", MigrationOptions? options = null);
```

`IDisposable` — alttaki encryptor'u serbest bırakır.

### `MigrationOptions`

| Özellik | Varsayılan | Amaç |
|---|---|---|
| `EncryptKeyPatterns` | boş | Değerlerinin şifreleneceği glob desenleri. |
| `Overwrite` | `true` | False ise mevcut satırlar korunur ve `KeysSkipped` olarak sayılır. |
| `EnvironmentOverride` | `null` | Dosya adından çıkarmak yerine environment sütununu zorlar. |

### `MigrationResult`

```csharp
record MigrationResult(int FilesProcessed, int KeysWritten, int KeysEncrypted, int KeysSkipped);
```

## SQLite şeması (Configurite 1.0)

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
-- Bilinen anahtarlar: SchemaVersion, EncryptionSalt (base64), EncryptionAlgorithm.
```
