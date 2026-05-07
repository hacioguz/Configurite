# `appsettings.json`'dan geçiş

## Tek seferlik geçiş

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

Console.WriteLine($"{result.FilesProcessed} dosya / {result.KeysWritten} anahtar / {result.KeysEncrypted} şifreli");
```

## Dosya adı kuralları

| Dosya | Environment sütunu |
|---|---|
| `appsettings.json` | `NULL` (global) |
| `appsettings.Development.json` | `Development` |
| `appsettings.Production.json` | `Production` |
| `appsettings.Anything.json` | `Anything` |

`MigrationOptions.EnvironmentOverride` ile geçersiz kılınabilir.

## JSON nasıl satıra dönüşür?

| JSON | SQLite anahtarı | Değer |
|---|---|---|
| `"AppName": "Demo"` | `AppName` | `Demo` |
| `"Logging": { "LogLevel": { "Default": "Info" } }` | `Logging:LogLevel:Default` | `Info` |
| `"Hosts": [ "a.local", "b.local" ]` | `Hosts:0`, `Hosts:1` | `a.local`, `b.local` |
| `"Forecast": { "Days": 7, "Enabled": true }` | `Forecast:Days`, `Forecast:Enabled` | `7`, `true` |

Bu .NET yapılandırma sistemiyle uyumludur — mevcut `IOptions<T>` bağlamaları olduğu gibi çalışmaya devam eder.

## Şifreleme desenleri

`EncryptKeyPatterns`, glob ifadelerinin listesidir. `*` segment sınırları boyunca herhangi sayıda karakteri eşler:

| Desen | Eşleşir |
|---|---|
| `ConnectionStrings:*` | `ConnectionStrings:Default`, `ConnectionStrings:ReadOnly` |
| `*:Password` | `Auth:Password`, `Database:Admin:Password` |
| `Auth:*Token` | `Auth:AccessToken`, `Auth:RefreshToken` |

Eşleştirme büyük/küçük harf duyarsızdır.

## Idempotency (Yinelenebilirlik)

Varsayılan olarak `Overwrite = true` — geçişin tekrar çalıştırılması eşleşen satırları değiştirir. Elle düzenlenmiş değerleri korumak için `Overwrite = false` yapın; sonuç `KeysSkipped` raporlar.

## Önerilen iş akışı

1. Configurite'ı bir feature dalında, geçiş çağrısını `if (!File.Exists(dbPath))` ile koruyarak ekle.
2. Lokalde bir kez çalıştır; oluşan `appsettings.db`'yi commit'le (sakıncası yok — şifreli sırlar ana anahtar olmadan işe yaramaz).
3. Eski `appsettings*.json` dosyalarını sil (veya acil rollback için sakla).
4. `*.db-journal`, `*.db-wal`, `*.db-shm` desenlerini `.gitignore`'a ekle.
