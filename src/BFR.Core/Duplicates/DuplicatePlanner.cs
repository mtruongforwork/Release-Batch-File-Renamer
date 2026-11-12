namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>Quy tắc tự chọn bản giữ lại trong một nhóm trùng. Người dùng vẫn sửa tay từng nhóm.</summary>
public enum KeepRule
{
    Newest,
    Oldest,
    ShortestPath,
    PreferFolder,
    CleanName
}

public enum DuplicateAction
{
    /// <summary>Đưa bản thừa vào khu cách ly (mặc định, hoàn tác được).</summary>
    Quarantine,

    /// <summary>Chuyển bản thừa sang một thư mục khác.</summary>
    MoveToFolder
}

public sealed record DuplicatePlan(IReadOnlyList<Operation> Operations, IReadOnlyList<FolderPlanSkip> Skipped, long BytesFreed);

/// <summary>
/// Chọn bản giữ lại và lập kế hoạch xử lý bản thừa. BẤT BIẾN: mỗi nhóm luôn còn ít nhất 1 tệp nguyên vẹn tại chỗ.
/// Không bao giờ xoá vĩnh viễn; tệp ở vùng cấm không bị đụng tới.
/// </summary>
public static class DuplicatePlanner
{
    private static readonly Regex CopyMarker = new(@"(\(\d+\)|\s-\sCopy|\scopy|\sbản sao)", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexLimits.Timeout);

    /// <summary>Chọn một tệp giữ lại theo quy tắc. Kết quả ổn định (hoà thì lấy theo thứ tự đường dẫn).</summary>
    public static FileItem Choose(DuplicateGroup group, KeepRule rule, IReadOnlyList<string>? preferredFolders = null)
    {
        var ordered = group.Files.OrderBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase).ToList();

        return rule switch
        {
            KeepRule.Newest => ordered.OrderByDescending(f => f.ModifiedUtc).First(),
            KeepRule.Oldest => ordered.OrderBy(f => f.ModifiedUtc).First(),
            KeepRule.ShortestPath => ordered.OrderBy(f => f.FullPath.Length).First(),
            KeepRule.PreferFolder => ordered.OrderBy(f => PreferredRank(f, preferredFolders)).First(),
            _ => ordered.OrderBy(f => CopyMarker.IsMatch(f.Name) ? 1 : 0).ThenBy(f => f.FullPath.Length).First()
        };
    }

    private static int PreferredRank(FileItem f, IReadOnlyList<string>? folders)
    {
        if (folders is null) return int.MaxValue;
        for (var i = 0; i < folders.Count; i++)
        {
            var folder = folders[i].Replace('\\', '/').TrimEnd('/') + "/";
            if (f.FullPath.Replace('\\', '/').StartsWith(folder, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return int.MaxValue;
    }

    /// <param name="keepers">Bản giữ lại cho từng nhóm (cùng chỉ số với <paramref name="groups"/>). Nhóm không hợp lệ bị bỏ qua.</param>
    /// <param name="moveFolder">Thư mục đích khi chọn <see cref="DuplicateAction.MoveToFolder"/>.</param>
    public static DuplicatePlan Plan(
        IReadOnlyList<DuplicateGroup> groups,
        IReadOnlyList<FileItem> keepers,
        DuplicateAction action,
        IFileSystem fs,
        string? moveFolder = null,
        ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var ops = new List<Operation>();
        var skipped = new List<FolderPlanSkip>();
        long freed = 0;
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (action == DuplicateAction.MoveToFolder && (string.IsNullOrWhiteSpace(moveFolder) || zones.IsProtected(moveFolder)))
        {
            skipped.Add(new FolderPlanSkip(moveFolder ?? string.Empty, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
            return new DuplicatePlan(ops, skipped, 0);
        }

        for (var i = 0; i < groups.Count; i++)
        {
            var group = groups[i];
            var keeper = i < keepers.Count ? keepers[i] : null;

            // Bản giữ lại phải thuộc nhóm; nếu không, bỏ qua cả nhóm (không xoá gì).
            if (keeper is null || !group.Files.Any(f => string.Equals(f.FullPath, keeper.FullPath, StringComparison.OrdinalIgnoreCase)))
            {
                skipped.Add(new FolderPlanSkip(group.Files[0].FullPath, CoreText.Get("CoreDupNoKeeper", "Nhóm chưa có bản giữ lại hợp lệ")));
                continue;
            }

            foreach (var file in group.Files)
            {
                if (string.Equals(file.FullPath, keeper.FullPath, StringComparison.OrdinalIgnoreCase)) continue;

                if (zones.IsProtected(file.FullPath))
                {
                    skipped.Add(new FolderPlanSkip(file.FullPath, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                    continue;
                }

                if (action == DuplicateAction.Quarantine)
                {
                    ops.Add(Operation.Quarantine(file.FullPath));
                }
                else
                {
                    ops.Add(Operation.Move(file.FullPath, UniqueTarget(moveFolder!, file, fs, taken)));
                }
                freed += file.Size;
            }
        }

        return new DuplicatePlan(ops, skipped, freed);
    }

    private static string UniqueTarget(string folder, FileItem file, IFileSystem fs, HashSet<string> taken)
    {
        var sep = folder.Contains('\\') || folder.Contains(':') ? '\\' : '/';
        var dir = folder.TrimEnd('\\', '/');
        var name = file.Extension.Length == 0 ? file.Name : file.Name + "." + file.Extension;
        var ext = file.Extension.Length == 0 ? string.Empty : "." + file.Extension;

        var candidate = dir + sep + name;
        for (var n = 2; taken.Contains(candidate) || fs.FileExists(candidate); n++)
        {
            candidate = $"{dir}{sep}{file.Name} ({n}){ext}";
        }
        taken.Add(candidate);
        return candidate;
    }
}
