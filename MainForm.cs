using static DesktopIconDropper.NativeMethods;

namespace DesktopIconDropper;

// Bu form kullanıcıya GÖRÜNMEZ. Sadece arka planda çalışıp animasyon
// döngüsünü yönetmek için var. Kullanıcı, sistem tepsisindeki
// (saat yanındaki) ikondan ayarları açabilir veya uygulamadan çıkabilir.
public class MainForm : Form
{
    private readonly DesktopIconManager _iconManager = new();
    private readonly MouseHook _mouseHook = new();
    private readonly System.Windows.Forms.Timer _animationTimer = new();
    private readonly List<IIconAnimation> _activeAnimations = new();
    private readonly Random _rng = new();
    private readonly Dictionary<int, Bitmap> _iconBitmaps = new();
    private readonly Dictionary<int, (float X, float Y)> _finalPositions = new();
    // Uygulama açıldığındaki orijinal simge konumları - çıkışta geri döndürmek için
    private readonly Dictionary<int, (int X, int Y)> _originalPositions = new();
    private NotifyIcon? _trayIcon;
    private OverlayForm? _overlay;
    private SettingsForm? _settingsForm;
    private int _reassertCounter;

    private readonly AppSettings _settings = AppSettings.Load();

    // --- Ses senkronizasyonu ---
    private AudioAnalyzer? _audioAnalyzer;
    private float _volumeBaseline;
    private DateTime _lastGlobalTrigger = DateTime.MinValue;
    private const float BaseRiseThreshold = 0.10f; // duyarlılık ayarıyla bölünür
    private const float BaseMinLevel = 0.06f;
    private const float BaselineSmoothing = 0.06f;

    // --- Sürükleme (drag & fırlatma) durumu ---
    private int? _draggingIndex;
    private float _dragX, _dragY;
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

        // Windows'un varsayılan zamanlayıcı hassasiyeti (~15ms) yerine 1ms -
        // animasyonlar daha pürüzsüz görünür.
        timeBeginPeriod(1);

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
        SaveOriginalPositions();

        _mouseHook.LeftButtonDown += OnLeftButtonDown;
        _mouseHook.MouseMove += OnMouseMove;
        _mouseHook.LeftButtonUp += OnLeftButtonUp;
        _mouseHook.Start();

        _animationTimer.Interval = 16; // ~60fps
        _animationTimer.Tick += OnAnimationTick;
        _animationTimer.Start();

        if (_settings.DropIconsOnStartup)
            DropAllIconsAutomatically();

        _audioAnalyzer = new AudioAnalyzer();
        bool audioOk = _audioAnalyzer.Start();
        Log(audioOk ? "Ses analizi başlatıldı." : "UYARI: Ses analizi başlatılamadı.");
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "IconRave"
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Ayarlar...", null, (_, _) => OpenSettings());
        menu.Items.Add("Simgeleri yeniden düşür", null, (_, _) => DropAllIconsAutomatically());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Çıkış", null, (_, _) => Application.Exit());
        _trayIcon.ContextMenuStrip = menu;

