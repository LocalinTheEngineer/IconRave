using System.Runtime.InteropServices;
using static DesktopIconDropper.NativeMethods;

namespace DesktopIconDropper;

// Bu sınıf, masaüstündeki simge listesini (SysListView32) bulur ve
// simgelerin ekran koordinatlarını okuyup değiştirmemizi sağlar.
//
// ÖNEMLİ: SysListView32 penceresi bizim programımıza değil, explorer.exe'ye
// aittir. Bazı mesajlar (LVM_SETITEMPOSITION gibi) veriyi doğrudan sayı olarak
// gönderdiği için sorun çıkarmaz. Ama bazıları (LVM_HITTEST, LVM_GETITEMPOSITION)
// veriyi bir BELLEK ADRESİ üzerinden alıp verir - kendi programımızın belleğindeki
// bir adresi explorer.exe'ye gönderirsek, o adres onun için anlamsız olur ve
// hep 0/varsayılan değer döner. Bu yüzden explorer.exe'nin kendi belleğinde
// küçük bir alan ayırıp veriyi oraya yazıyor, oradan okuyoruz.
internal class DesktopIconManager : IDisposable
{
    private nint _listViewHandle;
    private nint _remoteProcessHandle;
    private nint _remoteBuffer;
    private const uint RemoteBufferSize = 700; // ilk 128 bayt struct için, kalanı metin (isim) için
    private const int TextBufferOffset = 128;
    private const int MaxTextChars = 256;

    public bool Initialize()
    {
        _listViewHandle = FindDesktopListView();
        if (_listViewHandle == 0) return false;

        GetWindowThreadProcessId(_listViewHandle, out uint processId);
        _remoteProcessHandle = OpenProcess(
            PROCESS_VM_OPERATION | PROCESS_VM_READ | PROCESS_VM_WRITE | PROCESS_QUERY_INFORMATION,
            false, processId);

        if (_remoteProcessHandle == 0) return false;

        _remoteBuffer = VirtualAllocEx(_remoteProcessHandle, 0, RemoteBufferSize,
            MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);

        return _remoteBuffer != 0;
    }

    private static nint FindDesktopListView()
    {
        nint progman = FindWindow("Progman", null);
        nint listView = FindWindowEx(progman, 0, "SHELLDLL_DefView", null);
        nint result = FindWindowEx(listView, 0, "SysListView32", null);

        if (result != 0)
            return result;

        nint workerW = 0;
        do
        {
            workerW = FindWindowEx(0, workerW, "WorkerW", null);
            if (workerW == 0) break;

            nint defView = FindWindowEx(workerW, 0, "SHELLDLL_DefView", null);
            if (defView != 0)
            {
                result = FindWindowEx(defView, 0, "SysListView32", null);
                if (result != 0)
                    return result;
            }
        } while (workerW != 0);

        return 0;
    }

    public int GetIconCount()
    {
        if (_listViewHandle == 0) return 0;
        return (int)SendMessage(_listViewHandle, LVM_GETITEMCOUNT, 0, 0);
    }

    // Belirli bir simgenin, liste kutusunun İÇİNDEKİ (ekran değil) konumunu okur.
    // Uzak belleğe boş bir POINT yazıp, explorer'a "bu adresi doldur" diyoruz,
    // sonra o adresten geri okuyoruz.
    public POINT GetIconPosition(int index)
    {
        POINT empty = new();
        WriteRemote(empty);

        SendMessage(_listViewHandle, LVM_GETITEMPOSITION, index, _remoteBuffer);

        return ReadRemote<POINT>();
    }

    // Bu mesaj veriyi POINTER değil DOĞRUDAN SAYI (lParam içinde x,y) olarak
    // gönderdiği için uzak bellek gerektirmiyor.
    public void SetIconPosition(int index, int x, int y)
    {
        if (_listViewHandle == 0) return;
        nint lParam = MakeLParam(x, y);
        SendMessage(_listViewHandle, LVM_SETITEMPOSITION, index, lParam);
    }

    public RECT GetListViewScreenRect()
    {
        GetWindowRect(_listViewHandle, out RECT rect);
        return rect;
    }

    // Ekrandaki bir noktanın (örneğin fare tıklaması) hangi simgeye denk geldiğini bulur.
    // Bulamazsa -1 döner.
    public int HitTest(int screenX, int screenY)
    {
        RECT listRect = GetListViewScreenRect();

        LVHITTESTINFO info = new()
        {
            pt = new POINT { X = screenX - listRect.Left, Y = screenY - listRect.Top },
            flags = 0,
            iItem = -1,
            iSubItem = 0,
            iGroup = 0
        };

        WriteRemote(info);
        SendMessage(_listViewHandle, LVM_HITTEST, 0, _remoteBuffer);
        LVHITTESTINFO result = ReadRemote<LVHITTESTINFO>();

        return result.iItem;
    }

    // Simgenin görünen adını okur (örn. "Belgelerim", "Chrome"). Bunu, ikonun gerçek
    // dosya yolunu bulup üzerinden gerçek Windows ikon resmini çekebilmek için kullanıyoruz.
    public string GetItemText(int index)
    {
        nint textPtr = _remoteBuffer + TextBufferOffset;

        LVITEM item = new()
        {
            mask = LVIF_TEXT,
            iItem = index,
            iSubItem = 0,
            pszText = textPtr,
            cchTextMax = MaxTextChars
        };

        WriteRemote(item);
        SendMessage(_listViewHandle, LVM_GETITEMTEXTW, index, _remoteBuffer);

        byte[] bytes = new byte[MaxTextChars * 2];
        ReadProcessMemory(_remoteProcessHandle, textPtr, bytes, (uint)bytes.Length, out _);
        string text = System.Text.Encoding.Unicode.GetString(bytes);
        int nullIndex = text.IndexOf('\0');
        return nullIndex >= 0 ? text[..nullIndex] : text;
    }

    // --- Yardımcı fonksiyonlar: yerel struct'ı uzak (explorer.exe) belleğe yaz/oku ---

    private void WriteRemote<T>(T value) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        nint localPtr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, localPtr, false);
            byte[] bytes = new byte[size];
            Marshal.Copy(localPtr, bytes, 0, size);
            WriteProcessMemory(_remoteProcessHandle, _remoteBuffer, bytes, (uint)size, out _);
        }
        finally
        {
            Marshal.FreeHGlobal(localPtr);
        }
    }

    private T ReadRemote<T>() where T : struct
    {
        int size = Marshal.SizeOf<T>();
        byte[] bytes = new byte[size];
        ReadProcessMemory(_remoteProcessHandle, _remoteBuffer, bytes, (uint)size, out _);

        nint localPtr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.Copy(bytes, 0, localPtr, size);
            return Marshal.PtrToStructure<T>(localPtr)!;
        }
        finally
        {
            Marshal.FreeHGlobal(localPtr);
        }
    }

    private static nint MakeLParam(int lo, int hi)
    {
        return (nint)((hi << 16) | (lo & 0xFFFF));
    }

    public void Dispose()
    {
        if (_remoteBuffer != 0 && _remoteProcessHandle != 0)
        {
            VirtualFreeEx(_remoteProcessHandle, _remoteBuffer, 0, MEM_RELEASE);
            _remoteBuffer = 0;
        }
        if (_remoteProcessHandle != 0)
        {
            CloseHandle(_remoteProcessHandle);
            _remoteProcessHandle = 0;
        }
        GC.SuppressFinalize(this);
    }
}
