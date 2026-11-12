namespace BFR.Core;

using System;
using System.Collections.Generic;

public enum ImageFormat
{
    /// <summary>Giữ định dạng gốc (nếu ghi được; HEIC/WebP... chuyển thành JPEG).</summary>
    Keep,
    Jpeg,
    Png,
    Bmp,
    Tiff
}

public enum ImageResizeMode
{
    None,

    /// <summary>Cạnh dài nhất tối đa N điểm ảnh, giữ tỉ lệ.</summary>
    LongEdge,

    /// <summary>N phần trăm kích thước gốc.</summary>
    Percent,

    /// <summary>Nằm gọn trong khung rộng × cao, giữ tỉ lệ.</summary>
    Fit
}

/// <param name="ResizeValue">Số điểm ảnh (LongEdge) hoặc phần trăm (Percent).</param>
/// <param name="Suffix">Hậu tố thêm vào tên khi ghi cạnh tệp gốc.</param>
/// <param name="OutputFolder">Thư mục đầu ra; null = cạnh tệp gốc.</param>
/// <param name="OverwriteOriginal">Ghi đè bản gốc (bản gốc vào khu cách ly). Mặc định tắt, phải bật rõ ràng.</param>
public sealed record ImageOptions(
    ImageFormat Format = ImageFormat.Keep,
    ImageResizeMode Resize = ImageResizeMode.None,
    int ResizeValue = 0,
    int FitWidth = 0,
    int FitHeight = 0,
    int JpegQuality = 85,
    bool AutoOrient = true,
    bool AllowUpscale = false,
    string Suffix = "_edit",
    string? OutputFolder = null,
    bool OverwriteOriginal = false,
    bool PreserveTimes = true)
{
    public int ClampedQuality => Math.Clamp(JpegQuality, 60, 95);
}

public sealed record ImageInfo(int Width, int Height)
{
    public long Pixels => (long)Width * Height;
}

public sealed record ImageResult(bool Success, int Width = 0, int Height = 0, string? Error = null);

/// <summary>Bộ xử lý điểm ảnh (WIC trên Windows). Core chỉ lập kế hoạch và điều phối, không chạm điểm ảnh.</summary>
public interface IImageProcessor
{
    /// <summary>Đọc kích thước mà không giải mã toàn bộ ảnh; null nếu không đọc được.</summary>
    ImageInfo? Probe(string path);

    /// <summary>
    /// Ảnh xám thu nhỏ <paramref name="width"/>×<paramref name="height"/> (từng dòng, 1 byte mỗi điểm) để tính băm cảm nhận.
    /// null nếu không đọc được. Phải an toàn khi gọi song song.
    /// </summary>
    byte[]? GrayThumbnail(string path, int width, int height);

    /// <summary>Xử lý <paramref name="source"/> và ghi kết quả ra <paramref name="destination"/> (đường dẫn thật, tệp tạm).</summary>
    ImageResult Process(string source, string destination, ImageOptions options, ImageFormat outputFormat);
}

public enum ImageStatus
{
    Ready,
    Unsupported,
    Protected,
    Error
}

public sealed record ImageItem(FileItem File, ImageStatus Status, string OutputPath, ImageFormat OutputFormat, string? Message = null);

public sealed record ImagePlan(IReadOnlyList<ImageItem> Items, IReadOnlyList<string> Errors);
