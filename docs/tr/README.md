# Configurite — Türkçe Dokümantasyon

> ASP.NET Core / .NET 8+ için güvenli, şifrelenmiş SQLite tabanlı yapılandırma sağlayıcısı.

## Neden Configurite?

`appsettings.json` gizli verileri düz metin olarak taşır. Configurite onu şifrelenmiş bir SQLite dosyasıyla değiştirir, standart `IConfiguration` API'sini korur ve şunları ekler:

- Değer başına **AES-256-GCM** şifreleme (PBKDF2-HMAC-SHA256, 200K iterasyon).
- `FileSystemWatcher` ve `IChangeToken` ile **hot reload**.
- Tek dosyada **ortam-bilinçli** override'lar (`Development`, `Production`, …).
- Mevcut tüm `appsettings*.json` dosyalarından **tek seferlik geçiş**.

## İçindekiler

1. [Başlangıç](baslangic.md)
2. [Şifreleme modeli](sifreleme.md)
3. [appsettings.json'dan geçiş](gecis.md)
4. [Hot reload](hot-reload.md)
5. [Anahtar rotasyonu](anahtar-rotasyonu.md)
6. [CLI aracı](cli.md)
7. [Yönetim paneli](yonetim-paneli.md)
8. [Performans](performans.md)
9. [Güvenlik mimarisi](guvenlik.md)
10. [Gözlemlenebilirlik](gozlemlenebilirlik.md)
11. [PostgreSQL arka ucu](postgres-arka-uc.md)
12. [Sürümleme politikası](surumleme.md)
13. [API referansı](api-referansi.md)

## 30 saniyede başla

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

Sonra dilediğin yerde:

```csharp
public sealed class HomeController(IConfiguration cfg) : Controller
{
    public string ApiKey => cfg["Auth:ApiKey"]!; // Load sırasında çözüldü
}
```

## Lisans

MIT — bkz. [LICENSE](../../LICENSE).
