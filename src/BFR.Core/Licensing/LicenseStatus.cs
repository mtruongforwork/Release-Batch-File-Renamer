namespace BFR.Core.Licensing;

using System;

/// <summary>
/// Trạng thái giấy phép tại thời điểm đánh giá.
/// </summary>
public sealed record LicenseStatus
{
    public LicenseTier Tier { get; init; } = LicenseTier.Free;

    /// <summary>True nếu đang trong thời gian dùng thử Pro 30 ngày.</summary>
    public bool IsTrial { get; init; }

    /// <summary>Thời điểm hết hạn dùng thử (UTC), null nếu không dùng thử.</summary>
    public DateTime? TrialEndsUtc { get; init; }

    /// <summary>Product ID đã mua, null nếu chưa mua.</summary>
    public string? ProductId { get; init; }

    /// <summary>Thời điểm đánh giá (UTC).</summary>
    public DateTime EvaluatedUtc { get; init; }

    /// <summary>Nguồn xác định cấp: "store", "trial", "free", "dev".</summary>
    public string Source { get; init; } = "free";

    public bool IsPro => Tier == LicenseTier.Pro;

    public LicenseLimits Limits => LicenseLimits.For(Tier);

    public static LicenseStatus FreeStatus(DateTime nowUtc, string source = "free") => new()
    {
        Tier = LicenseTier.Free,
        EvaluatedUtc = nowUtc,
        Source = source
    };
}
