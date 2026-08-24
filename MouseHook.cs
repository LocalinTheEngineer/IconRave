using static DesktopIconDropper.NativeMethods;

namespace DesktopIconDropper;

// Windows'ta "global mouse hook": uygulamamız arka planda çalışırken bile,
// kullanıcının fare ile yaptığı HER ŞEYİ (basma, hareket, bırakma) ekranın
// neresinde olursa olsun yakalayabiliyoruz. Bunu hem tıklamayı hem de
// "sürükleyip fırlatma" hareketini algılamak için kullanıyoruz.
internal class MouseHook : IDisposable
{
    private nint _hookHandle;
    private LowLevelMouseProc? _proc;

    public event Action<int, int>? LeftButtonDown;
    public event Action<int, int>? MouseMove;
    public event Action<int, int>? LeftButtonUp;

    public void Start()
    {
        _proc = HookCallback;
        using var curProcess = System.Diagnostics.Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule!;
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName!), 0);
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var hookStruct = System.Runtime.InteropServices.Marshal.PtrToStructure<POINT>(lParam);

            if (wParam == WM_LBUTTONDOWN)
                LeftButtonDown?.Invoke(hookStruct.X, hookStruct.Y);
            else if (wParam == WM_MOUSEMOVE)
                MouseMove?.Invoke(hookStruct.X, hookStruct.Y);
            else if (wParam == WM_LBUTTONUP)
                LeftButtonUp?.Invoke(hookStruct.X, hookStruct.Y);
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != 0)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = 0;
        }
        GC.SuppressFinalize(this);
    }
}
