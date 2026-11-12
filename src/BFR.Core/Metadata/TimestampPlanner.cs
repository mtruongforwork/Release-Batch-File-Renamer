namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

public enum TimestampSource
{
    /// <summary>Giá trị cố định do người dùng nhập.</summary>
    Fixed,

    /// <summary>Giữ nguyên giá trị hiện có của từng trường, chỉ cộng/trừ <see cref="TimestampOptions.Offset"/> (sửa lệch múi giờ máy ảnh).</summary>
    Shift,

    /// <summary>Ngày chụp (EXIF DateTimeOriginal) hoặc ngày media.</summary>
    Exif,

    /// <summary>Ngày đọc từ tên tệp (<c>IMG_20240512_103000</c>).</summary>
    FileName,

    /// <summary>Sao chép từ một tệp khác.</summary>
    CopyFromFile
}

/// <param name="FixedLocal">Giá trị cho <see cref="TimestampSource.Fixed"/>, giờ địa phương.</param>
/// <param name="Offset">Cộng thêm sau khi tính (âm để lùi).</param>
/// <param name="FileNamePattern">Mẫu như <c>IMG_{yyyyMMdd_HHmmss}</c>; để trống để tự nhận dạng các dạng ngày phổ biến.</param>
public sealed record TimestampOptions(
    TimestampSource Source,
    bool SetCreated = false,
    bool SetModified = true,
    bool SetAccessed = false,
    DateTime? FixedLocal = null,
    TimeSpan Offset = default,
    string? FileNamePattern = null,
    string? CopyFromPath = null);

public enum TimestampStatus
{
    Ready,
    Unchanged,

    /// <summary>Không lấy được ngày từ nguồn (ảnh không có EXIF, tên không có ngày...).</summary>
    NoData,
    Protected,
    Error
}

public sealed record TimestampItem(FileItem File, TimestampStatus Status, FileTimes? Before, FileTimes? After, string? Message = null);

public sealed record TimestampPlan(IReadOnlyList<TimestampItem> Items, IReadOnlyList<Operation> Operations);

/// <summary>
/// Lập kế hoạch đặt ngày tạo/sửa/truy cập (M4). Thuần tuý: chỉ đọc ngày hiện tại qua <see cref="IFileSystem"/>.
/// Hoàn tác do <see cref="OperationExecutor"/> ghi giá trị cũ vào journal (chính xác tới từng tick).
/// </summary>
public static class TimestampPlanner
{
    private static readonly Regex AutoDate = new(
        @"(?<![0-9])((?:19|20)\d{2})[-_.]?(0[1-9]|1[0-2])[-_.]?(0[1-9]|[12]\d|3[01])(?:[ T_-]?([01]\d|2[0-3])[.:_-]?([0-5]\d)[.:_-]?([0-5]\d))?(?![0-9])",
        RegexOptions.Compiled, RegexLimits.Timeout);

