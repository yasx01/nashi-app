# Fedora 0.1.0 özellik durumu

Kaynak tabanı: upstream `dfcda63`, Nashi 0.7.18. Fedora uyarlaması bağımsız 0.1.0
sürümüdür. Ana ekranın özgün renkleri, görselleri, kart düzeni ve çark yaklaşımı
korunur; platforma özel editörlerin birebir kopyası henüz tamamlanmamıştır.

| Upstream aracı | Fedora 0.1.0 |
|---|---|
| Compress | Görsel, medya ve PDF için mevcut motor üzerinden |
| Metadata | Görsel/medya/PDF metaverisini temizleme; alan düzenleme yok |
| Edit Photo | Boyut, parlaklık, kontrast, doygunluk; diğer ayarlar henüz yok |
| Annotate Photo | Henüz taşınmadı |
| Add BG | Henüz taşınmadı |
| Crop Image | Piksel koordinatlarıyla; sürüklenebilir alan henüz yok |
| Normalize Volume | Mevcut ses motoru üzerinden |
| Remove Audio | Mevcut video motoru üzerinden |
| Trim Video | Başlangıç/bitiş saniyeleriyle; oynatıcı henüz yok |
| Split PDF | Sayfa başına yeni PDF |
| Organize PDF | Henüz taşınmadı |
| Extract Archive | Güvenli açma; şifre giriş ekranı henüz yok |
| Create PDF | Görsel → PDF dönüşümünde; çok sayfalı görsel birleştirme henüz yok |
| Merge PDFs | Seçim sırasıyla birleştirme |
| Crop Video | Henüz taşınmadı |
| Change Video Speed | Sayısal hız çarpanıyla |
| Join Videos | Seçim sırasıyla birleştirme |
| Video Snapshots | Seçilen saniyeden kare alma |
| Split Video | Eşit uzunlukta parçalara ayırma |
| Audio Visualizer | 1280 × 720 ses dalgası videosu |
| Trim Audio | Başlangıç/bitiş saniyeleriyle |
| Convert Audio Channels | Mono/stereo çıkış |
| Create Collage | Henüz taşınmadı |
| Redact Photo | Henüz taşınmadı |
| Redact Video | Henüz taşınmadı |
| Bleep Audio | Henüz taşınmadı |
| Read QR Codes | Görsel dosyalarından; PDF içinden henüz yok |

## Dönüşümler ve masaüstü

Çıktı listesi ortak katalog ile Linux'taki uygulamanın desteklediği yolların
kesişiminden üretilir. Desteklenmeyen bir yol çalışırmış gibi sunulmaz.
Görsel dönüşümleri Magick.NET, medya FFmpeg, belge/tablo/sunum LibreOffice,
arşiv ve altyazılar mevcut ortak motorla işlenir. PDF → TXT/MD metin çıkarır;
taranmış PDF için OCR yapılmaz. HEIC kodlayıcısı medya aracı kurulumu ister.
SVG bir raster formata çıkarılır. Office çıktısında yerleşim ve kodlayıcı desteği
girdi ile sistemdeki LibreOffice/FFmpeg sürümüne bağlıdır.

Dosya seçme, pencereye sürükleme, dosya/metin/bağlantı yapıştırma, kitaplık,
arama, açık/koyu tema, Ctrl+O/V/Space/1/2/F desteklenir. Panodan görsel alma,
genel sürükleme çarkı, genel kısayollar, tepsi, pencere yönetimi ve otomatik
güncelleme henüz yoktur. Wayland'da XWayland kullanılır.

İlk sürümün test kanıtları [doğrulama notlarında](VERIFICATION.md) bulunur.
