# Güvenlik mimarisi

> Son inceleme: 2026-04-28 (Configurite 8.4 / 9.4 / 10.4)

Bu doküman güvenlik mühendisleri, denetçiler ve hassas iş yükleri için Configurite'ı değerlendiren operatörler içindir.

## Özet

- **Algoritmalar**: AES-256-GCM (authenticated encryption) + PBKDF2-HMAC-SHA256 (200 000 iterasyon).
- Veritabanı başına **32 byte salt** + her şifrelemede **12 byte nonce**.
- Ana anahtar **veritabanında asla saklanmaz**.
- GCM tag doğrulamasıyla kurcalama tespit edilir; değiştirilmiş ciphertext okunduğunda hata fırlar.
- Doğrudan veya transitive bağımlılıklarda **bilinen güvenlik açığı yok** (`dotnet list package --vulnerable` 2026-04-28).

## Kriptografik primitiflər

| Katman | Tercih | Standart | Neden |
|---|---|---|---|
| Simetrik şifre | AES-256-GCM | NIST SP 800-38D | Authenticated encryption; FIPS-onaylı; .NET 8+'de native (`AesGcm`). |
| Anahtar türetimi | PBKDF2-HMAC-SHA256, 200 000 iter | RFC 8018 / OWASP 2023 | BCL-native (üçüncü parti crypto bağımlılığı yok); SHA-256 için OWASP minimumu. |
| Nonce | 12 byte rastgele | NIST SP 800-38D §8.2 | GCM standardı; `RandomNumberGenerator.GetBytes` (CSPRNG). |
| Salt | DB başına 32 byte rastgele | OWASP önerisi | `Metadata.EncryptionSalt`'ta şifresiz saklanır. |
| Auth tag | 16 byte (varsayılan) | NIST SP 800-38D | GCM standardı; tam bütünlük garantisi. |

### Neden Argon2id değil?

Argon2id GPU saldırılarına karşı modern kazanan, ama .NET BCL bunu içermiyor. Üçüncü parti bir crypto paketi eklemek güven yüzeyini büyütür — BCL-only kalmayı seçtik ve PBKDF2'yi OWASP minimumlarının üzerinde iterasyon sayısıyla kullandık. .NET 11'de yeniden değerlendirilecek.

### Ciphertext düzeni

```
base64( nonce(12) ‖ ciphertext(n) ‖ tag(16) )
```

Kesilmiş, kurcalanmış veya kısa bir yük okunduğunda `CryptographicException` fırlatır — provider bunu yapılandırma yükleme hatası olarak yansıtır, eksik değer olarak değil.

## Tehdit modeli

### Varlıklar

| Varlık | Hassasiyet |
|---|---|
| Düz yapılandırma değerleri (özellikle ConnectionStrings, ApiKeys, Passwords gibi sırlar) | Yüksek |
| Ana anahtar | Kritik |
| SQLite veritabanı dosyası | Orta (şifreli sırlar ana anahtarsız işe yaramaz) |
| Denetim günlüğü | Orta (operasyonel meta veriyi açığa çıkarır) |

### Saldırgan yetenekleri — KORUNUR

| Senaryo | Hafifletme |
|---|---|
| Saldırgan SQLite dosyasını diskten okur | Ana anahtar olmadan şifreli değerler işe yaramaz. |
| SQLite dosyası yedek banta, konteyner imajına veya Git'e sızdı | Aynı — encryption-at-rest. |
| Saldırgan saklı ciphertext'i kurcalar (bit flip, satır değiştirme) | GCM tag doğrulaması başarısız; hata fırlar. |
| Saldırgan eski şifreli değeri yeni anahtar/env'a tekrar oynatır | Her ciphertext salt'ına bağlı; rotasyon salt'ı yeniler. |
| Saldırgan aynı plaintext'in birden fazla şifrelemesini gözler | Şifreleme başına rastgele nonce — ciphertext'ler farklı. |

### Saldırgan yetenekleri — KORUNMAZ

