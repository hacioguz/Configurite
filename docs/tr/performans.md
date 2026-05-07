# Performans

> Aşağıdaki sayılar tek bir koşudan macOS / Apple Silicon üzerinde (.NET 8, BenchmarkDotNet 0.13, `--job short`) elde edildi. Yeniden üretmek için: `dotnet run --project benchmarks/Configurite.Benchmarks -c Release`.

Amaç *büyüklük mertebesi* hissi vermek, satıcılarla karşılaştırılabilir mutlak sayılar değil — donanımınız, OS'unuz ve SQLite sürümünüze göre değişir.

## Şifreleme (`AesGcmConfigEncryptor`)

| İşlem | Yük | Ortalama | Tahsis |
|---|---:|---:|---:|
| Encrypt | 16 B | **2.0 µs** | 464 B |
| Encrypt | 256 B | 2.2 µs | 1.8 KB |
| Encrypt | 4 KB | 4.2 µs | 23 KB |
| Decrypt | 16 B | **2.0 µs** | 376 B |
| Decrypt | 256 B | 2.4 µs | 1.6 KB |
| Decrypt | 4 KB | 6.9 µs | 20 KB |
| **PBKDF2 anahtar türetimi** (200 000 iterasyon, süreç başına tek seferlik) | — | **30.5 ms** | 159 B |

**Sonuç**: AES-GCM tek başına yapılandırma sırrı boyutlarında pratikte bedava (tek haneli mikrosaniye). PBKDF2 maliyeti süreç başına tek seferliktir ve tüm şifreli satırlar arasında amorti edilir.

## Store (`SqliteConfiguriteStore`)

> Sayılar Phase 17 optimizasyonlarını yansıtır: `journal_mode=WAL` + `synchronous=NORMAL` `EnsureSchema`'da bir kez uygulanır, sözlük 64 kapasite ile başlar.

| İşlem | Satır sayısı | Ortalama | Tahsis |
|---|---:|---:|---:|
| `TryGet` (var olan anahtar) | herhangi | **6.6 µs** | 1.3 KB |
| `ReadAll` | 10 | 17 µs | 5.3 KB |
| `ReadAll` | 100 | 81 µs | 20 KB |
| `ReadAll` | 1 000 | 727 µs | 221 KB |
| `Upsert` (yeni anahtar) | herhangi | **63 µs** | 1.8 KB |

**Sonuçlar**:
- `TryGet` sabit zaman (indeks — DB boyutundan bağımsız).
- `Upsert` `synchronous=NORMAL` sayesinde optimize edilmemiş baseline'a kıyasla ~5x hızlandı (hâlâ WAL-güvenli). Daha fazla amorti için yazımları tek bağlantı içinde grupla.
- `ReadAll` satır sayısıyla yaklaşık lineer ölçeklenir; tipik konfigürasyonlar için resize churn'unu önlemek için sözlük cömert kapasiteyle (64) başlar.

### Phase 17 öncesi/sonrası

| İşlem | Önce | Sonra | Δ |
|---|---:|---:|---:|
| `TryGet` | 7.4 µs | 6.6 µs | **-11%** |
| `Upsert` | 310 µs | 63 µs | **-80%** |
| `ReadAll` 1 000 (tahsis) | 251 KB | 221 KB | -12% |

## Provider yükleme

| Senaryo | Satır sayısı | Ortalama | Tahsis |
|---|---:|---:|---:|
| Sadece düz değerler | 10 | **43 µs** | 13 KB |
| Sadece düz değerler | 100 | 94 µs | 44 KB |
| Sadece düz değerler | 1 000 | 600 µs | 359 KB |
| %50 şifreli (anahtar çözümlemeli) | 10 | **38 ms** | 16 KB |
| %50 şifreli | 100 | 39 ms | 67 KB |
| %50 şifreli | 1 000 | 34 ms | 580 KB |

**Sonuç**: Şifreli sırlar kullanan bir uygulama startup'ta PBKDF2 için ~30 ms ödenir; sonrasında satır başına çözüm maliyeti ihmal edilebilir (birkaç µs). Şifreleme **kullanmayan** bir uygulamada tüm yapılandırma yüklemesi ~100 anahtara kadar milisaniye altıdır.

## Pratikte ne anlama geliyor

| Uygulama profili | Beklenen startup overhead'ı |
|---|---|
| Küçük servis, düz yapılandırma | ~50 µs |
| Tipik web API, ~100 anahtar, şifreleme yok | ~100 µs |
| Şifreli sırları olan üretim uygulaması | ~30 ms (tek seferlik PBKDF2) |
| Büyük monolit, ~1 000 anahtar, yarısı şifreli | ~35 ms |

Configurite hiçbir istek hot-path'ında değil — tek ilgili sayılar *startup* ve *hot reload*. İkisi de makul herhangi bir hedef altında bitiyor.

## Yeniden üretme

```bash
# Tüm benchmark'lar (uzun, ~5 dk):
dotnet run --project benchmarks/Configurite.Benchmarks -c Release

# Kısa form, ~1 dk, daha düşük hassasiyet:
dotnet run --project benchmarks/Configurite.Benchmarks -c Release -- --job short

# Tek sınıfa filtrele:
dotnet run --project benchmarks/Configurite.Benchmarks -c Release -- --filter '*EncryptionBenchmarks*'
```

BenchmarkDotNet detaylı raporları `BenchmarkDotNet.Artifacts/results/` altına yazar (Markdown, HTML, CSV).
