namespace BFR.App;

using System;
using System.IO;
using BFR.Core;

/// <summary>Dọn nền dữ liệu hoạt động theo thời hạn giữ (C8) và xoá toàn bộ theo yêu cầu.</summary>
internal static class ActivityMaintenance
{
    private static string AppDataDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchFileRenamer");

    /// <summary>Chạy khi khởi động: bỏ lịch sử, journal mồ côi và dòng log quá hạn. Không bao giờ ném lỗi.</summary>
    public static void Run()
    {
        try
        {
            var retention = HistoryStore.DefaultRetention;
            var history = new HistoryStore(new RealFileSystem(), Path.Combine(AppDataDir, "history.json"));

            history.Prune(retention);            // xoá cả journal của các mục bị bỏ
            history.SweepOrphanJournals();       // journal không còn mục lịch sử nào trỏ tới
            ActivityData.TrimLog(Path.Combine(AppDataDir, "watch", "watch.log"), retention);
        }
        catch
        {
            // Dọn dẹp là phụ: không bao giờ ảnh hưởng việc khởi động.
        }
    }

    /// <summary>Xoá toàn bộ lịch sử, journal, nhật ký, tệp tạm và tệp báo lỗi. Giữ preset, cài đặt, giấy phép.</summary>
    public static EraseResult EraseAll() =>
        ActivityData.EraseAll(AppDataDir, PathGuard.BfrTempRoot, App.CrashFilePath);
}
