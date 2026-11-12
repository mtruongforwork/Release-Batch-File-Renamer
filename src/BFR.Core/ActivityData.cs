namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

/// <summary>Kết quả xoá dữ liệu hoạt động.</summary>
public sealed record EraseResult(int FilesDeleted, int Failed);

/// <summary>
/// Dữ liệu hoạt động (C8): lịch sử, journal hoàn tác, nhật ký Watch Folder và tệp tạm. Tất cả chứa đường dẫn
/// của tệp người dùng. Lớp này dọn theo thời hạn và xoá toàn bộ khi người dùng yêu cầu.
/// KHÔNG bao giờ động tới preset, cài đặt, giấy phép hay tệp của người dùng.
/// </summary>
public static class ActivityData
{
    /// <summary>
    /// Xoá toàn bộ lịch sử, journal, nhật ký Watch Folder, tệp tạm của BFR và tệp báo lỗi.
    /// Mất khả năng Hoàn tác các lần đổi tên cũ. Mọi đường dẫn đều phải nằm trong <paramref name="appDataDir"/> / <paramref name="tempRoot"/>.
    /// </summary>
    public static EraseResult EraseAll(string appDataDir, string tempRoot, string? crashFile = null)
    {
        var deleted = 0;
        var failed = 0;

        void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) { File.Delete(path); deleted++; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
            }
        }

        void Wipe(string dir, string pattern)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.EnumerateFiles(dir, pattern, SearchOption.AllDirectories)) TryDelete(f);
        }

        TryDelete(Path.Combine(appDataDir, "history.json"));
        TryDelete(Path.Combine(appDataDir, "history.json.tmp"));
        TryDelete(Path.Combine(appDataDir, "history.json.bak"));
        Wipe(Path.Combine(appDataDir, "journal"), "*");
        Wipe(Path.Combine(appDataDir, "watch"), "*.log");

        // Thư mục tạm riêng của BFR (danh sách tệp từ Explorer, log chẩn đoán của shell).
        if (PathGuard.TryNormalize(tempRoot, out var root) &&
            root.EndsWith("BatchFileRenamer", StringComparison.OrdinalIgnoreCase))
        {
            Wipe(root, "*");
        }

        if (!string.IsNullOrEmpty(crashFile)) TryDelete(crashFile);
        return new EraseResult(deleted, failed);
    }

    /// <summary>
    /// Cắt nhật ký văn bản: bỏ các dòng có dấu thời gian <c>[yyyy-MM-dd HH:mm:ss]</c> cũ hơn <paramref name="maxAge"/>.
    /// Dòng không có dấu thời gian được giữ cùng dòng trước nó. Trả về số dòng đã bỏ.
    /// </summary>
    public static int TrimLog(string logPath, TimeSpan maxAge, DateTime? nowLocal = null)
    {
        if (!File.Exists(logPath)) return 0;

        var cutoff = (nowLocal ?? DateTime.Now) - maxAge;
        string[] lines;
        try { lines = File.ReadAllLines(logPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return 0; }

        var kept = new List<string>(lines.Length);
        var keepCurrent = true;
        foreach (var line in lines)
        {
            if (TryParseStamp(line, out var stamp)) keepCurrent = stamp >= cutoff;
            if (keepCurrent) kept.Add(line);
        }

        var dropped = lines.Length - kept.Count;
        if (dropped == 0) return 0;

        try
        {
            var temp = logPath + ".tmp";
            File.WriteAllLines(temp, kept);
            File.Move(temp, logPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
        return dropped;
    }

    private static bool TryParseStamp(string line, out DateTime stamp)
    {
        stamp = default;
        if (line.Length < 21 || line[0] != '[' || line[20] != ']') return false;
        return DateTime.TryParseExact(line.AsSpan(1, 19), "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out stamp);
    }
}
