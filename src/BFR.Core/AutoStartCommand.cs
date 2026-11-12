namespace BFR.Core;

/// <summary>Dòng lệnh dùng cho mục khởi động cùng Windows của bản portable (khoá Run).</summary>
public static class AutoStartCommand
{
    public const string BackgroundFlag = "--background";

    /// <summary>Đường dẫn được bọc trong ngoặc kép (an toàn với khoảng trắng) kèm cờ chạy nền.</summary>
    public static string Build(string exePath) => $"\"{exePath}\" {BackgroundFlag}";
}
