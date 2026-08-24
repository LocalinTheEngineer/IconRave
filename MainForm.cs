using static DesktopIconDropper.NativeMethods;

namespace DesktopIconDropper;

// Bu form kullanıcıya GÖRÜNMEZ. Sadece arka planda çalışıp animasyon
// döngüsünü yönetmek için var. Kullanıcı, sistem tepsisindeki
// (saat yanındaki) küçük ikondan uygulamayı kapatabilir.
public class MainForm : Form
{
    private readonly DesktopIconManager _iconManager = new();
    private readonly MouseHook _mouseHook = new();
    private readonly System.Windows.Forms.Timer _animationTimer = new();
    private readonly List<IIconAnimation> _activeAnimations = new();
    private readonly Random _rng = new();
    private readonly Dictionary<int, Bitmap> _iconBitmaps = new();
    private readonly Dictionary<int, (float X, float Y)> _finalPositions = new();
    private NotifyIcon? _trayIcon;
    private OverlayForm? _overlay;
    private int _reassertCounter;

    // --- Ses senkronizasyonu (genel ses seviyesine göre, TÜM simgeler birlikte) ---
    private AudioAnalyzer? _audioAnalyzer;
    private float _volumeBaseline; // sesin yavaşça takip eden "taban" seviyesi
    private DateTime _lastGlobalTrigger = DateTime.MinValue;
    private const float OverallRiseThreshold = 0.10f; // tabanın ne kadar üzerine çıkması gerekiyor
    private const float MinOverallLevel = 0.06f;
    private const float BaselineSmoothing = 0.06f; // taban ne kadar yavaş takip etsin
    private static readonly TimeSpan GlobalCooldown = TimeSpan.FromMilliseconds(110);
    private int _volumeLogCounter;

