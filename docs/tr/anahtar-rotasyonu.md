# Anahtar rotasyonu

> `Configurite` 1.1'den itibaren mevcut.

## Neden rotasyon?

Ana anahtarlar sızar. Uyumluluk rejimleri periyodik rotasyon ister. Yerleşik bir rotator, manuel SQL veya elle yazılmış script'ler olmadan ana anahtarı değiştirmenizi sağlar.

## API

```csharp
using var rotator = new ConfiguriteKeyRotator("appsettings.db");
var result = rotator.Rotate(oldMasterKey: "eski", newMasterKey: "yeni");

Console.WriteLine($"{result.RowsRotated} satır yeni anahtar altında yeniden şifrelendi.");
```

## Atomicity garantisi

Tüm rotasyon tek bir SQLite işleminin (transaction) içinde çalışır:

1. **Okuma fazı** — her `IsEncrypted = 1` satırı eski anahtarla bellekte çözülür.
2. **Yazma fazı (transaction)** — taze 32-byte salt yazılır, sonra her satır yeni anahtar altında şifreli metinle değiştirilir.
3. **Commit** — yalnızca her satır başarılı olduğunda.

Bir şey başarısız olursa — yanlış eski anahtar, IO hatası, süreç çökmesi — transaction geri alınır ve veritabanı tamamen **eski** anahtarda kalır. Yarı rotasyon durumu yoktur.

## Tarif: zamanlanmış rotasyon

```csharp
public sealed class KeyRotationJob(ILogger<KeyRotationJob> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var oldKey = Environment.GetEnvironmentVariable("CONFIGURITE_OLD_KEY")!;
                var newKey = Environment.GetEnvironmentVariable("CONFIGURITE_MASTER_KEY")!;

                using var rotator = new ConfiguriteKeyRotator("appsettings.db");
                var result = rotator.Rotate(oldKey, newKey);

                log.LogInformation("{Count} satır rotasyona girdi", result.RowsRotated);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Rotasyon başarısız; veritabanı değişmedi.");
            }

            await Task.Delay(TimeSpan.FromDays(90), stoppingToken);
        }
    }
}
```

## Sınır durumlar

| Senaryo | Davranış |
|---|---|
| Henüz şifreli satır yok | Transaction yine de salt'ı yeniler. `RowsRotated = 0`. |
| Sadece düz değer içeren ve hiç salt yazılmamış DB | `InvalidOperationException` ("rotasyona alınacak şey yok"). |
| Yanlış eski anahtar | `CryptographicException`; veritabanı değişmedi. |
| Boş `oldMasterKey` veya `newMasterKey` | `ArgumentException`. |
| Aynı eski + yeni anahtar | İzinli — salt yine rotasyona girer, ciphertext'ler değişir. |

## Hot reload ile birleştirme

Provider'ınız `ReloadOnChange = true` ile çalışıyorsa, rotasyon transaction'ı commit edildikten sonra watcher tetiklenir. Provider bir sonraki `Load()`'ta decryptor'ünü **yeni** ana anahtarla yeniden kurar. Rotator çalışmadan önce `ConfiguriteOptions.MasterKey` (veya `CONFIGURITE_MASTER_KEY`) yeni anahtara yönlenmiş olmalı.
