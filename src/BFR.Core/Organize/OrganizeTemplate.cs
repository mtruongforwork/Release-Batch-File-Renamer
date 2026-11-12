namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Mẫu đường dẫn thư mục đích của Organize, ví dụ <c>{exif.year}\{exif.month:00}</c>.
/// Mẫu được tách thành các cấp thư mục TRƯỚC khi thay token, nên giá trị token không bao giờ tạo thêm cấp
/// hay thoát khỏi thư mục gốc: mọi cấp đều được làm sạch (ký tự cấm, "..", tên dành riêng).
/// <para>Token: <c>{year} {month} {day}</c> (ngày chụp, rồi ngày media, rồi ngày sửa), <c>{exif.year|month|day}</c> (chỉ
/// ngày chụp/media), <c>{modified.*}</c>, <c>{created.*}</c> (cùng hậu tố year/month/day), <c>{ext}</c>, <c>{ext.upper}</c>,
/// <c>{ext.lower}</c>, <c>{type}</c>, <c>{name.first:N}</c>, <c>{size.bucket}</c>, <c>{parent}</c>, <c>{artist}</c>,
/// <c>{album}</c>. Đối số số nguyên (<c>{month:00}</c>) đệm số 0; <c>{year:yy}</c> lấy 2 chữ số.</para>
/// </summary>
public sealed class OrganizeTemplate
{
    private static readonly Regex TokenRegex = new(@"\{([^{}]+)\}", RegexOptions.Compiled, RegexLimits.Timeout);

    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private const int MaxSegmentLength = 100;

    private readonly string[] _segments;

    private OrganizeTemplate(string[] segments)
    {
        _segments = segments;
    }

    /// <summary>Số cấp thư mục của mẫu.</summary>
    public int Depth => _segments.Length;

    public static OrganizeTemplate Parse(string? template)
    {
        var parts = (template ?? string.Empty).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var list = new List<string>();
        foreach (var p in parts)
        {
            if (p.Trim().Length > 0) list.Add(p.Trim());
        }
        return new OrganizeTemplate(list.ToArray());
    }

