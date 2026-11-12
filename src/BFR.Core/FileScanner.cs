namespace BFR.Core;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

/// <summary>
/// Quét tệp/thư mục, áp bộ lọc, tạo danh sách FileItem bất biến (FR-A1..A4).
/// </summary>
public sealed class ScanOptions
{
    public bool Recursive { get; set; } = false;
    public int MaxDepth { get; set; } = int.MaxValue;
    public bool IncludeFolders { get; set; } = false;
    public bool ExcludeHidden { get; set; } = true;
    public bool ExcludeSystem { get; set; } = true;
    public string? IncludeGlob { get; set; }
    public string? ExcludeGlob { get; set; }
    public bool SortNatural { get; set; } = true;
}

public sealed class FileScanner
{
    public IReadOnlyList<FileItem> Scan(IEnumerable<string> paths, ScanOptions options, CancellationToken ct = default)
    {
        var files = new List<FileItem>();
        var dirs = new List<FileItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int loadOrder = 0;

        foreach (var path in paths)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (File.Exists(path))
                {
                    AddFile(path, files, seen, ref loadOrder);
                }
                else if (Directory.Exists(path))
                {
                    ScanDirectory(path, 0, options, files, dirs, seen, ref loadOrder, ct);
                }
            }
            catch (Exception)
            {
                // Bỏ qua đường dẫn không truy cập được; UI/CLI sẽ báo ở tầng trên.
            }
        }

        if (options.SortNatural)
        {
            var cmp = NaturalSortComparer.Instance;
            files.Sort((a, b) => cmp.Compare(a.FullPath, b.FullPath));
            dirs.Sort((a, b) => cmp.Compare(a.FullPath, b.FullPath));
        }

        // Thư mục xử lý từ lá lên gốc: sắp theo độ sâu giảm dần
        dirs.Sort((a, b) => Depth(b.FullPath).CompareTo(Depth(a.FullPath)));

        var result = new List<FileItem>(files.Count + dirs.Count);
        result.AddRange(files);
        result.AddRange(dirs);
        return result;
    }

    private void ScanDirectory(
        string directory, int depth, ScanOptions options,
        List<FileItem> files, List<FileItem> dirs, HashSet<string> seen,
        ref int loadOrder, CancellationToken ct)
    {
        if (ct.IsCancellationRequested) return;
        if (depth > options.MaxDepth) return;

        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (ct.IsCancellationRequested) break;
            if (!ShouldInclude(file, options)) continue;
            AddFile(file, files, seen, ref loadOrder);
        }

        if (!options.Recursive || depth >= options.MaxDepth) return;

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            if (ct.IsCancellationRequested) break;
            if (!ShouldInclude(sub, options)) continue;
            if (ShouldSkipDirectory(sub)) continue;
            if (options.IncludeFolders)
            {
                AddDirectory(sub, dirs, seen, ref loadOrder);
            }
            ScanDirectory(sub, depth + 1, options, files, dirs, seen, ref loadOrder, ct);
        }
    }

    /// <summary>
    /// Tạo <see cref="FileItem"/> từ một đường dẫn tệp cụ thể (dùng cho Watch Folder).
    /// </summary>
    public static FileItem FromFile(string path, int loadOrder = 0)
    {
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        var attributes = SafeAttributes(info);

        return new FileItem(
            full,
            Path.GetDirectoryName(full) ?? string.Empty,
            Path.GetFileNameWithoutExtension(full),
            Path.GetExtension(full).TrimStart('.'),
            false,
            info.Length,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            loadOrder)
        {
            IsOffline = IsOfflineAttribute(attributes),
            IsReadOnly = ((int)attributes & (int)FileAttributes.ReadOnly) != 0
        };
    }

    /// <summary>
    /// Tạo <see cref="FileItem"/> từ một đường dẫn tệp nhưng đọc thông tin qua
    /// <see cref="IFileSystem"/> (hỗ trợ cả hệ thống tệp giả dùng trong kiểm thử).
    /// </summary>
    public static FileItem FromFile(IFileSystem fs, string path, int loadOrder = 0)
    {
        var full = Path.GetFullPath(path);
        var ext = Path.GetExtension(full);

        long size = 0;
        var created = DateTime.UtcNow;
        var modified = DateTime.UtcNow;
        try
        {
            size = fs.GetLength(full);
            created = fs.GetCreationTimeUtc(full);
            modified = fs.GetLastWriteTimeUtc(full);
        }
        catch
        {
            // Tệp không truy cập được: dùng giá trị mặc định.
        }

        return new FileItem(
            full,
            Path.GetDirectoryName(full) ?? string.Empty,
            Path.GetFileNameWithoutExtension(full),
            ext.TrimStart('.'),
            false,
            size,
            created,
            modified,
            loadOrder);
    }

    /// <summary>
    /// True nếu tệp khớp mẫu bao gồm (không dùng mẫu = mọi tệp).
    /// </summary>
    public static bool MatchesGlobs(string path, string? includeGlob, string? excludeGlob)
    {
        var name = Path.GetFileName(path);

        if (!string.IsNullOrWhiteSpace(includeGlob) && !GlobMatch(name, includeGlob!)) return false;
        if (!string.IsNullOrWhiteSpace(excludeGlob) && GlobMatch(name, excludeGlob!)) return false;
        return true;
    }

    private static void AddFile(string path, List<FileItem> files, HashSet<string> seen, ref int loadOrder)
    {
        var full = Path.GetFullPath(path);
        if (!seen.Add(full)) return;
        var info = new FileInfo(full);
        var ext = Path.GetExtension(full);
        var name = Path.GetFileNameWithoutExtension(full);
        var attributes = SafeAttributes(info);
        files.Add(new FileItem(
            full,
            Path.GetDirectoryName(full) ?? string.Empty,
            name,
            ext.TrimStart('.'),
            false,
            info.Length,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            loadOrder++)
        {
            IsOffline = IsOfflineAttribute(attributes),
            IsReadOnly = ((int)attributes & (int)FileAttributes.ReadOnly) != 0
        });
    }

    private static void AddDirectory(string path, List<FileItem> dirs, HashSet<string> seen, ref int loadOrder)
    {
        var full = Path.GetFullPath(path).TrimEnd('\\', '/');
        if (!seen.Add(full)) return;
        var info = new DirectoryInfo(full);
        var attributes = SafeAttributes(info);
        dirs.Add(new FileItem(
            full,
            Path.GetDirectoryName(full) ?? string.Empty,
            info.Name,
            string.Empty,
            true,
            0,
            info.CreationTimeUtc,
            info.LastWriteTimeUtc,
            loadOrder++)
        {
            IsOffline = IsOfflineAttribute(attributes),
            IsReadOnly = ((int)attributes & (int)FileAttributes.ReadOnly) != 0
        });
    }

    private static FileAttributes SafeAttributes(FileSystemInfo info)
    {
        try
        {
            return info.Attributes;
        }
        catch
        {
            return 0;
        }
    }

    // Cờ "chỉ trực tuyến" của OneDrive/đám mây: tệp có thể bị tải về nếu bị đọc.
    private const int FileAttributeOffline = 0x00001000;
    private const int FileAttributeRecallOnOpen = 0x00040000;
    private const int FileAttributeRecallOnDataAccess = 0x00400000;

    /// <summary>
    /// Phát hiện tệp "placeholder" (OneDrive chỉ trực tuyến) để không vô tình tải tệp về máy.
    /// </summary>
    public static bool IsOfflineAttribute(FileAttributes attributes)
    {
        var raw = (int)attributes;
        return (raw & (FileAttributeOffline | FileAttributeRecallOnOpen | FileAttributeRecallOnDataAccess)) != 0;
    }

    private static bool ShouldInclude(string path, ScanOptions options)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            if (options.ExcludeHidden && attrs.HasFlag(FileAttributes.Hidden)) return false;
            if (options.ExcludeSystem && attrs.HasFlag(FileAttributes.System)) return false;
        }
        catch { /* bỏ qua */ }

        var name = Path.GetFileName(path);
        if (!string.IsNullOrWhiteSpace(options.IncludeGlob) && !GlobMatch(name, options.IncludeGlob!)) return false;
        if (!string.IsNullOrWhiteSpace(options.ExcludeGlob) && GlobMatch(name, options.ExcludeGlob!)) return false;
        return true;
    }

    private static bool GlobMatch(string name, string glob) => GlobMatcher.IsMatch(name, glob);

    /// <summary>
    /// Khi quét đệ quy (C10): không đi vào symlink/junction (tránh vòng lặp và thoát ra ngoài thư mục đã chọn)
    /// và không đi vào vùng hệ thống được bảo vệ.
    /// </summary>
    internal static bool ShouldSkipDirectory(string directory)
    {
        try
        {
            if (new DirectoryInfo(directory).LinkTarget is not null) return true;
        }
        catch
        {
            return true; // không đọc được thì không đi vào
        }

        return ProtectedZones.Default.IsProtected(directory);
    }

    private static int Depth(string path) => path.Count(c => c == '\\' || c == '/');
}
