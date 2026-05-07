---
title: Configurite — ASP.NET Core için şifrelenmiş SQLite tabanlı yapılandırma
date: 2026-04-29
tags: [.NET, ASP.NET Core, yapılandırma, güvenlik, SQLite]
---

# Configurite ile Tanışın

> *`appsettings.json` için tak-çalıştır alternatif. AES-256-GCM şifreleme, hot reload, denetim günlüğü, web yönetim paneli ve dotnet tool — 4 paket tek koordineli sürüm olarak yayınlanıyor.*

Bir takım arkadaşının PR'ını açıp `appsettings.Production.json`'ın gerçek veritabanı parolaları taşıdığını gördüyseniz, bu yazı sizin için.

## `appsettings.json`'ın Sorunu

`appsettings.json` .NET'in fiili çalışma zamanı yapılandırması. Sırlar olmayan şeyler için (log level, feature flag, ayarlanabilir parametreler) harika; geri kalanı için tehlikeli:

- **Diskte düz metin.** Dosya okuma erişimi olan herkes (CI logları, konteyner imajları, yedek bantlar, kazara git push'ları) sırlarınızı görür.
- **Denetim yok.** Geçen Salı `ConnectionStrings:Default`'u kim değiştirdi? Bilen yok.
- **Ortam başına bir dosya.** Override semantiği net ama dosya çoğalması gerçek bir dert.
- **Düzenleme = redeploy.** Operasyon kılavuzları "appsettings.json'u düzenle ve pod'u yeniden başlat" diyor... 2026'da.

Cloud yanıtı "secrets manager kullan" — Vault, KMS, Key Vault. Çalışırlar. Aynı zamanda çalıştırmanız, güvende tutmanız, gözlemlemeniz ve para ödemeniz gereken başka bir altyapı parçası. Global sır omurgası ihtiyacı olmayan servisler için bu cevap aşırılıktır.

**Configurite ortadaki çözüm.** Tek bir SQLite dosyası `appsettings.json`'ın yerine geçer. Şifreli olarak işaretlediğiniz değerler AES-256-GCM + PBKDF2-HMAC-SHA256 anahtar türetimi ile korunur. Düz değerler düz kalır (`Logging:LogLevel:Default` şifrelenmek zorunda değil). Tüm bu standart `IConfiguration` API'sini, hot reload'u ve dotnet ekosistem rahatlığını korur.

## Yazdığınız Şey Şu

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

Hepsi bu. İlk çalıştırma şemayı oluşturur, sonraki her çalıştırma SQLite'tan yükler. `IConfiguration["ConnectionStrings:Default"]` çözülmüş bir string'i şeffaf olarak döndürür. `IOptionsMonitor<T>` standart hot reload pipeline'ı üzerinden dosya değişikliklerine tepki verir.

## Kutudan Çıkar Çıkmaz Ne Alıyorsunuz?

8.5 / 9.5 / 10.5 sürümü **dört NuGet paketini** lockstep yayınlıyor:

| Paket | Amaç | Boyut |
|---|---|---|
| `Configurite` | Yapılandırma sağlayıcı kütüphanesi. | 40 KB |
| `Configurite.Cli` | `dotnet configurite` global aracı — migrate, rotate, audit, export. | 13 MB |
| `Configurite.AdminUI` | ASP.NET Core uygulamalarına tak-çalıştır web yönetim paneli. | 20 KB |
| `Configurite.Postgres` | PostgreSQL ile desteklenen `IConfiguriteStore` uygulaması. | 10 KB |

Bir, iki veya dördünü birden kurarsınız. Birlikte sürümlenirler: `Configurite 9.x` kurarsanız `Configurite.AdminUI 9.x`'i kaynak-uyumlu bilerek alabilirsiniz.

## Geçiş: JSON yayınlamayı bırakın, veritabanı yayınlamaya başlayın

```bash
dotnet configurite migrate ./appsettings.db ./config-folder \
    --encrypt "ConnectionStrings:*" \
    --encrypt "*:Password" \
    --encrypt "*:ApiKey"
```

Bu tek komut:

1. `./config-folder` içindeki her `appsettings.json` ve `appsettings.{Env}.json`'ı okur.
2. Yuvalanmış anahtarları .NET yapılandırma sisteminin kullandığı `:`-ayrılı yollara düzleştirir.
3. Her dosya adından ortamı algılar (`.Production.json` → `Environment = "Production"`).
4. Düz değerleri doğrudan yazar; glob desenlerinden herhangi biriyle eşleşen değerleri şifreler.
5. Tek satırlık özet rapor verir: `migrated 2 file(s): 9 keys written, 2 encrypted`.

Sonuçta oluşan `appsettings.db`'yi commit edebilirsiniz — ana anahtar olmadan şifreli byte'ları kurtarmak hesaplama açısından yapılamaz (AES-256, PBKDF2 200 K iterasyon, taze veritabanı saltı). Düz değerler şema gereği görülür; sadece hassas yollar şifrelenmeli.

Ters yönde dışa aktarıcı da var:

```bash
dotnet configurite export ./appsettings.db ./out --per-env --decrypt --master-key "$KEY"
```

İstediğinizde JSON'a geri dönebilirsiniz. (Ya da config'inizin zaman içinde sürümlerini karşılaştırabilirsiniz; denetim günlüğü bunu daha da kolaylaştırıyor.)