        // Tepsi ikonuna çift tıklayınca da ayarlar açılsın
        _trayIcon.DoubleClick += (_, _) => OpenSettings();
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.BringToFront();
            _settingsForm.Focus();
            return;
        }

        _settingsForm = new SettingsForm(_settings, OnSettingsChanged);
        _settingsForm.Show();
    }

    // Ayarlar değiştiğinde çağrılır. Çoğu ayar (fizik, ses) doğrudan _settings
    // üzerinden okunduğu için anında etkili olur; burada sadece özel durumları işliyoruz.
    private void OnSettingsChanged()
    {
        if (!_settings.AudioReactionEnabled)
            _volumeBaseline = 0f;
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

    // Uygulama açılışındaki konumları sakla - "çıkışta geri döndür" ayarı için
    private void SaveOriginalPositions()
    {
        int count = _iconManager.GetIconCount();
        for (int i = 0; i < count; i++)
        {
            var pos = _iconManager.GetIconPosition(i);
            _originalPositions[i] = (pos.X, pos.Y);
        }
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
        _activeAnimations.Add(new FallingIcon(index, startX, startY, targetY, minX, maxX,
            scatterBandHeight, _rng, _settings));
    }

    // --- Sürükleyip fırlatma ---

    private void OnLeftButtonDown(int screenX, int screenY)
    {
        if (!_settings.EnableDragThrow) return;

        int index = _iconManager.HitTest(screenX, screenY);
        if (index < 0) return;

        RECT listRect = _iconManager.GetListViewScreenRect();

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
        _iconManager.SetIconPosition(index, -3000, -3000);

        _draggingIndex = index;
        _dragX = localX;
        _dragY = localY;
        _dragSamples.Clear();
        _dragSamples.Add((DateTime.Now, localX, localY));
    }

    private void OnMouseMove(int screenX, int screenY)
    {
        if (_draggingIndex == null) return;

        RECT listRect = _iconManager.GetListViewScreenRect();
        _dragX = screenX - listRect.Left;
        _dragY = screenY - listRect.Top;

        var now = DateTime.Now;
        _dragSamples.Add((now, _dragX, _dragY));
        _dragSamples.RemoveAll(s => (now - s.Time).TotalMilliseconds > 150);
    }

    private void OnLeftButtonUp(int screenX, int screenY)
    {
        if (_draggingIndex == null) return;
        int index = _draggingIndex.Value;
        _draggingIndex = null;

        RECT listRect = _iconManager.GetListViewScreenRect();

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

        bool wasRealDrag = Math.Abs(velocityX) > 30 || Math.Abs(velocityY) > 30;
        if (!wasRealDrag)
        {
            velocityX = (float)(_rng.NextDouble() * 800 - 400);
            velocityY = (float)(-600 - _rng.NextDouble() * 400);
        }

        // Kullanıcının ayarladığı fırlatma gücü çarpanı
        velocityX *= _settings.ThrowPowerMultiplier;
        velocityY *= _settings.ThrowPowerMultiplier;

        // Aşırı büyük hızlara üst sınır (simge köşeye sıkışmasın)
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

        _activeAnimations.RemoveAll(a => a.IconIndex == index);
        _activeAnimations.Add(new ThrownIcon(index, _dragX, _dragY, velocityX, velocityY,
            minX, maxX, minY, floorY, _rng, _settings));
    }

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

    // Ses vuruşu algılandığında tüm simgeleri birlikte zıplatır.
    // Hangi frekans aralığına (vokal/bas/tiz/genel) bakılacağı, duyarlılık,
    // zıplama gücü ve çeşitliliği kullanıcının ayarlarından gelir.
    private void ProcessAudioReactivity()
    {
        if (_audioAnalyzer == null || !_settings.AudioReactionEnabled) return;

        float level = _audioAnalyzer.GetLevel(_settings.AudioMode);

        // Duyarlılık arttıkça eşikler düşer (küçük seslere de tepki verir)
        float riseThreshold = BaseRiseThreshold / Math.Max(0.2f, _settings.Sensitivity);
        float minLevel = BaseMinLevel / Math.Max(0.2f, _settings.Sensitivity);

        bool isBeat = level > _volumeBaseline + riseThreshold && level > minLevel;
        _volumeBaseline = _volumeBaseline * (1 - BaselineSmoothing) + level * BaselineSmoothing;

        if (!isBeat) return;
        var now = DateTime.Now;
        if ((now - _lastGlobalTrigger).TotalMilliseconds < _settings.CooldownMs) return;
        _lastGlobalTrigger = now;

        RECT listRect = _iconManager.GetListViewScreenRect();
        float rise = level - _volumeBaseline;
        float baseStrength = (260f + rise * 750f + level * 280f) * _settings.JumpStrength;
        baseStrength = Math.Min(baseStrength, 780f * _settings.JumpStrength);

        float varianceRange = 160f * _settings.JumpVariance;

        foreach (var kv in _finalPositions.ToList())
        {
            int index = kv.Key;
            var pos = kv.Value;
            var screenPoint = new Point(listRect.Left + (int)pos.X, listRect.Top + (int)pos.Y);
            var screen = Screen.FromPoint(screenPoint);
            var (minX, maxX, minY, floorY) = ComputeThrowBounds(screen, listRect);

            float variance = (float)(_rng.NextDouble() * varianceRange - varianceRange / 2);
            float vy = -(baseStrength + variance);
            float vx = (float)(_rng.NextDouble() * 200 - 100);

            // Gerçek simgeyi gizle - overlay'deki animasyonlu kopya gösterilecek
            _iconManager.SetIconPosition(index, -3000, -3000);

            _finalPositions.Remove(index);
            _activeAnimations.RemoveAll(a => a.IconIndex == index);
            _activeAnimations.Add(new ThrownIcon(index, pos.X, pos.Y, vx, vy,
                minX, maxX, minY, floorY, _rng, _settings));
        }

        // Hâlâ havada olanlara ekstra itme
        foreach (var thrown in _activeAnimations.OfType<ThrownIcon>().ToList())
        {
            float variance = (float)(_rng.NextDouble() * varianceRange * 0.75f - varianceRange * 0.375f);
            float vx = (float)(_rng.NextDouble() * 160 - 80);
            thrown.ApplyImpulse(vx, -(baseStrength * 0.6f + variance));
        }
    }

    // Çıkarken simgeleri açılıştaki yerlerine geri koy (ayara bağlı)
    private void RestoreOriginalPositions()
    {
        foreach (var (index, pos) in _originalPositions)
        {
            _iconManager.SetIconPosition(index, pos.X, pos.Y);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_settings.RestoreIconsOnExit)
        {
            try { RestoreOriginalPositions(); } catch { }
        }

        _mouseHook.Dispose();
        _animationTimer.Stop();
        _audioAnalyzer?.Dispose();
        _overlay?.Close();
        _iconManager.Dispose();
        timeEndPeriod(1);

        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
        }
        base.OnFormClosed(e);
    }
}
