using Microsoft.Win32;

namespace DesktopIconDropper;

// Uygulamanın Windows açılışında otomatik başlamasını sağlar.
// Bunu, Windows'un kayıt defterindeki (registry) "Run" anahtarına
// uygulamanın yolunu ekleyerek/kaldırarak yapıyoruz. Bu, sadece
// mevcut kullanıcı için geçerlidir ve yönetici yetkisi gerektirmez.
internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "IconRave";

    public static void SetStartup(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null) return;

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(AppName, $"\"{exePath}\"");
            }
            else
            {
                if (key.GetValue(AppName) != null)
                    key.DeleteValue(AppName, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Kayıt defterine erişilemezse sessizce geç - uygulama çalışmaya devam etsin
        }
    }

    public static bool IsStartupEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }
}
