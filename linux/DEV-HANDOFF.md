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

## Şu an commit edilmemiş iş — bunu koru

Pano/kitaplık/önizleme kodu yazıldı, henüz commit/push edilmedi:

- Yeni: `linux/Nashi.Linux/ClipboardImport.cs`, `FilePreviewWindow.cs`, `LibraryChecks.cs`.
- Değişen: `MainWindow.cs`, `MediaEditorWindow.cs`, `MediaPlayback.cs`, `SelfTest.cs`,
  `windows/Nashi.Core/LibraryStore.cs`, `linux/FULL-PARITY-PLAN.md`.
- Pano: metin/görsel, tekrar yapıştırma, 16 MB metin sınırı; görsel bırakma.
  Bitmap panonun kaynağına ait olabileceği için çağıran dispose etmiyor.
- Kitaplık: çoklu seçim, tekrar kullanma, önizleme, geçmişten kaldırma.
  Geçmişten kaldırma dosya silmez. Linux'ta `Case.txt`/`case.txt` ayrı tutuluyor.
- Ortak önizleme: görsel/metin, PDF sayfa/şifre, belge → PDF, klasör, gömülü medya;
  dosya geçişi eski yüklemeyi iptal eder, kapanış geçici dosyaları/oynatıcıyı temizler.
- İlk medya karesi `MediaPlayback.ArtworkAsync` ile ortaklaştırıldı. Medya boyut
  kontrolleri motorun 8192 sınırına eşitlendi. “Medya araçlarını kur” tam doğrulanmış
  araçları kuruyor; sistemdeki kısıtlı ffmpeg-free'ye güvenip eksik kodekte kalmamalı.

Bu bölümün tam masaüstü testi geçti: pano/tekrar yapıştırma/sınır, kitaplık/geçmiş,
PDF/şifre/metin/eksik dosya/medya, hızlı geçişte iptal. Ortak kısa kontrol **1.958**
geçti (FFmpeg/indirme ek kontrolleri bu kısa koşuda kapalıydı).
Son görsel değişikliğiyle tam masaüstü testi de tekrar geçti; kitaplık ve marka
varlığı önizleme ekran görüntüleri üretildi. Bu kod için
henüz yeni temiz Fedora CI başlatılmadı. `DEV-HANDOFF.md` de henüz commit edilmedi.

## Sıradaki adım

1. `git status --short` ve mevcut diff'i kontrol et; yukarıdaki işi baştan yazma.
2. Gerekliyse son görsel test değişikliğini doğrula, pano/kitaplık bölümünü
   `fedora-full` dalına commit/push et. Yeni CI kimliğini bu nota kaydet.
3. Aynı canlı CI koşusunu izle; gereksiz push ile iptal edip yeniden başlatma.
4. Ardından dosya eylemleri/Float ve ayarlar gibi kalan gruplara ilerle.

Kalan büyük gruplar: otomatik metin/yüz sansür alanı bulma; bütün 27 araç/46 girdi/
yaklaşık 300 dönüşüm yolunun doğrulanması; dosya eylemleri/Float/dışarı sürükleme;
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
