namespace BFR.App.Infrastructure;

using BFR.Core;
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

/// <summary>
/// Phím tắt toàn cục (B4) bằng <c>RegisterHotKey</c> trên cửa sổ chỉ nhận thông điệp.
/// Khác với hook chuột, không theo dõi bàn phím nói chung: Windows chỉ báo đúng tổ hợp đã đăng ký.
/// Ném <see cref="Win32Exception"/> nếu tổ hợp đã được ứng dụng khác chiếm.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;
    private const int HotkeyId = 0xBF01;

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(nint hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(nint hWnd, int id);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

    private readonly HwndSource _window;
    private readonly Action _pressed;
    private bool _registered;

    public GlobalHotkey(HotkeySpec spec, Action pressed)
    {
        _pressed = pressed;
        Spec = spec;

        _window = new HwndSource(new HwndSourceParameters("BFR.HotkeyHost")
        {
            ParentWindow = new nint(-3),   // HWND_MESSAGE
            Width = 0,
            Height = 0
        });
        _window.AddHook(WndProc);

        if (!RegisterHotKey(_window.Handle, HotkeyId, spec.Modifiers | MOD_NOREPEAT, spec.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            _window.RemoveHook(WndProc);
            _window.Dispose();
            throw new Win32Exception(error, $"Không đăng ký được phím tắt {spec}.");
        }

        _registered = true;
    }

    public HotkeySpec Spec { get; }

    /// <summary>Vị trí con trỏ (toạ độ vật lý) — nơi Shelf sẽ hiện ra khi bấm phím tắt.</summary>
    public static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var p);
        return (p.X, p.Y);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            handled = true;
            _pressed();
        }
        return 0;
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_window.Handle, HotkeyId);
            _registered = false;
        }
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }
}
