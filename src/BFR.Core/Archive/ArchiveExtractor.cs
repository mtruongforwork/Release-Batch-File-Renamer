namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;

public enum ExtractLayout
{
    /// <summary>Mỗi ZIP vào một thư mục cùng tên.</summary>
    OwnFolder,

    /// <summary>Tất cả vào một thư mục (tên trùng được thêm " (2)").</summary>
    SingleFolder
}

/// <param name="MaxRatio">Tỉ lệ nén tối đa (giải nén / dung lượng ZIP), chống zip bomb.</param>
/// <param name="MaxTotalBytes">Tổng dung lượng giải nén tối đa mỗi ZIP.</param>
/// <param name="DestinationRoot">Thư mục đích; null = cạnh từng ZIP (OwnFolder) hoặc thư mục "Extracted" cạnh ZIP đầu tiên (SingleFolder).</param>
public sealed record ExtractOptions(
    ExtractLayout Layout = ExtractLayout.OwnFolder,
    string? DestinationRoot = null,
    int MaxRatio = 100,
    long MaxTotalBytes = 10L * 1024 * 1024 * 1024,
    int MaxEntries = 100_000);

public sealed record RejectedArchive(string ZipPath, string Reason);

public sealed record ExtractResult(
    IReadOnlyList<Operation> Operations,
    IReadOnlyList<RejectedArchive> Rejected,
    IReadOnlyList<FolderPlanSkip> Skipped,
    long BytesWritten);

/// <summary>
/// Giải nén ZIP an toàn (M6). Chặn Zip Slip (đường dẫn tuyệt đối, ổ đĩa, <c>..</c>), zip bomb (tỉ lệ nén, tổng dung lượng,
/// số mục — kiểm tra cả trên số byte THỰC SỰ giải nén vì kích thước khai báo có thể bị nói dối), bỏ qua symlink.
/// Giải nén vào tệp tạm; chỉ khi cả ZIP hợp lệ mới lập các thao tác đưa tệp vào đích, nên ZIP độc không để lại gì.
/// </summary>
public static class ArchiveExtractor
{
    private const long RatioCheckFloor = 10L * 1024 * 1024; // dưới 10 MB không áp tỉ lệ để tránh báo nhầm tệp nhỏ