## Şifreleme Hikayesi

Crypto katmanları ve neden her biri:

- **AES-256-GCM** veri şifresi olarak. Authenticated encryption — kurcalama okunduğunda hata fırlatır. NIST onaylı, .NET 8'den beri BCL'de (`AesGcm`).
- **PBKDF2-HMAC-SHA256** 200 000 iterasyonla anahtar türetimi. SHA-256 için OWASP minimumu, BCL-native. Argon2id'yi düşündük — GPU saldırılarına karşı modern kazanan, ama üçüncü parti crypto paketi eklemek güven yüzeyini büyütürdü ve config-sırrı kullanım durumu için cömert iterasyonlu PBKDF2 yeterli. .NET 11'de yeniden değerlendireceğiz.
- **DB başına 32 byte salt**, `Metadata` tablosunda saklanır, ana anahtar yanına asla yazılmaz.
- **Şifreleme başına 12 byte nonce** `RandomNumberGenerator.GetBytes` ile. Aynı düz metin asla aynı şifreli metni üretmez.
- **Ana anahtar** fallback zincirinden çözümlenir: option → env var → `~/.configurite/master.key` anahtar dosyası. Veritabanında asla saklanmaz. Operasyonel hikâyeyi detaylı belgeliyoruz; kısaca: anahtarı veriyle aynı yere koymayın.

