namespace BFR.Core;

using System;
using System.IO;

/// <summary>
/// Đảo ngược và kiểm tra trạng thái của từng bước journal. Dùng chung cho Hoàn tác (<see cref="UndoService"/>)
/// và khôi phục sự cố (<see cref="RecoveryService"/>).
/// </summary>
internal static class OperationUndo
{
    /// <summary>
    /// Đảo ngược một bước đã thực hiện. Trả về null nếu thành công, ngược lại là thông báo lỗi (không ném lỗi).
    /// </summary>
    public static string? Undo(IFileSystem fs, JournalEntry e)
    {
        var error = UndoCore(fs, e);
        if (error is null && OperationExecutor.ParseKind(e) != OperationKind.CreateDir) RemoveCreatedParents(fs, e.Aux);
        return error;
    }

    /// <summary>Dọn các thư mục cha mà BFR đã tạo khi ghi đích, nếu chúng vẫn rỗng; dừng lặng lẽ ở thư mục đã có nội dung khác.</summary>
    internal static void RemoveCreatedParents(IFileSystem fs, string? chain)
    {
        if (string.IsNullOrEmpty(chain)) return;
        foreach (var dir in chain.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                if (!fs.DirectoryExists(dir)) continue;
                if (!fs.IsDirectoryEmpty(dir)) return;
                fs.Delete(dir, recursive: false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    private static string? UndoCore(IFileSystem fs, JournalEntry e)
    {
        var from = e.From ?? string.Empty;
        var to = e.To ?? string.Empty;

        try
        {
            switch (OperationExecutor.ParseKind(e))
            {
                case OperationKind.Move:
                case OperationKind.Quarantine:
                    if (!fs.FileExists(to)) return CoreText.Get("CoreUndoMissing", "Tệp tại vị trí mới không còn tồn tại");
                    // Đổi chữ hoa/thường: tên cũ "tồn tại" chỉ vì chính tệp này (hệ thống tệp không phân biệt hoa thường).
                    if (fs.FileExists(from) && !string.Equals(from.Replace('\\', '/'), to.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
                        return CoreText.Get("CoreUndoOldTaken", "Tên cũ đã bị chiếm");
                    EnsureParent(fs, from);
                    fs.Move(to, from, overwrite: false);
                    return null;

                case OperationKind.Copy:
                    if (!fs.FileExists(to)) return CoreText.Get("CoreUndoMissing", "Tệp tại vị trí mới không còn tồn tại");
                    fs.Delete(to, recursive: false);
                    return null;

                case OperationKind.CreateDir:
                    if (e.Aux == OperationExecutor.ExistedMarker) return null; // không phải do BFR tạo
                    foreach (var created in (e.Aux ?? from).Split('|', StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (!fs.DirectoryExists(created)) continue;
                        if (!fs.IsDirectoryEmpty(created)) return CoreText.Get("CoreDirNotEmpty", "Thư mục không rỗng");
                        fs.Delete(created, recursive: false);
                    }
                    return null;

                case OperationKind.RemoveDir:
                    if (!fs.DirectoryExists(from)) fs.EnsureDirectory(from);
                    return null;

                case OperationKind.SetTimestamps:
                    if (!Exists(fs, from)) return CoreText.Get("CoreUndoMissing", "Tệp tại vị trí mới không còn tồn tại");
                    fs.SetTimes(from, e.Before ?? throw new InvalidOperationException("Journal thiếu giá trị cũ."));
                    return null;

                case OperationKind.SetAttributes:
                    if (!Exists(fs, from)) return CoreText.Get("CoreUndoMissing", "Tệp tại vị trí mới không còn tồn tại");
                    fs.SetAttributes(from, (FileAttributes)(e.AttrBefore ?? throw new InvalidOperationException("Journal thiếu giá trị cũ.")));
                    return null;

                case OperationKind.WriteFile:
                    if (string.IsNullOrEmpty(e.Backup))
                    {
                        // Tệp mới hoàn toàn do công cụ tạo ra: hoàn tác = bỏ nó đi.
                        if (!fs.FileExists(to)) return CoreText.Get("CoreUndoMissing", "Tệp tại vị trí mới không còn tồn tại");
                        fs.Delete(to, recursive: false);
                        return null;
                    }

                    if (!fs.FileExists(e.Backup!)) return CoreText.Get("CoreUndoBackupMissing", "Bản sao lưu trong khu cách ly không còn");
                    if (fs.FileExists(to)) fs.Delete(to, recursive: false);
                    EnsureParent(fs, to);
                    fs.Move(e.Backup!, to, overwrite: false);
                    return null;

                default:
                    return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or NotSupportedException)
        {
            return ex.Message;
        }
    }

    /// <summary>
    /// Thao tác này đã thực sự xảy ra trên đĩa chưa? Dùng khi sự cố xảy ra giữa lúc làm và lúc ghi "done".
    /// </summary>
    public static bool IsApplied(IFileSystem fs, JournalEntry e)
    {
        var from = e.From ?? string.Empty;
        var to = e.To ?? string.Empty;

        return OperationExecutor.ParseKind(e) switch
        {
            OperationKind.Move or OperationKind.Quarantine => fs.FileExists(to) && !fs.FileExists(from),
            OperationKind.Copy => fs.FileExists(to),
            OperationKind.CreateDir => e.Aux != OperationExecutor.ExistedMarker && fs.DirectoryExists(from),
            OperationKind.RemoveDir => !fs.DirectoryExists(from),
            OperationKind.SetTimestamps => Exists(fs, from) && e.After is { } after && fs.GetTimes(from) == after,
            OperationKind.SetAttributes => Exists(fs, from) && e.AttrAfter is { } attr && (int)fs.GetAttributes(from) == attr,
            OperationKind.WriteFile => fs.FileExists(to) && !fs.FileExists(from),
            _ => false
        };
    }

    private static bool Exists(IFileSystem fs, string path) => fs.FileExists(path) || fs.DirectoryExists(path);

    private static void EnsureParent(IFileSystem fs, string path)
    {
        var parent = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(parent)) fs.EnsureDirectory(parent);
    }
}
