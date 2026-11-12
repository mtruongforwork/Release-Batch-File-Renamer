namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Nhận diện định dạng thật của tệp qua chữ ký đầu tệp để phát hiện đuôi sai (M8).
/// Chỉ trả kết quả khi chắc chắn; định dạng lạ trả null (không bao giờ báo sai).
/// </summary>
public static class MagicBytes
{
    private static readonly Dictionary<string, string[]> Equivalent = new(StringComparer.OrdinalIgnoreCase)
    {
        ["jpg"] = new[] { "jpg", "jpeg", "jpe", "jfif" },
        ["png"] = new[] { "png" },
        ["gif"] = new[] { "gif" },
        ["pdf"] = new[] { "pdf" },
        // ZIP là vỏ của rất nhiều định dạng văn phòng/đóng gói.
        ["zip"] = new[] { "zip", "docx", "xlsx", "pptx", "odt", "ods", "odp", "jar", "apk", "epub", "msix", "appx", "nupkg", "vsix", "xpi" },
        ["mp4"] = new[] { "mp4", "m4v", "mov", "m4a", "3gp", "heic", "heif", "avif", "cr3" },
        ["rar"] = new[] { "rar" },
        ["7z"] = new[] { "7z" },
        ["gz"] = new[] { "gz", "tgz" },
        ["exe"] = new[] { "exe", "dll", "sys", "scr", "ocx" },
        ["bmp"] = new[] { "bmp" },
        ["webp"] = new[] { "webp" },
        ["mp3"] = new[] { "mp3" },
        ["flac"] = new[] { "flac" }
    };

    /// <summary>Đuôi chuẩn của định dạng nhận ra từ 16 byte đầu, hoặc null nếu không nhận ra.</summary>
    public static string? Detect(ReadOnlySpan<byte> h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "jpg";
        if (h.Length >= 8 && h[0] == 0x89 && h[1] == 'P' && h[2] == 'N' && h[3] == 'G') return "png";
        if (h.Length >= 6 && h[0] == 'G' && h[1] == 'I' && h[2] == 'F' && h[3] == '8') return "gif";
        if (h.Length >= 5 && h[0] == '%' && h[1] == 'P' && h[2] == 'D' && h[3] == 'F' && h[4] == '-') return "pdf";
        if (h.Length >= 4 && h[0] == 'P' && h[1] == 'K' && (h[2] == 3 || h[2] == 5) && (h[3] == 4 || h[3] == 6)) return "zip";
        if (h.Length >= 8 && h[4] == 'f' && h[5] == 't' && h[6] == 'y' && h[7] == 'p') return "mp4";
        if (h.Length >= 6 && h[0] == 'R' && h[1] == 'a' && h[2] == 'r' && h[3] == '!') return "rar";
        if (h.Length >= 6 && h[0] == '7' && h[1] == 'z' && h[2] == 0xBC && h[3] == 0xAF) return "7z";
        if (h.Length >= 3 && h[0] == 0x1F && h[1] == 0x8B && h[2] == 0x08) return "gz";
        if (h.Length >= 2 && h[0] == 'M' && h[1] == 'Z') return "exe";
        if (h.Length >= 2 && h[0] == 'B' && h[1] == 'M') return "bmp";
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return "webp";
        if (h.Length >= 4 && h[0] == 'f' && h[1] == 'L' && h[2] == 'a' && h[3] == 'C') return "flac";
        if (h.Length >= 3 && h[0] == 'I' && h[1] == 'D' && h[2] == '3') return "mp3";
        return null;
    }

    public static string? DetectExtension(IFileSystem fs, string path)
    {
        try
        {
            using var stream = fs.OpenRead(path);
            var buffer = new byte[16];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n <= 0) break;
                read += n;
            }
            return Detect(buffer.AsSpan(0, read));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// True nếu đuôi hiện tại phù hợp với định dạng nhận ra. Đuôi không thuộc họ nào đã biết (ví dụ ".dat") cũng
    /// được coi là phù hợp để tránh báo nhầm; chỉ báo khi đuôi thuộc một định dạng khác hẳn.
    /// </summary>
    public static bool IsConsistent(string currentExtension, string detected)
    {
        var current = currentExtension.TrimStart('.').ToLowerInvariant();
        if (current.Length == 0) return false;
        if (Equivalent.TryGetValue(detected, out var family) && Array.IndexOf(family, current) >= 0) return true;

        foreach (var pair in Equivalent)
        {
            if (pair.Key == detected) continue;
            if (Array.IndexOf(pair.Value, current) >= 0) return false; // đuôi của định dạng khác
        }
        return true;
    }
}
