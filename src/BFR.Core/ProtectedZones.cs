namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Vùng cấm ghi (C10): thư mục hệ thống và dữ liệu nội bộ mà BFR không bao giờ đổi tên, di chuyển hay xoá,
/// bất kể người dùng thêm tệp bằng cách nào (kéo thả, menu chuột phải, IPC, CLI, Watch Folder).
/// Thuần tuý, chỉ so sánh đường dẫn: không chạm đĩa.
/// </summary>
public sealed class ProtectedZones
{
    private readonly string[] _roots;
    private readonly string[] _segments;

    /// <summary>Tên thư mục (ở bất kỳ cấp nào) luôn được bảo vệ.</summary>
    private static readonly string[] ProtectedSegments =
    {
        "$Recycle.Bin",
        "System Volume Information"
    };

    public ProtectedZones(IEnumerable<string> roots, IEnumerable<string>? segments = null)
    {
        _roots = roots
            .Select(Normalize)
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _segments = (segments ?? ProtectedSegments).ToArray();
    }

    /// <summary>Cấu hình mặc định theo máy hiện tại.</summary>
    public static ProtectedZones Default { get; } = Create();

    private static ProtectedZones Create()
    {
        string Folder(Environment.SpecialFolder f) => Environment.GetFolderPath(f);

        var local = Folder(Environment.SpecialFolder.LocalApplicationData);
        var roots = new List<string>
        {
            Folder(Environment.SpecialFolder.Windows),
            Folder(Environment.SpecialFolder.ProgramFiles),
            Folder(Environment.SpecialFolder.ProgramFilesX86),
            Folder(Environment.SpecialFolder.CommonProgramFiles),
            Folder(Environment.SpecialFolder.CommonProgramFilesX86)
        };

        if (!string.IsNullOrEmpty(local))
        {
            // Dữ liệu của ứng dụng đóng gói khác, và journal/lịch sử/khu cách ly của chính BFR.
            roots.Add(Path.Combine(local, "Packages"));
            roots.Add(Path.Combine(local, "BatchFileRenamer"));
        }

        return new ProtectedZones(roots);
    }

    /// <summary>True nếu <paramref name="path"/> là hoặc nằm bên trong một vùng cấm.</summary>
    public bool IsProtected(string? path) => TryGetReason(path, out _);

    /// <summary>Lý do (tên vùng cấm) nếu đường dẫn bị cấm.</summary>
    public bool TryGetReason(string? path, out string reason)
    {
        reason = string.Empty;
        var full = Normalize(path);
        if (full.Length == 0) return false;

        foreach (var root in _roots)
        {
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                reason = root;
                return true;
            }
        }

        foreach (var segment in _segments)
        {
            var needle = Path.DirectorySeparatorChar + segment;
            var idx = full.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                var end = idx + needle.Length;
                if (end == full.Length || full[end] == Path.DirectorySeparatorChar)
                {
                    reason = segment;
                    return true;
                }
                idx = full.IndexOf(needle, end, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            if (!Path.IsPathFullyQualified(path)) return string.Empty;
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }
}
