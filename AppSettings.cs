using System.Text.Json;

namespace DesktopIconDropper;

// Kullanıcının ayarlar penceresinden değiştirebileceği tüm seçenekleri tutar.
// Ayarlar, kullanıcının AppData klasöründeki bir JSON dosyasına kaydedilir,
// böylece uygulama kapanıp açılsa bile korunur.
internal class AppSettings
{
    // --- Genel ---
    public bool StartWithWindows { get; set; } = false;
    public bool DropIconsOnStartup { get; set; } = true;
    public bool EnableDragThrow { get; set; } = true;
    public bool RestoreIconsOnExit { get; set; } = true;

    // --- Fizik ---
    public float Gravity { get; set; } = 1300f;              // 400 - 3000
    public float FallGravityMultiplier { get; set; } = 1.9f; // 1.0 - 3.0
    public float WallBounciness { get; set; } = 0.5f;         // 0.0 - 0.95
    public float FloorBounciness { get; set; } = 0.3f;        // 0.0 - 0.95
    public float SpinAmount { get; set; } = 1.0f;             // 0.0 - 3.0 (takla miktarı çarpanı)
    public float ThrowPowerMultiplier { get; set; } = 1.0f;   // 0.2 - 3.0 (elle fırlatma gücü)

    // --- Ses tepkisi ---
    public bool AudioReactionEnabled { get; set; } = true;
    public AudioMode AudioMode { get; set; } = AudioMode.Vocal;
    public float JumpStrength { get; set; } = 1.0f;      // 0.2 - 3.0 (zıplama gücü çarpanı)
    public float Sensitivity { get; set; } = 1.0f;       // 0.2 - 3.0 (algılama duyarlılığı)
    public int CooldownMs { get; set; } = 110;            // 40 - 600 (iki zıplama arası min süre)
    public float JumpVariance { get; set; } = 1.0f;      // 0.0 - 3.0 (simgeler arası yükseklik farkı)

    // --- Dosya işlemleri ---

    private static string SettingsPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "IconRave");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                string json = File.ReadAllText(SettingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);
                if (loaded != null) return loaded;
            }
        }
        catch
        {
            // Bozuk/okunamayan ayar dosyası varsa varsayılanlara dön
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch
        {
            // Kaydedilemezse uygulamayı durdurma
        }
    }

    public void ResetToDefaults()
    {
        var d = new AppSettings();
        StartWithWindows = d.StartWithWindows;
        DropIconsOnStartup = d.DropIconsOnStartup;
        EnableDragThrow = d.EnableDragThrow;
        RestoreIconsOnExit = d.RestoreIconsOnExit;
        Gravity = d.Gravity;
        FallGravityMultiplier = d.FallGravityMultiplier;
        WallBounciness = d.WallBounciness;
        FloorBounciness = d.FloorBounciness;
        SpinAmount = d.SpinAmount;
        ThrowPowerMultiplier = d.ThrowPowerMultiplier;
        AudioReactionEnabled = d.AudioReactionEnabled;
        AudioMode = d.AudioMode;
        JumpStrength = d.JumpStrength;
        Sensitivity = d.Sensitivity;
        CooldownMs = d.CooldownMs;
        JumpVariance = d.JumpVariance;
    }
}

// Sesin hangi bölümüne tepki verileceği
internal enum AudioMode
{
    Vocal,    // insan sesi aralığı (300Hz-3kHz civarı) - şarkı sözlerine daha duyarlı
    Bass,     // düşük frekanslar - davul/bas vuruşları
    Treble,   // yüksek frekanslar - zil, hi-hat gibi tiz sesler
    Overall   // tüm spektrum - genel ses seviyesi
}
