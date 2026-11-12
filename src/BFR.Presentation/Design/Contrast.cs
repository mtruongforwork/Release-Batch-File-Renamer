namespace BFR.Presentation.Design;

using System;

/// <summary>
/// Tương phản màu theo WCAG 2.x (thuần tuý, không phụ thuộc WPF) để kiểm bảng màu giao diện bằng test và hiện trong Gallery.
/// Màu có độ trong suốt được trộn lên nền trước khi tính.
/// </summary>
public static class Contrast
{
    /// <summary>Màu ARGB 8 bit mỗi kênh.</summary>
    public readonly record struct Argb(byte A, byte R, byte G, byte B)
    {
        public static Argb Parse(string hex)
        {
            var s = hex.Trim().TrimStart('#');
            if (s.Length == 6) s = "FF" + s;
            if (s.Length != 8) throw new FormatException($"Màu không hợp lệ: {hex}");

            return new Argb(
                Convert.ToByte(s[..2], 16), Convert.ToByte(s[2..4], 16), Convert.ToByte(s[4..6], 16), Convert.ToByte(s[6..8], 16));
        }

        /// <summary>Trộn màu này (có alpha) lên một nền đặc.</summary>
        public Argb Over(Argb background)
        {
            var a = A / 255.0;
            byte Mix(byte fg, byte bg) => (byte)Math.Round((fg * a) + (bg * (1 - a)));
            return new Argb(255, Mix(R, background.R), Mix(G, background.G), Mix(B, background.B));
        }
    }

    /// <summary>Độ sáng tương đối (0 = đen, 1 = trắng).</summary>
    public static double Luminance(Argb c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Channel(c.R)) + (0.7152 * Channel(c.G)) + (0.0722 * Channel(c.B));
    }

    /// <summary>Tỉ số tương phản từ 1 đến 21. <paramref name="foreground"/> được trộn lên <paramref name="background"/> nếu có alpha.</summary>
    public static double Ratio(Argb foreground, Argb background)
    {
        var fg = foreground.A == 255 ? foreground : foreground.Over(background);
        var l1 = Luminance(fg);
        var l2 = Luminance(background);
        var (hi, lo) = l1 >= l2 ? (l1, l2) : (l2, l1);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Chữ thường cần 4.5; chữ lớn (≥ 18 pt hoặc 14 pt đậm) và thành phần giao diện cần 3.</summary>
    public const double TextMinimum = 4.5;
    public const double LargeOrUiMinimum = 3.0;
}
