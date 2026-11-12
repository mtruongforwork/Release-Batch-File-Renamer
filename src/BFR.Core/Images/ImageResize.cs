namespace BFR.Core;

using System;

/// <summary>Tính kích thước đầu ra khi resize (thuần tuý). Luôn giữ tỉ lệ, tối thiểu 1 điểm ảnh mỗi cạnh.</summary>
public static class ImageResize
{
    public const int MaxDimension = 65535;

    public static (int Width, int Height) Compute(int width, int height, ImageOptions o)
    {
        if (width <= 0 || height <= 0) return (Math.Max(width, 1), Math.Max(height, 1));

        double scale = o.Resize switch
        {
            ImageResizeMode.LongEdge when o.ResizeValue > 0 => (double)o.ResizeValue / Math.Max(width, height),
            ImageResizeMode.Percent when o.ResizeValue > 0 => o.ResizeValue / 100.0,
            ImageResizeMode.Fit when o.FitWidth > 0 && o.FitHeight > 0 => Math.Min((double)o.FitWidth / width, (double)o.FitHeight / height),
            _ => 1.0
        };

        if (!o.AllowUpscale && scale > 1.0) scale = 1.0;

        var w = (int)Math.Clamp(Math.Round(width * scale), 1, MaxDimension);
        var h = (int)Math.Clamp(Math.Round(height * scale), 1, MaxDimension);
        return (w, h);
    }

    /// <summary>EXIF Orientation 5-8 hoán đổi rộng/cao sau khi xoay.</summary>
    public static bool SwapsAxes(int orientation) => orientation is >= 5 and <= 8;
}
