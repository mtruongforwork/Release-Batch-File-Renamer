namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>
/// Hệ thống tệp "ảo" phủ lên một hệ thống tệp nền: mọi thay đổi chỉ ghi vào bộ nhớ, đĩa thật không bị chạm.
/// Dùng cho Workflow: chạy thử các bước liên tiếp để bước sau thấy kết quả của bước trước (xem trước tổng hợp)
/// bằng đúng <see cref="OperationExecutor"/> sẽ chạy thật. Nội dung tệp chỉ đọc từ nền khi cần.
/// </summary>
public sealed class OverlayFileSystem : IFileSystem
{
    private sealed record Entry(string Path, string? Origin, byte[]? Content, long Length, FileTimes Times, FileAttributes Attributes);

    private readonly IFileSystem _base;
    private readonly Dictionary<string, Entry?> _files = new(StringComparer.OrdinalIgnoreCase); // null = đã xoá
    private readonly Dictionary<string, string> _dirs = new(StringComparer.OrdinalIgnoreCase);  // thư mục đã tạo (canon -> tên hiển thị)
    private readonly HashSet<string> _removedDirs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FileTimes> _dirTimes = new(StringComparer.OrdinalIgnoreCase);

    public OverlayFileSystem(IFileSystem baseFs)
    {
        _base = baseFs;
    }

