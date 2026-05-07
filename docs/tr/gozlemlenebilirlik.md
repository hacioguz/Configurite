# Gözlemlenebilirlik

> `Configurite` 8.5 / 9.5 / 10.5'ten itibaren mevcut.

Configurite kutudan çıkar çıkmaz yapılandırılmış log'lar ve OpenTelemetry sinyalleri üretir. Mevcut pipeline'ınıza her birini tek satırla bağlayın.

## ILogger

Herhangi bir `ILogger` geçirin (veya `Microsoft.Extensions.Logging.Abstractions` no-op varsayılanına güvenin):

```csharp
builder.Configuration.AddConfigurite(opts =>
{
    opts.DatabasePath = "appsettings.db";
    opts.Logger       = builder.Services.BuildServiceProvider()
                              .GetRequiredService<ILoggerFactory>()
                              .CreateLogger("Configurite");
});
```

### Olaylar

| EventId | Seviye | Mesaj |
|---|---|---|
| 1 | Information | Configurite N satır (M şifre çözüldü) DbPath'tan X ms'de yükledi. |
| 2 | Debug | Configurite veritabanı DbPath'ta yok ama Optional=true; boş yükleniyor. |
| 3 | Error | Configurite veritabanı DbPath'ta bulunamadı ve Optional=false. |
| 4 | Debug | Configurite hot-reload DbPath için tamamlandı. |
| 5 | Warning | Configurite hot-reload başarısız; izleyici devam ediyor. (exception ile) |

Tüm log ifadeleri `LoggerMessage` source generator kullanır — seviye kapalıyken sıfır tahsis.

## OpenTelemetry

### Tracing

`Configurite` `ActivitySource`'una abone olun:

```csharp
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .WithTracing(t => t
        .AddSource(ConfiguriteTelemetry.SourceName)   // "Configurite"
        .AddOtlpExporter());
```

Yayılan activity'ler:

| İsim | Tag'ler |
|---|---|
| `Configurite.Load` | `configurite.database`, `configurite.environment`, `configurite.rows.total`, `configurite.rows.decrypted` |

### Metrikler

`Configurite` `Meter`'ına abone olun:

```csharp
using OpenTelemetry.Metrics;

builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddMeter(ConfiguriteTelemetry.MeterName)     // "Configurite"
        .AddOtlpExporter());
```

Enstrümanlar:

| İsim | Tür | Açıklama |
|---|---|---|
| `configurite.loads.total` | Counter (long) | Toplam provider Load() çağrısı. |
| `configurite.reloads.total` | Counter (long) | Tetiklenen toplam hot reload. |
| `configurite.decryptions.total` | Counter (long) | Load()'da çözülen toplam değer. |
| `configurite.load.duration` | Histogram (double, ms) | Provider Load() süresi. |
| `configurite.watcher.errors.total` | Counter (long) | Dosya izleyicide yutulan hatalar. |

## Önerilen dashboard'lar

| Soru | Metrik / activity |
|---|---|
| Hot reload üretimde tetikleniyor mu? | `configurite.reloads.total` (counter rate) |
| Config yükleme ne kadar sürüyor? | `configurite.load.duration` (p50/p99 histogram) |
| Şifreli sırlar gerçekten okunuyor mu? | `configurite.decryptions.total` (counter rate) |
| İzleyici sessizce başarısız oluyor mu? | `configurite.watcher.errors.total` (> 0 ise uyar) |
| Veritabanı başına trace korelasyonu | `Configurite.Load` activity + `configurite.database` tag |

## Logging en iyi uygulamalar

- **Çözülmüş değerleri kendiniz loglamayın.** Provider yapmıyor; aşağıdaki kod da yapmamalı.
- Geliştirmede reload olayları için **Debug seviyesi**, üretimde load özetleri için **Information**.
- Information olayı (1) `RowCount`, `DecryptedCount`, `DbPath` ve `ElapsedMs` taşır — tüm okumaları sample'lamadan kapasite planlaması için kullanışlı.
