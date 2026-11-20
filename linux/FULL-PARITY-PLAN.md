# Tam Fedora sürümü: kabul listesi

Hedef: upstream Nashi 0.7.18'in 27 aracı, 46 girdi biçimi ve masaüstü
iş akışlarının Fedora/KDE/Wayland karşılığını çalışır ve doğrulanmış olarak sunmak.
0.1.0'ın sınırlı kapsamı bu hedefin tamamlandığı anlamına gelmez.
Geliştirme `fedora-full` dalında; yayımlanan sürüm `fedora-port` dalında kalır.

## Gerekli kapılar

- [ ] Görsel araçları: anotasyon (şekil/metin/serbest çizim, seç/taşı/sil/geri al),
  fon/çerçeve, kolaj, redaksiyon, etkileşimli kırpma, bütün fotoğraf ayarları,
  döndürme/çevirme, çok sayfalı görsel PDF, hedef boyutta sıkıştırma ve metaveri; otomatik metin/yüz sansür alanı bulma.
- [ ] Medya: gömülü oynatma/ses, klavye taşıma kontrolleri, zaman çizgisi,
  seçili aralık oynatma, kırpma, video/ses redaksiyonu, bölme işaretleri,
  kare listesi, görselleştirme seçenekleri ve işlenmiş çıktı önizlemesi.
- [ ] PDF: sayfa önizleme/seç/sırala/kopyala/döndür, eşit genişlik, şifre,
  QR, bütün çıktı biçimleri, düzenlenebilir/korunan yerleşim ve EPUB.
- [ ] Belge: Pages, RTFD, textClipping, EPUB ve katalogdaki tüm dönüşüm yolları;
  harici dosya/bağlantı/makro sınırları ve büyük girdi limitleri korunur.
- [ ] Arşiv/altyazı: tüm formatlar, şifre, çoklu dosya ve sıralama.
- [ ] Pano/kitaplık/önizleme: klasörler ve görseller dahil içe alma,
  çoklu önizleme, dosyayı göster, eksik dosyalar, yeniden kullanım ve geçmiş.
- [ ] Dosya eylemleri/Float: kopyala/taşı/yol/paylaş/terminal, sürükle dışarı,
  çoklu seçim, kenara tutunma ve bağımsız yüzen raf.
- [ ] KDE/Wayland: genel kısayollar, sürükleme çarkı, sekiz pencere yerleşimi,
  güvenli IPC, tepsi ve oturum açılışı. Gerekirse masaüstü eklentisi paketlenir.
- [ ] Ayarlar: tema, geri bildirim/animasyon, kısayollar, format profilleri,
  çıktı/indirme konumu ve doğrulanan güncelleme/geri alma.
- [ ] Testler: her araç için gerçek çıktı, her dönüşüm ailesi, veri koruma,
  iptal/bozuk girdi/boyut sınırları, editör etkileşimleri ve yeniden başlatma.
- [ ] Temiz Fedora RPM kurulum/yükseltme, yerel pencere testi, Codex içi ekran
  önizlemeleri, paket dosyalarının doğrulanması ve tam sürüm yayını.

Tamamlandı işareti için kod, gerçek çıktı veya etkileşim testi kanıtı gerekir.
Yalnızca katalogda görünmesi veya genel derleme testinin geçmesi yeterli değildir.

## Doğrulanan geliştirme ilerlemesi

- Görsel motoru: sekiz anotasyon türü, üç redaksiyon türü, dört kolaj yerleşimi,
  dört arka plan türü, sıralı çok sayfalı PDF, döndürme ve hedef boyutta sıkıştırma
  gerçek dosya çıktılarıyla geçti. Orijinaller ve bozuk geometri sınırları kontrol edildi.
- Editör: gerçek fare olaylarıyla kırpma oluşturma/taşıma/Shift ile oran koruma,
  Enter ile kabul, anotasyon taşıma/silme/Ctrl+Z testleri geçti.
- Görsel metaveri alan düzenlemesi ve dönüşümde koruma eklendi; gerçek ExifTool
  testleri geçti. Otomatik sansür alanı bulma ve kalan kabul kapıları tamamlanmadı.
- Belge motoru: Linux Poppler ile sınırlı PDF görüntüleme; PDF/Office/EPUB
  dönüşümleri, düzenlenebilir/korunan belge düzeni, Pages dosyası ve klasör paketi
  gerçek roundtrip testleriyle geçti. PDF sayfalarından QR okuma doğrulandı.
  OCR gerçek tarama çıktısıyla temiz Fedora testinde doğrulandı.
- RTFD dönüştürücüsü ortak motora taşındı. Ek dosyaların sembolik bağlantılarla
  paket dışından okunması ve aşırı açılmış içerik reddediliyor; sınır testleri geçti.
- PDF sayfa düzenleyicisi: gerçek çıktı ve arayüz testlerinde sıralama, çoğaltma,
  her kopyayı ayrı döndürme, eşit genişlik ve sayfa aralığıyla bölme geçti.
  Kullanıcı/sahip şifreleri, yanlış şifre ve aşırı büyük sayfa reddi doğrulandı.
- Ortak motora taşıma sonrası Windows çapraz derlemesi sıfır hata/uyarıyla geçti;
  ortak motorun doğrulanan araçlarla 2.158 kontrolü yeniden geçti.
- Medya düzenleyicisi: LibVLC ile uygulama içi video/ses oynatma, duraklatma,
  ileri sarma ve yerel dosya sınırı gerçek çözme testinden geçti. Video kırpma,
  zamanlı video sansürü, birden çok ses sansür aralığı ve işlenmiş dosya önizlemesi
  gerçek fare/çıktı testleriyle doğrulandı. Test oynatımı sessiz ses çıkışı kullanır;
  fiziksel hoparlör ve temiz Fedora codec kapsamı henüz doğrulanmadı.
- Anotasyon fontu artık fontconfig'in verdiği dosyadan yükleniyor. Generic Magick
  font adı dekoratif bir font seçiyordu; yeni tarama yerelde gerçek Tesseract ile
  `FEDORA OCR TEST 123` metninin tamamını verdi. Temiz Fedora OCR tekrarı da geçti (CI 44778026475).
- Üst veri editörü: mevcut alanları gösterme, yalnız değişen alanları yazma ve
  toplu temizleme arayüz testleri geçti. Unicode/çok satırlı alanlar, JPEG piksellerinin
  yeniden kodlanmaması, dönüşümde etiket koruma ve yönün normalleştirilmesi doğrulandı.
  Görsel sansürü eski özel üst verileri yeniden eklemiyor.
- PDF sıkıştırma: tarama dosyası gerçek hedef boyuta indi ve tekrar görüntülendi;
  özellikler korundu. Formlu PDF byte düzeyinde aynı kaldı; ulaşılamayan hedefte
  yarım çıktı yayımlanmadı. Ortak motor sıkıştırması imza/form/ekleri koruyor.
