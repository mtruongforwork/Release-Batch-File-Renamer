namespace BFR.Core.Licensing;

/// <summary>
/// Các tính năng bị khóa ở bản Free (PHẦN 3.6).
/// </summary>
public enum ProFeature
{
    /// <summary>Chuỗi quy tắc không giới hạn (Free: tối đa 3 quy tắc).</summary>
    UnlimitedRules,

    /// <summary>Ngày chụp EXIF ({taken}).</summary>
    ExifDate,

    /// <summary>Ngày tạo media (video/audio).</summary>
    MediaDate,

    /// <summary>Quét đệ quy không giới hạn cấp (Free: tối đa 2 cấp).</summary>
    UnlimitedRecursion,

    /// <summary>Lưu không giới hạn preset (Free: tối đa 3).</summary>
    UnlimitedPresets,

    /// <summary>Nhập/xuất tệp preset (.bfrpreset).</summary>
    PresetImportExport,

    /// <summary>Hoàn tác nhiều bước, lưu 30 ngày (Free: 1 bước gần nhất).</summary>
    MultiStepUndo,

    /// <summary>Xuất báo cáo CSV/JSON.</summary>
    ReportExport,

    /// <summary>Công cụ dòng lệnh (bfr.exe) cho tác vụ đổi tên hàng loạt.</summary>
    CommandLine,

    /// <summary>Đánh số kiểu La Mã.</summary>
    RomanNumbering,

    /// <summary>Shelf không giới hạn số tệp (Free: tối đa 50 tệp mỗi lần).</summary>
    UnlimitedShelf,

    /// <summary>Danh sách đổi tên / từ điển thay thế không giới hạn dòng (Free: tối đa 50 dòng).</summary>
    UnlimitedListRows,

    /// <summary>Nhập danh sách từ tệp CSV.</summary>
    ListCsvImport,

    // ----- v2.0 -----

    /// <summary>Mẫu Organize sâu hơn 2 cấp thư mục.</summary>
    OrganizeDeepTemplate,

    /// <summary>Quét trùng lặp quá 1.000 tệp mỗi lần.</summary>
    DuplicatesUnlimited,

    /// <summary>Tìm ảnh gần giống và thay bản trùng bằng hard link.</summary>
    SimilarImages,

    /// <summary>Thay bản trùng bằng hard link.</summary>
    HardLinkDedupe,

    /// <summary>Xử lý ảnh quá 20 ảnh mỗi lô.</summary>
    ImageUnlimited,

    /// <summary>Ghi và xoá toàn bộ EXIF (xoá GPS luôn miễn phí).</summary>
    ExifWrite,

    /// <summary>Nén ZIP chia nhóm, đặt tên theo mẫu.</summary>
    ArchiveAdvanced,

    /// <summary>Xuất báo cáo phân tích thư mục.</summary>
    AnalyzeExport,

    /// <summary>Workflow nhiều bước và gắn vào Watch Folder.</summary>
    Workflow,

    /// <summary>Chỉnh thời gian giữ khu cách ly (7 đến 90 ngày).</summary>
    QuarantineRetention
}
