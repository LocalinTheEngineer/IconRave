using System.Runtime.InteropServices;
using static DesktopIconDropper.NativeMethods;

namespace DesktopIconDropper;

// Masaüstündeki bir simgenin GÖRÜNEN ADINDAN yola çıkarak (örn. "Chrome"), gerçek
// dosya/kısayol yolunu bulup Windows'tan o dosyanın gerçek ikon resmini (Bitmap) çeker.
// Bunu, "takla atma" animasyonu sırasında gerçek simgeyi kendi çizdiğimiz bir resimle
// (döndürerek) göstermek için kullanıyoruz.
internal static class IconBitmapResolver
{
    private static readonly string[] DesktopDirs =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
    };

    public static Bitmap? GetIconBitmap(string itemName)
    {
        string? path = ResolvePath(itemName);
        if (path == null) return null;

        SHFILEINFO shfi = new();
        nint result = SHGetFileInfo(path, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFO>(),
            SHGFI_ICON | SHGFI_LARGEICON);

        if (result == 0 || shfi.hIcon == 0) return null;

        try
        {
            using Icon icon = Icon.FromHandle(shfi.hIcon);
            return (Bitmap)icon.ToBitmap().Clone();
        }
        finally
        {
            DestroyIcon(shfi.hIcon);
        }
    }

    private static string? ResolvePath(string itemName)
    {
        foreach (var dir in DesktopDirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;

            string direct = Path.Combine(dir, itemName);
            if (File.Exists(direct) || Directory.Exists(direct))
                return direct;

            // Windows kısayollarda uzantıyı (.lnk vs.) gizlediği için, isim tam
            // eşleşmezse dosya adını uzantısız karşılaştırarak arıyoruz.
            try
            {
                var match = Directory.EnumerateFileSystemEntries(dir).FirstOrDefault(f =>
                    string.Equals(Path.GetFileNameWithoutExtension(f), itemName, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }
            catch
            {
                // erişim engellenmiş bir klasör olabilir, yoksay
            }
        }
        return null;
    }
}