| Senaryo | Öneri |
|---|---|
| Saldırgan hem DB dosyasına **hem de** ana anahtara sahip | Ana anahtarı OS-düzeyi gizli yöneticisinde tut (Vault, KMS, Keychain); asla bir arada bulundurma. |
| Çalışan sürecin bellek dump'ı | OS-düzeyi sıkılaştırma. |
| Ele geçirilmiş uygulama binary'si | Kod imzalama + supply-chain kontrolleri. |
| AES-GCM'e side-channel saldırılar (timing, cache) | AES-NI donanımı kullan (x64/arm64'te varsayılan). |
| Uygulama düzeyinde ana anahtar brute force | Yüksek entropili ana anahtar kullan (≥ 32 karakter rastgele). |
| DB write erişimi olan saldırgan tarafından denetim günlüğü tahrifi | Denetim günlüğü aynı DB'de; kriptografik zincir yok. v9.x yol haritasında. |

### Spesifik saldırı senaryoları

#### A1: Geliştirici yanlışlıkla `appsettings.db`'yi commit eder

✅ **Hafifletilmiş**. Ana anahtar olmadan ciphertext'leri kurtarmak hesaplama açısından yapılamaz (AES-256 + taze salt üzerinde PBKDF2 200 K iter). Düz değerler (`AppName`, `Logging:LogLevel:Default`) şema gereği görülür — sadece hassas yollar şifrelenmeli.

#### A2: Saldırgan değerin ciphertext'ini yerinde değiştirir

✅ **Hafifletilmiş**. GCM kimliği doğrular. Byte değiştirme `CryptographicException` tetikler.

#### A3: Saldırgan tüm DB'yi sahte biriyle değiştirir

⚠️ **Kısmi**. Dosya yolu örtük güvene dayanır. Saldırganın dosya sistemine yazma erişimi varsa dosyayı değiştirebilir. Out-of-band bütünlük kontrolleri (FS ACL'leri, immutable mount, deployment sistemi üzerinden checksum) Configurite'ı tamamlamalı.

#### A4: Uygulama çalışırken ana anahtar rotasyonu

✅ **Atomik**. Rotator salt + her şifreli satırı tek SQLite transaction'da günceller. Ortada çökme tam rollback yapar — uygulama kısmi durumu asla görmez.

#### A5: Denetim günlüğü tahrifi

⚠️ **Açık**. DB write erişimi olan saldırgan denetim günlüğünü düzenleyebilir veya kesebilir. Bunu belgeliyoruz; yüksek-güvence ortamlar için harici append-only log'a (örn. syslog/SIEM'e fan-out yapan `IConfiguriteAuditLog` decorator) gönderin.

## Operasyonel rehber

### Ana anahtar yönetimi

| ❌ Yapma | ✅ Yap |
|---|---|
| Anahtarı Docker imajına göm. | Runtime env üzerinden enjekte et veya `chmod 600` ile anahtar dosyası mount et. |
| `~/.configurite/master.key`'i Git'e commit'le. | Repo dışında sakla; gizli yöneticisi üzerinden senkronize et. |
| Aynı anahtarı ortamlar arasında yeniden kullan. | Ortam başına bir anahtar; TLS sertifikalarıyla aynı tempoda rotasyona sok. |
| Kısa veya hatırlanabilir anahtarlar kullan. | ≥ 32 karakter rastgele (`openssl rand -base64 24`). |

### Yedekleme & geri yükleme

Şifreli SQLite dosyası **olduğu gibi** yedeklenebilir. Yeni bir makinede geri yüklemek için ana anahtar da gerekir. **Kurtarma prosedürünü belgele** — anahtarı kaybetmek veriyi kaybetmekle eşdeğerdir.

### Üretim kontrol listesi

- [ ] Ana anahtar bir gizli depo (Vault/KMS/Keychain) tarafından yönetiliyor, host env var değil.
- [ ] `Configurite.AdminUI` `RequireAuthorization` ardına monte edilmiş (rol bazlı).
- [ ] Denetim günlüğü saklama politikası tanımlandı.
- [ ] Ana anahtar rotasyon temposu belgelendi (≤ 90 gün öneriliyor).
- [ ] Yedekler tam restore + decrypt provası ile test edildi.
- [ ] CI pipeline `dotnet list package --vulnerable` çalıştırıyor.

## Güvenlik açığı bildirme

Lütfen depodaki GitHub Security Advisories'i kullanın. Koordineli açıklama penceresi: bildirim onayından itibaren 90 gün.
