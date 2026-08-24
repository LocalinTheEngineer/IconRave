# Desktop Icon Dropper — 1. Adım

Bu, projenin **ilk aşaması**: masaüstündeki bir simgeye tıkladığında, o simge
yerçekimi + sekme fiziğiyle görev çubuğunun üstüne "düşüyor".

Ses senkronizasyonu (2. proje adımı) henüz eklenmedi — NAudio kütüphanesi
projeye şimdiden eklendi, bir sonraki adımda kullanacağız.

## Nasıl çalıştırılır (Windows'ta)

1. **Visual Studio Community** kurulu değilse ücretsiz indir:
   https://visualstudio.microsoft.com/tr/vs/community/
   Kurulum ekranında **".NET Desktop Development"** iş yükünü işaretlemeyi unutma.

2. Bu klasördeki tüm dosyaları (`DesktopIconDropper.csproj` dahil) kendi
   bilgisayarına bir klasöre kopyala.

3. Visual Studio'yu aç → **"Open a project or solution"** yerine
   **"Open a local folder"** seç → bu klasörü seç.
   (Ya da terminalden: `dotnet run` — .NET 6 SDK kurulu olmalı.)

4. Üstteki yeşil ▶ **Start** butonuna bas (ya da terminalde `dotnet run`).

5. Uygulama açıldığında görünür bir pencere ÇIKMAZ — bu normal, bilerek öyle
   tasarlandı. Sağ altta, saat yanındaki sistem tepsisinde küçük bir ikon
   belirecek. Uygulamayı kapatmak istersen o ikona sağ tıklayıp "Çıkış" de.

6. Masaüstünde bir simgeye (kısayol, dosya, klasör fark etmez) tıkla —
   simgenin görev çubuğuna doğru düşüp sekmesini görmelisin.

## Bilinen sınırlamalar / test ederken dikkat et

- Bu, Windows'un **belgelenmemiş iç yapısını** (SysListView32) kullanıyor.
  Yani Windows 10 ve 11'de test edilmesi gerekiyor — sürüme göre küçük
  farklar çıkabilir.
- "Simgeleri otomatik düzenle" (Auto arrange icons) açıksa, Windows
  simgeleri anında eski konumuna geri çekebilir. Masaüstünde sağ tık →
  Görünüm → "Simgeleri otomatik yerleştir" seçeneğini KAPALI yapman gerekebilir.
- Antivirüs programları, global mouse hook + explorer.exe'ye erişim yapan
  uygulamaları bazen şüpheli bulup uyarı verebilir. Bu normal, kaynak kodun
  tamamı elimizde ve zararsız.

## Bana geri bildirim ver

Çalıştırıp test ettikten sonra bana şunu söyle:
- Simgeler düşüyor mu, hiç tepki yok mu, yoksa hata mesajı mı çıkıyor?
- Hata çıkarsa, tam metnini kopyala yapıştır bana at.

Bu bilgiyle bir sonraki adıma (fizik ince ayarı + ses senkronizasyonu) geçeriz.
