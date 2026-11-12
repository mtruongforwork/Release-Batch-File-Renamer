namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

public sealed record FileRecord(string Path, string Name, string Extension, long Size, DateTime ModifiedUtc);

public sealed record TypeUsage(FileCategory Category, int Count, long Bytes);

public sealed record FolderUsage(string Path, long Bytes, int Files);

public sealed record MismatchedExtension(string Path, string CurrentExtension, string DetectedExtension);

public sealed class AnalysisReport
{
    public long TotalBytes { get; init; }
    public int TotalFiles { get; init; }
    public int TotalFolders { get; init; }
    public IReadOnlyList<TypeUsage> ByType { get; init; } = Array.Empty<TypeUsage>();
    public IReadOnlyList<FolderUsage> TopFolders { get; init; } = Array.Empty<FolderUsage>();
    public IReadOnlyList<FileRecord> Largest { get; init; } = Array.Empty<FileRecord>();
    public IReadOnlyList<FileRecord> Old { get; init; } = Array.Empty<FileRecord>();
    public IReadOnlyList<MismatchedExtension> Mismatched { get; init; } = Array.Empty<MismatchedExtension>();
    public IReadOnlyList<string> Skipped { get; init; } = Array.Empty<string>();
}

public sealed record AnalyzeOptions(int TopLargest = 100, int OlderThanYears = 0, bool CheckExtensions = true, int MaxOld = 1000);

/// <summary>
/// Phân tích thư mục (M8): dung lượng theo loại và theo thư mục con, tệp lớn nhất, tệp cũ, đuôi sai.
/// Chỉ đọc. Bộ nhớ theo O(tổng số tệp cần giữ) chỉ cho top-N, không giữ danh sách mọi tệp.
/// Không đi vào symlink/junction và vùng cấm; không đọc nội dung tệp "chỉ trực tuyến".
/// </summary>
public static class FolderAnalyzer
{
    public static AnalysisReport Analyze(
        IFileSystem fs, string root, AnalyzeOptions options, DateTime nowUtc,
        ProtectedZones? zones = null, CancellationToken ct = default, IProgress<int>? progress = null)
    {
        zones ??= ProtectedZones.Default;
        var skipped = new List<string>();
        var byType = new Dictionary<FileCategory, (int count, long bytes)>();
        var folderBytes = new Dictionary<string, (long bytes, int files)>(StringComparer.OrdinalIgnoreCase);
        var largest = new List<FileRecord>();
        var old = new List<FileRecord>();
        var mismatched = new List<MismatchedExtension>();
        long total = 0;
        var files = 0;
        var folders = 0;

        var cutoff = options.OlderThanYears > 0 ? nowUtc.AddYears(-options.OlderThanYears) : (DateTime?)null;
        var stack = new Stack<string>();
        stack.Push(root);
        var rootCanon = Canon(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();

            string[] subDirs, dirFiles;
            try
            {
                subDirs = fs.GetDirectories(dir, false);
                dirFiles = fs.GetFiles(dir, "*", false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(dir);
                continue;
            }

            foreach (var sub in subDirs)
            {
                if (fs.IsLink(sub) || zones.IsProtected(sub)) { skipped.Add(sub); continue; }
                folders++;
                stack.Push(sub);
            }

            // Thư mục con cấp 1 của gốc dùng làm "nhóm" để xếp hạng dung lượng.
            var bucket = BucketOf(rootCanon, dir);

            foreach (var path in dirFiles)
            {
                FileRecord record;
                try
                {
                    var name = Path.GetFileName(path);
                    var ext = Path.GetExtension(name).TrimStart('.');
                    record = new FileRecord(path, name, ext, fs.GetLength(path), fs.GetLastWriteTimeUtc(path));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped.Add(path);
                    continue;
                }

                files++;
                total += record.Size;
                var category = FileTypeClassifier.Classify(record.Extension);
                byType.TryGetValue(category, out var t);
                byType[category] = (t.count + 1, t.bytes + record.Size);
                folderBytes.TryGetValue(bucket, out var fb);
                folderBytes[bucket] = (fb.bytes + record.Size, fb.files + 1);

                Keep(largest, record, options.TopLargest, (a, b) => b.Size.CompareTo(a.Size));
                if (cutoff is { } c && record.ModifiedUtc < c)
                {
                    Keep(old, record, options.MaxOld, (a, b) => a.ModifiedUtc.CompareTo(b.ModifiedUtc));
                }

                if (options.CheckExtensions && record.Size > 0)
                {
                    var detected = MagicBytes.DetectExtension(fs, path);
                    if (detected is not null && !MagicBytes.IsConsistent(record.Extension, detected))
                    {
                        mismatched.Add(new MismatchedExtension(path, record.Extension, detected));
                    }
                }

                if (files % 1000 == 0) progress?.Report(files);
            }
        }

        return new AnalysisReport
        {
            TotalBytes = total,
            TotalFiles = files,
            TotalFolders = folders,
            ByType = byType.Select(p => new TypeUsage(p.Key, p.Value.count, p.Value.bytes)).OrderByDescending(x => x.Bytes).ToList(),
            TopFolders = folderBytes.Select(p => new FolderUsage(p.Key, p.Value.bytes, p.Value.files)).OrderByDescending(x => x.Bytes).Take(50).ToList(),
            Largest = largest,
            Old = old,
            Mismatched = mismatched,
            Skipped = skipped
        };
    }

    /// <summary>Giữ tối đa <paramref name="limit"/> phần tử đứng đầu theo <paramref name="order"/>; bộ nhớ O(limit).</summary>
    private static void Keep(List<FileRecord> list, FileRecord record, int limit, Comparison<FileRecord> order)
    {
        if (limit <= 0) return;
        var index = list.FindIndex(x => order(record, x) < 0);
        if (index < 0)
        {
            if (list.Count < limit) list.Add(record);
            return;
        }
        list.Insert(index, record);
        if (list.Count > limit) list.RemoveAt(list.Count - 1);
    }

    private static string Canon(string p) => p.Replace('\\', '/').TrimEnd('/');

    private static string BucketOf(string rootCanon, string dir)
    {
        var d = Canon(dir);
        if (d.Equals(rootCanon, StringComparison.OrdinalIgnoreCase)) return dir;
        var rest = d[(rootCanon.Length + 1)..];
        var slash = rest.IndexOf('/');
        var first = slash < 0 ? rest : rest[..slash];
        return rootCanon + "/" + first;
    }
}
