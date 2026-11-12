namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Mô tả một phím tắt toàn cục (B4), ví dụ "Ctrl+Alt+Space". Thuần chuỗi, không phụ thuộc Win32:
/// phần đăng ký thật nằm ở BFR.App (GlobalHotkey). Bắt buộc có ít nhất một phím bổ trợ để không
/// chiếm phím gõ thường.
/// </summary>
public sealed record HotkeySpec(bool Ctrl, bool Alt, bool Shift, bool Win, string Key)
{
    /// <summary>Các tổ hợp mặc định cho người dùng chọn.</summary>
    public static IReadOnlyList<string> Presets { get; } = new[]
    {
        "Ctrl+Shift+Space", "Ctrl+Alt+Q", "Ctrl+Alt+B", "Ctrl+Alt+Space", "Ctrl+Alt+S"
    };

    /// <summary>Mặc định ít đụng nhất: Ctrl+Alt+Space và Ctrl+Alt+S đã bị nhiều ứng dụng chiếm làm phím toàn cục.</summary>
    public const string Default = "Ctrl+Shift+Space";

    /// <summary>Phím chính được hỗ trợ: A–Z, 0–9, F1–F12, Space.</summary>
    public static bool IsSupportedKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0])) return true;
        if (key.Equals("Space", StringComparison.OrdinalIgnoreCase)) return true;
        return key.Length is 2 or 3 && (key[0] is 'F' or 'f') && int.TryParse(key.AsSpan(1), out var n) && n is >= 1 and <= 12;
    }

    public bool HasModifier => Ctrl || Alt || Shift || Win;

    public static bool TryParse(string? text, out HotkeySpec spec)
    {
        spec = null!;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ctrl = false, alt = false, shift = false, win = false;
        string? key = null;

        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win" or "windows": win = true; break;
                default:
                    if (key is not null) return false;   // hai phím chính
                    key = raw;
                    break;
            }
        }

        if (key is null || !IsSupportedKey(key)) return false;
        var normalized = key.Equals("space", StringComparison.OrdinalIgnoreCase)
            ? "Space"
            : key.ToUpperInvariant();

        var candidate = new HotkeySpec(ctrl, alt, shift, win, normalized);
        if (!candidate.HasModifier) return false;

        spec = candidate;
        return true;
    }

    /// <summary>Dạng chuẩn hoá: Ctrl+Alt+Shift+Win+Phím.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }

    /// <summary>Mã phím ảo Win32 của phím chính.</summary>
    public uint VirtualKey
    {
        get
        {
            if (Key == "Space") return 0x20;
            if (Key.Length == 1) return Key[0];                       // 'A'–'Z', '0'–'9' trùng mã VK
            return (uint)(0x70 + int.Parse(Key.AsSpan(1)) - 1);       // F1 = 0x70
        }
    }

    /// <summary>Cờ MOD_* của RegisterHotKey (MOD_NOREPEAT được thêm bởi nơi gọi).</summary>
    public uint Modifiers =>
        (Alt ? 0x1u : 0u) | (Ctrl ? 0x2u : 0u) | (Shift ? 0x4u : 0u) | (Win ? 0x8u : 0u);
}
