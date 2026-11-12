namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Khu cách ly: nơi mọi thao tác "xoá" của BFR đưa tệp vào thay vì xoá vĩnh viễn, để Hoàn tác luôn khả thi.
/// Mỗi lần thực thi (journal) có một thư mục con riêng. Chỉ <see cref="Purge"/> mới xoá thật, và chỉ khi quá hạn.
/// </summary>
public sealed class QuarantineStore
{
    public const int DefaultRetentionDays = 30;

    private readonly IFileSystem _fs;

    public QuarantineStore(IFileSystem fs, string? root = null)
    {
        _fs = fs;
        Root = root ?? DefaultRoot;
    }

    public static string DefaultRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchFileRenamer", "quarantine");

    public string Root { get; }

    /// <summary>Đường dẫn trong khu cách ly cho một tệp của một lần thực thi. Duy nhất theo (journal, thứ tự).</summary>
    public string PathFor(string journalId, int seq, string sourcePath)
    {
        var name = Path.GetFileName(sourcePath.TrimEnd('\\', '/'));
        if (string.IsNullOrEmpty(name)) name = "item";
        return Path.Combine(Root, journalId, $"{seq:D5}-{name}");
    }

    public bool IsInside(string path) => PathGuard.IsInside(path, Root);

    /// <summary>Xoá vĩnh viễn các thư mục cách ly mà tệp mới nhất trong đó cũ hơn <paramref name="maxAge"/>. Trả về số thư mục đã xoá.</summary>
    public int Purge(TimeSpan maxAge, DateTime? nowUtc = null)
    {
        if (!_fs.DirectoryExists(Root)) return 0;

        var cutoff = (nowUtc ?? DateTime.UtcNow) - maxAge;
        var removed = 0;
        foreach (var dir in _fs.GetDirectories(Root, false))
        {
            try
            {
                var files = _fs.GetFiles(dir, "*", true);
                var newest = files.Length == 0
                    ? _fs.GetLastWriteTimeUtc(dir)
                    : files.Max(f => _fs.GetLastWriteTimeUtc(f));
                if (newest >= cutoff) continue;

                _fs.Delete(dir, recursive: true);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // đang được dùng: lần dọn sau sẽ thử lại
            }
        }
        return removed;
    }

    /// <summary>Tổng số tệp và dung lượng đang nằm trong khu cách ly.</summary>
    public (int Files, long Bytes) GetUsage()
    {
        if (!_fs.DirectoryExists(Root)) return (0, 0);
        var files = _fs.GetFiles(Root, "*", true);
        return (files.Length, files.Sum(f => _fs.GetLength(f)));
    }
}
