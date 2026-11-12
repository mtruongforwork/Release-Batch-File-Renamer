namespace BFR.App.Infrastructure;

using System.Windows;
using System.Windows.Media;

/// <summary>
/// Cầu nối giữa token cũ (Theme.Dark/Light: AppBackground, Accent, TextPrimary…) và bộ token mới (Themes/Bfr).
/// Ghi đè trực tiếp các khoá cũ ở mức Application.Resources (ưu tiên hơn từ điển gộp) bằng màu lấy từ token mới,
/// nên mọi màn hình chưa viết lại vẫn đổi diện mạo theo palette, bảng màu nhấn và sáng/tối mới.
/// Màn hình viết lại xong sẽ dùng thẳng token Bfr; khi không còn khoá cũ nào được dùng thì xoá lớp này.
/// Tương phản cao: gỡ ghi đè để quay về hành vi cũ.
/// </summary>
public static class LegacyBridge
{
    private static readonly string[] Keys =
    {
        "AppBackground", "PanelBackground", "SurfaceBackground", "SurfaceHover", "SurfaceSelected", "CardBackground",
        "TextPrimary", "TextSecondary", "TextDisabled", "TextOnAccent",
        "Accent", "AccentHover", "AccentPressed", "AccentSoft", "AccentBorder",
        "Border", "BorderStrong", "Divider",
        "Success", "SuccessSoft", "Warning", "WarningSoft", "Error", "ErrorSoft", "Info", "InfoSoft",
        "ChipBackground", "ChipBorder", "ChipSelectedBackground", "ChipSelectedBorder",
        "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
        "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush"
    };

    public static void Apply()
    {
        var app = Application.Current;
        if (app is null) return;

        if (BfrTheme.IsHighContrast)
        {
            foreach (var key in Keys) app.Resources.Remove(key);
            return;
        }

        var dark = BfrTheme.IsDark;
        var bg = C("BfrBgBase");
        var surface1 = C("BfrSurface1");
        var surface2 = C("BfrSurface2");
        var accent = C(dark ? "BfrAccFgDark" : "BfrAccFgLight");

        void Set(string key, Color color) => app.Resources[key] = Frozen(color);

        Set("AppBackground", bg);
        Set("PanelBackground", surface1);
        Set("SurfaceBackground", surface2);
        Set("CardBackground", surface2);
        Set("SurfaceHover", Over(C("BfrGlass"), Over(C("BfrGlass"), surface2)));
        Set("SurfaceSelected", Over(C("BfrAcc1Soft"), surface1));

        Set("TextPrimary", C("BfrFg"));
        Set("TextSecondary", C("BfrFg2"));
        Set("TextDisabled", C("BfrFg3"));
        Set("TextOnAccent", dark ? Color.FromRgb(0x10, 0x10, 0x10) : Colors.White);

        Set("Accent", accent);
        Set("AccentHover", Mix(accent, Colors.White, 0.12));
        Set("AccentPressed", Mix(accent, Colors.Black, 0.14));
        Set("AccentSoft", Over(C("BfrAcc1Soft"), surface1));
        Set("AccentBorder", Over(Color.FromArgb(0x80, accent.R, accent.G, accent.B), surface1));

        Set("Border", Over(C("BfrLine"), surface1));
        Set("BorderStrong", Mix(Over(C("BfrLine"), surface1), C("BfrFg3"), 0.5));
        Set("Divider", Over(C("BfrLine"), surface1));

        Set("Success", C("BfrOk"));
        Set("SuccessSoft", Over(C("BfrOkSoft"), surface1));
        Set("Warning", C("BfrWarn"));
        Set("WarningSoft", Over(C("BfrWarnSoft"), surface1));
        Set("Error", C("BfrBad"));
        Set("ErrorSoft", Over(C("BfrBadSoft"), surface1));
        Set("Info", accent);
        Set("InfoSoft", Over(C("BfrAcc1Soft"), surface1));

        Set("ChipBackground", surface2);
        Set("ChipBorder", Over(C("BfrLine"), surface2));
        Set("ChipSelectedBackground", Over(C("BfrAcc1Soft"), surface1));
        Set("ChipSelectedBorder", accent);

        // Nút Primary của WPF-UI: nền là đầu gradient (trắng trên đó đạt 4.5:1 ở mọi bảng màu), không dùng bản sáng hoá của WPF-UI.
        var fill = C("BfrAcc1");
        Set("AccentFillColorDefaultBrush", fill);
        Set("AccentFillColorSecondaryBrush", Mix(fill, Colors.White, 0.10));
        Set("AccentFillColorTertiaryBrush", Mix(fill, Colors.Black, 0.12));
        Set("TextOnAccentFillColorPrimaryBrush", Colors.White);
        Set("TextOnAccentFillColorSecondaryBrush", Mix(Colors.White, fill, 0.15));
    }

    private static Color C(string key) =>
        Application.Current.TryFindResource(key + "Color") is Color color ? color : Colors.Magenta;

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    /// <summary>Phủ màu trong suốt <paramref name="top"/> lên nền đặc <paramref name="under"/>.</summary>
    private static Color Over(Color top, Color under)
    {
        var a = top.A / 255.0;
        byte Blend(byte t, byte u) => (byte)(t * a + u * (1 - a) + 0.5);
        return Color.FromRgb(Blend(top.R, under.R), Blend(top.G, under.G), Blend(top.B, under.B));
    }

    private static Color Mix(Color a, Color b, double t)
    {
        byte Lerp(byte x, byte y) => (byte)(x + (y - x) * t + 0.5);
        return Color.FromRgb(Lerp(a.R, b.R), Lerp(a.G, b.G), Lerp(a.B, b.B));
    }
}
