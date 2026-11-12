namespace BFR.App.Infrastructure;

using System;

using BFR.Core;
using BFR.Core.Updates;

/// <summary>Kết quả kiểm tra cập nhật.</summary>
public enum UpdateCheckStatus
{
    /// <summary>Bản cài do Microsoft Store quản lý — không tự kiểm tra.</summary>
    StoreManaged,

    /// <summary>Đường dẫn nguồn cập nhật không hợp lệ.</summary>
    InvalidFeed,

    /// <summary>Đã có bản mới.</summary>
    UpdateAvailable,

    /// <summary>Đang dùng bản mới nhất.</summary>
    UpToDate,

    /// <summary>Không kiểm tra được (lỗi mạng, feed hỏng...).</summary>
    Failed
}

/// <summary>Kết quả kiểm tra cập nhật kèm thông báo và đường dẫn tải bản mới.</summary>
public sealed class UpdateCheckResult
{
    public UpdateCheckResult(UpdateCheckStatus status, string message, string? releaseUrl = null, string? latestVersion = null)
    {
        Status = status;
        Message = message;
        ReleaseUrl = releaseUrl;
        LatestVersion = latestVersion;
    }

    public UpdateCheckStatus Status { get; }

    /// <summary>Thông báo đã địa phương hóa, sẵn sàng hiển thị.</summary>
    public string Message { get; }

    /// <summary>Đường dẫn tải bản mới (nếu có).</summary>
    public string? ReleaseUrl { get; }

    /// <summary>Phiên bản mới nhất tìm được.</summary>
    public string? LatestVersion { get; }
}

/// <summary>
/// Kiểm tra cập nhật — dùng chung cho menu chính và trang Cài đặt.
/// </summary>
public static class UpdateRunner
{
    /// <summary>Phiên bản hiện tại của ứng dụng.</summary>
    public const string CurrentVersion = BFR.Core.AppVersion.Current;

    /// <summary>Kiểm tra cập nhật từ nguồn đã cấu hình.</summary>
    public static async Task<UpdateCheckResult> CheckAsync()
    {
        if (ShellIntegration.IsPackaged)
        {
            return new UpdateCheckResult(UpdateCheckStatus.StoreManaged, Loc.T("UpdateStoreManaged"));
        }

        var settings = SettingsStore.Load();
        if (!Uri.TryCreate(settings.UpdateFeedUrl, UriKind.Absolute, out var feedUri)
            || settings.UpdateFeedUrl.Contains("YOUR-ORG", StringComparison.OrdinalIgnoreCase)
            || !UpdateFeedPolicy.IsAllowedFeed(feedUri))
        {
            return new UpdateCheckResult(UpdateCheckStatus.InvalidFeed, Loc.T("UpdateFeedInvalid"));
        }

        try
        {
            using var feed = new GitHubReleasesUpdateFeed(feedUri);
            var checker = new UpdateChecker(CurrentVersion, feed);
            var result = await checker.CheckAsync();

            if (!result.UpdateAvailable)
            {
                var message = string.IsNullOrWhiteSpace(result.Error)
                    ? Loc.F("UpdateNone", result.CurrentVersion)
                    : Loc.F("UpdateFailed", result.Error!);

                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, message);
            }

            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                Loc.F("UpdateAvailable", result.LatestVersion, result.CurrentVersion),
                result.ReleaseUrl,
                result.LatestVersion);
        }
        catch (Exception ex)
        {
            return new UpdateCheckResult(UpdateCheckStatus.Failed, Loc.F("UpdateFailed", ex.Message));
        }
    }
}
