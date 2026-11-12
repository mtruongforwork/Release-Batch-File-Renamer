namespace BFR.Core;

using System.Collections.Concurrent;
using System.Collections.Generic;

/// <summary>
/// Bản giả in-memory của IFileSystem để kiểm thử, kể cả giả lập lỗi/mất điện.
/// </summary>
public sealed class FakeFileSystem : IFileSystem
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dirs = new(StringComparer.OrdinalIgnoreCase) { string.Empty };
    public bool ThrowOnMove { get; set; }
    public string? MoveExceptionMessage { get; set; } = "Lỗi giả lập";
    /// <summary>Giả lập tệp bị khóa/chỉ đọc: các đường dẫn này sẽ ném lỗi khi Move.</summary>
    public HashSet<string> FailOnMovePaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool FileExists(string path) => _files.ContainsKey(Normalize(path));
    public bool DirectoryExists(string path) => _dirs.Contains(Normalize(path)) || _files.Keys.Any(k => k.StartsWith(Normalize(path) + "/"));

    public void EnsureDirectory(string path)
    {
        var p = Normalize(path);
        if (p.Length == 0) return;
        _dirs.Add(p);
        // tạo các thư mục cha
        var parts = p.Split('/');
        for (int i = 1; i <= parts.Length; i++) _dirs.Add(string.Join("/", parts[..i]));
    }

    public void Move(string from, string to, bool overwrite)
    {
        var f = Normalize(from);
        if (ThrowOnMove || FailOnMovePaths.Contains(f))
            throw new IOException(MoveExceptionMessage ?? "Lỗi giả lập");
        var t = Normalize(to);
        // Kiểm tra trước khi gỡ nguồn: một lần Move thất bại không được làm mất tệp (giống hệ thống tệp thật).
        if (!_files.ContainsKey(f)) throw new FileNotFoundException("Tệp không tồn tại", from);
        if (_files.ContainsKey(t) && !overwrite) throw new IOException("Tệp đích đã tồn tại");
        _files.TryRemove(f, out var content);
        _files[t] = content!;
        // Di chuyển giữ nguyên ngày giờ và thuộc tính (như hệ thống tệp thật); đích cũ bị ghi đè mất giá trị của nó.
        if (_times.TryRemove(f, out var times)) _times[t] = times; else _times.TryRemove(t, out _);
        if (_attributes.TryRemove(f, out var attrs)) _attributes[t] = attrs; else _attributes.TryRemove(t, out _);
        EnsureDirectory(ParentDirectory(t));
    }

    public string[] GetFiles(string directory, string pattern, bool recursive)
    {
        var prefix = Normalize(directory);
        if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += "/";
        return _files.Keys
            .Where(k => k.StartsWith(prefix))
            .Where(k => recursive || !k[prefix.Length..].Contains('/'))
            .Where(k => MatchesPattern(System.IO.Path.GetFileName(k), pattern))
            .ToArray();
    }

    public string[] GetDirectories(string directory, bool recursive)
    {
        var prefix = Normalize(directory);
        if (prefix.Length > 0 && !prefix.EndsWith('/')) prefix += "/";
        return _dirs
            .Where(d => d.Length > 0 && d != prefix && d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Where(d => recursive || IsImmediateChild(d, prefix))
            .ToArray();
    }

    private static bool IsImmediateChild(string path, string prefix)
    {
        if (prefix.Length == 0)
        {
            var trimmed = path.TrimStart('/');
            return trimmed.Length > 0 && !trimmed.Contains('/');
        }
        return path.Length > prefix.Length && !path[prefix.Length..].Contains('/');
    }

    public string ReadAllText(string path) =>
        _files.TryGetValue(Normalize(path), out var bytes) ? System.Text.Encoding.UTF8.GetString(bytes)
        : throw new FileNotFoundException("Tệp không tồn tại", path);

    public void WriteAllText(string path, string content)
    {
        var p = Normalize(path);
        EnsureDirectory(ParentDirectory(p));
        _files[p] = System.Text.Encoding.UTF8.GetBytes(content);
    }

    public void AppendAllText(string path, string content)
    {
        var p = Normalize(path);
        EnsureDirectory(ParentDirectory(p));
        var existing = _files.TryGetValue(p, out var bytes) ? System.Text.Encoding.UTF8.GetString(bytes) : string.Empty;
        _files[p] = System.Text.Encoding.UTF8.GetBytes(existing + content);
    }

    public void Delete(string path, bool recursive)
    {
        var p = Normalize(path);
        var removedFile = _files.TryRemove(p, out _);
        var removedDir = _dirs.Remove(p);
        if (!removedFile && !removedDir)
            throw new FileNotFoundException("Tệp không tồn tại", path);

        if (recursive)
        {
            var prefix = p.EndsWith('/') ? p : p + "/";
            foreach (var key in _files.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _files.TryRemove(key, out _);
            }
            foreach (var dir in _dirs.Where(d => d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                _dirs.Remove(dir);
            }
        }
    }

    public long GetLength(string path) => _files.TryGetValue(Normalize(path), out var b) ? b.Length : 0;
    public DateTime GetCreationTimeUtc(string path) => System.DateTime.UtcNow;
    public DateTime GetLastWriteTimeUtc(string path) => System.DateTime.UtcNow;

    private readonly ConcurrentDictionary<string, FileTimes> _times = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, FileAttributes> _attributes = new(StringComparer.OrdinalIgnoreCase);

    public void Copy(string from, string to, bool overwrite)
    {
        var f = Normalize(from);
        var t = Normalize(to);
        if (!_files.TryGetValue(f, out var content)) throw new FileNotFoundException("Tệp không tồn tại", from);
        if (_files.ContainsKey(t) && !overwrite) throw new IOException("Tệp đích đã tồn tại");
        _files[t] = (byte[])content.Clone();
        if (_times.TryGetValue(f, out var times)) _times[t] = times; else _times.TryRemove(t, out _);
        if (_attributes.TryGetValue(f, out var attrs)) _attributes[t] = attrs; else _attributes.TryRemove(t, out _);
        EnsureDirectory(ParentDirectory(t));
    }

    public FileTimes GetTimes(string path)
    {
        var p = Normalize(path);
        if (!_files.ContainsKey(p) && !DirectoryExists(path)) throw new FileNotFoundException("Tệp không tồn tại", path);
        var now = System.DateTime.UtcNow;
        return _times.GetOrAdd(p, _ => FileTimes.From(now, now, now));
    }

    public void SetTimes(string path, FileTimes times)
    {
        var p = Normalize(path);
        if (!_files.ContainsKey(p) && !DirectoryExists(path)) throw new FileNotFoundException("Tệp không tồn tại", path);
        _times[p] = times;
    }

    public FileAttributes GetAttributes(string path)
    {
        var p = Normalize(path);
        if (!_files.ContainsKey(p) && !DirectoryExists(path)) throw new FileNotFoundException("Tệp không tồn tại", path);
        return _attributes.TryGetValue(p, out var a) ? a : FileAttributes.Normal;
    }

    public void SetAttributes(string path, FileAttributes attributes)
    {
        var p = Normalize(path);
        if (!_files.ContainsKey(p) && !DirectoryExists(path)) throw new FileNotFoundException("Tệp không tồn tại", path);
        _attributes[p] = attributes;
    }

    private readonly HashSet<string> _links = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Đánh dấu một đường dẫn là symlink/junction (cho test).</summary>
    public void MarkLink(string path) => _links.Add(Normalize(path));

    public bool IsLink(string path) => _links.Contains(Normalize(path));

    public Stream OpenRead(string path) =>
        _files.TryGetValue(Normalize(path), out var bytes)
            ? new MemoryStream(bytes, writable: false)
            : throw new FileNotFoundException("Tệp không tồn tại", path);

    public Stream OpenWrite(string path)
    {
        var p = Normalize(path);
        EnsureDirectory(ParentDirectory(p));
        _files[p] = Array.Empty<byte>();
        return new CommitStream(bytes => _files[p] = bytes);
    }

    /// <summary>Luồng ghi trong bộ nhớ, ghi kết quả vào hệ thống tệp giả khi đóng.</summary>
    private sealed class CommitStream : MemoryStream
    {
        private readonly Action<byte[]> _commit;
        private bool _done;

        public CommitStream(Action<byte[]> commit) { _commit = commit; }

        protected override void Dispose(bool disposing)
        {
            if (!_done) { _done = true; _commit(ToArray()); }
            base.Dispose(disposing);
        }
    }

    /// <summary>Ghi nội dung nhị phân (cho test).</summary>
    public void WriteAllBytes(string path, byte[] content)
    {
        var p = Normalize(path);
        EnsureDirectory(ParentDirectory(p));
        _files[p] = (byte[])content.Clone();
    }

    public bool IsDirectoryEmpty(string path)
    {
        var p = Normalize(path);
        if (!DirectoryExists(path)) return false;
        var prefix = p + "/";
        return !_files.Keys.Any(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            && !_dirs.Any(d => d.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    static string Normalize(string p) => p.Replace('\\', '/').TrimEnd('/');

    static string ParentDirectory(string p)
    {
        var np = Normalize(p);
        var lastSlash = np.LastIndexOf('/');
        return lastSlash <= 0 ? string.Empty : np[..lastSlash];
    }

    static bool MatchesPattern(string fileName, string pattern)
    {
        // hỗ trợ đơn giản *.ext
        if (pattern == "*") return true;
        if (pattern.StartsWith("*."))
        {
            var ext = pattern[1..];
            return fileName.EndsWith(ext, System.StringComparison.OrdinalIgnoreCase);
        }
        return fileName.Equals(pattern, System.StringComparison.OrdinalIgnoreCase);
    }
}
