# Şifreleme modeli

## Algoritma

| Katman | Tercih | Gerekçe |
|---|---|---|
| Şifre | **AES-256-GCM** | Authenticated encryption — kurcalama çözüm sırasında hata fırlatır. .NET 8'de native `AesGcm`. |
| KDF | **PBKDF2-HMAC-SHA256** | OWASP önerili, BCL-native (üçüncü parti yok). |
| İterasyon | **200.000** | OWASP 2023 SHA-256 minimumu. |
| DB başına salt | **32 byte** rastgele | `Metadata.EncryptionSalt`'ta şifresiz saklanır. |
| Değer başına nonce | **12 byte** rastgele | Aynı düz metin asla aynı şifreli metni üretmez. |
| Auth tag | **16 byte** | GCM standardı. |

## Diskte düzen (her şifreli değer)

```
base64( nonce(12) ‖ ciphertext(n) ‖ tag(16) )
```

`Configuration` tablosunda bir satır:

| Sütun | Şifreli değer | Düz değer |
|---|---|---|
| `Key` | `ConnectionStrings:Default` | `AppName` |
| `Value` | `dhO+FTdRdGm…` | `Configurite Demo` |
| `IsEncrypted` | `1` | `0` |

## Ana anahtar çözümlemesi

```text
ConfiguriteOptions.MasterKey  ──►  CONFIGURITE_MASTER_KEY env  ──►  ~/.configurite/master.key
```

Bir satır `IsEncrypted = 1` ama resolver hiçbir şey döndürmüyorsa, `Load()` mevcut kaynakları açıklayan bir `InvalidOperationException` fırlatır.

## Anahtar rotasyonu

1. Eski anahtarla çöz (yapılandırmayı normal şekilde yükle).
2. Her `IsEncrypted = 1` satırı oku.
3. Yeni anahtar + taze üretilmiş salt ile yeniden şifrele.
4. `Metadata.EncryptionSalt` değerini değiştir ve satırı yeniden yaz.

Rotasyon için yerleşik bir helper sonraki sürümlerde gelecek; o zamana kadar yukarıdaki döngü migrator'ın açtığı public store API ile kolayca yapılabilir.

## Tehdit modeli

Configurite **şuna karşı** korur:

- Diskten SQLite dosyasını okuyan saldırgan (yedek bant, sızdırılmış konteyner imajı, kayıp dizüstü).
- Veritabanını sızdıran yanlış yapılandırılmış bir Git push (gözle taranacak düz metin yok).
- Şifreli değerlerde kötü niyetli değişiklik (GCM tag doğrulaması).

Configurite **şuna karşı korumaz**:

- Hem veritabanı dosyasına hem ana anahtara sahip saldırgan.
- Çalışan sürecin bellek dump'ı.
- Encryptor'ı değiştiren ele geçirilmiş uygulama binary'si.

Derinlemesine savunma için Configurite'ı OS düzeyi gizli depolarla (Vault, KMS, Keychain) birleştirin.