    /// <summary>Danh sách lỗi cú pháp: token lạ. Rỗng nghĩa là hợp lệ.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        foreach (var segment in _segments)
        {
            foreach (Match m in TokenRegex.Matches(segment))
            {
                if (!IsKnownToken(m.Groups[1].Value))
                {
                    errors.Add(CoreText.Get("CoreInvalidToken", "Token không hợp lệ: {0}", m.Value));
                }
            }

            var leftover = TokenRegex.Replace(segment, string.Empty);
            if (leftover.Contains('{') || leftover.Contains('}'))
            {
                errors.Add(CoreText.Get("CoreInvalidToken", "Token không hợp lệ: {0}", segment));
            }
        }
        return errors;
    }

    /// <summary>
    /// Thay token cho một tệp. <paramref name="missing"/> = true khi một token (ví dụ ngày chụp) không có dữ liệu:
    /// khi đó tệp nên vào thư mục "không xác định". Trả về danh sách cấp thư mục đã làm sạch.
    /// </summary>
    public IReadOnlyList<string> Expand(FileItem file, RuleContext ctx, out bool missing)
    {
        var anyMissing = false;
        var result = new List<string>(_segments.Length);

        foreach (var segment in _segments)
        {
            var text = TokenRegex.Replace(segment, m =>
            {
                var value = Resolve(m.Groups[1].Value, file, ctx);
                if (value is null)
                {
                    anyMissing = true;
                    return string.Empty;
                }
                return value;
            });

            var clean = SanitizeSegment(text);
            if (clean.Length > 0) result.Add(clean);
        }

        missing = anyMissing;
        return result;
    }

    /// <summary>Làm sạch một cấp thư mục: không bao giờ chứa dấu phân cách, "..", ký tự cấm hay tên dành riêng.</summary>
    public static string SanitizeSegment(string? segment)
    {
        if (string.IsNullOrWhiteSpace(segment)) return string.Empty;

        var sb = new StringBuilder(segment.Length);
        foreach (var ch in segment)
        {
            sb.Append(ch < 32 || "<>:\"/\\|?*".Contains(ch) ? '_' : ch);
        }

        var text = sb.ToString().Trim().TrimEnd('.', ' ');
        if (text.Length == 0) return string.Empty;
        if (text.Length > MaxSegmentLength) text = text[..MaxSegmentLength].TrimEnd('.', ' ');
        if (text.Length == 0) return string.Empty;

        var stem = text.Split('.')[0].TrimEnd(' ');
        if (Reserved.Contains(stem)) text = "_" + text;
        return text;
    }

    private static bool IsKnownToken(string token)
    {
        var parts = token.Split(':', 2);
        var name = parts[0].ToLowerInvariant();
        return name switch
        {
            "year" or "month" or "day" => true,
            "exif.year" or "exif.month" or "exif.day" => true,
            "modified.year" or "modified.month" or "modified.day" => true,
            "created.year" or "created.month" or "created.day" => true,
            "ext" or "ext.upper" or "ext.lower" => true,
            "type" or "size.bucket" or "parent" or "artist" or "album" => true,
            "name.first" => true,
            _ => false
        };
    }

    private static string? Resolve(string token, FileItem file, RuleContext ctx)
    {
        var parts = token.Split(':', 2);
        var name = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1] : null;

        switch (name)
        {
            case "year" or "month" or "day":
                return DatePart(name, BestDate(file, ctx) ?? file.ModifiedUtc.ToLocalTime(), arg);
            case "exif.year" or "exif.month" or "exif.day":
                return ExifDate(file, ctx) is { } exif ? DatePart(name[5..], exif, arg) : null;
            case "modified.year" or "modified.month" or "modified.day":
                return DatePart(name[9..], file.ModifiedUtc.ToLocalTime(), arg);
            case "created.year" or "created.month" or "created.day":
                return DatePart(name[8..], file.CreatedUtc.ToLocalTime(), arg);
            case "ext":
                return file.Extension.Length == 0 ? NoExtension() : file.Extension;
            case "ext.upper":
                return file.Extension.Length == 0 ? NoExtension() : file.Extension.ToUpperInvariant();
            case "ext.lower":
                return file.Extension.Length == 0 ? NoExtension() : file.Extension.ToLowerInvariant();
            case "type":
                return FileTypeClassifier.FolderName(FileTypeClassifier.Classify(file.Extension));
            case "size.bucket":
                return SizeBucket(file.Size);
            case "parent":
                return System.IO.Path.GetFileName(file.Directory.TrimEnd('\\', '/'));
            case "name.first":
            {
                var n = int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) && v > 0 ? v : 1;
                var stem = file.Name.Trim();
                if (stem.Length == 0) return null;
                var take = Math.Min(n, stem.Length);
                // Không cắt giữa cặp thay thế (surrogate) của ký tự ngoài BMP.
                if (take < stem.Length && char.IsHighSurrogate(stem[take - 1])) take++;
                return stem[..take].ToUpperInvariant();
            }
            case "artist" or "album":
            {
                var map = ctx.MetadataFor(file);
                return map is not null && map.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
            }
            default:
                return null;
        }
    }

    private static string NoExtension() => CoreText.Get("CoreOrganizeNoExtension", "Không đuôi");

    private static string SizeBucket(long size)
    {
        const long mb = 1024L * 1024L;
        if (size < mb) return CoreText.Get("CoreSizeSmall", "Dưới 1 MB");
        if (size <= 100 * mb) return CoreText.Get("CoreSizeMedium", "1-100 MB");
        return CoreText.Get("CoreSizeLarge", "Trên 100 MB");
    }

    private static DateTime? ExifDate(FileItem file, RuleContext ctx)
    {
        var map = ctx.MetadataFor(file);
        if (map is null) return null;

        foreach (var key in new[] { "taken", "media" })
        {
            if (map.TryGetValue(key, out var text) && !string.IsNullOrWhiteSpace(text) &&
                DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date))
            {
                return date;
            }
        }
        return null;
    }

    private static DateTime? BestDate(FileItem file, RuleContext ctx) => ExifDate(file, ctx);

    private static string DatePart(string part, DateTime date, string? arg)
    {
        var width = int.TryParse(arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out var w) && w > 0 ? Math.Min(w, 8) : 0;

        switch (part)
        {
            case "year":
                if (string.Equals(arg, "yy", StringComparison.OrdinalIgnoreCase))
                    return (date.Year % 100).ToString("00", CultureInfo.InvariantCulture);
                return Pad(date.Year, Math.Max(width, 4));
            case "month":
                return Pad(date.Month, width == 0 ? 2 : width);
            default:
                return Pad(date.Day, width == 0 ? 2 : width);
        }
    }

    private static string Pad(int value, int width) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');
}
