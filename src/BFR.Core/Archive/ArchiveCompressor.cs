namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

public enum ArchiveGrouping
{
    /// <summary>Tất cả vào một ZIP.</summary>
    AllInOne,

    /// <summary>Mỗi thư mục chứa tệp một ZIP.</summary>
    PerFolder,

    /// <summary>Chia thành nhiều ZIP theo số tệp hoặc dung lượng (để gửi email).</summary>
    PerGroup
}

/// <param name="NameTemplate">Tên ZIP không gồm đuôi. Token: <c>{name}</c> (tên thư mục hoặc "Archive"), <c>{n}</c> (số thứ tự), <c>{date}</c>.</param>
/// <param name="MaxFiles">Với PerGroup: tối đa số tệp mỗi ZIP (0 = không giới hạn).</param>
/// <param name="MaxBytes">Với PerGroup: tối đa dung lượng gốc mỗi ZIP (0 = không giới hạn). Một tệp lớn hơn giới hạn vẫn nằm riêng một ZIP.</param>
public sealed record CompressOptions(
    ArchiveGrouping Grouping = ArchiveGrouping.AllInOne,
    string? OutputFolder = null,
    string NameTemplate = "{name}",
    CompressionLevel Level = CompressionLevel.Optimal,
    int MaxFiles = 0,
    long MaxBytes = 0);

public sealed record ArchiveEntrySource(string SourcePath, string EntryName, long Size, DateTime ModifiedUtc);

public sealed record ArchiveJob(string ZipPath, IReadOnlyList<ArchiveEntrySource> Entries);

public sealed record CompressResult(IReadOnlyList<Operation> Operations, IReadOnlyList<FolderPlanSkip> Skipped, long BytesBefore, long BytesAfter);

/// <summary>
/// Nén tệp thành ZIP (M6). Lập kế hoạch thuần tuý rồi ghi ZIP ra tệp tạm; <see cref="OperationExecutor"/> đưa vào chỗ đích
/// (ZIP mới, hoàn tác = xoá ZIP). Không bao giờ ghi đè ZIP có sẵn và không bao giờ nén chính ZIP đang tạo.
/// </summary>
public static class ArchiveCompressor
{
    public static IReadOnlyList<ArchiveJob> Plan(IReadOnlyList<FileItem> files, CompressOptions options, IFileSystem fs, ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var usable = files.Where(f => !f.IsDirectory && !f.IsOffline && !zones.IsProtected(f.FullPath)).ToList();
        if (usable.Count == 0) return Array.Empty<ArchiveJob>();

        var batches = new List<(string name, string baseDir, List<FileItem> files)>();
        switch (options.Grouping)
        {
            case ArchiveGrouping.PerFolder:
                foreach (var g in usable.GroupBy(f => Canon(f.Directory), StringComparer.OrdinalIgnoreCase))
                {
                    var dir = g.First().Directory;
                    batches.Add((LastSegment(dir), dir, g.ToList()));
                }
                break;

            case ArchiveGrouping.PerGroup:
            {
                var baseDir = CommonDirectory(usable);
                var current = new List<FileItem>();
                long bytes = 0;
                foreach (var f in usable)
                {
                    var full = (options.MaxFiles > 0 && current.Count >= options.MaxFiles)
                        || (options.MaxBytes > 0 && current.Count > 0 && bytes + f.Size > options.MaxBytes);
                    if (full)
                    {
                        batches.Add(("Archive", baseDir, current));
                        current = new List<FileItem>();
                        bytes = 0;
                    }
                    current.Add(f);
                    bytes += f.Size;
                }
                if (current.Count > 0) batches.Add(("Archive", baseDir, current));
                break;
            }

            default:
                batches.Add(("Archive", CommonDirectory(usable), usable));
                break;
        }

        var jobs = new List<ArchiveJob>();
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var (name, baseDir, members) in batches)
        {
            index++;
            var outDir = options.OutputFolder ?? baseDir;
            var template = options.NameTemplate;
            if (options.Grouping == ArchiveGrouping.PerGroup && batches.Count > 1 && !template.Contains("{n}", StringComparison.OrdinalIgnoreCase))
            {
                template += "_{n}"; // nhiều phần cùng tên: tự đánh số để dễ sắp xếp
            }
            var stem = OrganizeTemplate.SanitizeSegment(ExpandName(template, name, index, batches.Count));
            if (stem.Length == 0) stem = "Archive";

            var zip = UniquePath(outDir, stem, ".zip", fs, taken);
            taken.Add(Canon(zip));

            var entries = new List<ArchiveEntrySource>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in members)
            {
                if (SamePath(f.FullPath, zip)) continue;
                var entryName = UniqueEntry(Relative(baseDir, f.FullPath), names);
                entries.Add(new ArchiveEntrySource(f.FullPath, entryName, f.Size, f.ModifiedUtc));
            }

