namespace ShakeTrial;

using System.Diagnostics;
using System.Runtime.InteropServices;

/// <summary>Hook chuột toàn cục (WH_MOUSE_LL): theo dõi trạng thái nút trái và vị trí.</summary>
public sealed class MouseHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT { public int X, Y; public uint MouseData, Flags, Time; public nint ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc cb, nint mod, uint tid);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint h);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint h, int n, nint w, nint l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);

    private readonly HookProc _proc;   // giữ tham chiếu để GC không thu hồi delegate
    private nint _handle;
    private readonly ShakeDetector _detector;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public bool LeftDown { get; private set; }
    public int Moves { get; private set; }
    public event Action<int, int>? Shaken;   // (x, y) lúc lắc khi đang giữ nút trái

    public MouseHook(ShakeDetector detector)
    {
        _detector = detector;
        _proc = Callback;
        _handle = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(Process.GetCurrentProcess().MainModule?.ModuleName), 0);
        if (_handle == 0) throw new InvalidOperationException("SetWindowsHookEx failed: " + Marshal.GetLastWin32Error());
    }

    private nint Callback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            switch ((int)wParam)
            {
                case WM_LBUTTONDOWN: LeftDown = true; _detector.Reset(); break;
                case WM_LBUTTONUP: LeftDown = false; _detector.Reset(); break;
                case WM_MOUSEMOVE:
                    Moves++;
                    if (LeftDown && _detector.Feed(info.X, _clock.ElapsedMilliseconds))
                        Shaken?.Invoke(info.X, info.Y);
                    break;
            }
        }
        return CallNextHookEx(_handle, nCode, wParam, lParam);   // không chặn input
    }

    public void Dispose()
    {
        if (_handle != 0) { UnhookWindowsHookEx(_handle); _handle = 0; }
    }
}
