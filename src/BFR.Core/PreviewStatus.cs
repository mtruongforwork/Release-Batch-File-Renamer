namespace BFR.Core;

/// <summary>
/// Trạng thái từng dòng preview (FR-C2).
/// </summary>
public enum PreviewStatus
{
    Ok,
    Unchanged,
    Conflict,
    InvalidName,
    PathTooLong,
    Locked,
    MissingData,
    /// <summary>Tệp nằm trong vùng hệ thống được bảo vệ (C10): không bao giờ được áp dụng.</summary>
    Protected
}
