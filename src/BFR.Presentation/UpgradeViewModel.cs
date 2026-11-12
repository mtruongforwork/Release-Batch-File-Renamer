namespace BFR.Presentation;

using BFR.Core.Licensing;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

/// <summary>Trạng thái của hộp thoại nâng cấp Pro.</summary>
public enum UpgradeState
{
    /// <summary>Chưa dùng thử: có cả "Mua Pro" và "Dùng thử Pro 30 ngày".</summary>
    NotTrialed,

    /// <summary>Đang dùng thử: ẩn nút dùng thử, hiện số ngày còn lại.</summary>
    Trialing,

    /// <summary>Hết hạn dùng thử: chỉ còn "Mua Pro" và nhập mã kích hoạt.</summary>
    TrialExpired,

    /// <summary>Đã là Pro: mở màn hình thông tin license, không mở hộp thoại nâng cấp.</summary>
    AlreadyPro
}

/// <summary>Một dòng trong bảng so sánh Free vs Pro.</summary>
public sealed record UpgradeFeatureRow(string Feature, string FreeText, string ProText, bool FreeHasIt, bool ProHasIt);

/// <summary>
/// ViewModel cho hộp thoại nâng cấp Pro: quyết định trạng thái hiển thị theo cấp giấy phép,
/// dựng bảng so sánh Free/Pro từ đúng giới hạn trong <see cref="LicenseLimits"/>,
/// và điều phối các hành động: mua, dùng thử, nhập mã kích hoạt.
/// </summary>
public sealed class UpgradeViewModel : ObservableObject
{
    private readonly LicenseCoordinator _license;
    private readonly IProPriceProvider? _priceProvider;
    private readonly string? _contextFeatureKey;
    private readonly IStringProvider _strings;

    public UpgradeViewModel(
        LicenseCoordinator license,
        IProPriceProvider? priceProvider = null,
        ProFeature? contextFeature = null,
        string? configuredPrice = null,
        IStringProvider? strings = null)
    {
        _license = license;
        _strings = strings ?? new KeyOnlyStringProvider();
        _priceProvider = priceProvider;
        _contextFeatureKey = contextFeature is null ? null : FeatureResourceKey(contextFeature.Value);
        ConfiguredPrice = configuredPrice;

        ShowLicenseKeyCommand = new RelayCommand(() =>
        {
            IsLicenseKeyVisible = !IsLicenseKeyVisible;
            LicenseKeyError = null;
        });

        ActivateKeyCommand = new RelayCommand(() =>
        {
            var error = _license.License.TryActivateKey(LicenseKeyText);
            if (error != LicenseKey.KeyError.None)
            {
                LicenseKeyError = _strings.F("UpgradeKeyError_" + error);
                return;
            }

            LicenseKeyError = null;
            IsLicenseKeyVisible = false;
            Upgraded?.Invoke(this, EventArgs.Empty);
        });

        BuyCommand = new RelayCommand(() => _ = BuyAsync());
        TrialCommand = new RelayCommand(() =>
        {
            if (_license.License.StartTrial())
            {
                Upgraded?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                LicenseKeyError = _strings.T(CanStartTrial ? "UpgradeTrialUsed" : "UpgradeTrialUnavailable");
            }
        });
    }

    /// <summary>Đặt giá dự phòng (từ cài đặt) khi Store không trả giá.</summary>
    public string? ConfiguredPrice { get; set; }

    /// <summary>Giá hiển thị (đã định dạng) hoặc null nếu không có giá.</summary>
    public string? PriceText { get; private set; }

    public UpgradeState State
    {
        get
        {
            var status = _license.Status;

            // Đang dùng thử phải được xử lý TRƯỚC trạng thái "đã là Pro"
            // (gói dùng thử cũng cấp quyền Pro nhưng vẫn cần hộp thoại nâng cấp).
            if (status.IsTrial) return UpgradeState.Trialing;
            if (status.IsPro) return UpgradeState.AlreadyPro;
            if (_license.License.HasUsedTrial) return UpgradeState.TrialExpired;
            return UpgradeState.NotTrialed;
        }
    }

    public string Header => _strings.T("UpgradeHeader");

    public string Subtitle => _strings.T("UpgradeSubtitle");

    /// <summary>Dòng bối cảnh: tính năng nào đang chạm tới Pro (mục 4 của yêu cầu).</summary>
    public string? ContextMessage => _contextFeatureKey is null
        ? null
        : _strings.F("UpgradeContext", _strings.T(_contextFeatureKey));

    public bool HasContext => !string.IsNullOrEmpty(ContextMessage);

    public IReadOnlyList<UpgradeFeatureRow> Features => BuildRows(_license.License.Limits, _strings);

    /// <summary>Số ngày dùng thử còn lại (làm tròn lên).</summary>
    public int TrialDaysRemaining
    {
        get
        {
            var remaining = _license.License.TrialRemaining();
            if (remaining is null) return 0;
            return (int)Math.Ceiling(remaining.Value.TotalDays);
        }
    }

