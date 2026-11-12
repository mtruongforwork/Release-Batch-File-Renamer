namespace BFR.Core.Licensing;

using System;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Điều phối giữa trạng thái cục bộ và Microsoft Store IAP:
/// tự đồng bộ giấy phép khi khởi động và sau khi mua.
/// </summary>
public sealed class LicenseCoordinator
{
    private readonly LicenseService _license;
    private readonly IStoreLicenseProvider? _provider;

    public LicenseCoordinator(LicenseService license, IStoreLicenseProvider? provider = null)
    {
        _license = license;
        _provider = provider;
    }

    public LicenseService License => _license;

    public LicenseStatus Status => _license.Status;

    public IStoreLicenseProvider? Provider => _provider;

    /// <summary>
    /// Đồng bộ giấy phép: nếu Store báo đã mua Pro thì ghi nhận vào bản ghi cục bộ;
    /// nếu chưa mua thì giữ nguyên trạng thái (kèm dùng thử nếu đủ điều kiện).
    /// </summary>
    public async Task<LicenseStatus> RefreshAsync(CancellationToken ct = default)
    {
        if (_provider is null || !_provider.IsAvailable)
        {
            return _license.Evaluate();
        }

        StoreLicenseSnapshot snapshot;
        try
        {
            snapshot = await _provider.GetLicenseAsync(ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return _license.Evaluate();
        }

        if (snapshot.HasPro)
        {
            _license.RecordPurchase(LicenseService.ProProductId);
        }
        else if (snapshot.IsTrialActive)
        {
            // Store trial: dùng thử 30 ngày cục bộ tương ứng với thời hạn Store.
            if (!_license.Status.IsTrial && _license.StartTrial()) { /* bắt đầu dùng thử */ }
        }

        return _license.Evaluate();
    }

    /// <summary>
    /// Mua Pro. Trả về kết quả giao dịch; nếu thành công thì cấp đã được cập nhật.
    /// </summary>
    public async Task<PurchaseOutcome> PurchaseProAsync(CancellationToken ct = default)
    {
        if (_provider is null || !_provider.IsAvailable)
        {
            return new PurchaseOutcome(PurchaseStatus.NotSupported, CoreText.Get("CoreStoreNotSupported", "Mua Pro chỉ khả dụng trong bản cài từ Microsoft Store."));
        }

        PurchaseOutcome outcome;
        try
        {
            outcome = await _provider.PurchaseAsync(LicenseService.ProProductId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new PurchaseOutcome(PurchaseStatus.Failed, ex.Message);
        }

        if (outcome.IsSuccess)
        {
            _license.RecordPurchase(LicenseService.ProProductId);
        }

        return outcome;
    }
}
