namespace BFR.Core.Licensing;

using System;
using System.Collections.Generic;

/// <summary>
/// Nguồn cấp giấy phép: quyết định ứng dụng đang ở cấp Free hay Pro và các giới hạn tương ứng.
///
/// Luồng: <see cref="LicenseStore"/> (trạng thái cục bộ) + <see cref="IStoreLicenseProvider"/>
/// (mua đứt trên Microsoft Store) → <see cref="LicenseCoordinator"/> → tầng UI/CLI.
/// </summary>
public sealed class LicenseService
{
    /// <summary>Product ID của add-on Pro mua đứt trọn đời (khớp với Partner Center).</summary>
    public const string ProProductId = "BFR_PRO_LIFETIME";

    /// <summary>Thời gian dùng thử Pro (30 ngày, một lần cho mỗi cài đặt).</summary>
    public static readonly TimeSpan TrialDuration = TimeSpan.FromDays(30);

    private readonly LicenseStore _store;
    private readonly Func<DateTime> _clock;
    private readonly Func<LicenseTier>? _tierOverride;
    private readonly bool _trialEnabled;
    private LicenseStatus _status;

    /// <param name="store">Kho trạng thái giấy phép cục bộ.</param>
    /// <param name="clock">Nguồn thời gian (mặc định <see cref="DateTime.UtcNow"/>), dùng để kiểm thử.</param>
    /// <param name="tierOverride">Ghi đè cấp giấy phép (chỉ dùng cho test và bản dev).</param>
    /// <param name="trialEnabled">Cho phép dùng thử Pro hay không (bản portable không bật).</param>
    public LicenseService(
        LicenseStore store,
        Func<DateTime>? clock = null,
        Func<LicenseTier>? tierOverride = null,
        bool trialEnabled = true)
    {
        _store = store;
        _clock = clock ?? (() => DateTime.UtcNow);
        _tierOverride = tierOverride;
        _trialEnabled = trialEnabled;
        IsTrialAvailable = trialEnabled;
        _status = Evaluate();
    }

    /// <summary>Trạng thái giấy phép hiện tại.</summary>
    public LicenseStatus Status => _status;

    /// <summary>Giới hạn ứng dụng theo cấp hiện tại.</summary>
    public LicenseLimits Limits => _status.Limits;

    /// <summary>Đánh giá lại cấp giấy phép từ trạng thái đã lưu.</summary>
    public LicenseStatus Evaluate()
    {
        var now = _clock();
        var record = _store.Load();

        if (_tierOverride is not null)
        {
            var tier = _tierOverride();
            _status = new LicenseStatus
            {
                Tier = tier,
                EvaluatedUtc = now,
                Source = "dev",
                ProductId = record.PurchasedProductId
            };
            return _status;
        }

        if (!string.IsNullOrWhiteSpace(record.PurchasedProductId))
        {
            _status = new LicenseStatus
            {
                Tier = LicenseTier.Pro,
                EvaluatedUtc = now,
                Source = "store",
                ProductId = record.PurchasedProductId
            };
            return _status;
        }

        if (_trialEnabled && record.TrialStartedUtc.HasValue)
        {
            var ends = record.TrialStartedUtc.Value + TrialDuration;
            if (now < ends)
            {
                _status = new LicenseStatus
                {
                    Tier = LicenseTier.Pro,
                    IsTrial = true,
                    TrialEndsUtc = ends,
                    EvaluatedUtc = now,
                    Source = "trial"
                };
                return _status;
            }
        }

        _status = LicenseStatus.FreeStatus(now, _trialEnabled ? "free" : "free");
        return _status;
    }

    /// <summary>
    /// Bắt đầu dùng thử Pro 30 ngày nếu chưa dùng. Trả về true nếu lần này bắt đầu mới.
    /// </summary>
    public bool StartTrial()
    {
        if (!_trialEnabled) return false;

        var record = _store.Load();
        if (record.TrialStartedUtc.HasValue) return false;

        record.TrialStartedUtc = _clock();
        record.TrialProductId = ProProductId;
        _store.Save(record);
        Evaluate();
        return true;
    }

    /// <summary>Ghi nhận đã mua Pro (thường gọi sau khi PurchaseAsync trả về Completed).</summary>
    public void RecordPurchase(string productId = ProProductId, DateTime? whenUtc = null)
    {
        var record = _store.Load();
        record.PurchasedProductId = productId;
        record.PurchasedUtc = whenUtc ?? _clock();
        record.ActivationSource = "store";
        _store.Save(record);
        Evaluate();
    }

