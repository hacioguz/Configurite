# Başlangıç

## 1. Kurulum

```bash
dotnet add package Configurite
```

## 2. Ana anahtar sağla

Configurite ana anahtarı asla saklamaz. **Bir** kaynak seç — resolver bunları sırayla dener:

| Öncelik | Kaynak | Örnek |
|---|---|---|
| 1 | `ConfiguriteOptions.MasterKey` | `opts.MasterKey = "…"` |
| 2 | Ortam değişkeni | `export CONFIGURITE_MASTER_KEY="…"` |
| 3 | Anahtar dosyası | `~/.configurite/master.key` (tek satır) |

Hiç şifreli değer yazmıyorsan ana anahtar gerekmez.

## 3. Provider'ı bağla

```csharp
using Configurite;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfigurite(opts =>
{
    opts.DatabasePath    = "appsettings.db";
    opts.Environment     = builder.Environment.EnvironmentName;
    opts.CreateIfMissing = true;   // ilk çalıştırmada şemayı oluştur
    opts.ReloadOnChange  = true;   // canlı düzenlemeleri yakala
});
```

## 4. Yapılandırmayı her zamanki gibi oku

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
    Greeting        = cfg["Greeting"],
    ConnString      = cfg["ConnectionStrings:Default"], // okuma sırasında çözüldü
    DefaultLogLevel = cfg["Logging:LogLevel:Default"],
});
```

## 5. (İsteğe bağlı) Mevcut JSON'u geçir

[appsettings.json'dan geçiş](gecis.md) bölümüne bakın. Tek çağrı tüm JSON dosyalarını değiştirir:

```csharp
using var migrator = new JsonToSqliteMigrator("appsettings.db");
migrator.MigrateDirectory(AppContext.BaseDirectory, "appsettings", new MigrationOptions
{
    EncryptKeyPatterns = { "ConnectionStrings:*", "*:Password", "*:ApiKey" }
});
```