    private static string Canon(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static string Parent(string canon)
    {
        var i = canon.LastIndexOf('/');
        return i <= 0 ? string.Empty : canon[..i];
    }

    private static string ParentDisplay(string path)
    {
        var t = path.TrimEnd('\\', '/');
        var i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
        return i <= 0 ? string.Empty : t[..i];
    }

    private Entry? Get(string path)
    {
        var c = Canon(path);
        if (_files.TryGetValue(c, out var e)) return e;
        if (!_base.FileExists(path)) return null;
        return new Entry(path, path, null, _base.GetLength(path), _base.GetTimes(path), _base.GetAttributes(path));
    }

    private Entry GetOrThrow(string path) =>
        Get(path) ?? throw new FileNotFoundException("Tệp không tồn tại", path);

    // ---------- tồn tại ----------

    public bool FileExists(string path) => Get(path) is not null;

    public bool DirectoryExists(string path)
    {
        var c = Canon(path);
        if (c.Length == 0) return true;
        if (HasOverlayFileUnder(c)) return true;
        if (_dirs.ContainsKey(c)) return true;
        if (_removedDirs.Contains(c)) return false;
        return _base.DirectoryExists(path);
    }

    private bool HasOverlayFileUnder(string canonDir)
    {
        var prefix = canonDir + "/";
        foreach (var (key, entry) in _files)
        {
            if (entry is not null && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public bool IsDirectoryEmpty(string path) =>
        DirectoryExists(path) && GetFiles(path, "*", false).Length == 0 && GetDirectories(path, false).Length == 0;

    public bool IsLink(string path) => !_files.ContainsKey(Canon(path)) && _base.IsLink(path);

    // ---------- liệt kê ----------

    public string[] GetFiles(string directory, string pattern, bool recursive)
    {
        var dirCanon = Canon(directory);
        var prefix = dirCanon + "/";
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (_base.DirectoryExists(directory) && !_removedDirs.Contains(dirCanon))
        {
            foreach (var f in _base.GetFiles(directory, "*", recursive))
            {
                var c = Canon(f);
                if (!_files.ContainsKey(c)) result[c] = f;
            }
        }

        foreach (var (key, entry) in _files)
        {
            if (entry is null || !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            if (!recursive && key.IndexOf('/', prefix.Length) >= 0) continue;
            result[key] = entry.Path;
        }

        return result.Values.Where(p => MatchesPattern(LastSegment(p), pattern)).ToArray();
    }

    public string[] GetDirectories(string directory, bool recursive)
    {
        var dirCanon = Canon(directory);
        var prefix = dirCanon + "/";
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string canon, string display)
        {
            if (!canon.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return;
            if (!recursive && canon.IndexOf('/', prefix.Length) >= 0) return;
            if (_removedDirs.Contains(canon) && !_dirs.ContainsKey(canon) && !HasOverlayFileUnder(canon)) return;
            result[canon] = display;
        }

        if (_base.DirectoryExists(directory))
        {
            foreach (var d in _base.GetDirectories(directory, recursive)) Add(Canon(d), d);
        }

        foreach (var (canon, display) in _dirs) Add(canon, display);

        // Thư mục cha ngầm của các tệp trong lớp phủ.
        foreach (var (key, entry) in _files)
        {
            if (entry is null) continue;
            var parent = Parent(key);
            var display = ParentDisplay(entry.Path);
            while (parent.Length > 0 && parent.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                Add(parent, display);
                parent = Parent(parent);
                display = ParentDisplay(display);
            }
        }

        return result.Values.ToArray();
    }

    private static string LastSegment(string path)
    {
        var t = path.TrimEnd('\\', '/');
        var i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
        return i < 0 ? t : t[(i + 1)..];
    }

    private static bool MatchesPattern(string name, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || pattern == "*" || pattern == "*.*") return true;
        // Khớp ký tự đại diện * và ? theo vòng lặp (không dùng regex).
        int n = 0, p = 0, star = -1, mark = 0;
        while (n < name.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || char.ToUpperInvariant(pattern[p]) == char.ToUpperInvariant(name[n]))) { n++; p++; }
            else if (p < pattern.Length && pattern[p] == '*') { star = p++; mark = n; }
            else if (star >= 0) { p = star + 1; n = ++mark; }
            else return false;
        }
        while (p < pattern.Length && pattern[p] == '*') p++;
        return p == pattern.Length;
    }

    // ---------- thay đổi ----------

    public void EnsureDirectory(string path)
    {
        var display = path.TrimEnd('\\', '/');
        while (display.Length > 0)
        {
            var c = Canon(display);
            if (c.Length == 0) break;
            _removedDirs.Remove(c);
            if (!_dirs.ContainsKey(c)) _dirs[c] = display;
            var parent = ParentDisplay(display);
            if (parent == display) break;
            display = parent;
        }
    }

    public void Move(string from, string to, bool overwrite)
    {
        var source = GetOrThrow(from);
        // Đổi chữ hoa/thường của chính tệp đó không phải xung đột (hệ thống tệp thật cho phép).
        if (!string.Equals(Canon(from), Canon(to), StringComparison.OrdinalIgnoreCase) && FileExists(to) && !overwrite) throw new IOException("Tệp đích đã tồn tại");

        _files[Canon(from)] = null;
        _files[Canon(to)] = source with { Path = to };
        EnsureDirectory(ParentDisplay(to));
    }

    public void Copy(string from, string to, bool overwrite)
    {
        var source = GetOrThrow(from);
        if (FileExists(to) && !overwrite) throw new IOException("Tệp đích đã tồn tại");

        _files[Canon(to)] = source with { Path = to };
        EnsureDirectory(ParentDisplay(to));
    }

    public void Delete(string path, bool recursive)
    {
        var c = Canon(path);
        if (Get(path) is not null)
        {
            _files[c] = null;
            return;
        }

        if (!DirectoryExists(path)) throw new DirectoryNotFoundException(path);

        if (!recursive)
        {
            if (!IsDirectoryEmpty(path)) throw new IOException("Thư mục không rỗng");
        }
        else
        {
            foreach (var f in GetFiles(path, "*", true)) _files[Canon(f)] = null;
            foreach (var d in GetDirectories(path, true)) { _dirs.Remove(Canon(d)); _removedDirs.Add(Canon(d)); }
        }

        _dirs.Remove(c);
        _removedDirs.Add(c);
    }

    // ---------- nội dung và thuộc tính ----------

    public string ReadAllText(string path)
    {
        using var stream = OpenRead(path);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    public void WriteAllText(string path, string content) => WriteAllBytes(path, System.Text.Encoding.UTF8.GetBytes(content));

    public void AppendAllText(string path, string content)
    {
        var existing = Get(path) is null ? string.Empty : ReadAllText(path);
        WriteAllText(path, existing + content);
    }

    public void WriteAllBytes(string path, byte[] content)
    {
        var now = DateTime.UtcNow;
        var times = Get(path)?.Times ?? FileTimes.From(now, now, now);
        _files[Canon(path)] = new Entry(path, null, (byte[])content.Clone(), content.Length, times, FileAttributes.Normal);
        EnsureDirectory(ParentDisplay(path));
    }

    public Stream OpenRead(string path)
    {
        var e = GetOrThrow(path);
        if (e.Content is not null) return new MemoryStream(e.Content, writable: false);
        return _base.OpenRead(e.Origin ?? path);
    }

    public Stream OpenWrite(string path) => new CommitStream(bytes => WriteAllBytes(path, bytes));

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

    public long GetLength(string path) => GetOrThrow(path).Length;

    public DateTime GetCreationTimeUtc(string path) => GetTimes(path).CreatedUtc;

    public DateTime GetLastWriteTimeUtc(string path) => GetTimes(path).ModifiedUtc;

    public FileTimes GetTimes(string path)
    {
        var e = Get(path);
        if (e is not null) return e.Times;
        var c = Canon(path);
        if (_dirTimes.TryGetValue(c, out var t)) return t;
        if (DirectoryExists(path) && !_dirs.ContainsKey(c) && _base.DirectoryExists(path)) return _base.GetTimes(path);
        if (DirectoryExists(path))
        {
            var now = DateTime.UtcNow;
            return FileTimes.From(now, now, now);
        }
        throw new FileNotFoundException("Không tồn tại", path);
    }

    public void SetTimes(string path, FileTimes times)
    {
        var e = Get(path);
        if (e is not null) { _files[Canon(path)] = e with { Times = times }; return; }
        if (!DirectoryExists(path)) throw new FileNotFoundException("Không tồn tại", path);
        _dirTimes[Canon(path)] = times;
    }

    public FileAttributes GetAttributes(string path)
    {
        var e = Get(path);
        if (e is not null) return e.Attributes;
        if (DirectoryExists(path)) return FileAttributes.Directory;
        throw new FileNotFoundException("Không tồn tại", path);
    }

    public void SetAttributes(string path, FileAttributes attributes)
    {
        var e = GetOrThrow(path);
        _files[Canon(path)] = e with { Attributes = attributes };
    }
}
