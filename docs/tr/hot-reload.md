# Hot reload

## Aktivasyon

```csharp
builder.Configuration.AddConfigurite(opts =>
{
    opts.DatabasePath   = "appsettings.db";
    opts.ReloadOnChange = true;
});
```

Bu durumda provider veritabanını içeren klasörü izler ve `.db`, `-journal`, `-wal` veya `-shm` dosyası değiştiğinde `IChangeToken` tetikler. `IOptionsMonitor<T>` kullanan tüketiciler otomatik tepki verir.

## Mekanizma

1. Veritabanı klasörü üzerinde `FileSystemWatcher`, dört SQLite dosya adıyla filtrelenmiş.
2. 250 ms'lik debounce penceresi tek bir commit'ten gelen olay burst'ünü tek bir reload'a çevirir.
3. Provider veritabanını yeniden okur, şifreli satırları aynı ana anahtarla çözer ve `OnReload()` sinyalini verir.

Geri çağrım içindeki hatalar (örn. yazma sırasında geçici dosya kilidi) yutulur — izleyici hayatta kalır ve bir sonraki olayda tekrar dener.

## Değişikliklere tepki verme

```csharp
public sealed class GreetingService
{
    private readonly IOptionsMonitor<GreetingOptions> _opts;

    public GreetingService(IOptionsMonitor<GreetingOptions> opts)
    {
        _opts = opts;
        _opts.OnChange(o => Console.WriteLine($"yeni mesaj: {o.Message}"));
    }

    public string CurrentMessage => _opts.CurrentValue.Message;
}
```

Herhangi bir araçtan (CLI, admin UI, kendi kodun) bir satırı güncellemek, süreci yeniden başlatmadan değişikliği tetikler.

## Dikkat edilecekler

- Bazı ağ dosya sistemleri ve Docker bind mount'ları güvenilmez olaylar yayar; hedef ortamda test edin.
- Hot reload yalnızca Configurite'ı yeniden değerlendirir. Diğer yapılandırma kaynakları (env vars, komut satırı) etkilenmez.
- Uygulama çalışırken ana anahtar rotasyonu yapılırsa, eski encryptor dispose edilene kadar bellekte kalır — yeni anahtarı almak için yeniden başlatın.