    /// <summary>
    /// Kích hoạt bằng mã kích hoạt (bản portable). Trả về lý do lỗi nếu mã không hợp lệ.
    /// </summary>
    public LicenseKey.KeyError TryActivateKey(string? key, DateTime? whenUtc = null)
    {
        var error = LicenseKey.Validate(key);
        if (error != LicenseKey.KeyError.None) return error;

        var record = _store.Load();
        record.ActivationKey = LicenseKey.Normalize(key);
        record.ActivationSource = "key";
        record.PurchasedProductId = ProProductId;
        record.PurchasedUtc = whenUtc ?? _clock();
        _store.Save(record);
        Evaluate();
        return LicenseKey.KeyError.None;
    }

    /// <summary>True nếu Pro được kích hoạt bằng mã (không phải mua qua Store).</summary>
    public bool ActivatedByKey
    {
        get
        {
            var record = _store.Load();
            return string.Equals(record.ActivationSource, "key", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(record.ActivationKey);
        }
    }

    /// <summary>True nếu bản cài này cho phép dùng thử Pro 30 ngày.</summary>
    public bool IsTrialAvailable { get; }

    /// <summary>True nếu người dùng đã từng bắt đầu dùng thử (kể cả khi đã hết hạn).</summary>
    public bool HasUsedTrial => _store.Load().TrialConsumed;

    /// <summary>Thời điểm bắt đầu dùng thử (null nếu chưa dùng).</summary>
    public DateTime? TrialStartedUtc => _store.Load().TrialStartedUtc;

    /// <summary>Thời điểm mua/kích hoạt Pro (null nếu chưa có).</summary>
    public DateTime? PurchasedUtc => _store.Load().PurchasedUtc;

    /// <summary>Xoá trạng thái mua (dùng cho môi trường thử nghiệm, không phải trong sản phẩm).</summary>
    public void ClearPurchase()
    {
        var record = _store.Load();
        record.PurchasedProductId = null;
        record.PurchasedUtc = null;
        _store.Save(record);
        Evaluate();
    }

    /// <summary>Kiểm tra một tính năng Pro có được phép dùng không.</summary>
    public GateResult Check(ProFeature feature)
    {
        var limits = Limits;
        var allowed = feature switch
        {
            ProFeature.UnlimitedRules => limits.MaxRules == int.MaxValue,
            ProFeature.ExifDate => limits.AllowExifDate,
            ProFeature.MediaDate => limits.AllowMediaDate,
            ProFeature.UnlimitedRecursion => limits.MaxRecursionDepth == int.MaxValue,
            ProFeature.UnlimitedPresets => limits.MaxSavedPresets == int.MaxValue,
            ProFeature.PresetImportExport => limits.AllowPresetImportExport,
            ProFeature.MultiStepUndo => limits.MaxUndoEntries == int.MaxValue,
            ProFeature.ReportExport => limits.AllowReportExport,
            ProFeature.CommandLine => limits.AllowCommandLine,
            ProFeature.RomanNumbering => limits.AllowRomanNumbering,
            ProFeature.UnlimitedShelf => limits.MaxShelfItems == int.MaxValue,
            ProFeature.UnlimitedListRows => limits.MaxListRows == int.MaxValue,
            ProFeature.ListCsvImport => limits.AllowListCsvImport,
            ProFeature.OrganizeDeepTemplate => limits.MaxOrganizeDepth == int.MaxValue,
            ProFeature.DuplicatesUnlimited => limits.MaxDuplicateFiles == int.MaxValue,
            ProFeature.SimilarImages or ProFeature.HardLinkDedupe => limits.AllowSimilarImages,
            ProFeature.ImageUnlimited => limits.MaxImagesPerBatch == int.MaxValue,
            ProFeature.ExifWrite => limits.AllowExifWrite,
            ProFeature.ArchiveAdvanced => limits.AllowArchiveAdvanced,
            ProFeature.AnalyzeExport => limits.AllowAnalyzeExport,
            ProFeature.Workflow => limits.AllowWorkflow,
            ProFeature.QuarantineRetention => limits.MaxQuarantineDays > QuarantineStore.DefaultRetentionDays,
            _ => true
        };

        return allowed
            ? GateResult.Allow()
            : GateResult.Deny(feature.ToString(), FeatureMessages.For(feature));
    }

    public bool Allows(ProFeature feature) => Check(feature).Allowed;

    /// <summary>Số cấp thư mục tối đa của mẫu Organize.</summary>
    public int MaxOrganizeDepth => Limits.MaxOrganizeDepth;

    /// <summary>Số tệp tối đa mỗi lần quét trùng lặp.</summary>
    public int MaxDuplicateFiles => Limits.MaxDuplicateFiles;

    /// <summary>Số ảnh tối đa mỗi lô Image Tools.</summary>
    public int MaxImagesPerBatch => Limits.MaxImagesPerBatch;

    /// <summary>Số quy tắc tối đa được phép dùng.</summary>
    public int MaxRules => Limits.MaxRules;

    /// <summary>Độ sâu quét tối đa (Free: 2, Pro: không giới hạn).</summary>
    public int MaxRecursionDepth => Limits.MaxRecursionDepth;

    /// <summary>Số preset người dùng tối đa được lưu.</summary>
    public int MaxSavedPresets => Limits.MaxSavedPresets;

    /// <summary>Số dòng tối đa của danh sách đổi tên (Free: 50, Pro: không giới hạn).</summary>
    public int MaxListRows => Limits.MaxListRows;

    /// <summary>Số tệp tối đa trong Shelf (Free: 50, Pro: không giới hạn).</summary>
    public int MaxShelfItems => Limits.MaxShelfItems;

    /// <summary>Số mục lịch sử còn giữ để hoàn tác.</summary>
    public int MaxUndoEntries => Limits.MaxUndoEntries;

    /// <summary>Kiểm tra chuỗi quy tắc có vượt giới hạn không.</summary>
    public GateResult CheckRuleCount(int ruleCount)
    {
        if (ruleCount <= MaxRules) return GateResult.Allow();
        return GateResult.Deny(nameof(ProFeature.UnlimitedRules), FeatureMessages.For(ProFeature.UnlimitedRules));
    }

    /// <summary>Kiểm tra độ sâu quét có vượt giới hạn không.</summary>
    public GateResult CheckRecursionDepth(int depth)
    {
        if (depth <= MaxRecursionDepth) return GateResult.Allow();
        return GateResult.Deny(nameof(ProFeature.UnlimitedRecursion), FeatureMessages.For(ProFeature.UnlimitedRecursion));
    }

    /// <summary>Kiểm tra số preset đã lưu có vượt giới hạn không.</summary>
    public GateResult CheckPresetCount(int savedCount)
    {
        if (savedCount <= MaxSavedPresets) return GateResult.Allow();
        return GateResult.Deny(nameof(ProFeature.UnlimitedPresets), FeatureMessages.For(ProFeature.UnlimitedPresets));
    }

    /// <summary>Cắt giá trị về giới hạn hiện hành (dùng cho độ sâu quét, số quy tắc).</summary>
    public int Clamp(int value, int limit) => value > limit ? limit : value;

    /// <summary>Thời gian dùng thử còn lại (null nếu không dùng thử).</summary>
    public TimeSpan? TrialRemaining()
    {
        if (!Status.IsTrial || Status.TrialEndsUtc is null) return null;
        var remaining = Status.TrialEndsUtc.Value - _clock();
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>Tóm tắt ngắn gọn cho CLI/giao diện.</summary>
    public IReadOnlyDictionary<string, string> Describe()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tier"] = Status.Tier.ToString(),
            ["source"] = Status.Source,
            ["isTrial"] = Status.IsTrial ? "true" : "false",
            ["trialEndsUtc"] = Status.TrialEndsUtc?.ToString("O") ?? string.Empty,
            ["productId"] = Status.ProductId ?? string.Empty,
            ["maxRules"] = MaxRules == int.MaxValue ? "unlimited" : MaxRules.ToString(),
            ["maxRecursionDepth"] = MaxRecursionDepth == int.MaxValue ? "unlimited" : MaxRecursionDepth.ToString(),
            ["maxSavedPresets"] = MaxSavedPresets == int.MaxValue ? "unlimited" : MaxSavedPresets.ToString(),
            ["maxUndoEntries"] = MaxUndoEntries == int.MaxValue ? "unlimited" : MaxUndoEntries.ToString()
        };
        return dict;
    }

