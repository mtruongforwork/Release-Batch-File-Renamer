namespace BFR.Core;

/// <summary>
/// Nguồn phiên bản duy nhất của ứng dụng. Khi phát hành, đổi giá trị này cùng với
/// <c>Version</c> trong <c>packaging/msix/AppxManifest.xml</c> (dạng 4 số: <c>Current + ".0"</c>)
/// và <c>$version</c> trong <c>packaging/msix/build-msix.ps1</c>.
/// </summary>
public static class AppVersion
{
    /// <summary>Phiên bản hiển thị / so sánh cập nhật (ngữ nghĩa, 3 số).</summary>
    public const string Current = "2.0.0";
}
