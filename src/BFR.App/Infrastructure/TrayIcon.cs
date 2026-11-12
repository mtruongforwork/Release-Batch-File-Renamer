namespace BFR.App.Infrastructure;

using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

/// <summary>
/// Biểu tượng khay hệ thống (Shell_NotifyIcon) viết bằng Win32 thuần.
/// Không dùng WinForms để tránh kéo namespace System.Windows.Forms vào cả project.
/// Menu là menu Win32 gốc (TrackPopupMenu) nên đóng/mở ổn định và đúng kiểu hệ thống.
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_APP_TRAY = 0x8000 + 1;
    private const int WM_LBUTTONUP = 0x0202, WM_LBUTTONDBLCLK = 0x0203, WM_RBUTTONUP = 0x0205, WM_NULL = 0;
    private const uint NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const uint NIF_MESSAGE = 1, NIF_ICON = 2, NIF_TIP = 4;
    private const uint MF_STRING = 0, MF_SEPARATOR = 0x800;
    private const uint TPM_RIGHTBUTTON = 2, TPM_RETURNCMD = 0x100, TPM_BOTTOMALIGN = 0x20;

    private const int CmdOpen = 1, CmdShelf = 2, CmdExit = 3;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public nint hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);
    [DllImport("user32.dll")] private static extern nint CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(nint menu, uint flags, nuint id, string? text);
    [DllImport("user32.dll")] private static extern bool DestroyMenu(nint menu);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    [DllImport("user32.dll")] private static extern int TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved, nint hWnd, nint rect);
    [DllImport("user32.dll")] private static extern bool PostMessage(nint hWnd, int msg, nint w, nint l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, nint[]? large, nint[]? small, uint count);

    private readonly HwndSource _window;
    private readonly string _tooltip;
    private readonly Action _open, _shelf, _exit;
    private readonly string _openText, _shelfText, _exitText;
    private readonly uint _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
    private nint _icon;
    private bool _added;

    public TrayIcon(string tooltip, string openText, string shelfText, string exitText,
        Action open, Action shelf, Action exit)
    {
        _tooltip = tooltip;
        _openText = openText; _shelfText = shelfText; _exitText = exitText;
        _open = open; _shelf = shelf; _exit = exit;

        // Cửa sổ ẩn chỉ để nhận thông điệp từ biểu tượng khay (message-only window).
        _window = new HwndSource(new HwndSourceParameters("BFR.TrayHost")
        {
            ParentWindow = new nint(-3),   // HWND_MESSAGE
            Width = 0, Height = 0
        });
        _window.AddHook(WndProc);

        var small = new nint[1];
        if (ExtractIconEx(Environment.ProcessPath ?? string.Empty, 0, null, small, 1) > 0) _icon = small[0];

        Add();
    }

    private void Add()
    {
        var data = NewData();
        _added = Shell_NotifyIcon(NIM_ADD, ref data);
    }

    private NOTIFYICONDATA NewData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _window.Handle,
        uID = 1,
        uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
        uCallbackMessage = WM_APP_TRAY,
        hIcon = _icon,
        szTip = _tooltip.Length > 127 ? _tooltip[..127] : _tooltip
    };

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if ((uint)msg == _taskbarCreated)
        {
            Add();   // Explorer khởi động lại: thêm lại biểu tượng
            return 0;
        }

        if (msg == WM_APP_TRAY)
        {
            var mouse = (int)(lParam.ToInt64() & 0xFFFF);
            if (mouse == WM_LBUTTONUP || mouse == WM_LBUTTONDBLCLK) { handled = true; _open(); }
            else if (mouse == WM_RBUTTONUP) { handled = true; ShowMenu(); }
        }
        return 0;
    }

    private void ShowMenu()
    {
        var menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MF_STRING, CmdOpen, _openText);
            AppendMenu(menu, MF_STRING, CmdShelf, _shelfText);
            AppendMenu(menu, MF_SEPARATOR, 0, null);
            AppendMenu(menu, MF_STRING, CmdExit, _exitText);

            GetCursorPos(out var p);
            SetForegroundWindow(_window.Handle);   // bắt buộc để menu tự đóng khi bấm ra ngoài
            var cmd = TrackPopupMenu(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD | TPM_BOTTOMALIGN, p.X, p.Y, 0, _window.Handle, 0);
            PostMessage(_window.Handle, WM_NULL, 0, 0);

            switch (cmd)
            {
                case CmdOpen: _open(); break;
                case CmdShelf: _shelf(); break;
                case CmdExit: _exit(); break;
            }
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    public void Dispose()
    {
        if (_added)
        {
            var data = NewData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
            _added = false;
        }
        _window.RemoveHook(WndProc);
        _window.Dispose();
        if (_icon != 0) { DestroyIcon(_icon); _icon = 0; }
    }
}
