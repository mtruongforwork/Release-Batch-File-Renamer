namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;

public enum GpsStripStatus
{
    /// <summary>Có GPS, sẽ xoá.</summary>
    Ready,

    /// <summary>Không có GPS trong EXIF.</summary>
    NoGps,

    /// <summary>Không phải JPEG (hoặc quá lớn): công cụ này chưa hỗ trợ.</summary>
    Unsupported,
    Protected,
    Error
}

/// <param name="XmpGpsRemains">XMP vẫn chứa toạ độ: cần cảnh báo.</param>
public sealed record GpsStripItem(FileItem File, GpsStripStatus Status, bool XmpGpsRemains = false, string? Message = null);

public sealed record GpsStripPlan(IReadOnlyList<GpsStripItem> Items, IReadOnlyList<Operation> Operations);

/// <summary>
/// Lập kế hoạch xoá GPS khỏi ảnh JPEG (M4). Bản đã sửa được ghi ra tệp tạm trong <c>tempRoot</c>; thao tác
/// <see cref="OperationKind.WriteFile"/> đưa bản gốc vào khu cách ly rồi thay thế, sau đó đặt lại đúng ngày giờ cũ.
/// </summary>
public static class GpsStripPlanner
{
    public const long MaxFileBytes = 200L * 1024 * 1024;

    public static GpsStripPlan Plan(IReadOnlyList<FileItem> files, IFileSystem fs, string tempRoot, ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var items = new List<GpsStripItem>(files.Count);
        var ops = new List<Operation>();
        var counter = 0;
        var sep = tempRoot.Contains('\\') || tempRoot.Contains(':') ? '\\' : '/';

        foreach (var file in files)
        {
            var ext = file.Extension.ToLowerInvariant();
            if (file.IsDirectory || file.IsOffline || file.Size > MaxFileBytes || ext is not ("jpg" or "jpeg" or "jpe"))
            {
                items.Add(new GpsStripItem(file, GpsStripStatus.Unsupported));
                continue;
            }

            if (zones.IsProtected(file.FullPath))
            {
                items.Add(new GpsStripItem(file, GpsStripStatus.Protected, Message: CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                continue;
            }

            try
            {
                byte[] bytes;
                using (var stream = fs.OpenRead(file.FullPath))
                using (var ms = new MemoryStream())
                {
                    stream.CopyTo(ms);
                    bytes = ms.ToArray();
                }

                if (!ExifGpsStripper.IsJpeg(bytes))
                {
                    items.Add(new GpsStripItem(file, GpsStripStatus.Unsupported));
                    continue;
                }

                var result = ExifGpsStripper.Strip(bytes);
                if (!result.ExifGpsFound)
                {
                    items.Add(new GpsStripItem(file, GpsStripStatus.NoGps, result.XmpGpsRemains));
                    continue;
                }

                var times = fs.GetTimes(file.FullPath);
                var temp = $"{tempRoot.TrimEnd('\\', '/')}{sep}{++counter:D5}-{file.Name}.{file.Extension}";
                fs.WriteAllBytes(temp, result.Data);

                ops.Add(Operation.WriteFile(temp, file.FullPath));
                ops.Add(Operation.SetTimestamps(file.FullPath, times)); // giữ nguyên ngày giờ gốc của ảnh
                items.Add(new GpsStripItem(file, GpsStripStatus.Ready, result.XmpGpsRemains));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                items.Add(new GpsStripItem(file, GpsStripStatus.Error, Message: ex.Message));
            }
        }

        return new GpsStripPlan(items, ops);
    }
}
