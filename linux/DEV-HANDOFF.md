# Nashi Fedora — kaldığımız yer

Güncelleme: 20 Kasım 2026. **Önce bu notu oku; sohbeti baştan tarama.**
İlerledikçe aynı dosyayı güncelle. Ayrıntılı kabul listesi: [FULL-PARITY-PLAN.md](FULL-PARITY-PLAN.md).

## Hedef ve sınırlar

- Kullanıcı orijinal tasarımı koruyan, bütün özellikleri çalışan Fedora sürümü istiyor.
  **Tam sürüm henüz bitmedi; aktif hedefi tamamlandı sayma.**
- Türkçe, kısa iletişim; gereksiz tekrar testinden kaçın. Büyük değişiklikten önce
  kısa plan ver; önemli engel/yöntem değişiminde sor. Önizlemeler Codex içinde.
- Depo: `/home/mrx/Belgeler/Coding-Project/Kork./nashi-app`; ortam Fedora KDE/Wayland.
- Aktif dal **`fedora-full`**; origin `yasx01/nashi-app`, upstream `orkunisitmak/nashi-app`.
- Yayımlanmış deneysel sürüm `fedora-v0.1.0` / `fedora-port`. Üstüne geliştirme
  çıktısı yayımlama. Upstream temel `dfcda63` / 0.7.18.
- Avalonia 11.3.23 / .NET 10; motor `windows/Nashi.Core` ile ortak. Kaynak mevcut;
  REA gerekmedi. Yeni subagent görevi için açık yetki yok.

## Son doğrulanıp kaydedilen işler

| Commit | İçerik | Başarılı temiz Fedora CI |
| --- | --- | --- |
| `df11170` | Gömülü medya editörü, video/ses sansürü, kırpma, çıktı önizlemesi; font/OCR düzeltmesi | `44778026475` |
| `b395e9c` | Üst veri alanları/koruma; PDF hedef boyut ve form/ek/imza koruması | `44779415243` |

Önceki commit'lerde görsel editörleri, belge/Pages/RTFD/EPUB ve PDF sayfa düzenleyicisi
var. Yeniden uygulama. İki CI koşusunda RPM üretimi/kurulumu, kurulan uygulamanın
testleri ve Xvfb yerel pencere testi geçti. Son tam ortak motor kontrolü **2.158**;
Windows çapraz derlemesi sıfır hata/uyarı. OCR temiz Fedora'da gerçek taramayla geçti.

## Güncel çalışma

Pano/kitaplık/önizleme **`446cfc2`** ile commit/push edildi. Temiz Fedora CI
**`44781964965` başarılı**: RPM üretimi/kurulumu ve yerel pencere dahil. Bu bölümü
tekrar yazma. Pano 16 MB metin sınırı ve tekrarlı görsel; kitaplık çoklu seçim,
geçmişten kaldırma, Linux case-sensitive yollar; ortak PDF/şifre/metin/medya
önizlemesi ve hızlı geçişte iptal testli. `.library.png` üretildi.

**Dosya eylemleri / Float bölümü `bb37748` ile commit/push edildi.**
CI `44785250238` Float ekran görüntüsünü ilk çizimden önce aldığı için başarısız oldu.
Motor/dosya işlemleri geçti. `FloatChecks` artık en fazla 5 saniye gerçek ilk kareyi
bekliyor; boş kareyi hâlâ hata sayıyor. Bu düzeltme yerel tam testte geçti; yeni CI bekleniyor. Aşağıdaki kodu yeniden yazma:

- Yeni: `FileActions.cs`, `FileActionChecks.cs`, `FloatShelf.cs`, `FloatChecks.cs`.
- Değişen: `MainWindow.cs`, `SelfTest.cs`, `linux/nashi.spec`, ortak
  `ExportTransaction.cs`, `windows/Nashi.Verify/Program.cs`.
- Native coreutils ile üzerine yazmadan kopyalama/taşıma; Unix izinleri, boş dosya,
  Unicode/yeni satırlı isim, çakışma, fiziksel hedefin kaynak içinde olmaması,
  iç içe seçim, sembolik bağlantı hedefinin korunması ve kopya iptalinde rollback
  testleri **geçti**. Taşıma yarıda kalırsa tamamlananlar ve hedef açıkça bildirilir.
- `ExportTransaction.Publish` yeni `allowEmptyFiles` parametresini yalnız dosya
  kopyalamada kullanıyor. Varsayılan hâlâ boş dönüştürme çıktısını reddediyor;
  buna ayrı regresyon eklendi. Yeni ortak kısa kontrol **1.960 geçti** (salt okunur temizlik regresyonu dahil).
- Dosya eylemleri ana sayfa ve kitaplık seçimine bağlandı: dosya/yol panosu,
  klasöre kopyala/taşı, klasörde göster, Konsole. Sistem uygulamalarını test için
  açmadık. **Paylaşım henüz eklenmedi.**
- Float: kompakt/geniş raf, dosya ekleme, çoklu seçim, referansı raftan kaldırma,
  Nashi'ye geri alma, ortak önizleme ve gerçek dosya panosu; negatif monitör
  koordinatlarıyla kenar geometrisi testleri **geçti**. Native dışarı sürükleme
  bağlandı, ancak başka uygulamaya gerçek DnD ve KDE kenar yerleşimi doğrulanmadı.
