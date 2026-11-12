namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public enum OrganizeMode
{
    Move,
    Copy
}

/// <summary>Cách xử lý khi tên tệp ở đích đã bị chiếm.</summary>
public enum OrganizeConflictPolicy
{
    /// <summary>Thêm hậu tố " (2)", " (3)"... trước phần mở rộng.</summary>
    Rename,

    /// <summary>Bỏ qua tệp đó.</summary>
    Skip
}

public enum OrganizeStatus
{
    /// <summary>Sẽ được xử lý.</summary>
    Ready,

    /// <summary>Đã nằm đúng chỗ.</summary>
    Unchanged,

    /// <summary>Bỏ qua (trùng tên và chọn bỏ qua, hoặc là thư mục).</summary>
    Skipped,

    /// <summary>Mẫu lỗi hoặc đường dẫn đích không hợp lệ.</summary>
    Invalid,

    /// <summary>Nguồn hoặc đích nằm trong vùng cấm (C10).</summary>
    Protected
}

/// <param name="Root">Thư mục gốc đích. Mọi tệp chỉ được đưa vào bên trong thư mục này.</param>
/// <param name="Template">Mẫu cấp thư mục, ví dụ <c>{exif.year}\{exif.month:00}</c>.</param>
/// <param name="MaxDepth">Số cấp tối đa của mẫu; 0 = không giới hạn (Free: 2).</param>
public sealed record OrganizeOptions(
    string Root,
    string Template,
    OrganizeMode Mode = OrganizeMode.Move,
    OrganizeConflictPolicy Conflict = OrganizeConflictPolicy.Rename,
    int MaxDepth = 0);

public sealed record OrganizeItem(
    FileItem File,
    OrganizeStatus Status,
    string TargetPath,
    string TargetDirectory,
    bool UsedFallbackFolder = false,
    bool CrossVolume = false,
    string? Message = null);

public sealed class OrganizePlan
{
    public OrganizePlan(IReadOnlyList<OrganizeItem> items, IReadOnlyList<Operation> operations, IReadOnlyList<string> templateErrors)
    {
        Items = items;
        Operations = operations;
        TemplateErrors = templateErrors;
    }

    public IReadOnlyList<OrganizeItem> Items { get; }

    /// <summary>Các thao tác Move/Copy cho <see cref="OperationExecutor"/>. Thư mục đích do executor tự tạo và hoàn tác tự dọn.</summary>
    public IReadOnlyList<Operation> Operations { get; }

    /// <summary>Lỗi của chính mẫu (token lạ, quá sâu...). Khác rỗng thì không có thao tác nào.</summary>
    public IReadOnlyList<string> TemplateErrors { get; }

    public bool HasBlockingIssues => TemplateErrors.Count > 0 || Items.Any(i => i.Status is OrganizeStatus.Invalid or OrganizeStatus.Protected);