    public static ExtractResult Extract(
        IReadOnlyList<string> zipPaths, ExtractOptions options, IFileSystem fs, string tempRoot,
        ProtectedZones? zones = null, CancellationToken ct = default)
    {
        zones ??= ProtectedZones.Default;
        var ops = new List<Operation>();
        var rejected = new List<RejectedArchive>();
        var skipped = new List<FolderPlanSkip>();
        long total = 0;
        var sep = tempRoot.Contains('\\') || tempRoot.Contains(':') ? '\\' : '/';
        var index = 0;

        string? singleRoot = null;
        var takenDest = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var takenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var zipPath in zipPaths)
        {
            ct.ThrowIfCancellationRequested();
            index++;

            if (zones.IsProtected(zipPath))
            {
                rejected.Add(new RejectedArchive(zipPath, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                continue;
            }

            string destFolder;
            if (options.Layout == ExtractLayout.SingleFolder)
            {
                singleRoot ??= options.DestinationRoot ?? Join(ParentOf(zipPath), CoreText.Get("CoreExtractedFolder", "Extracted"));
                destFolder = singleRoot;
            }
            else
            {
                var parent = options.DestinationRoot ?? ParentOf(zipPath);
                var stem = OrganizeTemplate.SanitizeSegment(Path.GetFileNameWithoutExtension(zipPath));
                if (stem.Length == 0) stem = "Extracted";
                destFolder = UniqueFolder(Join(parent, stem), fs, takenFolders);
            }

            if (zones.IsProtected(destFolder))
            {
                rejected.Add(new RejectedArchive(zipPath, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                continue;
            }

            var work = $"{tempRoot.TrimEnd('\\', '/')}{sep}{index:D4}";
            try
            {
                var archiveOps = new List<Operation>();
                var written = ExtractOne(zipPath, destFolder, work, sep, options, fs, takenDest, archiveOps, skipped, ct, out var reason);
                if (reason is not null)
                {
                    rejected.Add(new RejectedArchive(zipPath, reason));
                    CleanDir(fs, work);
                    continue;
                }

                if (archiveOps.Count == 0) { CleanDir(fs, work); continue; }

                if (options.Layout == ExtractLayout.OwnFolder || !fs.DirectoryExists(destFolder)) ops.Add(Operation.CreateDir(destFolder));
                ops.AddRange(archiveOps);
                total += written;
            }
            catch (OperationCanceledException)
            {
                CleanDir(fs, work);
                throw;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                rejected.Add(new RejectedArchive(zipPath, ex.Message));
                CleanDir(fs, work);
            }
        }

        return new ExtractResult(ops, rejected, skipped, total);
    }

    private static long ExtractOne(
        string zipPath, string destFolder, string work, char sep, ExtractOptions options, IFileSystem fs,
        HashSet<string> takenDest, List<Operation> ops, List<FolderPlanSkip> skipped, CancellationToken ct, out string? reason)
    {
        reason = null;
        long zipLength;
        try { zipLength = Math.Max(1, fs.GetLength(zipPath)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { reason = ex.Message; return 0; }

        using var stream = fs.OpenRead(zipPath);
        ZipArchive archive;
        try { archive = new ZipArchive(stream, ZipArchiveMode.Read); }
        catch (InvalidDataException) { reason = CoreText.Get("CoreZipInvalid", "Không phải tệp ZIP hợp lệ"); return 0; }

        using (archive)
        {
            var entries = archive.Entries;
            if (entries.Count > options.MaxEntries)
            {
                reason = CoreText.Get("CoreZipTooManyEntries", "ZIP có quá nhiều mục ({0})", entries.Count);
                return 0;
            }

            long declared = entries.Sum(e => e.Length);
            if (declared > options.MaxTotalBytes)
            {
                reason = CoreText.Get("CoreZipTooLarge", "ZIP giải nén ra quá lớn (nghi zip bomb)");
                return 0;
            }
            if (declared > RatioCheckFloor && declared / zipLength > options.MaxRatio)
            {
                reason = CoreText.Get("CoreZipRatio", "Tỉ lệ nén bất thường (nghi zip bomb)");
                return 0;
            }

            // Kiểm tra TOÀN BỘ tên trước khi ghi bất cứ gì: một mục độc thì từ chối cả ZIP.
            var planned = new List<(ZipArchiveEntry entry, string[] parts, bool isDir)>();
            foreach (var entry in entries)
            {
                var name = entry.FullName;
                if (IsSymlink(entry)) { skipped.Add(new FolderPlanSkip(zipPath + "!" + name, CoreText.Get("CoreZipSymlink", "Liên kết tượng trưng bị bỏ qua"))); continue; }

                if (!TrySafeParts(name, out var parts, out var bad))
                {
                    reason = CoreText.Get("CoreZipUnsafePath", "ZIP chứa đường dẫn nguy hiểm: {0}", bad);
                    return 0;
                }
                if (parts.Length == 0) continue;
                planned.Add((entry, parts, name.EndsWith('/') || name.EndsWith('\\')));
            }

            long written = 0;
            var dirsMade = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (entry, parts, isDir) in planned)
            {
                ct.ThrowIfCancellationRequested();
                var target = Join(destFolder, string.Join(sep, parts));

                if (isDir)
                {
                    if (dirsMade.Add(target) && !fs.DirectoryExists(target)) ops.Add(Operation.CreateDir(target));
                    continue;
                }

                target = UniqueFile(target, fs, takenDest);
                var temp = $"{work}{sep}{ops.Count + 1:D6}.tmp";

                using (var input = entry.Open())
                using (var output = fs.OpenWrite(temp))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        written += read;
                        // Kích thước khai báo có thể nói dối: kiểm tra trên số byte thực.
                        if (written > options.MaxTotalBytes)
                        {
                            reason = CoreText.Get("CoreZipTooLarge", "ZIP giải nén ra quá lớn (nghi zip bomb)");
                            return 0;
                        }
                        if (written > RatioCheckFloor && written / zipLength > options.MaxRatio)
                        {
                            reason = CoreText.Get("CoreZipRatio", "Tỉ lệ nén bất thường (nghi zip bomb)");
                            return 0;
                        }
                        output.Write(buffer, 0, read);
                    }
                }

                ops.Add(Operation.Move(temp, target));
            }

            return written;
        }
    }

    /// <summary>
    /// Tách tên mục ZIP thành các cấp thư mục an toàn. False (kèm tên mục) nếu là đường dẫn tuyệt đối, UNC, có ổ đĩa
    /// hoặc có "..". Ký tự cấm khác chỉ bị thay bằng "_".
    /// </summary>
    public static bool TrySafeParts(string entryName, out string[] parts, out string bad)
    {
        parts = Array.Empty<string>();
        bad = entryName;

        if (string.IsNullOrEmpty(entryName) || entryName.Length > 1024) return false;
        if (entryName.StartsWith('/') || entryName.StartsWith('\\')) return false;
        foreach (var c in entryName) if (c == '\0' || c < 32) return false;

        var raw = entryName.Split('/', '\\');
        var list = new List<string>();
        for (var i = 0; i < raw.Length; i++)
        {
            var seg = raw[i];
            if (seg.Length == 0) continue;                    // "a//b" hoặc dấu / ở cuối
            if (seg == "..") return false;
            if (seg == ".") continue;
            if (i == 0 && seg.Length >= 2 && seg[1] == ':') return false; // "C:..."
            var clean = OrganizeTemplate.SanitizeSegment(seg);
            if (clean.Length == 0) continue;
            list.Add(clean);
        }

        parts = list.ToArray();
        return true;
    }

    private static bool IsSymlink(ZipArchiveEntry entry)
    {
        // ZIP tạo trên Unix: 16 bit cao của ExternalAttributes là st_mode; 0xA000 = symlink.
        var mode = (entry.ExternalAttributes >> 16) & 0xF000;
        return mode == 0xA000;
    }

    private static string UniqueFile(string target, IFileSystem fs, HashSet<string> taken)
    {
        var candidate = target;
        var dot = target.LastIndexOf('.');
        var slash = Math.Max(target.LastIndexOf('/'), target.LastIndexOf('\\'));
        var stem = dot > slash + 1 ? target[..dot] : target;
        var ext = dot > slash + 1 ? target[dot..] : string.Empty;

        for (var n = 2; taken.Contains(candidate) || fs.FileExists(candidate) || fs.DirectoryExists(candidate); n++)
        {
            candidate = $"{stem} ({n}){ext}";
        }
        taken.Add(candidate);
        return candidate;
    }

    private static string UniqueFolder(string folder, IFileSystem fs, HashSet<string> taken)
    {
        var candidate = folder;
        for (var n = 2; taken.Contains(candidate) || fs.DirectoryExists(candidate) || fs.FileExists(candidate); n++) candidate = $"{folder} ({n})";
        taken.Add(candidate);
        return candidate;
    }

    private static string ParentOf(string path)
    {
        var t = path.TrimEnd('\\', '/');
        var i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
        return i <= 0 ? t[..Math.Max(i, 0)] + (path.Contains('\\') ? "\\" : "/") : t[..i];
    }

    private static string Join(string dir, string name)
    {
        var sep = dir.Contains('\\') || dir.Contains(':') ? '\\' : '/';
        return dir.TrimEnd('\\', '/') + sep + name;
    }

    private static void CleanDir(IFileSystem fs, string dir)
    {
        try { if (fs.DirectoryExists(dir)) fs.Delete(dir, recursive: true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
