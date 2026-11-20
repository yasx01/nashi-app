# Fedora 0.1.0 doğrulama

20 Kasım 2026, Fedora 44 x86_64. Kaynak tabanı upstream `dfcda63`.

## Yerel kontroller

- .NET 10 Release derlemesi: sıfır hata ve sıfır uyarı.
- `Nashi.Verify`: ortak motorun 1.951 güvenlik/işlev kontrolü geçti.
- Eksik Linux araçlarının gerçek indirmesi ve SHA-256 kontrolü de çalıştırıldı:
  indirilen FFmpeg ile dönüştürme/kesme/ses kaldırma/hız/kırpma/bölme/birleştirme/
  normalleştirme/kare alma/redaksiyon kontrolleri ve ExifTool indirme/yazma/okuma
  dahil toplam 2.151 kontrol geçti. Bu motor kanıtıdır; taşınmamış editörlerin
  Linux arayüzünde bulunduğu anlamına gelmez.
- Linux self-test: gerçek PNG/JPEG/PDF çıktısı; fotoğraf ayarıyla piksel değişimi;
  PDF birleştirme/ayırma; ZIP açma; SRT → VTT; gerçek QR çözümleme;
  LibreOffice metin → PDF ve kaynak metnin çıktıdan okunması;
  FFmpeg WAV → FLAC ve mono ses çıktısı geçti.
- Genişletilmiş medya testi: ses → 1280 × 720 MP4, iki video parçası ve seçilen
  zamandan PNG kare çıktısı geçti (`NASHI_TEST_FULL_MEDIA=1`).
- Orijinal dosya baytlarının korunması, ad çakışmasında yeni ad, iptal,
  sınır dışı kırpmanın reddi ve geçici çıktı temizliği geçti.
- Gerçek Avalonia kontrol ağacından ana ekran, açık tema ve çark PNG'leri
  üretildi; kitaplık ve çark klavye kısayolları geçti.
- Masaüstü başlatıcısı `desktop-file-validate` ile kontrol edilir.
- Paketleme betiği RPM, bağımsız arşiv, bağımlılık lisans envanteri ve göreli
  dosya adları içeren SHA-256 manifesti üretir.

## Tekrar çalıştırma

```sh
dotnet restore linux/Nashi.Linux -r linux-x64 --locked-mode
dotnet build linux/Nashi.Linux -c Release --no-restore
dotnet run --project windows/Nashi.Verify -c Release
dotnet run --project linux/Nashi.Linux -c Release --no-build -- --self-test /tmp/nashi-fedora.png
NASHI_TEST_FULL_MEDIA=1 dotnet run --project linux/Nashi.Linux -c Release --no-build -- --self-test /tmp/nashi-media.png
bash scripts/build-fedora.sh
build/fedora/app/Nashi.Linux --self-test /tmp/nashi-packaged.png
(cd build/fedora && sha256sum -c SHA256SUMS)
rpm -K build/fedora/nashi-*.rpm
```

Genişletilmiş medya testi libx264/AAC destekleyen FFmpeg ister; Fedora'nın
`ffmpeg-free` paketi tüm kodlayıcıları içermez. Uygulamadaki araç kurulumu
upstream'in doğrulanan Linux FFmpeg derlemesini indirebilir.
FFmpeg/LibreOffice yüklü değilse standart self-test ilgili dış araç kontrolünü
atlar; başarılı arayüz testi bunların kurulu veya çalışır olduğunun kanıtı değildir.

## Sınırlar

Headless arayüz testi gerçek Wayland/X11 pencere sistemi, masaüstü dosya
seçicisi veya dosya yöneticisinden sürükleme testi değildir. Bu akışlar
Avalonia'nın platform uygulamasını kullanır, canlı masaüstünde doğrulanmadı.
RPM sistem genelinde otomatik kurulmaz. Paket içeriği, çalışma zamanı ve
dışa aktarma akışları kurulumdan bağımsız sınanır. Paket imzasızdır;
SHA-256 manifesti dosya bütünlüğünü kontrol etmek içindir.

Diğer mimariler, diğer Fedora sürümleri, tüm medya siteleri ve katalogdaki her
olası girdi/çıktı kombinasyonu test edilmedi. İşlev kapsamı [özellik tablosundadır](FEATURES.md).

GitHub'daki **Fedora desktop release checks** iş akışı temiz bir Fedora 44
ortamında kaynak derlemesini ve paket içindeki uygulamayı tekrar sınar.