    /// <summary>True nếu bản cài này cho phép bắt đầu dùng thử Pro.</summary>
    public bool CanStartTrial => _license.License.IsTrialAvailable;

    public bool ShowTrialButton => State == UpgradeState.NotTrialed && CanStartTrial;

    public bool ShowTrialRemaining => State == UpgradeState.Trialing;

    public string TrialRemainingText => _strings.F("UpgradeTrialRemaining", TrialDaysRemaining);

    public bool ShowLicenseKeyLink => State != UpgradeState.AlreadyPro;

    public string LicenseKeyLinkText => _strings.T("UpgradeHaveKey");

    public string LicenseKeyPlaceholder => _strings.T("UpgradeKeyPlaceholder");

    public string ActivateKeyText => _strings.T("UpgradeActivateKey");

    public bool IsLicenseKeyVisible { get; private set; }

    public string LicenseKeyText
    {
        get => _licenseKeyText;
        set
        {
            if (!Set(ref _licenseKeyText, value)) return;
            LicenseKeyError = null;
        }
    }

    private string _licenseKeyText = string.Empty;

    /// <summary>Thông báo lỗi kích hoạt (null nếu không lỗi).</summary>
    public string? LicenseKeyError
    {
        get => _licenseKeyError;
        private set
        {
            if (!Set(ref _licenseKeyError, value)) return;
            Raise(nameof(HasLicenseKeyError));
        }
    }

    private string? _licenseKeyError;

    public bool HasLicenseKeyError => !string.IsNullOrEmpty(LicenseKeyError);

    public string BuyButtonText => string.IsNullOrWhiteSpace(PriceText)
        ? _strings.T("UpgradeBuy")
        : _strings.F("UpgradeBuyWithPrice", PriceText!);

    public string TrialButtonText => _strings.T("UpgradeTrial");

    public string LaterButtonText => _strings.T("UpgradeLater");

    public string CloseButtonName => _strings.T("CloseDialog");

    // ---------------------------------------------------------------- hành động

    public RelayCommand ShowLicenseKeyCommand { get; }

    public RelayCommand ActivateKeyCommand { get; }

    public RelayCommand BuyCommand { get; }

    public RelayCommand TrialCommand { get; }

    public string FreeColumnHeader => _strings.T("UpgradeColumnFree");

    public string ProColumnHeader => _strings.T("UpgradeColumnPro");

    /// <summary>Bắn khi nâng cấp thành công để UI đóng hộp thoại và cập nhật badge.</summary>
    public event EventHandler? Upgraded;

    /// <summary>Lấy giá từ Store (nếu có), nếu không thì dùng giá cấu hình.</summary>
    public async Task LoadPriceAsync()
    {
        if (_priceProvider is not null && _priceProvider.IsAvailable)
        {
            var info = await _priceProvider.GetPriceAsync(LicenseService.ProProductId);
            if (info.HasPrice)
            {
                PriceText = info.FormattedPrice;
                return;
            }
        }

        PriceText = string.IsNullOrWhiteSpace(ConfiguredPrice) ? null : ConfiguredPrice;
        Raise(nameof(BuyButtonText));
    }

    private async Task BuyAsync()
    {
        if (_license.Provider is null || !_license.Provider.IsAvailable)
        {
            LicenseKeyError = _strings.T("UpgradeStoreOnly");
            return;
        }

        var outcome = await _license.PurchaseProAsync();
        switch (outcome.Status)
        {
            case PurchaseStatus.Completed:
                Upgraded?.Invoke(this, EventArgs.Empty);
                break;
            case PurchaseStatus.UserCancelled:
                LicenseKeyError = _strings.T("UpgradeCancelled");
                break;
            case PurchaseStatus.NotSupported:
                LicenseKeyError = _strings.T("UpgradeStoreOnly");
                break;
            default:
                LicenseKeyError = _strings.F("UpgradeFailed", outcome.Error ?? outcome.Status.ToString());
                break;
        }
    }

    // ---------------------------------------------------------------- dữ liệu

    /// <summary>Khoá resource của tên tính năng Pro (dùng cho dòng bối cảnh).</summary>
    public static string FeatureResourceKey(ProFeature feature) => feature switch
    {
        ProFeature.UnlimitedRules => "FeatureUnlimitedRules",
        ProFeature.ExifDate => "FeatureExifMedia",
        ProFeature.MediaDate => "FeatureExifMedia",
        ProFeature.UnlimitedRecursion => "FeatureUnlimitedRecursion",
        ProFeature.UnlimitedPresets => "FeatureUnlimitedPresets",
        ProFeature.PresetImportExport => "FeatureReportExport",
        ProFeature.MultiStepUndo => "FeatureMultiStepUndo",
        ProFeature.ReportExport => "FeatureReportExport",
        ProFeature.CommandLine => "FeatureCommandLine",
        ProFeature.RomanNumbering => "FeatureUnlimitedRules",
        ProFeature.UnlimitedShelf => "FeatureUnlimitedShelf",
        ProFeature.UnlimitedListRows => "FeatureUnlimitedListRows",
        ProFeature.ListCsvImport => "FeatureListCsvImport",
        ProFeature.OrganizeDeepTemplate => "FeatureOrganizeDeep",
        ProFeature.DuplicatesUnlimited => "FeatureDuplicatesUnlimited",
        ProFeature.SimilarImages or ProFeature.HardLinkDedupe => "FeatureSimilarImages",
        ProFeature.ImageUnlimited => "FeatureImageUnlimited",
        ProFeature.ExifWrite => "FeatureExifWrite",
        ProFeature.ArchiveAdvanced => "FeatureArchiveAdvanced",
        ProFeature.AnalyzeExport => "FeatureAnalyzeExport",
        ProFeature.Workflow => "FeatureWorkflow",
        ProFeature.QuarantineRetention => "FeatureQuarantineRetention",
        _ => "FeatureUnlimitedRules"
    };

