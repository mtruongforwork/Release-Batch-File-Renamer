namespace BFR.Core;

using System;
using System.Text.RegularExpressions;

/// <summary>
/// Che đường dẫn trong văn bản chẩn đoán (nhật ký lỗi, log của shell) trước khi ghi ra đĩa (C8):
/// đường dẫn tệp của người dùng không được nằm trong log.
/// </summary>
public static class LogRedactor
{
    public const string Placeholder = "<path>";

    // Đường dẫn trong dấu nháy (có thể chứa khoảng trắng) lấy tới dấu nháy đóng.
    private static readonly Regex Quoted = new(
        @"(['""])(?:[A-Za-z]:[\\/]|\\\\)[^'""\r\n]*\1",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexLimits.Timeout);

    // Đường dẫn không nháy: ổ đĩa hoặc UNC, lấy tới khoảng trắng hoặc ký tự không thể có trong tên.
    private static readonly Regex Bare = new(
        @"(?:[A-Za-z]:[\\/]|\\\\)[^\s'""<>|*?]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexLimits.Timeout);

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        try
        {
            var s = Quoted.Replace(text, m => m.Groups[1].Value + Placeholder + m.Groups[1].Value);
            return Bare.Replace(s, Placeholder);
        }
        catch (RegexMatchTimeoutException)
        {
            // Không che được thì không ghi gì còn hơn ghi đường dẫn.
            return "[redaction timed out]";
        }
    }
}
