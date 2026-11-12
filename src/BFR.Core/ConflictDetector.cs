namespace BFR.Core;

using System;
using System.Collections.Generic;

/// <summary>
/// Phát hiện ba loại xung đột (PHẦN 7.2).
/// Xung đột được xét theo từng thư mục: hai tệp ở hai thư mục khác nhau
/// có thể cùng tên mới mà không xung đột.
/// </summary>
public enum ConflictType
{
    None,
    DuplicateInBatch,   // hai tệp trong cùng thư mục có tên mới giống nhau
    CollisionOutsideBatch, // tên mới trùng tệp/thư mục ngoài lô
    ReservedName,
    InvalidName
}

public sealed record ConflictInfo(
    ConflictType Type,
    FileItem File,
    string NewName,
    string? Message = null);

public sealed class ConflictDetector
{
    private readonly NameValidator _validator = new();

    /// <summary>
    /// Kiểm tra với một tập tên ngoài lô dùng chung (tương thích API cũ).
    /// </summary>
    public IReadOnlyList<ConflictInfo?> Detect(
        IReadOnlyList<PreviewItem> items,
        IReadOnlySet<string> outsideBatchNames)
        => DetectCore(items, _ => outsideBatchNames);

    /// <summary>
    /// Kiểm tra với tên ngoài lô theo từng thư mục (khóa đã chuẩn hóa bằng
    /// <see cref="NormalizeDirectory"/>).
    /// </summary>
    public IReadOnlyList<ConflictInfo?> Detect(
        IReadOnlyList<PreviewItem> items,
        IReadOnlyDictionary<string, IReadOnlySet<string>> outsideByDirectory)
        => DetectCore(items, item =>
        {
            var key = NormalizeDirectory(item.File.Directory);
            return outsideByDirectory.TryGetValue(key, out var names) ? names : null;
        });

    private IReadOnlyList<ConflictInfo?> DetectCore(
        IReadOnlyList<PreviewItem> items,
        Func<PreviewItem, IReadOnlySet<string>?> outsideFor)
    {
        var results = new List<ConflictInfo?>(items.Count);
        var newNamesInBatch = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            var issue = _validator.Validate(it.NewName);
            if (issue != NameIssue.None)
            {
                results.Add(new ConflictInfo(ToConflictType(issue), it.File, it.NewName, CoreText.Get("CoreInvalidName", "Tên không hợp lệ: {0}", issue)));
                continue;
            }

            // Khóa theo (thư mục, tên mới) để không báo nhầm giữa hai thư mục.
            var key = NormalizeDirectory(it.File.Directory) + "\0" + it.NewName.ToLowerInvariant();
            if (!newNamesInBatch.TryGetValue(key, out var indexes))
            {
                indexes = new List<int>();
                newNamesInBatch[key] = indexes;
            }
            indexes.Add(i);

            var outside = outsideFor(it);
            if (outside is not null && outside.Contains(it.NewName))
            {
                results.Add(new ConflictInfo(ConflictType.CollisionOutsideBatch, it.File, it.NewName,
                    CoreText.Get("CoreCollisionOutside", "Tên mới trùng tệp/thư mục ngoài lô")));
                continue;
            }

            results.Add(null);
        }

        // Đánh dấu trùng trong lô (các nhóm có > 1 phần tử).
        foreach (var (_, indexes) in newNamesInBatch)
        {
            if (indexes.Count > 1)
            {
                foreach (var i in indexes)
                {
                    results[i] = new ConflictInfo(ConflictType.DuplicateInBatch, items[i].File, items[i].NewName,
                        CoreText.Get("CoreDuplicateInBatch", "Trùng tên trong lô: '{0}'", items[i].NewName));
                }
            }
        }

        return results.AsReadOnly();
    }

    public static string NormalizeDirectory(string directory) =>
        directory.Replace('\\', '/').TrimEnd('/').ToLowerInvariant();

    private static ConflictType ToConflictType(NameIssue issue) => issue switch
    {
        NameIssue.Empty => ConflictType.InvalidName,
        NameIssue.InvalidChar => ConflictType.InvalidName,
        NameIssue.TrailingDotSpace => ConflictType.InvalidName,
        NameIssue.ReservedName => ConflictType.ReservedName,
        NameIssue.NameTooLong => ConflictType.InvalidName,
        NameIssue.PathTooLong => ConflictType.InvalidName,
        _ => ConflictType.None
    };
}
