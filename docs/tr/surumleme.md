# Sürümleme politikası

Configurite **TFM-major sürümleme** kullanır — paket sürümünün major bileşeni .NET hedef framework'ünü yansıtır. Library ve CLI **lockstep** yayınlanır (her sürümde aynı numara).

## Kural

| Hedef framework | Paket hattı | Örnek sürüm |
|---|---|---|
| `net8.0` | `8.x.y` | `Configurite 8.2.0` |
| `net9.0` | `9.x.y` | `Configurite 9.2.0` |
| `net10.0` | `10.x.y` | `Configurite 10.2.0` |

Tüketici floating aralıklarla bir hatta sabitlenir:

```xml
<!-- net8.0 projesi -->
<PackageReference Include="Configurite"     Version="8.*" />
<PackageReference Include="Configurite.Cli" Version="8.*" />
```

Kullanıcı .NET 9'a geçtiğinde `9.*`'a çevirir. Aradan sürpriz major bump çıkmaz.

## Üç bağımsız hat, tek kaynak

Her sürümde **üç ayrı `.nupkg`** yayınlarız; hepsi aynı kaynak ağacından üretilir:

```
Configurite.8.2.0.nupkg     →  yalnızca lib/net8.0/
Configurite.9.2.0.nupkg     →  yalnızca lib/net9.0/
Configurite.10.2.0.nupkg    →  yalnızca lib/net10.0/
```

Aynı kod, üç TFM, üç sürüm. NuGet resolver tüketici projesine uygun `.nupkg`'i doğal olarak seçer.

## Sürüm temposu

### Senkron feature sürümü

Yeni bir özellik tüm desteklenen TFM'lerde aynı anda yayınlanır:

```
8.2.0, 9.2.0, 10.2.0   →  özellik X
```

Aynı feature dalgası için minor bileşen üç hat boyunca paylaşılır.

### TFM-spesifik patch (asenkron)

Yalnızca bir TFM'i etkileyen bir hata yalıtılmış bir patch alır:

```
8.2.1                  →  net8-spesifik hotfix
9.2.x ve 10.2.x değişmeden kalır
```

`pack-all.sh --line net8 --patch 1` komutu yalnızca `8.2.1` üretir.

### Yeni .NET dalgası

.NET N+1 yayınlandığında, mevcut hatların yanına `(N+1).0.0`'da yeni bir hat ekleriz:

```
8.2.0, 9.2.0, 10.2.0    →  mevcut durum
... .NET 11 yayınlanır ...
8.2.0, 9.2.0, 10.2.0, 11.0.0   →  ilk 11.x hattı
8.3.0, 9.3.0, 10.3.0, 11.1.0   →  bir sonraki feature dalgası
```

.NET sürümünün ömrü dolduğunda hatlar emekliye ayrılabilir.

## Lokalde derleme

```bash
# Üç hatta da senkron feature sürümü:
scripts/pack-all.sh --minor 2 --patch 0

# net8-spesifik hotfix:
scripts/pack-all.sh --line net8 --minor 2 --patch 1
```

Script çıktıyı `./out/` klasörüne yazar. Symbol paketler (`.snupkg`) ana paketlerin yanında üretilir.

## Bu şema neden?

| Fayda | Detay |
|---|---|
| Net uyumluluk | `Configurite 9.x` ⇒ ".NET 9 lazım" — paketin TFM listesini kontrol etmeye gerek yok. |
| Stabil tüketici sabitlemesi | net8 kullanıcısı yıllarca `8.*`'da kalır; bizden kazara major bump gelmez. |
| Bağımsız servis | net8-spesifik bir CVE, `9.x` veya `10.x`'e dokunmadan `8.x.(y+1)` olarak yayınlanabilir. |
| Lockstep CLI + library | `Configurite 9.2.0` ile `Configurite.Cli 9.2.0` daima kaynak-uyumludur. |

## Tek bir multi-target paket neden **değil**?

Microsoft'un `Microsoft.Extensions.*` paketleri tersini yapıyor: `lib/` altında desteklenen her TFM'i içeren tek bir `.nupkg`. İdiomatik ama bu yaklaşım iki yükseltme eksenini (özellik ve framework) iç içe sokar. TFM başına paketle, tüketicinin `Version="8.*"` sabitlemesi `9.*`'a geçmek isteyene kadar kalıcıdır — bizim tarafımızdan otomatik bump yapılmaz.
