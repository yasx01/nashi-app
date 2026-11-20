# Tam Fedora sürümü: kabul listesi

Hedef: upstream Nashi 0.7.18'in 27 aracı, 46 girdi biçimi ve masaüstü
iş akışlarının Fedora/KDE/Wayland karşılığını çalışır ve doğrulanmış olarak sunmak.
0.1.0'ın sınırlı kapsamı bu hedefin tamamlandığı anlamına gelmez.
Geliştirme `fedora-full` dalında; yayımlanan sürüm `fedora-port` dalında kalır.

## Gerekli kapılar

- [ ] Görsel araçları: anotasyon (şekil/metin/serbest çizim, seç/taşı/sil/geri al),
  fon/çerçeve, kolaj, redaksiyon, etkileşimli kırpma, bütün fotoğraf ayarları,
  döndürme/çevirme, çok sayfalı görsel PDF, hedef boyutta sıkıştırma ve metaveri.
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
- Görsel metaveri alan düzenlemesi ve kalan kabul kapıları hâlâ tamamlanmadı.
- Belge motoru: Linux Poppler ile sınırlı PDF görüntüleme; PDF/Office/EPUB
  dönüşümleri, düzenlenebilir/korunan belge düzeni, Pages dosyası ve klasör paketi
  gerçek roundtrip testleriyle geçti. PDF sayfalarından QR okuma doğrulandı.
  OCR yolu eklendi; ayrı OCR çıktısı testi henüz tamamlanmadı.
- RTFD dönüştürücüsü ortak motora taşındı. Ek dosyaların sembolik bağlantılarla
  paket dışından okunması ve aşırı açılmış içerik reddediliyor; sınır testleri geçti.
- PDF sayfa düzenleyicisi: gerçek çıktı ve arayüz testlerinde sıralama, çoğaltma,
  her kopyayı ayrı döndürme, eşit genişlik ve sayfa aralığıyla bölme geçti.
  Kullanıcı/sahip şifreleri, yanlış şifre ve aşırı büyük sayfa reddi doğrulandı.
- Ortak motora taşıma sonrası Windows çapraz derlemesi sıfır hata/uyarıyla geçti;
  ortak motorun doğrulanan araçlarla 2.151 kontrolü yeniden geçti.
