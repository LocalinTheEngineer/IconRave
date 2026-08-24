using System.Runtime.InteropServices;

namespace DesktopIconDropper;

// Bu dosya, Windows'un kendi iç fonksiyonlarını (Win32 API) C# içine "çağırmamızı"
// sağlayan tanımları içerir. Masaüstü simgeleri normal bir uygulama penceresi değil,
// Windows Gezgini (explorer.exe) içinde gizli bir liste kutusudur (SysListView32).
// Bu fonksiyonlarla o listeye erişip simgelerin yerini okuyup değiştirebiliriz.
internal static class NativeMethods
{
    // --- Pencere bulma ---
    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint FindWindowEx(nint hwndParent, nint hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    public static extern nint GetShellWindow();

    [DllImport("user32.dll")]
    public static extern nint GetDesktopWindow();

    // --- Pencereye mesaj gönderme (liste kutusunu kontrol etmek için) ---
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern nint SendMessage(nint hWnd, int msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern nint SendMessage(nint hWnd, int msg, nint wParam, ref POINT lParam);

    // --- Görev çubuğunun konumunu bulmak için ---
    [DllImport("user32.dll")]
    public static extern nint FindWindow(string lpClassName, nint zero);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

    // --- Fare kancası (global tıklama yakalamak için) ---
    [DllImport("user32.dll")]
    public static extern nint SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    public static extern nint GetModuleHandle(string lpModuleName);

    // --- Süreçler arası bellek erişimi (cross-process memory) ---
    // LVM_HITTEST ve LVM_GETITEMPOSITION mesajları, veriyi bir POINTER (bellek adresi)
    // üzerinden alıp veriyor. Ama bizim programımız ile masaüstünü yöneten explorer.exe
    // AYRI bellek alanlarında çalışıyor - kendi adresimizi ona gönderirsek bir işe yaramaz.
    // Bu yüzden explorer.exe'nin belleğinde küçük bir alan ayırıp, veriyi oraya yazıp/okuyoruz.
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint VirtualAllocEx(nint hProcess, nint lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool VirtualFreeEx(nint hProcess, nint lpAddress, uint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteProcessMemory(nint hProcess, nint lpBaseAddress, byte[] lpBuffer, uint nSize, out nint lpNumberOfBytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(nint hProcess, nint lpBaseAddress, byte[] lpBuffer, uint nSize, out nint lpNumberOfBytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(nint hObject);

    public const uint PROCESS_VM_OPERATION = 0x0008;
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint PROCESS_VM_WRITE = 0x0020;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;

    public const uint MEM_COMMIT = 0x1000;
    public const uint MEM_RESERVE = 0x2000;
    public const uint MEM_RELEASE = 0x8000;
    public const uint PAGE_READWRITE = 0x04;

    // --- Simge adını okumak için (dosya yolunu bulup gerçek ikon resmini çekebilmek amacıyla) ---
    public const int LVM_GETITEMTEXTW = 0x1073;
    public const uint LVIF_TEXT = 0x0001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct LVITEM
    {
        public uint mask;
        public int iItem;
        public int iSubItem;
        public uint state;
        public uint stateMask;
        public nint pszText;
        public int cchTextMax;
        public int iImage;
        public nint lParam;
        public int iIndent;
        public int iGroupId;
        public uint cColumns;
        public nint puColumns;
        public nint piColFmt;
        public int iGroup;
    }

    // --- Dosya yolundan gerçek Windows ikonunu çekmek için ---
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern nint SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(nint hIcon);

    public const uint SHGFI_ICON = 0x100;
    public const uint SHGFI_LARGEICON = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    // --- Şeffaf/tıklamaya kapalı üst-pencere (overlay) oluşturmak için ---
    public const int WS_EX_TRANSPARENT = 0x20;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public delegate nint LowLevelMouseProc(int nCode, nint wParam, nint lParam);

    // --- Sabitler ---
    public const int WH_MOUSE_LL = 14;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;

    // ListView (simge listesi) mesajları
    public const int LVM_GETITEMCOUNT = 0x1004;
    public const int LVM_GETITEMPOSITION = 0x1010;
    public const int LVM_SETITEMPOSITION = 0x100F;
    public const int LVM_HITTEST = 0x1012;
    public const int LVM_GETITEMRECT = 0x100E;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct LVHITTESTINFO
    {
        public POINT pt;
        public uint flags;
        public int iItem;
        public int iSubItem;
        public int iGroup;
    }
}
