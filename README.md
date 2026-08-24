# IconRave 🎉

Windows masaüstü simgelerinizi gerçek bir fizik motoruyla hayata geçiren eğlenceli bir masaüstü aracı.

Uygulama açıldığı anda tüm masaüstü simgeleriniz yerçekimiyle düşer, takla atarak
rastgele noktalara fırlar; istediğiniz simgeyi tutup elle fırlatabilir, ekran
kenarlarına çarpıp sekmesini izleyebilirsiniz. Üstüne, bilgisayarınızda çalan
sistem sesini gerçek zamanlı analiz ederek, müzikteki vuruşlara göre simgelerin
zıplamasını sağlar.

## Özellikler

- **Fizik tabanlı düşme** - Açılışta tüm simgeler yerçekimiyle, hafif yaprak gibi
  sallanarak düşer ve rastgele bir noktaya takla atarak yerleşir
- **Elle sürükle-fırlat** - Bir simgeyi tutup fareyle hızlıca çekip bırakınca,
  gerçek fare hızınızla fırlar; ekranın kenarlarına top gibi çarpıp seker
- **Ses senkronizasyonu** - WASAPI loopback + FFT ile sistem sesini analiz eder;
  müzikte vuruş (beat) algıladığında tüm simgeler birlikte zıplar
- **Gerçek Windows simgeleri döner** - Simgeler uçarken/takla atarken kendi
  gerçek ikon görselleriyle döner (özel bir overlay katmanı sayesinde)

## Gereksinimler

- Windows 10 veya 11
- [.NET 6.0 SDK](https://dotnet.microsoft.com/download/dotnet/6.0)
- (Geliştirme için, isteğe bağlı) Visual Studio 2022+ - ".NET Desktop Development" iş yükü ile

## Çalıştırma

```bash
git clone https://github.com/LocalinTheEngineer/IconRave.git
cd IconRave
dotnet run
```

Uygulama görünür bir pencere açmaz; sistem tepsisinde (saat yanında) küçük bir
ikon belirir. Çıkmak için o ikona sağ tıklayıp **Çıkış**'ı seçin.

## Nasıl çalışır (teknik özet)

Windows masaüstü simgeleri aslında `explorer.exe` içindeki gizli bir
`SysListView32` liste kutusudur. Bu proje, Win32 API'ye doğrudan erişip
(`FindWindow`, `SendMessage`, cross-process bellek okuma/yazma gibi
belgelenmemiş teknikler) simgelerin konumlarını okuyup değiştiriyor. Gerçek
simgeler döndürülemediği için, bir simge havadayken gerçek simge geçici
olarak gizlenir ve yerine şeffaf bir üst katman (overlay) penceresinde
döndürerek çizilen bir kopyası gösterilir; yere inince gerçek simge tekrar
görünür olur.

## Bilinen sınırlamalar

- Belgelenmemiş Windows iç yapılarını kullandığı için Windows sürümüne göre
  küçük davranış farkları olabilir
- "Simgeleri otomatik yerleştir" ve "Simgeleri kılavuza hizala" seçeneklerinin
  kapalı olması önerilir (masaüstünde sağ tık → Görünüm)
- Ses senkronizasyonu varsayılan ses çıkış cihazını dinler; bazı özel ses
  sürücüsü yapılandırmalarında çalışmayabilir

## Lisans

Bu proje kişisel/eğlence amaçlı geliştirilmiştir.