- Geniş raf 460 px yüksekliğe indirildi; son tam masaüstü testi bu görselle geçti.
- Salt okunur geçici klasörler iptalde kalıyordu; ortak rollback yalnız kendisine
  ait klasörlere silme izni ekliyor, symlink hedeflerini takip etmiyor. Gerçek
  500 izinli klasörle ve dış hedefle test edildi: kaynak/hedef izinleri korunuyor.
- Medya önizlemesi artık native oynatıcı gerçekten Paused/Playing durumuna
  gelmeden hazır sayılmıyor. Açılış iptal/zaman aşımı da ele alındı; testler geçti.
- Son tam masaüstü günlüğü: `/tmp/nashi-full-desktop-tests.log`. Float görselleri:
  `nashi-full-progress.float.png` ve `.float-compact.png`.
- Son ortak temel kontrol **1.960 geçti**; Windows çapraz derlemesi de sıfır
  hata/uyarıyla geçti. Sonuç `/tmp/nashi-windows-shared-build.log` içinde.

## Sıradaki adım

1. Float ilk kare test düzeltmesi commit edilmek üzere; `git log` ve son CI
   kimliğini kontrol et. Yerel tam test, 1.960 temel kontrol ve Windows derlemesi geçti.
2. Paylaşım için KDE Purpose API'si, KDE kısayolları için KWin API'si inceleniyor;
   bu iki alanda henüz kod değişikliği yapılmadı. Paylaşım için mevcut
   `python3-pyside6` (Qt 6.11.2) + `kf6-purpose` kullanılabilir; yeni C++/Qt
   köprüsü gerekmiyor. Yerel API: `/usr/lib64/qt6/qml/org/kde/purpose/AlternativesView.qml`
   (`pluginType`, `inputData`, `finished(output,error,message)`). KWin genel dosya sürükleme verisini
   script API'sinden doğrudan sunmuyor; native helper gerekip gerekmediği araştırılmalı.
3. Ardından ayarlar/profiller ve kalan KDE entegrasyonuna ilerle. Native DnD'yi
   headless geometri testinden geçti diye çalışıyor sayma.

Kalan büyük gruplar: otomatik metin/yüz sansür alanı bulma; bütün 27 araç/46 girdi/
yaklaşık 300 dönüşüm yolunun doğrulanması; paylaşım ve native Float/dışarı sürükleme doğrulaması;
KDE/Wayland genel kısayol ve sürükleme çarkı, sekiz pencere yerleşimi, IPC, tepsi,
oturum açılışı; ayarlar/profiller/dil/çıktı konumu; doğrulanmış güncelleme/geri alma;
tam sürüm yayını. Platformda başka yöntem gerekiyorsa önemli kararı kullanıcıyla
netleştir. Fiziksel ses çıkışı test edilmedi; testler sessiz ses çıkışı kullanıyor.

## Gerekli komutlar ve kanıtlar

SDK: `/tmp/nashi-dotnet/dotnet` (10.0.404). Araçlar: `/tmp/nashi-release-test-tools`;
ExifTool: `/tmp/nashi-release-test-tools Metadata`. `/tmp` yeniden başlatmada kaybolabilir.

```sh
# Depo dizininde; derleme
/tmp/nashi-dotnet/dotnet build linux/Nashi.Linux -c Release --no-restore
# Tam masaüstü testi: harici pencere açmaz; medya sessiz
GSETTINGS_BACKEND=memory NASHI_TEST_FULL_MEDIA=1 NASHI_VERIFY_TOOLS_DIRECTORY=/tmp/nashi-release-test-tools /tmp/nashi-dotnet/dotnet run --project linux/Nashi.Linux -c Release --no-restore -- --self-test /home/mrx/.codex/visualizations/2026/11/15/01a1e03e-c480-7ff0-889e-619b8a771d08/nashi-full-progress.png
```

- Günlükler: `/tmp/nashi-full-desktop-tests.log`, `/tmp/nashi-full-shared-tests.log`,
  `/tmp/nashi-library-shared-tests.log`, `/tmp/nashi-windows-shared-build.log`.
- Görseller yukarıdaki önizleme dizininde: `nashi-full-progress.media.png`,
  `.metadata.png`, `.preview.png`, `.preview-pdf.png`, `.crop-artwork.png`.
  `.library.png` de üretildi.
- GitHub sorgularında **`-R yasx01/nashi-app` kullan**; gh varsayılanı upstream olabilir.
- Kilitli restore: `dotnet restore linux/Nashi.Linux -r linux-x64 --locked-mode`.
  Core Linux kilidi `packages.linux-x64.lock.json`; Windows kilidini bozma;
  NuGet audit'ini kapatma. Aynı çıktı dizinine derlemeleri paralel başlatma.
- Paket: `DOTNET_COMMAND=/tmp/nashi-dotnet/dotnet bash scripts/build-fedora.sh`.
- Kod girişleri: `MainWindow`, `Operations`, `SelfTest`, ilgili `*Checks`;
  `ImageEditing/ImageCanvas/ImageEditorWindow`, `MediaEditorWindow/MediaPlayback`,
  `DocumentConversion/LinuxPdf/PdfOrganizerWindow/PdfCompression`,
  `MetadataEditorWindow` ve ortak `MetadataService/MediaMetadata`.
