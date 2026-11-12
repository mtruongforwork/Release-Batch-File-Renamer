namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Kiểm tra và chuẩn hoá dữ liệu đến từ ngoài tiến trình (IPC, dòng lệnh, tệp danh sách).
/// Thuần tuý: không chạm đĩa, nên test được mà không cần hệ thống tệp thật.
/// </summary>
public static class PathGuard
{
    public const int MaxPathLength = 32767;
    public const int MaxPaths = 20_000;
    public const int MaxPresetNameLength = 256;
    public const int MaxQuickKeyLength = 64;

    /// <summary>Kích thước tối đa của một tin nhắn IPC (ký tự JSON).</summary>
    public const int MaxIpcMessageChars = 4 * 1024 * 1024;

    /// <summary>Thư mục tạm riêng của BFR; chỉ tệp danh sách trong đây mới được đọc qua IPC và bị xoá sau khi đọc.</summary>
    public static string BfrTempRoot =>
        Path.Combine(Path.GetTempPath(), "BatchFileRenamer");

    /// <summary>
    /// Đường dẫn hợp lệ: không rỗng, không quá dài, không có ký tự điều khiển hoặc NUL,
    /// là đường dẫn tuyệt đối. Trả về dạng đã chuẩn hoá (<see cref="Path.GetFullPath(string)"/>).
    /// </summary>
    public static bool TryNormalize(string? path, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxPathLength) return false;

        foreach (var c in path)
        {
            if (char.IsControl(c)) return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(path)) return false;
            normalized = Path.GetFullPath(path);
            return normalized.Length <= MaxPathLength;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>True khi <paramref name="path"/> nằm bên trong <paramref name="root"/> (không tính chính root).</summary>
    public static bool IsInside(string path, string root)
    {
        if (!TryNormalize(path, out var full) || !TryNormalize(root, out var rootFull)) return false;

        var prefix = rootFull.EndsWith(Path.DirectorySeparatorChar) ? rootFull : rootFull + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True khi là tệp danh sách nằm trong thư mục tạm của BFR.</summary>
    public static bool IsBfrTempFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && IsInside(path, BfrTempRoot);

    /// <summary>
    /// Làm sạch tin nhắn IPC: bỏ đường dẫn không hợp lệ, giới hạn số lượng và độ dài,
    /// chỉ giữ <c>FileListPath</c> nằm trong thư mục tạm của BFR. Không bao giờ ném lỗi.
    /// </summary>
    public static IpcPayload SanitizeIpcPayload(IpcPayload? input)
    {
        var result = new IpcPayload();
        if (input is null) return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in input.Paths ?? new List<string>())
        {
            if (result.Paths.Count >= MaxPaths) break;
            if (TryNormalize(p, out var full) && seen.Add(full)) result.Paths.Add(full);
        }

        if (IsBfrTempFile(input.FileListPath) && TryNormalize(input.FileListPath, out var list))
        {
            result.FileListPath = list;
        }

        result.PresetName = Limit(input.PresetName, MaxPresetNameLength);
        result.QuickPreset = Limit(input.QuickPreset, MaxQuickKeyLength);
        result.Tool = KnownTools.IsKnown(input.Tool) ? input.Tool : null; // danh sách trắng: khoá lạ bị bỏ
        return result;
    }

    private static string? Limit(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max) return null;
        foreach (var c in value)
        {
            if (char.IsControl(c)) return null;
        }
        return value;
    }
}