    /// <summary>Số tệp sẽ vào từng thư mục đích (dùng dựng cây xem trước).</summary>
    public IReadOnlyDictionary<string, int> FolderCounts() =>
        Items.Where(i => i.Status == OrganizeStatus.Ready)
            .GroupBy(i => i.TargetDirectory, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Lập kế hoạch sắp xếp tệp vào thư mục dựng theo mẫu token (M2). Thuần tuý: chỉ hỏi <see cref="IFileSystem"/>
/// xem đích đã có tệp chưa, không ghi gì. Bất biến: mọi đích đều nằm bên trong thư mục gốc.
/// </summary>
public static class OrganizePlanner
{
    public static OrganizePlan Plan(
        IReadOnlyList<FileItem> files,
        OrganizeOptions options,
        RuleContext context,
        IFileSystem fs,
        ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var template = OrganizeTemplate.Parse(options.Template);

        var errors = new List<string>(template.Validate());
        if (template.Depth == 0)
        {
            errors.Add(CoreText.Get("CoreOrganizeEmptyTemplate", "Mẫu thư mục đang trống"));
        }
        if (options.MaxDepth > 0 && template.Depth > options.MaxDepth)
        {
            errors.Add(CoreText.Get("CoreOrganizeTooDeep", "Mẫu có {0} cấp, vượt giới hạn {1} cấp", template.Depth, options.MaxDepth));
        }

        var root = options.Root.TrimEnd('\\', '/');
        if (root.Length == 0 || zones.IsProtected(root))
        {
            errors.Add(CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ"));
        }

        if (errors.Count > 0)
        {
            var blocked = files.Select(f => new OrganizeItem(f, OrganizeStatus.Invalid, string.Empty, string.Empty, Message: errors[0])).ToList();
            return new OrganizePlan(blocked, Array.Empty<Operation>(), errors);
        }

        var unknownName = OrganizeTemplate.SanitizeSegment(CoreText.Get("CoreOrganizeUnknownFolder", "_Không xác định"));
        if (unknownName.Length == 0) unknownName = "_Unknown";

        // Tên đã bị chiếm trong lô này, theo thư mục đích (không phân biệt hoa thường như Windows).
        var taken = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var items = new List<OrganizeItem>(files.Count);
        var operations = new List<Operation>(files.Count);
        var rootVolume = Path.GetPathRoot(root) ?? string.Empty;

        foreach (var file in files)
        {
            if (file.IsDirectory)
            {
                items.Add(new OrganizeItem(file, OrganizeStatus.Skipped, string.Empty, string.Empty,
                    Message: CoreText.Get("CoreOrganizeFolderSkipped", "Thư mục không được sắp xếp")));
                continue;
            }

            var segments = template.Expand(file, context, out var missing);
            var fallback = missing || segments.Count == 0;
            var directory = fallback
                ? Join(root, new[] { unknownName })
                : Join(root, segments);
            var fileName = file.Extension.Length == 0 ? file.Name : file.Name + "." + file.Extension;

            if (new NameValidator().Validate(fileName) != NameIssue.None)
            {
                items.Add(new OrganizeItem(file, OrganizeStatus.Invalid, string.Empty, directory,
                    Message: CoreText.Get("CoreOrganizeBadName", "Tên tệp không hợp lệ")));
                continue;
            }

            if (zones.IsProtected(file.FullPath) || zones.IsProtected(directory))
            {
                items.Add(new OrganizeItem(file, OrganizeStatus.Protected, string.Empty, directory,
                    Message: CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                continue;
            }

            if (!InsideRoot(directory, root))
            {
                // Không thể xảy ra với mẫu đã làm sạch; giữ làm hàng rào cuối cùng.
                items.Add(new OrganizeItem(file, OrganizeStatus.Invalid, string.Empty, directory,
                    Message: CoreText.Get("CoreOrganizeOutsideRoot", "Đích nằm ngoài thư mục gốc")));
                continue;
            }

            var target = Join(directory, new[] { fileName });

            if (SamePath(target, file.FullPath))
            {
                items.Add(new OrganizeItem(file, OrganizeStatus.Unchanged, target, directory, fallback));
                MarkTaken(taken, directory, fileName);
                continue;
            }

            var set = TakenFor(taken, directory);
            bool Free(string name) => !set.Contains(name) && !fs.FileExists(Join(directory, new[] { name })) && !fs.DirectoryExists(Join(directory, new[] { name }));

            var finalName = fileName;
            if (!Free(finalName))
            {
                if (options.Conflict == OrganizeConflictPolicy.Skip)
                {
                    items.Add(new OrganizeItem(file, OrganizeStatus.Skipped, target, directory, fallback,
                        Message: CoreText.Get("CoreOrganizeExists", "Đã có tệp trùng tên ở đích")));
                    continue;
                }

                var stem = file.Name;
                var ext = file.Extension.Length == 0 ? string.Empty : "." + file.Extension;
                for (var n = 2; ; n++)
                {
                    var candidate = $"{stem} ({n}){ext}";
                    if (Free(candidate)) { finalName = candidate; break; }
                }
            }

            set.Add(finalName);
            target = Join(directory, new[] { finalName });

            var cross = !string.Equals(Path.GetPathRoot(file.FullPath) ?? string.Empty, rootVolume, StringComparison.OrdinalIgnoreCase);
            items.Add(new OrganizeItem(file, OrganizeStatus.Ready, target, directory, fallback, options.Mode == OrganizeMode.Move && cross));
            operations.Add(options.Mode == OrganizeMode.Copy
                ? Operation.Copy(file.FullPath, target)
                : Operation.Move(file.FullPath, target));
        }

        return new OrganizePlan(items, operations, errors);
    }

    private static HashSet<string> TakenFor(Dictionary<string, HashSet<string>> taken, string directory)
    {
        if (!taken.TryGetValue(directory, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            taken[directory] = set;
        }
        return set;
    }

    private static void MarkTaken(Dictionary<string, HashSet<string>> taken, string directory, string name) =>
        TakenFor(taken, directory).Add(name);

    private static string Join(string baseDir, IEnumerable<string> parts)
    {
        var sep = baseDir.Contains('\\') || baseDir.Contains(':') ? '\\' : '/';
        var sb = new System.Text.StringBuilder(baseDir.TrimEnd('\\', '/'));
        foreach (var p in parts) sb.Append(sep).Append(p);
        return sb.ToString();
    }

    private static string Canon(string path) => path.Replace('\\', '/').TrimEnd('/');

    private static bool SamePath(string a, string b) => string.Equals(Canon(a), Canon(b), StringComparison.OrdinalIgnoreCase);

    private static bool InsideRoot(string directory, string root)
    {
        var d = Canon(directory);
        var r = Canon(root);
        return d.Equals(r, StringComparison.OrdinalIgnoreCase) || d.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase);
    }
}