            if (entries.Count > 0) jobs.Add(new ArchiveJob(zip, entries));
        }

        return jobs;
    }

    /// <summary>Ghi mọi ZIP ra <paramref name="tempRoot"/> và trả về thao tác đặt chúng vào đích.</summary>
    public static CompressResult Run(
        IReadOnlyList<ArchiveJob> jobs, CompressOptions options, IFileSystem fs, string tempRoot, CancellationToken ct = default)
    {
        var ops = new List<Operation>();
        var skipped = new List<FolderPlanSkip>();
        long before = 0, after = 0;
        var sep = tempRoot.Contains('\\') || tempRoot.Contains(':') ? '\\' : '/';
        var n = 0;

        foreach (var job in jobs)
        {
            ct.ThrowIfCancellationRequested();
            var temp = $"{tempRoot.TrimEnd('\\', '/')}{sep}{++n:D4}.zip";
            var written = 0;
            long raw = 0;

            try
            {
                using (var output = fs.OpenWrite(temp))
                using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: false))
                {
                    foreach (var e in job.Entries)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            using var input = fs.OpenRead(e.SourcePath);
                            var entry = zip.CreateEntry(e.EntryName, options.Level);
                            entry.LastWriteTime = ClampZipTime(e.ModifiedUtc);
                            using var target = entry.Open();
                            input.CopyTo(target);
                            written++;
                            raw += e.Size;
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            skipped.Add(new FolderPlanSkip(e.SourcePath, ex.Message));
                        }
                    }
                }
            }
            catch
            {
                SafeDelete(fs, temp);
                throw;
            }

            if (written == 0)
            {
                SafeDelete(fs, temp);
                continue;
            }

            before += raw;
            try { after += fs.GetLength(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            ops.Add(Operation.WriteFile(temp, job.ZipPath));
        }

        return new CompressResult(ops, skipped, before, after);
    }

    private static DateTime ClampZipTime(DateTime utc)
    {
        var local = utc.ToLocalTime();
        if (local.Year < 1980) return new DateTime(1980, 1, 1);
        if (local.Year > 2107) return new DateTime(2107, 12, 31);
        return local;
    }

    private static string ExpandName(string template, string name, int index, int total) =>
        (string.IsNullOrWhiteSpace(template) ? "{name}" : template)
            .Replace("{name}", name, StringComparison.OrdinalIgnoreCase)
            .Replace("{n}", total > 1 ? index.ToString("00") : string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase);

    private static string UniquePath(string dir, string stem, string ext, IFileSystem fs, HashSet<string> taken)
    {
        var sep = dir.Contains('\\') || dir.Contains(':') ? '\\' : '/';
        var baseDir = dir.TrimEnd('\\', '/');
        var candidate = $"{baseDir}{sep}{stem}{ext}";
        for (var n = 2; taken.Contains(Canon(candidate)) || fs.FileExists(candidate); n++) candidate = $"{baseDir}{sep}{stem} ({n}){ext}";
        return candidate;
    }

    private static string UniqueEntry(string name, HashSet<string> names)
    {
        var candidate = name;
        for (var n = 2; !names.Add(candidate); n++)
        {
            var dot = name.LastIndexOf('.');
            var slash = name.LastIndexOf('/');
            candidate = dot > slash + 1 ? $"{name[..dot]} ({n}){name[dot..]}" : $"{name} ({n})";
        }
        return candidate;
    }

    private static string Relative(string baseDir, string full)
    {
        var b = Canon(baseDir);
        var f = full.Replace('\\', '/');
        if (f.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase)) return f[(b.Length + 1)..];
        return LastSegment(f);
    }

    private static string CommonDirectory(IReadOnlyList<FileItem> files)
    {
        var parts = files.Select(f => Canon(f.Directory).Split('/')).ToList();
        var common = parts[0];
        foreach (var p in parts.Skip(1))
        {
            var len = 0;
            while (len < common.Length && len < p.Length && string.Equals(common[len], p[len], StringComparison.OrdinalIgnoreCase)) len++;
            common = common[..len];
        }

        var joined = string.Join("/", common);
        if (joined.Length == 0) return files[0].Directory;
        // Giữ kiểu dấu phân cách của đường dẫn gốc.
        return files[0].Directory.Contains('\\') ? joined.Replace('/', '\\') : joined;
    }

    private static string LastSegment(string path)
    {
        var t = path.TrimEnd('\\', '/');
        var i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
        var seg = i < 0 ? t : t[(i + 1)..];
        return seg.Length == 0 || seg.EndsWith(':') ? "Archive" : seg;
    }

    private static string Canon(string p) => p.Replace('\\', '/').TrimEnd('/');

    private static bool SamePath(string a, string b) => string.Equals(Canon(a), Canon(b), StringComparison.OrdinalIgnoreCase);

    private static void SafeDelete(IFileSystem fs, string path)
    {
        try { if (fs.FileExists(path)) fs.Delete(path, recursive: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