    /// <summary>Bảng so sánh dựng từ giới hạn thật của cấp Free (không hard-code lệch với logic).</summary>
    private static IReadOnlyList<UpgradeFeatureRow> BuildRows(LicenseLimits limits, IStringProvider s)
    {
        var free = LicenseLimits.Free;

        return new List<UpgradeFeatureRow>
        {
            new(
                s.T("FeatureUnlimitedRules"),
                s.F("FeatureLimitRules", free.MaxRules),
                s.T("FeatureUnlimited"),
                free.MaxRules == int.MaxValue,
                true),
            new(
                s.T("FeatureExifMedia"),
                s.T("FeatureNotIncluded"),
                s.T("FeatureIncluded"),
                free.AllowExifDate,
                true),
            new(
                s.T("FeatureUnlimitedRecursion"),
                s.F("FeatureLimitDepth", free.MaxRecursionDepth),
                s.T("FeatureUnlimited"),
                free.MaxRecursionDepth == int.MaxValue,
                true),
            new(
                s.T("FeatureMultiStepUndo"),
                s.F("FeatureLimitUndo", free.MaxUndoEntries),
                s.T("FeatureUndo30Days"),
                free.MaxUndoEntries == int.MaxValue,
                true),
            new(
                s.T("FeatureUnlimitedPresets"),
                s.F("FeatureLimitPresets", free.MaxSavedPresets),
                s.T("FeatureUnlimited"),
                free.MaxSavedPresets == int.MaxValue,
                true),
            new(
                s.T("FeatureReportExport"),
                s.T("FeatureNotIncluded"),
                s.T("FeatureIncluded"),
                free.AllowReportExport,
                true),
            new(
                s.T("FeatureCommandLine"),
                s.T("FeatureNotIncluded"),
                s.T("FeatureIncluded"),
                free.AllowCommandLine,
                true),
            new(
                s.T("FeatureUnlimitedShelf"),
                s.F("FeatureLimitShelf", free.MaxShelfItems),
                s.T("FeatureUnlimited"),
                free.MaxShelfItems == int.MaxValue,
                true),
            new(
                s.T("FeatureUnlimitedListRows"),
                s.F("FeatureLimitListRows", free.MaxListRows),
                s.T("FeatureUnlimited"),
                free.MaxListRows == int.MaxValue,
                true),
            new(
                s.T("FeatureListCsvImport"),
                s.T("FeatureNotIncluded"),
                s.T("FeatureIncluded"),
                free.AllowListCsvImport,
                true),
            new(
                s.T("FeatureOrganizeDeep"),
                s.F("FeatureLimitOrganizeDepth", free.MaxOrganizeDepth),
                s.T("FeatureUnlimited"),
                free.MaxOrganizeDepth == int.MaxValue,
                true),
            new(
                s.T("FeatureDuplicatesUnlimited"),
                s.F("FeatureLimitDuplicates", free.MaxDuplicateFiles),
                s.T("FeatureUnlimited"),
                free.MaxDuplicateFiles == int.MaxValue,
                true),
            new(
                s.T("FeatureSimilarImages"),
                s.T("FeatureNotIncluded"),
                s.T("FeatureIncluded"),
                free.AllowSimilarImages,
                true),
            new(
                s.T("FeatureImageUnlimited"),
                s.F("FeatureLimitImages", free.MaxImagesPerBatch),
                s.T("FeatureUnlimited"),
                free.MaxImagesPerBatch == int.MaxValue,
                true),
            new(
                s.T("FeatureArchiveAdvanced"),
                s.T("FeatureNotIncluded"),
                s.T("FeatureIncluded"),
                free.AllowArchiveAdvanced,
                true),
            new(
                s.T("FeatureAnalyzeExport"),
                s.T("FeatureNotIncluded"),
                s.T("FeatureIncluded"),
                free.AllowAnalyzeExport,
                true),
            new(
                s.T("FeatureWorkflow"),
                s.T("FeatureNotIncluded"),
                s.T("FeatureIncluded"),
                free.AllowWorkflow,
                true)
        };
    }
}
