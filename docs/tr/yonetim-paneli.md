# Yönetim Paneli

> `Configurite.AdminUI` 8.4 / 9.4 / 10.4'ten itibaren mevcut.

Configurite veritabanları için tak-çalıştır web yönetim paneli. Anahtarları gözatın, değerleri düzenleyin (düz veya şifreli), denetim günlüğünü tarayıcıdan ayrılmadan inceleyin. Çift dilli (İngilizce / Türkçe), front-end framework dependency'si yok, ~20 KB nupkg.

## Kurulum

```bash
dotnet add package Configurite.AdminUI
```

## Bağlama

```csharp
using Configurite;
using Configurite.AdminUI;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddConfigurite("appsettings.db");

builder.Services.AddHttpContextAccessor();
builder.Services.AddConfiguriteAdmin(opts =>
{
    opts.DatabasePath    = "appsettings.db";
    opts.DefaultLanguage = "tr";   // veya "en"
    opts.EnableAuditLog  = true;
});

var app = builder.Build();

app.MapConfiguriteAdmin("/configurite-admin")
   .RequireAuthorization("ConfiguriteAdmin");   // Auth politikasını SİZ yazarsınız

app.Run();
```

Hepsi bu. `/configurite-admin` adresine gidin.

## Sayfalar

| Yol | Amaç |
|---|---|
| `/configurite-admin/` | Panel — toplam satır, şifreli sayısı, ortamlar, şema sürümü, denetim sayısı. |
| `/configurite-admin/keys` | Gözat / ortama göre filtrele / ekle / düzenle / sil / şifreliyi göster. |
| `/configurite-admin/audit` | Ters kronolojik denetim günlüğü (son 200 girdi). |

Herhangi bir URL'ye `?lang=tr` (veya `?lang=en`) ekleyerek dili değiştirin. Tercih bir `configurite_lang` çerezinde tutulur.

## Denetim günlüğü

`EnableAuditLog = true` (varsayılan) iken, arayüz üzerinden yapılan her `Upsert` ve `Delete` SQLite `AuditLog` tablosuna kaydedilir. `User` sütunu `HttpContext.User.Identity.Name` değerini yakalar; bu nedenle auth pipeline'ınızın yönetim endpoint'lerinden **önce** çalıştığından emin olun.

Denetim günlüğünü programatik olarak da kullanabilirsiniz:

```csharp
IConfiguriteAuditLog audit = new SqliteConfiguriteAuditLog("appsettings.db");
audit.Record("CustomOp", "MyKey", "Production", "ci-bot");
foreach (var entry in audit.ReadRecent(50)) { /* … */ }
```

## Güvenlik notu

Yönetim arayüzü **varsayılan olarak kimlik doğrulamasız** — bunu kasıtlı olarak yaptık; kendi politikanızın arkasına yerleştirirsiniz. Önerilen desen:

```csharp
app.MapConfiguriteAdmin("/configurite-admin")
   .RequireAuthorization(policy => policy
       .RequireAuthenticatedUser()
       .RequireRole("Admin"));
```

İç/intranet uygulamaları için ağ düzeyi kısıtlama (VPN/IP allowlist arkasında) da kabul edilebilir.

UI, kullanıcı **Göster**'e tıklayana kadar şifreli değerleri görüntülemez. Reveal işlemi anahtar+env bilgisini URL'de geçirir; tarayıcı bağlamında master-key resolver zincirine güvenmiyorsanız ana anahtarı tamamen atın — şifreli satırlar opak kalır.

## Şema geçişi

Yönetim arayüzü şema **sürüm 2** gerektirir (8.4 / 9.4 / 10.4'te eklendi). İlk çalıştırmada `EnsureSchema()` `AuditLog` tablosunu ekler ve `Metadata.SchemaVersion`'ı `"1"`den `"2"`ye yükseltir. Mevcut veriler dokunulmadan kalır.