Tüm bu [docs/tr/guvenlik.md](https://github.com/hacioguz/configurite/blob/main/docs/tr/guvenlik.md)'de belgelendi — tehdit modeli, beş somut saldırı senaryosu ve altı maddelik üretim kontrol listesi.

## Gerçekten Çalışan Hot Reload

`ReloadOnChange = true` ve bitti:

```csharp
public sealed class GreetingService(IOptionsMonitor<GreetingOptions> opts)
{
    public string CurrentMessage => opts.CurrentValue.Message;
    public void StartListening() => opts.OnChange(o => Console.WriteLine($"yeni: {o.Message}"));
}
```

Altta:

1. Veritabanı klasörü üzerinde `FileSystemWatcher`, `.db` + `-journal` / `-wal` / `-shm` kardeşleri için filtreleme.
2. 250 ms debounce — tek bir SQLite commit'i birden çok dosya event'i fırlatır, hepsi tek reload'a birleştirilir.
3. Provider yeniden okur, çözer, `IChangeToken` üzerinden `OnReload()` sinyali verir. `IOptionsMonitor<T>` yayar.
4. Geri çağrımdaki başarısızlıklar (yazma sırasında geçici dosya kilidi) yutulur; izleyici hayatta kalır.

Üç .NET sürümü altında (8 / 9 / 10) Linux, macOS ve Windows'ta test edildi.

## Atomik Anahtar Rotasyonu

Yerleşik bir helper, kesinti olmadan ana anahtarı rotasyona sokar:

```csharp
using var rotator = new ConfiguriteKeyRotator("appsettings.db");
var result = rotator.Rotate(oldKey, newKey);
Console.WriteLine($"{result.RowsRotated} satır yeni anahtar altında yeniden şifrelendi.");
```

Veya CLI'dan:

```bash
configurite rotate ./appsettings.db --old "$OLD_KEY" --new "$NEW_KEY"
```

Tüm rotasyon tek bir SQLite transaction'da çalışır:

1. Her `IsEncrypted = 1` satırı eski anahtarla bellekte çöz.
2. **Begin transaction.** Yeni salt üret, `Metadata`'ya yaz. Yeni encryptor kur. Her satırın ciphertext'ini değiştir.
3. **Commit.** Veya herhangi bir hatada tamamen geri al — veritabanı **eski** anahtarda kalır. Kısmi durum yoktur.

Yanlış eski anahtar herhangi bir yazma yapılmadan **önce** `CryptographicException` fırlatır. Orijinal ciphertext'i byte-byte yakalayıp başarısız rotasyondan sonra dokunulmadığını doğrulayan testimiz var.

## Yönetim Paneli

Bazen bir değeri düzenlemeniz gerekir. Bazen ne olduğunu görmek istersiniz.

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddConfiguriteAdmin(opts =>
{
    opts.DatabasePath    = "appsettings.db";
    opts.DefaultLanguage = "tr";   // veya "en"
});

app.MapConfiguriteAdmin("/configurite-admin")
   .RequireAuthorization("ConfiguriteAdmin");
```

`/configurite-admin`'de üç sayfa:

- **Panel** — toplam satır, şifreli sayısı, ortamlar, şema sürümü, denetim sayısı.
- **Anahtarlar** — gözat, ortama göre filtrele, ekle, sil, şifreli değerleri talep üzerine göster.
- **Denetim günlüğü** — son 200 girdi, ters kronolojik, kim ne zaman ne değiştirdi.

`~20 KB` bağımlılıksız sunucu-rendered HTML — JS framework yok, Razor compilation yok, Blazor runtime yok. Bilerek minimal çünkü **auth size ait** (`RequireAuthorization`), ve admin UI karmaşıklığı üretimde ayağa basabilir.

Denetim günlüğü tablosu aynı SQLite veritabanında (`AuditLog`); admin UI üzerinden değişiklikler her geldiğinde otomatik dolar. Programatik `IConfiguriteAuditLog` kendi kodunuzdan yazmanıza da izin verir.

## Gözlemlenebilirlik

Bir `ILogger` geçirin:

```csharp
opts.Logger = loggerFactory.CreateLogger("Configurite");
```

Beş `EventId`-tagged mesaj alırsınız: load tamamlandı, optional eksik, required eksik, reload tamamlandı, reload başarısız. Hepsi `LoggerMessage` source generator ile derlenmiş — seviye kapalıyken sıfır tahsis.

OpenTelemetry `ActivitySource` ve `Meter`'a abone olun:

```csharp
.WithTracing(t => t.AddSource(ConfiguriteTelemetry.SourceName))
.WithMetrics(m => m.AddMeter(ConfiguriteTelemetry.MeterName))
```

Activity'ler ve metrikler:

- `Configurite.Load` activity, `database`, `environment`, `rows.total`, `rows.decrypted` tag'leri ile.
- `configurite.loads.total`, `configurite.reloads.total`, `configurite.decryptions.total`, `configurite.watcher.errors.total` sayaçları.
- `configurite.load.duration` histogramı (p50/p99 hazır).

Standart .NET araçları, standart bağlama, sıfır özel telemetri pipeline'ı.

## Performans

Aşağıdaki sayılar BenchmarkDotNet `--job short` macOS / Apple Silicon'dan. Yeniden üretmek: `dotnet run --project benchmarks/Configurite.Benchmarks -c Release`.

| İşlem | Maliyet |
|---|---|
| AES-GCM encrypt 16 B | 2.0 µs |
| AES-GCM encrypt 4 KB | 4.2 µs |
| PBKDF2 türet (200 K iter) | 30.5 ms (süreç başına bir kez) |
| `TryGet` | 6.6 µs sabit |
| `Upsert` (WAL+synchronous=NORMAL sonrası) | 63 µs |
| Provider load 1 000 düz satır | ~700 µs |
| Provider load 1 000 satır, %50 şifreli | ~35 ms |

Manşet: şifreli sırları olan bir uygulama startup'ta PBKDF2 için ~30 ms ödenir; sonrasında satır başına çözüm maliyeti ihmal edilebilir. Şifreleme kullanmayan bir uygulama 1 000 anahtar için ~700 µs öder. Configurite hiçbir zaman istek hot path'ında değildir.

## Anlamlı Sürümleme

Configurite **TFM-major sürümleme** kullanır — paket sürümünün major bileşeni .NET hedef framework'ünü yansıtır. Library, CLI, AdminUI ve Postgres **lockstep** yayınlanır.

| .NET | Paket hattı |
|---|---|
| `net8.0` | `Configurite 8.x.y` |
| `net9.0` | `Configurite 9.x.y` |
| `net10.0` | `Configurite 10.x.y` |

Floating aralıklarla bir hatta sabitleyin:

```xml
<PackageReference Include="Configurite" Version="8.*" />
```

.NET 9'a geçmek için `9.*`'a çevirirsiniz. Aradan **sürpriz major bump çıkmaz**. Her sürüm tek kaynak ağacından üç ayrı `.nupkg` üretir — aynı kod, üç TFM, üç sürüm.

## Sıradaki

8.x'te yer alan public yüzeye bağlıyız. Beklenenler:

- **8.6 / 9.6 / 10.6**: `ConfiguriteOptions.StoreFactory` — provider sabit kodlu SQLite store yerine herhangi bir `IConfiguriteStore` (Postgres, kendi arka ucunuz) doğrudan kullanabilsin.
- **8.7 / 9.7 / 10.7**: Tahrif kanıtı için hash zincirli append-only denetim günlüğü (mevcut denetim günlüğü aynı DB'de ve write erişimi olan herkes tarafından düzenlenebilir — bunu belgeliyoruz; bir sonraki minor kapatıyor).
- **9.x**: .NET 11 yayınlandığında Argon2id anahtar türetimi yeniden değerlendirme.
- **2.0**: Kararlaştırılacak. Dinliyoruz.

## Deneyin

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

Kaynak, tam çift dilli dokümantasyon (İngilizce + Türkçe) ve benchmark'lar repo'da. Site her main push'unda GitHub Pages'e otomatik yayınlanıyor.

## Neden?

Çünkü ayrı bir secrets manager *veya* repo'nuzdaki düz metin sırlar olmadan bir .NET servisi yayınlayabilmelisiniz. Çünkü `appsettings.json` problemin yanlış yarısını çözdü. Çünkü kripto zordur ve kolay varsayılan değilse insanlar yanlış primitif (veya hiç) seçer.

Configurite istediğimiz kolay varsayılandı. Umarız sizin için de kolay varsayılandır.

— *Configurite katkıda bulunanları, 2026-04-29*
