namespace BFR.App;

using System;
using System.IO;
using System.Text.Json;

public sealed class AppSettings
{
    public string Language { get; set; } = "vi";

    /// <summary>
    /// Tự động kiểm tra bản cập nhật mới (chỉ áp dụng cho bản portable;
    /// bản cài từ Microsoft Store được Store cập nhật tự động).
    /// Mặc định TẮT theo nguyên tắc opt-in.
    /// </summary>
    public bool CheckForUpdates { get; set; } = false;

    /// <summary>URL nguồn kiểm tra phiên bản (API latest release của GitHub).</summary>
    public string UpdateFeedUrl { get; set; } = "https://api.github.com/repos/YOUR-ORG/BatchFileRenamer/releases/latest";

    /// <summary>Thư mục Watch Folder đang dùng (để mở lại).</summary>
    public string? WatchFolder { get; set; }

    /// <summary>Preset dùng cho Watch Folder.</summary>
    public string? WatchPreset { get; set; }

    /// <summary>
    /// Lắc chuột khi đang kéo tệp để mở Shelf (dùng hook chuột toàn cục).
    /// Mặc định TẮT theo nguyên tắc opt-in.
    /// </summary>
    public bool ShakeToOpenShelf { get; set; } = false;

    /// <summary>Phím tắt toàn cục mở Shelf (B4). Mặc định TẮT.</summary>
    public bool ShelfHotkeyEnabled { get; set; } = false;

    /// <summary>Tổ hợp phím, ví dụ "Ctrl+Alt+Space" (xem <c>HotkeySpec.Presets</c>).</summary>
    public string ShelfHotkey { get; set; } = "Ctrl+Shift+Space";

    /// <summary>Phím tắt toàn cục mở menu tròn các công cụ. Mặc định TẮT (opt-in).</summary>
    public bool RadialHotkeyEnabled { get; set; } = false;

    public string RadialHotkey { get; set; } = "Ctrl+Alt+Q";

    /// <summary>
    /// Chạy nền ở khay hệ thống: đóng cửa sổ chính chỉ ẩn đi, ứng dụng vẫn chạy
    /// (cần cho lắc chuột mở Shelf khi không mở cửa sổ). Mặc định TẮT.
    /// </summary>
    public bool RunInBackground { get; set; } = false;

    /// <summary>Giao diện: "System", "Light" hoặc "Dark".</summary>
    public string Theme { get; set; } = "System";

    /// <summary>Bảng màu nhấn: "Aurora" (mặc định), "Mint" hoặc "Sunset".</summary>
    public string AccentPalette { get; set; } = "Aurora";

    /// <summary>Số lần đổi tên thành công (dùng để gợi ý đánh giá Store sau 2 lần).</summary>
    public int SuccessfulRenameCount { get; set; } = 0;

    /// <summary>Đã hiển thị gợi ý đánh giá Store chưa.</summary>
    public bool HasPromptedRating { get; set; } = false;

    /// <summary>Giá hiển thị của Pro (dự phòng khi Store không trả giá). Không hard-code trong mã nguồn.</summary>
    public string? ProPriceText { get; set; }

    /// <summary>Số ngày giữ lịch sử, journal hoàn tác và nhật ký (C8). Chỉ nhận các giá trị trong <see cref="RetentionChoices"/>.</summary>
    public int HistoryRetentionDays { get; set; } = 30;

    public static readonly int[] RetentionChoices = { 7, 30, 90, 365 };

    /// <summary>Đưa giá trị bất kỳ (kể cả từ tệp bị sửa) về một lựa chọn hợp lệ gần nhất; mặc định 30.</summary>
    public static int NormalizeRetentionDays(int days) =>
        Array.IndexOf(RetentionChoices, days) >= 0 ? days : 30;
}

/// <summary>
/// Lưu cài đặt người dùng (ngôn ngữ) vào %LocalAppData%\BatchFileRenamer\settings.json.
/// </summary>
public static class SettingsStore
{
    private static readonly string Directory_ = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchFileRenamer");

    private static readonly string SettingsPath = Path.Combine(Directory_, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new AppSettings();
            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Directory_);
            var temp = SettingsPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, SettingsPath, overwrite: true);
        }
        catch
        {
            // Cài đặt không phải dữ liệu quan trọng; bỏ qua lỗi ghi.
        }
    }
}
