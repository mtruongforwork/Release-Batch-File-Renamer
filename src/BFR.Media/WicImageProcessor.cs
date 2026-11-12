namespace BFR.Media;

using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BFR.Core;

/// <summary>
/// Xử lý ảnh bằng WIC của Windows: đổi định dạng, resize giữ tỉ lệ, xoay theo EXIF Orientation (rồi đặt lại về 1),
/// nén JPEG. Mỗi lần gọi dùng đối tượng riêng nên an toàn khi chạy song song. Không ghi đè nguồn.
/// Metadata (EXIF: ngày chụp, máy ảnh...) được giữ khi nguồn và đích cùng họ JPEG hoặc TIFF; với định dạng khác chỉ
/// chép được ngày chụp.
/// </summary>
public sealed class WicImageProcessor : IImageProcessor
{
    private const string JpegOrientationQuery = "/app1/ifd/{ushort=274}";
    private const string TiffOrientationQuery = "/ifd/{ushort=274}";

    public ImageInfo? Probe(string path)
    {
        try
        {
            using var stream = OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            return new ImageInfo(frame.PixelWidth, frame.PixelHeight);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            return null;
        }
    }

    public ImageResult Process(string source, string destination, ImageOptions options, ImageFormat outputFormat)
    {
        try
        {
            BitmapSource image;
            BitmapMetadata? sourceMetadata;
            int orientation = 1;
            var sourceFamily = Family(Path.GetExtension(source));

            using (var stream = OpenRead(source))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var frame = decoder.Frames[0];
                sourceMetadata = frame.Metadata as BitmapMetadata;
                if (options.AutoOrient) orientation = ReadOrientation(sourceMetadata);
                image = frame;
                image.Freeze();
            }

            if (options.AutoOrient && orientation > 1) image = ApplyOrientation(image, orientation);

            var (w, h) = ImageResize.Compute(image.PixelWidth, image.PixelHeight, options);
            if (w != image.PixelWidth || h != image.PixelHeight)
            {
                var scaled = new TransformedBitmap(image, new ScaleTransform((double)w / image.PixelWidth, (double)h / image.PixelHeight));
                scaled.Freeze();
                image = scaled;
            }

            if (outputFormat is ImageFormat.Jpeg or ImageFormat.Bmp && HasAlpha(image.Format)) image = FlattenOnWhite(image);

            var metadata = BuildMetadata(sourceMetadata, sourceFamily, outputFormat, orientation, options.AutoOrient);
            var encoder = CreateEncoder(outputFormat, options.ClampedQuality);

            if (!TryEncode(encoder, image, metadata, destination))
            {
                // Siêu dữ liệu không tương thích với bộ mã hoá: ghi lại không kèm metadata thay vì thất bại.
                TryEncode(CreateEncoder(outputFormat, options.ClampedQuality), image, null, destination, rethrow: true);
            }

            return new ImageResult(true, image.PixelWidth, image.PixelHeight);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            TryDelete(destination);
            return new ImageResult(false, Error: ex.Message);
        }
    }

    private static bool TryEncode(BitmapEncoder encoder, BitmapSource image, BitmapMetadata? metadata, string destination, bool rethrow = false)
    {
        try
        {
            encoder.Frames.Add(BitmapFrame.Create(image, null, metadata, null));
            var parent = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
            using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(output);
            return true;
        }
        catch (Exception ex) when (!rethrow && (ex is NotSupportedException or InvalidOperationException or ArgumentException or IOException) && metadata is not null)
        {
            TryDelete(destination);
            return false;
        }
    }

    private static Stream OpenRead(string path) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    private static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException
            or ArgumentException or FileFormatException or OutOfMemoryException or System.Runtime.InteropServices.COMException;

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string Family(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "jpg" or "jpeg" or "jpe" => "jpeg",
        "tif" or "tiff" => "tiff",
        _ => "other"
    };

    private static BitmapEncoder CreateEncoder(ImageFormat format, int quality) => format switch
    {
        ImageFormat.Png => new PngBitmapEncoder(),
        ImageFormat.Bmp => new BmpBitmapEncoder(),
        ImageFormat.Tiff => new TiffBitmapEncoder { Compression = TiffCompressOption.Zip },
        _ => new JpegBitmapEncoder { QualityLevel = quality }
    };

    private static int ReadOrientation(BitmapMetadata? metadata)
    {
        if (metadata is null) return 1;
        foreach (var query in new[] { JpegOrientationQuery, TiffOrientationQuery })
        {
            try
            {
                if (metadata.ContainsQuery(query) && metadata.GetQuery(query) is ushort value && value is >= 1 and <= 8) return value;
            }
            catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
            {
                // Container không có truy vấn này.
            }
        }
        return 1;
    }

    /// <summary>Metadata cho tệp đầu ra: bản sao nguyên vẹn khi cùng họ, hoặc chỉ ngày chụp; Orientation đặt lại về 1 nếu đã xoay.</summary>
    private static BitmapMetadata? BuildMetadata(BitmapMetadata? source, string sourceFamily, ImageFormat output, int orientation, bool oriented)
    {
        if (source is null) return null;
        var outFamily = output switch { ImageFormat.Jpeg => "jpeg", ImageFormat.Tiff => "tiff", _ => "other" };

        try
        {
            if (outFamily != "other" && outFamily == sourceFamily)
            {
                var clone = source.Clone();
                if (oriented && orientation > 1)
                {
                    var query = outFamily == "jpeg" ? JpegOrientationQuery : TiffOrientationQuery;
                    if (clone.ContainsQuery(query)) clone.SetQuery(query, (ushort)1);
                }
                return clone;
            }

            if (output == ImageFormat.Jpeg && !string.IsNullOrWhiteSpace(source.DateTaken))
            {
                var fresh = new BitmapMetadata("jpg");
                fresh.DateTaken = source.DateTaken;
                return fresh;
            }
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            // Bỏ metadata còn hơn thất bại.
        }
        return null;
    }

    /// <summary>Áp dụng EXIF Orientation 2-8 để ảnh hiển thị đúng chiều.</summary>
    private static BitmapSource ApplyOrientation(BitmapSource source, int orientation)
    {
        BitmapSource Rotate(BitmapSource s, double angle) { var t = new TransformedBitmap(s, new RotateTransform(angle)); t.Freeze(); return t; }
        BitmapSource Flip(BitmapSource s, bool horizontal)
        {
            var t = new TransformedBitmap(s, horizontal ? new ScaleTransform(-1, 1) : new ScaleTransform(1, -1));
            t.Freeze();
            return t;
        }

        return orientation switch
        {
            2 => Flip(source, true),
            3 => Rotate(source, 180),
            4 => Flip(source, false),
            5 => Flip(Rotate(source, 90), true),
            6 => Rotate(source, 90),
            7 => Flip(Rotate(source, 270), true),
            8 => Rotate(source, 270),
            _ => source
        };
    }

    private static bool HasAlpha(PixelFormat format) =>
        format == PixelFormats.Bgra32 || format == PixelFormats.Pbgra32 || format == PixelFormats.Prgba64 || format == PixelFormats.Rgba64
        || format == PixelFormats.Prgba128Float || format == PixelFormats.Rgba128Float;

    /// <summary>JPEG/BMP không có kênh trong suốt: đặt ảnh lên nền trắng thay vì để nền đen.</summary>
    private static BitmapSource FlattenOnWhite(BitmapSource source)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
            dc.DrawImage(source, new Rect(0, 0, source.PixelWidth, source.PixelHeight));
        }
        var target = new RenderTargetBitmap(source.PixelWidth, source.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }
}