    /// <summary>
    /// Móc thử nghiệm cho bản dev: biến môi trường BFR_DEV_TIER=pro|free|trial.
    ///
    /// CHỈ có tác dụng khi ứng dụng KHÔNG chạy từ gói MSIX. Người dùng bản Store
    /// (có package identity) không thể tự mở khoá Pro chỉ bằng biến môi trường.
    /// </summary>
    public static Func<LicenseTier>? ResolveDevTierOverride(bool packaged)
    {
        if (packaged) return null;

        var value = Environment.GetEnvironmentVariable("BFR_DEV_TIER");
        if (string.IsNullOrWhiteSpace(value)) return null;

        return value.Trim().ToLowerInvariant() switch
        {
            "pro" or "trial" => () => LicenseTier.Pro,
            "free" => () => LicenseTier.Free,
            _ => null
        };
    }
}

/// <summary>
/// Thông điệp mặc định khi một tính năng bị khóa (tiếng Việt; tầng trên có thể dịch).
/// </summary>
public static class FeatureMessages
{
    public static string For(ProFeature feature) => feature switch
    {
        ProFeature.UnlimitedRules => "Bản Free chỉ dùng tối đa 3 quy tắc trong một chuỗi. Nâng cấp Pro để không giới hạn.",
        ProFeature.ExifDate => "Ngày chụp EXIF là tính năng Pro. Bản Free chỉ dùng ngày tạo/ngày sửa/ngày hiện tại.",
        ProFeature.MediaDate => "Ngày tạo media (video/audio) là tính năng Pro.",
        ProFeature.UnlimitedRecursion => "Bản Free quét tối đa 2 cấp thư mục con. Nâng cấp Pro để quét không giới hạn.",
        ProFeature.UnlimitedPresets => "Bản Free lưu tối đa 3 preset. Nâng cấp Pro để lưu không giới hạn.",
        ProFeature.PresetImportExport => "Nhập/xuất preset là tính năng Pro.",
        ProFeature.MultiStepUndo => "Hoàn tác nhiều bước (30 ngày) là tính năng Pro. Bản Free giữ 1 lần gần nhất.",
        ProFeature.ReportExport => "Xuất báo cáo CSV/JSON là tính năng Pro.",
        ProFeature.CommandLine => "Công cụ dòng lệnh bfr là tính năng Pro.",
        ProFeature.RomanNumbering => "Đánh số kiểu La Mã là tính năng Pro.",
        ProFeature.UnlimitedShelf => "Bản Free giữ tối đa 50 tệp trong Shelf. Nâng cấp Pro để không giới hạn.",
        ProFeature.UnlimitedListRows => "Bản Free dùng tối đa 50 dòng trong danh sách đổi tên. Nâng cấp Pro để không giới hạn.",
        ProFeature.ListCsvImport => "Nhập danh sách từ tệp CSV là tính năng Pro.",
        ProFeature.OrganizeDeepTemplate => "Bản Free sắp xếp tối đa 2 cấp thư mục. Nâng cấp Pro để không giới hạn.",
        ProFeature.DuplicatesUnlimited => "Bản Free quét tối đa 1.000 tệp mỗi lần tìm trùng. Nâng cấp Pro để không giới hạn.",
        ProFeature.SimilarImages => "Tìm ảnh gần giống là tính năng Pro.",
        ProFeature.HardLinkDedupe => "Thay bản trùng bằng hard link là tính năng Pro.",
        ProFeature.ImageUnlimited => "Bản Free xử lý tối đa 20 ảnh mỗi lô. Nâng cấp Pro để không giới hạn.",
        ProFeature.ExifWrite => "Ghi và xoá toàn bộ EXIF là tính năng Pro. Xoá GPS luôn miễn phí.",
        ProFeature.ArchiveAdvanced => "Nén chia nhóm và đặt tên theo mẫu là tính năng Pro.",
        ProFeature.AnalyzeExport => "Xuất báo cáo phân tích là tính năng Pro.",
        ProFeature.Workflow => "Workflow nhiều bước là tính năng Pro.",
        ProFeature.QuarantineRetention => "Chỉnh thời gian giữ khu cách ly là tính năng Pro. Bản Free giữ 30 ngày.",
        _ => "Tính năng này cần bản Pro."
    };
}
