namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

/// <summary>Một mục bị bỏ qua khi lập kế hoạch, kèm lý do để hiển thị.</summary>
public sealed record FolderPlanSkip(string Path, string Reason);

/// <summary>Kế hoạch của một công cụ thư mục: danh sách <see cref="Operation"/> và những gì bị bỏ qua.</summary>
public sealed record FolderPlan(IReadOnlyList<Operation> Operations, IReadOnlyList<FolderPlanSkip> Skipped);

public sealed record FlattenOptions(bool PrefixPath = false, string Separator = "_", bool RemoveEmptiedFolders = true);

public sealed record RemoveEmptyOptions(bool TreatJunkAsEmpty = true);

public sealed record WrapOptions(bool GroupSameName = true);

/// <summary>
/// Công cụ thư mục (M7): làm phẳng, xoá thư mục rỗng, bọc tệp vào thư mục, tạo cây thư mục.
/// Thuần tuý: chỉ đọc qua <see cref="IFileSystem"/>, không bao giờ ghi. "Xoá" luôn là đưa vào khu cách ly
/// hoặc xoá thư mục RỖNG (hoàn tác tạo lại). Không đi vào symlink/junction và vùng cấm (C10).
/// </summary>
public static class FolderToolsPlanner
{
    private static readonly HashSet<string> JunkNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db", "desktop.ini", ".DS_Store"
    };

    public const int MaxTreeFolders = 5000;
    public const int MaxTreeDepth = 32;

    private sealed class Node
    {
        public string Path = string.Empty;
        public List<string> Files = new();
        public List<Node> Children = new();
        public bool Unreadable;
    }

    private static Node Walk(IFileSystem fs, string root, ProtectedZones zones, List<FolderPlanSkip> skipped)
    {
        var top = new Node { Path = root };
        var stack = new Stack<Node>();
        stack.Push(top);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            try
            {
                node.Files.AddRange(fs.GetFiles(node.Path, "*", false).OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
                foreach (var dir in fs.GetDirectories(node.Path, false).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    if (fs.IsLink(dir))
                    {
                        node.Unreadable = true;
                        skipped.Add(new FolderPlanSkip(dir, CoreText.Get("CoreFolderLinkSkipped", "Liên kết thư mục không được đi vào")));
                        continue;
                    }
                    if (zones.IsProtected(dir))
                    {
                        node.Unreadable = true;
                        skipped.Add(new FolderPlanSkip(dir, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                        continue;
                    }
                    var child = new Node { Path = dir };
                    node.Children.Add(child);
                    stack.Push(child);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                node.Unreadable = true;
                skipped.Add(new FolderPlanSkip(node.Path, ex.Message));
            }
        }
        return top;
    }

    private static string Name(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var i = Math.Max(trimmed.LastIndexOf('\\'), trimmed.LastIndexOf('/'));
        return i < 0 ? trimmed : trimmed[(i + 1)..];
    }

    private static string Join(string dir, string name)
    {
        var sep = dir.Contains('\\') || dir.Contains(':') ? '\\' : '/';
        return dir.TrimEnd('\\', '/') + sep + name;
    }

    private static string Canon(string p) => p.Replace('\\', '/').TrimEnd('/');

    /// <summary>Đưa mọi tệp trong cây thư mục lên thư mục gốc; tên trùng được thêm " (n)".</summary>
    public static FolderPlan Flatten(string root, FlattenOptions options, IFileSystem fs, ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var skipped = new List<FolderPlanSkip>();
        var ops = new List<Operation>();
        if (zones.IsProtected(root)) return Blocked(root);

        var top = Walk(fs, root, zones, skipped);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in top.Files) taken.Add(Name(f));
        foreach (var d in top.Children) taken.Add(Name(d.Path)); // tên thư mục con cũng chiếm chỗ cho tới khi gỡ bỏ

        // Duyệt theo thứ tự ổn định (cha trước con) để hậu tố " (n)" xác định.
        var retained = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var order = new List<(Node node, List<string> rel)>();
        void Collect(Node n, List<string> rel)
        {
            order.Add((n, rel));
            foreach (var c in n.Children) Collect(c, new List<string>(rel) { Name(c.Path) });
        }
        foreach (var c in top.Children) Collect(c, new List<string> { Name(c.Path) });

        foreach (var (node, rel) in order)
        {
            var keep = node.Unreadable;
            foreach (var file in node.Files)
            {
                if (zones.IsProtected(file))
                {
                    keep = true;
                    skipped.Add(new FolderPlanSkip(file, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                    continue;
                }

                var baseName = Name(file);
                var name = options.PrefixPath
                    ? string.Join(options.Separator, rel.Select(OrganizeTemplate.SanitizeSegment).Where(s => s.Length > 0).Append(baseName))
                    : baseName;
                name = Unique(name, taken, fs, root);
                taken.Add(name);
                ops.Add(Operation.Move(file, Join(root, name)));
            }
            retained[Canon(node.Path)] = keep;
        }

        if (options.RemoveEmptiedFolders)
        {
            // Sâu nhất trước; giữ lại thư mục còn tệp không di chuyển được hoặc có con bị giữ.
            foreach (var (node, _) in order.AsEnumerable().Reverse())
            {
                var keep = retained[Canon(node.Path)] || node.Children.Any(c => retained[Canon(c.Path)]);
                retained[Canon(node.Path)] = keep;
                if (!keep) ops.Add(Operation.RemoveDir(node.Path));
            }
        }

        return new FolderPlan(ops, skipped);
    }

    private static string Unique(string name, HashSet<string> taken, IFileSystem fs, string root)
    {
        bool Free(string n) => !taken.Contains(n) && !fs.FileExists(Join(root, n));
        if (Free(name)) return name;

        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var n = 2; ; n++)
        {
            var candidate = $"{stem} ({n}){ext}";
            if (Free(candidate)) return candidate;
        }
    }

    /// <summary>Tìm thư mục rỗng (hoặc chỉ chứa tệp rác như Thumbs.db) và lập kế hoạch đưa vào khu cách ly. Không bao giờ xoá thư mục gốc.</summary>
    public static FolderPlan RemoveEmpty(string root, RemoveEmptyOptions options, IFileSystem fs, ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var skipped = new List<FolderPlanSkip>();
        var ops = new List<Operation>();
        if (zones.IsProtected(root)) return Blocked(root);

        var top = Walk(fs, root, zones, skipped);
        var removable = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        // Hậu thứ tự: con trước cha.
        var post = new List<Node>();
        void Visit(Node n)
        {
            foreach (var c in n.Children) Visit(c);
            post.Add(n);
        }
        Visit(top);

        foreach (var node in post)
        {
            var empty = !node.Unreadable
                && node.Children.All(c => removable[Canon(c.Path)])
                && node.Files.All(f => options.TreatJunkAsEmpty && JunkNames.Contains(Name(f)));
            removable[Canon(node.Path)] = empty;
            if (!empty || ReferenceEquals(node, top)) continue;

            foreach (var junk in node.Files) ops.Add(Operation.Quarantine(junk));
            ops.Add(Operation.RemoveDir(node.Path));
        }

        return new FolderPlan(ops, skipped);
    }

    /// <summary>Mỗi tệp vào một thư mục cùng tên (hoặc nhóm các tệp cùng tên khác đuôi vào chung một thư mục).</summary>
    public static FolderPlan Wrap(IReadOnlyList<FileItem> files, WrapOptions options, IFileSystem fs, ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var skipped = new List<FolderPlanSkip>();
        var ops = new List<Operation>();

        var stemCounts = files.Where(f => !f.IsDirectory)
            .GroupBy(f => Canon(f.Directory).ToLowerInvariant() + "\0" + f.Name.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.Count());

        foreach (var file in files)
        {
            if (file.IsDirectory) continue;
            if (zones.IsProtected(file.FullPath))
            {
                skipped.Add(new FolderPlanSkip(file.FullPath, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                continue;
            }

            var key = Canon(file.Directory).ToLowerInvariant() + "\0" + file.Name.ToLowerInvariant();
            var folder = file.Name;
            if (!options.GroupSameName && stemCounts[key] > 1 && file.Extension.Length > 0)
            {
                folder = file.Name + "_" + file.Extension; // hai tệp cùng tên khác đuôi không được dồn chung khi chọn chế độ riêng lẻ
            }

            folder = OrganizeTemplate.SanitizeSegment(folder);
            if (folder.Length == 0)
            {
                skipped.Add(new FolderPlanSkip(file.FullPath, CoreText.Get("CoreOrganizeBadName", "Tên tệp không hợp lệ")));
                continue;
            }

            var dir = Join(file.Directory, folder);
            var fileName = file.Extension.Length == 0 ? file.Name : file.Name + "." + file.Extension;
            var target = Join(dir, fileName);

            if (fs.FileExists(dir) || fs.FileExists(target))
            {
                skipped.Add(new FolderPlanSkip(file.FullPath, CoreText.Get("CoreOrganizeExists", "Đã có tệp trùng tên ở đích")));
                continue;
            }

            ops.Add(Operation.Move(file.FullPath, target));
        }

        return new FolderPlan(ops, skipped);
    }

    /// <summary>
    /// Dựng cây thư mục từ văn bản dán vào: danh sách đường dẫn (<c>2024\Q1</c>) hoặc danh sách thụt lề (tab hoặc dấu cách).
    /// Mọi cấp đều được làm sạch nên không thể thoát khỏi <paramref name="root"/>. Giới hạn số thư mục và độ sâu.
    /// </summary>
    public static FolderPlan BuildTree(string root, string text, IFileSystem fs, ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var skipped = new List<FolderPlanSkip>();
        if (zones.IsProtected(root)) return Blocked(root);

        var lines = (text ?? string.Empty).Replace("\r", string.Empty).Split('\n')
            .Where(l => l.Trim().Length > 0).ToList();

        var paths = new List<string[]>();
        var pathMode = lines.Any(l => l.Contains('\\') || l.Contains('/'));
        if (pathMode)
        {
            foreach (var l in lines)
            {
                var parts = l.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(OrganizeTemplate.SanitizeSegment).Where(s => s.Length > 0).ToArray();
                if (parts.Length > 0) paths.Add(parts);
            }
        }
        else
        {
            var stack = new List<(int indent, string name)>();
            foreach (var l in lines)
            {
                var indent = 0;
                while (indent < l.Length && (l[indent] == ' ' || l[indent] == '\t')) indent += l[indent] == '\t' ? 4 : 1;

                var name = OrganizeTemplate.SanitizeSegment(l.Trim());
                if (name.Length == 0) continue;

                while (stack.Count > 0 && stack[^1].indent >= indent) stack.RemoveAt(stack.Count - 1);
                stack.Add((indent, name));
                paths.Add(stack.Select(s => s.name).ToArray());
            }
        }

        var ops = new List<Operation>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        foreach (var parts in paths)
        {
            if (parts.Length > MaxTreeDepth)
            {
                skipped.Add(new FolderPlanSkip(string.Join("\\", parts), CoreText.Get("CoreFolderTreeTooDeep", "Cây thư mục quá sâu")));
                continue;
            }

            var current = root;
            foreach (var part in parts)
            {
                current = Join(current, part);
                if (!seen.Add(Canon(current))) continue;
                if (fs.DirectoryExists(current)) continue;
                if (fs.FileExists(current))
                {
                    skipped.Add(new FolderPlanSkip(current, CoreText.Get("CoreOrganizeExists", "Đã có tệp trùng tên ở đích")));
                    break;
                }
                if (++count > MaxTreeFolders)
                {
                    skipped.Add(new FolderPlanSkip(current, CoreText.Get("CoreFolderTreeTooMany", "Quá nhiều thư mục (tối đa {0})", MaxTreeFolders)));
                    return new FolderPlan(ops, skipped);
                }
                ops.Add(Operation.CreateDir(current));
            }
        }

        return new FolderPlan(ops, skipped);
    }

    private static FolderPlan Blocked(string root) =>
        new(Array.Empty<Operation>(), new[] { new FolderPlanSkip(root, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")) });
}
