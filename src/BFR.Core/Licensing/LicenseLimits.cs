namespace BFR.Core.Licensing;

/// <summary>
/// Giới hạn theo từng cấp giấy phép. Giá trị <see cref="int.MaxValue"/> nghĩa là không giới hạn.
/// </summary>
public sealed record LicenseLimits
{
    /// <summary>Số quy tắc tối đa trong một chuỗi quy tắc.</summary>
    public int MaxRules { get; init; }

    /// <summary>Số preset người dùng tối đa được lưu trong thư viện.</summary>
    public int MaxSavedPresets { get; init; }

    /// <summary>Số lần áp dụng tối đa còn trong lịch sử để hoàn tác.</summary>
    public int MaxUndoEntries { get; init; }

    /// <summary>Độ sâu quét thư mục con tối đa (0 = chỉ thư mục gốc).</summary>
    public int MaxRecursionDepth { get; init; }

    /// <summary>Cho phép dùng ngày chụp EXIF.</summary>
    public bool AllowExifDate { get; init; }

    /// <summary>Cho phép dùng ngày tạo media (video/audio).</summary>
    public bool AllowMediaDate { get; init; }

    /// <summary>Cho phép nhập/xuất tệp preset.</summary>
    public bool AllowPresetImportExport { get; init; }

    /// <summary>Cho phép xuất báo cáo CSV/JSON.</summary>
    public bool AllowReportExport { get; init; }

    /// <summary>Cho phép dùng CLI cho tác vụ đổi tên.</summary>
    public bool AllowCommandLine { get; init; }

    /// <summary>Cho phép đánh số kiểu La Mã.</summary>
    public bool AllowRomanNumbering { get; init; }

    /// <summary>Số dòng tối đa của danh sách đổi tên / từ điển thay thế.</summary>
    public int MaxListRows { get; init; }

    /// <summary>Cho phép nhập danh sách từ tệp CSV.</summary>
    public bool AllowListCsvImport { get; init; }

    /// <summary>Số tệp tối đa trong Shelf.</summary>
    public int MaxShelfItems { get; init; }

    // ----- v2.0: bộ công cụ tệp (spec §6.1) -----

    /// <summary>Số cấp thư mục tối đa trong mẫu Organize.</summary>
    public int MaxOrganizeDepth { get; init; }

    /// <summary>Số tệp tối đa mỗi lần quét trùng lặp.</summary>
    public int MaxDuplicateFiles { get; init; }

    /// <summary>Ảnh gần giống và thay bản trùng bằng hard link.</summary>
    public bool AllowSimilarImages { get; init; }

    /// <summary>Số ảnh tối đa mỗi lô của Image Tools.</summary>
    public int MaxImagesPerBatch { get; init; }

    /// <summary>Ghi/xoá toàn bộ EXIF (Free chỉ được xoá GPS).</summary>
    public bool AllowExifWrite { get; init; }

    /// <summary>Archive: chia nhóm và đặt tên theo mẫu.</summary>
    public bool AllowArchiveAdvanced { get; init; }

    /// <summary>Analyze: xuất báo cáo.</summary>
    public bool AllowAnalyzeExport { get; init; }

    /// <summary>Workflow nhiều bước.</summary>
    public bool AllowWorkflow { get; init; }

    /// <summary>Số ngày tối đa người dùng được chọn để giữ khu cách ly (Free cố định 30 ngày, Pro chỉnh tới 90; khu cách ly không bao giờ dọn sớm hơn 30 ngày).</summary>
    public int MaxQuarantineDays { get; init; }

    /// <summary>Giới hạn của bản miễn phí (PHẦN 3.6).</summary>
    public static LicenseLimits Free { get; } = new()
    {
        MaxRules = 3,
        MaxSavedPresets = 3,
        MaxUndoEntries = 1,
        MaxRecursionDepth = 2,
        AllowExifDate = false,
        AllowMediaDate = false,
        AllowPresetImportExport = false,
        AllowReportExport = false,
        AllowCommandLine = false,
        AllowRomanNumbering = false,
        MaxShelfItems = 50,
        MaxListRows = 50,
        AllowListCsvImport = false,
        MaxOrganizeDepth = 2,
        MaxDuplicateFiles = 1000,
        AllowSimilarImages = false,
        MaxImagesPerBatch = 20,
        AllowExifWrite = false,
        AllowArchiveAdvanced = false,
        AllowAnalyzeExport = false,
        AllowWorkflow = false,
        MaxQuarantineDays = 30
    };

    /// <summary>Giới hạn của bản Pro: không giới hạn.</summary>
    public static LicenseLimits Pro { get; } = new()
    {
        MaxRules = int.MaxValue,
        MaxSavedPresets = int.MaxValue,
        MaxUndoEntries = int.MaxValue,
        MaxRecursionDepth = int.MaxValue,
        AllowExifDate = true,
        AllowMediaDate = true,
        AllowPresetImportExport = true,
        AllowReportExport = true,
        AllowCommandLine = true,
        AllowRomanNumbering = true,
        MaxShelfItems = int.MaxValue,
        MaxListRows = int.MaxValue,
        AllowListCsvImport = true,
        MaxOrganizeDepth = int.MaxValue,
        MaxDuplicateFiles = int.MaxValue,
        AllowSimilarImages = true,
        MaxImagesPerBatch = int.MaxValue,
        AllowExifWrite = true,
        AllowArchiveAdvanced = true,
        AllowAnalyzeExport = true,
        AllowWorkflow = true,
        MaxQuarantineDays = 90
    };

    public static LicenseLimits For(LicenseTier tier) =>
        tier == LicenseTier.Pro ? Pro : Free;

    /// <summary>True nếu giá trị <paramref name="value"/> vượt quá giới hạn đã cho.</summary>
    public bool Exceeds(int limit, int value) => value > limit;
}
