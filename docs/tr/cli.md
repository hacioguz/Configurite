# CLI aracı

> `Configurite.Cli` 1.2'den itibaren mevcut.

`configurite` komut satırı aracı, bir Configurite veritabanına karşı geliştiricinin ihtiyaç duyduğu her operasyonu sarmalar — CI script'leri, ops runbook'ları ve anlık inceleme için kullanışlıdır.

## Kurulum

```bash
dotnet tool install -g Configurite.Cli
```

Tam referans için `configurite --help` çalıştırın.

## Komutlar

| Komut | Amaç |
|---|---|
| `init <db>` | `<db>` içinde şemayı oluştur. |
| `migrate <db> <json-or-dir> [opts]` | `appsettings*.json` dosyalarını geçir. |
| `rotate <db> --old <key> --new <key>` | Atomik ana anahtar rotasyonu. |
| `get <db> <key> [--env <name>]` | Tek bir değer oku (gerekirse çözer). |
| `set <db> <key> <value> [opts]` | Bir değer ekle veya güncelle. |
| `list <db> [--env <name>] [--reveal]` | Satırları listele. `--reveal` şifreli değerleri çözer. |
| `delete <db> <key> [--env <name>]` | Bir satırı sil. |

## Yaygın opsiyonlar

| Opsiyon | Geçerli | Açıklama |
|---|---|---|
| `--env <name>` | get/set/list/delete | Ortam kapsamı (`Development`, `Production`, …). |
| `--encrypt` | set | Yeni değeri şifreler. |
| `--encrypt <pattern>` | migrate | Glob deseni; eşleşen anahtarlar şifrelenir. Tekrarlanabilir. |
| `--master-key <key>` | şifreleme bilen tüm komutlar | `CONFIGURITE_MASTER_KEY`'i geçersiz kılar. |
| `--no-overwrite` | migrate | Mevcut satırları korur. |
| `--reveal` | list | Şifreli değerleri görüntü için çözer. |

## Çıkış kodları

| Kod | Anlam |
|---|---|
| 0 | Başarılı |
| 1 | Genel hata |
| 2 | Hatalı argüman / bilinmeyen komut |
| 3 | Dosya bulunamadı |
| 4 | Ana anahtar çözümlenemedi (şifreleme kullanılamaz) |
| 5 | `get` / `delete` için anahtar bulunamadı |

## Örnekler

```bash
# Mevcut JSON dosyalarından sıfırdan bir veritabanı kur.
configurite migrate ./appsettings.db ./config-folder \
    --encrypt "ConnectionStrings:*" --encrypt "*:Password" --encrypt "*:ApiKey"

# Şifreli bir gizli değer ekle veya güncelle.
configurite set ./appsettings.db Auth:ApiKey "$(pass show api/prod)" --encrypt

# Bir değer oku.
configurite get ./appsettings.db ConnectionStrings:Default --env Production

# Veritabanını incele (şifreli değerler --reveal olmadan opak kalır).
configurite list ./appsettings.db
configurite list ./appsettings.db --env Development --reveal

# Ana anahtarı rotasyona sok. CI-dostu: tek transaction, atomik.
export OLD_KEY=...; export NEW_KEY=...
configurite rotate ./appsettings.db --old "$OLD_KEY" --new "$NEW_KEY"

# Eski bir girdiyi kaldır.
configurite delete ./appsettings.db OldFlag --env Development
```

## Ana anahtar çözümlemesi

CLI, library ile aynı fallback zincirini takip eder:

1. `--master-key <key>` bayrağı.
2. `CONFIGURITE_MASTER_KEY` ortam değişkeni.
3. `~/.configurite/master.key` dosyası.

Şifreli değere dokunmayan komutlar (`init`, `--encrypt`'siz `migrate`, düz `set`/`get`, `--reveal`'siz `list`, `delete`) ana anahtar aramaz.
