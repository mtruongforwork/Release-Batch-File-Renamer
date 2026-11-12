namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Đặt/bỏ thuộc tính tệp (M4): chỉ đọc, ẩn, lưu trữ. Thuộc tính hệ thống và các thuộc tính khác không bao giờ bị đổi.
/// </summary>
public static class AttributePlanner
{
    public const FileAttributes Allowed = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.Archive;

    public static FolderPlan Plan(
        IReadOnlyList<FileItem> files, FileAttributes set, FileAttributes clear, IFileSystem fs, ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        set &= Allowed;
        clear &= Allowed & ~set; // nếu vừa đặt vừa bỏ thì đặt thắng

        var ops = new List<Operation>();
        var skipped = new List<FolderPlanSkip>();

        foreach (var file in files)
        {
            if (file.IsDirectory) continue;

            if (zones.IsProtected(file.FullPath))
            {
                skipped.Add(new FolderPlanSkip(file.FullPath, CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")));
                continue;
            }

            try
            {
                var current = fs.GetAttributes(file.FullPath);
                var next = (current | set) & ~clear;
                // "Normal" chỉ hợp lệ khi đứng một mình.
                next &= ~FileAttributes.Normal;
                if (next == 0) next = FileAttributes.Normal;
                if (next != current) ops.Add(Operation.SetAttributes(file.FullPath, next));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped.Add(new FolderPlanSkip(file.FullPath, ex.Message));
            }
        }

        return new FolderPlan(ops, skipped);
    }
}
