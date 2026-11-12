namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Lập kế hoạch công cụ ảnh (M5): quyết định định dạng và đường dẫn đầu ra cho từng ảnh.
/// Mặc định KHÔNG ghi đè bản gốc: ghi cạnh tệp gốc với hậu tố hoặc vào thư mục khác, và không bao giờ đè tệp có sẵn.
/// </summary>
public static class ImagePlanner
{
    private static readonly HashSet<string> Decodable = new(StringComparer.OrdinalIgnoreCase)
    {
        "jpg", "jpeg", "jpe", "png", "bmp", "tif", "tiff", "gif", "heic", "heif", "webp"
    };

    public static bool IsSupportedSource(string extension) => Decodable.Contains(extension.TrimStart('.'));

    public static string ExtensionOf(ImageFormat format) => format switch
    {
        ImageFormat.Png => "png",
        ImageFormat.Bmp => "bmp",
        ImageFormat.Tiff => "tif",
        _ => "jpg"
    };

    /// <summary>Định dạng ghi ra cho một đuôi nguồn, theo tuỳ chọn.</summary>
    public static ImageFormat ResolveFormat(string sourceExtension, ImageFormat requested)
    {
        if (requested != ImageFormat.Keep) return requested;
        return sourceExtension.TrimStart('.').ToLowerInvariant() switch
        {
            "png" => ImageFormat.Png,
            "bmp" => ImageFormat.Bmp,
            "tif" or "tiff" => ImageFormat.Tiff,
            _ => ImageFormat.Jpeg // jpg, gif, heic, webp...
        };
    }

    public static ImagePlan Plan(IReadOnlyList<FileItem> files, ImageOptions options, IFileSystem fs, ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var errors = new List<string>();

        if (options.Format == ImageFormat.Keep && options.Resize == ImageResizeMode.None && !options.AutoOrient)
            errors.Add(CoreText.Get("CoreImageNothingToDo", "Chưa chọn thao tác nào cho ảnh"));
        if (options.Resize is ImageResizeMode.LongEdge or ImageResizeMode.Percent && options.ResizeValue <= 0)
            errors.Add(CoreText.Get("CoreImageBadSize", "Kích thước không hợp lệ"));
        if (options.Resize == ImageResizeMode.Percent && options.ResizeValue > 1000)
            errors.Add(CoreText.Get("CoreImageBadSize", "Kích thước không hợp lệ"));
        if (options.Resize == ImageResizeMode.Fit && (options.FitWidth <= 0 || options.FitHeight <= 0))
            errors.Add(CoreText.Get("CoreImageBadSize", "Kích thước không hợp lệ"));
        if (options.OutputFolder is not null && zones.IsProtected(options.OutputFolder))
            errors.Add(CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ"));
        if (!options.OverwriteOriginal && options.OutputFolder is null && string.IsNullOrWhiteSpace(options.Suffix))
            errors.Add(CoreText.Get("CoreImageNoSuffix", "Cần hậu tố hoặc thư mục đầu ra để không ghi đè bản gốc"));

        var items = new List<ImageItem>(files.Count);
        if (errors.Count > 0)
        {
            foreach (var f in files) items.Add(new ImageItem(f, ImageStatus.Error, string.Empty, ImageFormat.Keep, errors[0]));
            return new ImagePlan(items, errors);
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in files)
        {
            if (file.IsDirectory || file.IsOffline || !IsSupportedSource(file.Extension))
            {
                items.Add(new ImageItem(file, ImageStatus.Unsupported, string.Empty, ImageFormat.Keep));
                continue;
            }

            if (zones.IsProtected(file.FullPath))
            {
                items.Add(new ImageItem(file, ImageStatus.Protected, string.Empty, ImageFormat.Keep, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                continue;
            }

            var format = ResolveFormat(file.Extension, options.Format);
            var ext = ExtensionOf(format);
            var dir = options.OutputFolder ?? file.Directory;
            var stem = options.OverwriteOriginal || options.OutputFolder is not null && string.IsNullOrEmpty(options.Suffix)
                ? file.Name
                : file.Name + options.Suffix;

            var output = Join(dir, $"{stem}.{ext}");
            var isOriginalPath = SamePath(output, file.FullPath);

            if (isOriginalPath && !options.OverwriteOriginal)
            {
                // Ví dụ: thư mục đầu ra giống thư mục gốc và không có hậu tố. Không bao giờ đè bản gốc ngoài ý muốn.
                output = Join(dir, $"{file.Name} (2).{ext}");
            }

            if (!isOriginalPath || !options.OverwriteOriginal)
            {
                for (var n = 2; taken.Contains(Canon(output)) || fs.FileExists(output); n++)
                {
                    output = Join(dir, $"{stem} ({n}).{ext}");
                }
            }

            taken.Add(Canon(output));
            items.Add(new ImageItem(file, ImageStatus.Ready, output, format));
        }

        return new ImagePlan(items, errors);
    }

    private static string Join(string dir, string name)
    {
        var sep = dir.Contains('\\') || dir.Contains(':') ? '\\' : '/';
        return dir.TrimEnd('\\', '/') + sep + name;
    }

    private static string Canon(string p) => p.Replace('\\', '/').TrimEnd('/');

    private static bool SamePath(string a, string b) => string.Equals(Canon(a), Canon(b), StringComparison.OrdinalIgnoreCase);
}
