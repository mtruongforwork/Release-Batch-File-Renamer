namespace BFR.Core.Updates;

using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Nguồn cập nhật mặc định: API "latest release" của GitHub (dùng cho bản portable).
///
/// Quyền riêng tư: chỉ gọi đúng URL do người dùng cấu hình, không gửi thông tin
/// máy, không gửi tên tệp; chỉ gửi User-Agent để GitHub nhận diện ứng dụng.
/// </summary>
public sealed class GitHubReleasesUpdateFeed : IUpdateFeed, IDisposable
{
    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly Uri _releasesApiUri;

    public GitHubReleasesUpdateFeed(Uri releasesApiUri, HttpClient? client = null)
    {
        if (!UpdateFeedPolicy.IsAllowedFeed(releasesApiUri))
        {
            throw new ArgumentException("Nguồn cập nhật phải là HTTPS tới api.github.com.", nameof(releasesApiUri));
        }

        _releasesApiUri = releasesApiUri;
        _ownsClient = client is null;
        _client = client ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
            MaxResponseContentBufferSize = UpdateFeedPolicy.MaxResponseBytes
        };
        if (!string.IsNullOrWhiteSpace(_client.DefaultRequestHeaders.UserAgent.ToString()))
        {
            _client.DefaultRequestHeaders.UserAgent.Clear();
        }
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"BatchFileRenamer/{AppVersion.Current}");
    }

    public async Task<UpdateFeedResult> FetchAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _client.GetAsync(_releasesApiUri, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateFeedResult(false, Error: $"HTTP {(int)response.StatusCode}");
            }

            // Sau chuyển hướng (nếu có) vẫn phải là máy chủ đã cho phép.
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is not null && !UpdateFeedPolicy.IsAllowedFeed(finalUri))
            {
                return new UpdateFeedResult(false, Error: "Nguồn cập nhật chuyển hướng tới máy chủ không được phép.");
            }

            if (response.Content.Headers.ContentLength > UpdateFeedPolicy.MaxResponseBytes)
            {
                return new UpdateFeedResult(false, Error: "Phản hồi cập nhật quá lớn.");
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (json.Length > UpdateFeedPolicy.MaxResponseBytes)
            {
                return new UpdateFeedResult(false, Error: "Phản hồi cập nhật quá lớn.");
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
            // Chỉ giữ URL https tới github.com: giá trị này sau đó được mở bằng trình duyệt.
            var url = UpdateFeedPolicy.SafeReleaseUrl(
                root.TryGetProperty("html_url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                    ? urlProp.GetString()
                    : null);
            var body = root.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;

            if (string.IsNullOrWhiteSpace(tag))
            {
                return new UpdateFeedResult(false, Error: "Phản hồi không có tag_name.");
            }

            return new UpdateFeedResult(true, tag, url, body);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new UpdateFeedResult(false, Error: ex.Message);
        }
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
