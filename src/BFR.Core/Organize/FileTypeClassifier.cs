namespace BFR.Core;

using System;
using System.Collections.Generic;

/// <summary>Nhóm tệp thô dùng cho token <c>{type}</c> của Organize.</summary>
public enum FileCategory
{
    Other,
    Image,
    Video,
    Audio,
    Document,
    Archive
}

/// <summary>Phân loại tệp theo phần mở rộng (không đọc nội dung). Thuần tuý.</summary>
public static class FileTypeClassifier
{
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp4", "m4v", "mov", "avi", "mkv", "wmv", "flv", "webm", "3gp", "m2ts", "mpg", "mpeg"
    };

    private static readonly HashSet<string> Audios = new(StringComparer.OrdinalIgnoreCase)
    {
        "mp3", "m4a", "wav", "aac", "flac", "ogg", "wma", "opus", "aiff"
    };

    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
    {
        "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "rtf", "odt", "ods", "odp",
        "csv", "md", "epub", "html", "htm", "xml", "json"
    };

    private static readonly HashSet<string> Archives = new(StringComparer.OrdinalIgnoreCase)
    {
        "zip", "rar", "7z", "tar", "gz", "bz2", "xz", "cab", "iso"
    };

    public static FileCategory Classify(string? extension)
    {
        var ext = (extension ?? string.Empty).Trim().TrimStart('.');
        if (ext.Length == 0) return FileCategory.Other;
        if (MediaFileTypes.MayHaveExif(ext)) return FileCategory.Image;
        if (Videos.Contains(ext)) return FileCategory.Video;
        if (Audios.Contains(ext)) return FileCategory.Audio;
        if (Documents.Contains(ext)) return FileCategory.Document;
        if (Archives.Contains(ext)) return FileCategory.Archive;
        return FileCategory.Other;
    }

    /// <summary>Tên thư mục hiển thị cho nhóm (đã bản địa hoá qua <see cref="CoreText"/>).</summary>
    public static string FolderName(FileCategory category) => category switch
    {
        FileCategory.Image => CoreText.Get("CoreTypeImage", "Ảnh"),
        FileCategory.Video => CoreText.Get("CoreTypeVideo", "Video"),
        FileCategory.Audio => CoreText.Get("CoreTypeAudio", "Nhạc"),
        FileCategory.Document => CoreText.Get("CoreTypeDocument", "Tài liệu"),
        FileCategory.Archive => CoreText.Get("CoreTypeArchive", "Nén"),
        _ => CoreText.Get("CoreTypeOther", "Khác")
    };
}