    // --- Sürükleme (drag & fırlatma) durumu ---
    private int? _draggingIndex;
    private float _dragX, _dragY; // liste kutusuna göre YEREL koordinat
    private readonly List<(DateTime Time, float X, float Y)> _dragSamples = new();

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "debug.log");

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
        catch { }
    }

    private DateTime _lastTick = DateTime.Now;

    public MainForm()
    {
        try { File.WriteAllText(LogPath, $"=== Başlatıldı {DateTime.Now} ==={Environment.NewLine}"); } catch { }

        WindowState = FormWindowState.Minimized;
        ShowInTaskbar = false;
        Opacity = 0;
        Load += (_, _) => Hide();

        SetupTrayIcon();

        if (!_iconManager.Initialize())
        {
            Log("HATA: Masaüstü simge listesi bulunamadı.");
            MessageBox.Show(
                "Masaüstü simge listesi bulunamadı. Uygulama kapatılıyor.",
                "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        Log($"Masaüstü listesi bulundu. Simge sayısı: {_iconManager.GetIconCount()}");

        _overlay = new OverlayForm();
        _overlay.Show();

        PreloadIconBitmaps();

        _mouseHook.LeftButtonDown += OnLeftButtonDown;
        _mouseHook.MouseMove += OnMouseMove;
        _mouseHook.LeftButtonUp += OnLeftButtonUp;
        _mouseHook.Start();
        Log("Fare kancası (mouse hook) başlatıldı.");

        _animationTimer.Interval = 16; // ~60fps
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();

        DropAllIconsAutomatically();

        _audioAnalyzer = new AudioAnalyzer();
        bool audioOk = _audioAnalyzer.Start();
        Log(audioOk ? "Ses analizi başlatıldı." : "UYARI: Ses analizi başlatılamadı, bu özellik olmadan devam ediliyor.");
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "Desktop Icon Dropper - çıkmak için tıkla"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Çıkış", null, (_, _) => Application.Exit());
        _trayIcon.ContextMenuStrip = menu;
    }

    private void PreloadIconBitmaps()
    {
        int count = _iconManager.GetIconCount();
        for (int i = 0; i < count; i++)
        {
            string name = _iconManager.GetItemText(i);
            Bitmap? bmp = IconBitmapResolver.GetIconBitmap(name);
            _iconBitmaps[i] = bmp ?? SystemIcons.Application.ToBitmap();
        }
        Log($"{count} simge için resimler önceden yüklendi.");
    }

    private void DropAllIconsAutomatically()
    {
        int count = _iconManager.GetIconCount();
        RECT listRect = _iconManager.GetListViewScreenRect();

        for (int index = 0; index < count; index++)
        {
            var currentPos = _iconManager.GetIconPosition(index);
            StartAutoFall(index, listRect, currentPos.X, currentPos.Y);
        }

        Log($"{count} simge için otomatik düşme animasyonu başlatıldı.");
    }

    // Program açılışındaki otomatik düşme + rastgele fırlama (FallingIcon kullanır)
    private void StartAutoFall(int index, RECT listRect, float startX, float startY)
    {
        _activeAnimations.RemoveAll(a => a.IconIndex == index);
        _finalPositions.Remove(index);

        var screenPoint = new Point(listRect.Left + (int)startX, listRect.Top + (int)startY);
        var screen = Screen.FromPoint(screenPoint);
        int taskbarTopScreenY = screen.WorkingArea.Bottom;
        float targetY = taskbarTopScreenY - listRect.Top - 40;

        int listWidth = listRect.Right - listRect.Left;
        float minX = 0;
        float maxX = Math.Max(minX, listWidth - 40);
        float scatterBandHeight = 15f;

        _iconManager.SetIconPosition(index, -3000, -3000);
        _activeAnimations.Add(new FallingIcon(index, startX, startY, targetY, minX, maxX, scatterBandHeight, _rng));
    }

    // --- Sürükleyip fırlatma akışı ---

    private void OnLeftButtonDown(int screenX, int screenY)
    {
        int index = _iconManager.HitTest(screenX, screenY);
        Log($"MouseDown ({screenX},{screenY}) -> HitTest index: {index}");
        if (index < 0) return;

        RECT listRect = _iconManager.GetListViewScreenRect();

        // Bu simge şu an animasyondaysa/sabitse, sürüklemeye başlarken elimizdeki
        // en güncel görünür konumunu kullan.
        float localX, localY;
        if (_finalPositions.TryGetValue(index, out var last))
        {
            localX = last.X;
            localY = last.Y;
        }
        else
        {
            var pos = _iconManager.GetIconPosition(index);
            localX = pos.X;
            localY = pos.Y;
        }

        _activeAnimations.RemoveAll(a => a.IconIndex == index);
        _finalPositions.Remove(index);
        _iconManager.SetIconPosition(index, -3000, -3000); // gerçek simgeyi gizle, overlay gösterecek

        _draggingIndex = index;
        _dragX = localX;
        _dragY = localY;
        _dragSamples.Clear();
        _dragSamples.Add((DateTime.Now, localX, localY));

        Log($"Sürükleme başladı: index {index} konum ({localX},{localY})");
    }

    private void OnMouseMove(int screenX, int screenY)
    {
        if (_draggingIndex == null) return;

        RECT listRect = _iconManager.GetListViewScreenRect();
        _dragX = screenX - listRect.Left;
        _dragY = screenY - listRect.Top;

        var now = DateTime.Now;
        _dragSamples.Add((now, _dragX, _dragY));
        // sadece son ~150ms'lik örnekleri tut (hız hesaplamak için yeterli)
        _dragSamples.RemoveAll(s => (now - s.Time).TotalMilliseconds > 150);
    }

    private void OnLeftButtonUp(int screenX, int screenY)
    {
        if (_draggingIndex == null) return;
        int index = _draggingIndex.Value;
        _draggingIndex = null;

        RECT listRect = _iconManager.GetListViewScreenRect();

        // Fare hızını, son birkaç örnekten (konum farkı / zaman farkı) hesaplıyoruz.
        float velocityX = 0, velocityY = 0;
        if (_dragSamples.Count >= 2)
        {
            var oldest = _dragSamples[0];
            var newest = _dragSamples[^1];
            float dt = (float)(newest.Time - oldest.Time).TotalSeconds;
            if (dt > 0.001f)
            {
                velocityX = (newest.X - oldest.X) / dt;
                velocityY = (newest.Y - oldest.Y) / dt;
            }
        }

        // Neredeyse hiç sürüklenmemişse (basit tıklama) - küçük rastgele bir
        // fırlatma hızı ver, yine de eğlenceli bir tepki olsun.
        bool wasRealDrag = Math.Abs(velocityX) > 30 || Math.Abs(velocityY) > 30;
        if (!wasRealDrag)
        {
            velocityX = (float)(_rng.NextDouble() * 800 - 400);
            velocityY = (float)(-600 - _rng.NextDouble() * 400);
        }

        // Fare olayları bazen çok yakın zamanlı gelip gerçekçi olmayan devasa
        // hızlar hesaplanmasına sebep olabiliyor (simge ekranın köşesine sıkışıp
        // kalıyordu) - bu yüzden makul bir üst sınır koyuyoruz.
        const float MaxThrowSpeed = 2600f;
        float speed = MathF.Sqrt(velocityX * velocityX + velocityY * velocityY);
        if (speed > MaxThrowSpeed)
        {
            float scale = MaxThrowSpeed / speed;
            velocityX *= scale;
            velocityY *= scale;
        }

        var screenPoint = new Point(listRect.Left + (int)_dragX, listRect.Top + (int)_dragY);
        var screen = Screen.FromPoint(screenPoint);

        var (minX, maxX, minY, floorY) = ComputeThrowBounds(screen, listRect);

        Log($"Fırlatıldı: index {index} hız ({velocityX:0},{velocityY:0}) gerçekSürükleme={wasRealDrag}");

        _activeAnimations.RemoveAll(a => a.IconIndex == index);
        _activeAnimations.Add(new ThrownIcon(index, _dragX, _dragY, velocityX, velocityY,
            minX, maxX, minY, floorY, _rng));
    }

    // Bir simgenin çarpabileceği ekran sınırlarını (yerel liste kutusu koordinatlarında) hesaplar
    private static (float minX, float maxX, float minY, float floorY) ComputeThrowBounds(Screen screen, RECT listRect)
    {
        float minX = screen.Bounds.Left - listRect.Left;
        float maxX = screen.Bounds.Right - listRect.Left - 40;
        float minY = screen.Bounds.Top - listRect.Top;
        float floorY = screen.WorkingArea.Bottom - listRect.Top - 40;
        return (minX, maxX, minY, floorY);
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        float delta = (float)(now - _lastTick).TotalSeconds;
        _lastTick = now;
        if (delta > 0.05f) delta = 0.05f;

        RECT listRect = _iconManager.GetListViewScreenRect();
        bool anyActive = _activeAnimations.Count > 0 || _draggingIndex != null;

        // Şu an elde sürüklenen simgeyi (varsa) çiz
        if (_draggingIndex is int dragIdx)
        {
            Bitmap dragBmp = _iconBitmaps.TryGetValue(dragIdx, out var db) ? db : SystemIcons.Application.ToBitmap();
            _overlay?.SetIcon(dragIdx, dragBmp, listRect.Left + _dragX, listRect.Top + _dragY, 0f);
        }

        for (int i = _activeAnimations.Count - 1; i >= 0; i--)
        {
            var anim = _activeAnimations[i];
            anim.Update(delta);

            float screenX = listRect.Left + anim.X;
            float screenY = listRect.Top + anim.Y;
            Bitmap bmp = _iconBitmaps.TryGetValue(anim.IconIndex, out var b) ? b : SystemIcons.Application.ToBitmap();
            _overlay?.SetIcon(anim.IconIndex, bmp, screenX, screenY, anim.RotationDegrees);

            if (anim.IsSettled)
            {
                _iconManager.SetIconPosition(anim.IconIndex, (int)anim.X, (int)anim.Y);
                _finalPositions[anim.IconIndex] = (anim.X, anim.Y);
                _overlay?.RemoveIcon(anim.IconIndex);
                _activeAnimations.RemoveAt(i);
            }
        }

        if (anyActive)
            _overlay?.RedrawAll();

        ProcessAudioReactivity();

        _reassertCounter++;
        if (_reassertCounter >= 6)
        {
            _reassertCounter = 0;
            foreach (var (index, pos) in _finalPositions)
            {
                _iconManager.SetIconPosition(index, (int)pos.X, (int)pos.Y);
            }
        }
    }

    // Her karede genel ses seviyesine bakar. Ses aniden yükseldiğinde (bir vuruş/beat
    // olduğunda), YERDEKİ VE HAVADAKİ TÜM simgeler AYNI ANDA zıplar - ama hepsi birebir
    // aynı yüksekliğe değil, her birine küçük rastgele bir fark (biri biraz daha yüksek,
    // biri biraz daha alçak) veriyoruz ki doğal/canlı görünsün.
    private void ProcessAudioReactivity()
    {
        if (_audioAnalyzer == null) return;

        float level = _audioAnalyzer.OverallVolume;

        _volumeLogCounter++;
        if (_volumeLogCounter >= 90) // ~1.5 saniyede bir
        {
            _volumeLogCounter = 0;
            Log($"[ses izleme] anlık seviye={level:0.00} taban={_volumeBaseline:0.00}");
        }

        // Ses, yavaşça takip eden "taban" seviyesinin belirgin şekilde üzerine
        // çıktığında bunu bir vuruş (beat) sayıyoruz - kare-kare fark almaktan
        // çok daha tutarlı, gerçek vurgu anlarıyla daha iyi örtüşüyor.
        bool isBeat = level > _volumeBaseline + OverallRiseThreshold && level > MinOverallLevel;

        // Taban, vuruş anında değil normal seyirde yavaşça sesi takip etsin
        _volumeBaseline = _volumeBaseline * (1 - BaselineSmoothing) + level * BaselineSmoothing;

        if (!isBeat) return;
        var now = DateTime.Now;
        if (now - _lastGlobalTrigger < GlobalCooldown) return;
        _lastGlobalTrigger = now;

        float rise = level - _volumeBaseline;
        Log($"Ses vuruşu algılandı: seviye={level:0.00} taban={_volumeBaseline:0.00}");

        RECT listRect = _iconManager.GetListViewScreenRect();
        float baseStrength = 260f + rise * 750f + level * 280f;
        baseStrength = Math.Min(baseStrength, 780f);

        // Yerdeki (yerleşmiş) simgeleri zıplat
        foreach (var kv in _finalPositions.ToList())
        {
            int index = kv.Key;
            var pos = kv.Value;
            var screenPoint = new Point(listRect.Left + (int)pos.X, listRect.Top + (int)pos.Y);
            var screen = Screen.FromPoint(screenPoint);
            var (minX, maxX, minY, floorY) = ComputeThrowBounds(screen, listRect);

            // Her simgeye küçük, birbirinden farklı bir sapma - hepsi aynı yükseklikte
            // durmasın ama çok da uzak olmasın (yaklaşık %20'lik doğal fark)
            float variance = (float)(_rng.NextDouble() * 160 - 80);
            float vy = -(baseStrength + variance);
            float vx = (float)(_rng.NextDouble() * 200 - 100);

            // Gerçek Windows simgesini gizle - aksi halde eski yerinde bir "klon"
            // olarak görünmeye devam ediyordu, overlay'deki animasyonlu kopyayla birlikte.
            _iconManager.SetIconPosition(index, -3000, -3000);

            _finalPositions.Remove(index);
            _activeAnimations.RemoveAll(a => a.IconIndex == index);
            _activeAnimations.Add(new ThrownIcon(index, pos.X, pos.Y, vx, vy, minX, maxX, minY, floorY, _rng));
        }

        // Hâlâ havada olanlara da (yeni animasyon oluşturmadan) ekstra itme ver
        foreach (var thrown in _activeAnimations.OfType<ThrownIcon>().ToList())
        {
            float variance = (float)(_rng.NextDouble() * 120 - 60);
            float vx = (float)(_rng.NextDouble() * 160 - 80);
            thrown.ApplyImpulse(vx, -(baseStrength * 0.6f + variance));
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _mouseHook.Dispose();
        _animationTimer.Stop();
        _iconManager.Dispose();
        _audioAnalyzer?.Dispose();
        _overlay?.Close();
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.OnFormClosed(e);
    }
}
