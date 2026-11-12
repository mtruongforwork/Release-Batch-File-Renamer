namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Tiện ích đọc danh sách đường dẫn từ tệp tạm (hỗ trợ tham số --file-list từ Explorer/CLI).
/// </summary>
public static class FileListReader
{
    public static IReadOnlyList<string> Read(string filePath, IFileSystem? fs = null)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return Array.Empty<string>();

        var fileSystem = fs ?? new RealFileSystem();
        if (!fileSystem.FileExists(filePath)) return Array.Empty<string>();

        var content = fileSystem.ReadAllText(filePath);
        var results = new List<string>();

        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("#")) continue; // Bỏ qua ghi chú nếu có

            // Loại bỏ dấu ngoặc kép nếu đường dẫn bị bao bởi ngoặc kép
            if (line.Length >= 2 && line.StartsWith("\"") && line.EndsWith("\""))
            {
                line = line[1..^1].Trim();
            }

            if (!string.IsNullOrEmpty(line))
            {
                results.Add(line);
            }
        }

        return results;
    }

    public static void Write(string filePath, IEnumerable<string> paths, IFileSystem? fs = null)
    {
        var fileSystem = fs ?? new RealFileSystem();
        var content = string.Join(Environment.NewLine, paths) + Environment.NewLine;
        fileSystem.WriteAllText(filePath, content);
    }

    /// <summary>
    /// Đọc danh sách do shell/CLI tạo trong thư mục tạm rồi xóa tệp.
    /// Chỉ xóa khi dùng hệ thống tệp thật để không ảnh hưởng FakeFileSystem trong test.
    /// </summary>
    public static IReadOnlyList<string> ReadAndDelete(string filePath, IFileSystem? fs = null)
    {
        try
        {
            return Read(filePath, fs);
        }
        finally
        {
            // Chỉ xoá tệp danh sách do BFR tạo trong thư mục tạm riêng; đường dẫn từ nguồn ngoài
            // (IPC, dòng lệnh) không được phép khiến BFR xoá tệp tuỳ ý của người dùng.
            if ((fs is null || fs is RealFileSystem) && PathGuard.IsBfrTempFile(filePath))
            {
                try
                {
                    if (File.Exists(filePath)) File.Delete(filePath);
                }
                catch
                {
                    // Tệp tạm không xóa được không phải lỗi chức năng.
                }
            }
        }
    }
}
