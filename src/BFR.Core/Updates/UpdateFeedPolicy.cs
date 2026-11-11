namespace BFR.Core.Updates;

using System;

/// <summary>
/// Chính sách an toàn cho nguồn cập nhật: chỉ HTTPS và chỉ máy chủ GitHub đã biết.
/// Ngăn cấu hình (hoặc phản hồi bị giả mạo) dẫn tới việc gọi máy chủ lạ hoặc mở URL không phải https.
/// </summary>
public static class UpdateFeedPolicy
{
    public const string ApiHost = "api.github.com";
    public const string WebHost = "github.com";

    public const int MaxResponseBytes = 1 * 1024 * 1024;

    /// <summary>URL API "latest release" hợp lệ: https + api.github.com, không kèm thông tin đăng nhập, cổng mặc định.</summary>
    public static bool IsAllowedFeed(Uri? uri) =>
        uri is not null
        && uri.IsAbsoluteUri
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.IsDefaultPort
        && string.IsNullOrEmpty(uri.UserInfo)
        && uri.Host.Equals(ApiHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>URL trang phát hành an toàn để mở bằng trình duyệt: https + github.com. Trả về null nếu không đạt.</summary>
    public static string? SafeReleaseUrl(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort) return null;
        if (!uri.Host.Equals(WebHost, StringComparison.OrdinalIgnoreCase)) return null;
        return uri.AbsoluteUri;
    }
}