    public static TimestampPlan Plan(
        IReadOnlyList<FileItem> files, TimestampOptions options, RuleContext context, IFileSystem fs, ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var items = new List<TimestampItem>(files.Count);
        var ops = new List<Operation>();

        FileTimes? copied = null;
        if (options.Source == TimestampSource.CopyFromFile)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(options.CopyFromPath) && (fs.FileExists(options.CopyFromPath) || fs.DirectoryExists(options.CopyFromPath)))
                    copied = fs.GetTimes(options.CopyFromPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                copied = null;
            }
        }

        foreach (var file in files)
        {
            if (zones.IsProtected(file.FullPath))
            {
                items.Add(new TimestampItem(file, TimestampStatus.Protected, null, null, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                continue;
            }

            FileTimes before;
            try
            {
                before = fs.GetTimes(file.FullPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                items.Add(new TimestampItem(file, TimestampStatus.Error, null, null, ex.Message));
                continue;
            }

            var baseUtc = ResolveBase(file, options, context, copied, before);
            if (baseUtc is null && options.Source != TimestampSource.Shift && options.Source != TimestampSource.CopyFromFile)
            {
                items.Add(new TimestampItem(file, TimestampStatus.NoData, before, null, CoreText.Get("CoreTimestampNoData", "Không lấy được ngày từ nguồn đã chọn")));
                continue;
            }
            if (options.Source == TimestampSource.CopyFromFile && copied is null)
            {
                items.Add(new TimestampItem(file, TimestampStatus.NoData, before, null, CoreText.Get("CoreTimestampNoData", "Không lấy được ngày từ nguồn đã chọn")));
                continue;
            }

            long Pick(bool set, long current, DateTime? fromSource)
            {
                if (!set) return current;
                var utc = options.Source == TimestampSource.Shift
                    ? new DateTime(current, DateTimeKind.Utc)
                    : fromSource!.Value;
                return Clamp(utc + options.Offset);
            }

            DateTime? srcCreated = baseUtc, srcModified = baseUtc, srcAccessed = baseUtc;
            if (options.Source == TimestampSource.CopyFromFile)
            {
                srcCreated = copied!.Value.CreatedUtc;
                srcModified = copied.Value.ModifiedUtc;
                srcAccessed = copied.Value.AccessedUtc;
            }

            var after = new FileTimes(
                Pick(options.SetCreated, before.CreatedUtcTicks, srcCreated),
                Pick(options.SetModified, before.ModifiedUtcTicks, srcModified),
                Pick(options.SetAccessed, before.AccessedUtcTicks, srcAccessed));

            if (after == before)
            {
                items.Add(new TimestampItem(file, TimestampStatus.Unchanged, before, after));
                continue;
            }

            items.Add(new TimestampItem(file, TimestampStatus.Ready, before, after));
            ops.Add(Operation.SetTimestamps(file.FullPath, after));
        }

        return new TimestampPlan(items, ops);
    }

    private static DateTime? ResolveBase(FileItem file, TimestampOptions o, RuleContext ctx, FileTimes? copied, FileTimes before) =>
        o.Source switch
        {
            TimestampSource.Fixed => o.FixedLocal is { } f ? ToUtc(f) : null,
            TimestampSource.Exif => ExifUtc(file, ctx),
            TimestampSource.FileName => FromFileName(file.Name, o.FileNamePattern) is { } n ? ToUtc(n) : null,
            _ => null
        };

    private static DateTime ToUtc(DateTime local) =>
        local.Kind == DateTimeKind.Utc ? local : DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();

    private static DateTime? ExifUtc(FileItem file, RuleContext ctx)
    {
        var map = ctx.MetadataFor(file);
        if (map is null) return null;
        foreach (var key in new[] { "taken", "media" })
        {
            if (map.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text) &&
                DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            {
                return ToUtc(date);
            }
        }
        return null;
    }

    private static long Clamp(DateTime utc)
    {
        // NTFS không chấp nhận ngày trước 1601; giữ trong khoảng an toàn.
        var min = new DateTime(1601, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var max = new DateTime(9999, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        if (utc < min) utc = min;
        if (utc > max) utc = max;
        return utc.Ticks;
    }

    /// <summary>
    /// Đọc ngày (giờ địa phương) từ tên tệp. Có mẫu: <c>IMG_{yyyyMMdd_HHmmss}</c> (phần ngoài ngoặc là chữ cố định).
    /// Không có mẫu: tự nhận dạng <c>20240512_103000</c>, <c>2024-05-12 10.30.00</c>, <c>20240512</c>...
    /// </summary>
    public static DateTime? FromFileName(string name, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            var m = AutoDate.Match(name);
            if (!m.Success) return null;
            int G(int i, int dflt) => m.Groups[i].Success ? int.Parse(m.Groups[i].Value, CultureInfo.InvariantCulture) : dflt;
            try
            {
                return new DateTime(G(1, 0), G(2, 1), G(3, 1), G(4, 0), G(5, 0), G(6, 0), DateTimeKind.Local);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null; // ví dụ 20240231
            }
        }

        var open = pattern.IndexOf('{');
        var close = pattern.IndexOf('}');
        if (open < 0 || close < open) return null;

        var prefix = pattern[..open];
        var format = pattern[(open + 1)..close];
        var suffix = pattern[(close + 1)..];
        if (format.Length == 0) return null;

        var start = prefix.Length == 0 ? 0 : name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        start += prefix.Length;
        if (start + format.Length > name.Length) return null;

        var text = name.Substring(start, format.Length);
        var rest = name[(start + format.Length)..];
        if (suffix.Length > 0 && !rest.StartsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;

        return DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Local)
            : null;
    }
}
