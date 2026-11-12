namespace BFR.Presentation;

using System.Text.RegularExpressions;

/// <summary>
/// Bỏ ký hiệu phím tắt kiểu menu khỏi chuỗi giao diện: "_Mở" → "Mở", "打开(_O)" → "打开".
/// Khoá như WatchFolder được viết cho MenuItem nên có "_"; trong TextBlock nó hiện nguyên văn.
/// </summary>
public static class AccessKey
{
    private static readonly Regex Marker = new(@"\(_[^)]\)|_(?!_)", RegexOptions.Compiled);

    public static string Strip(string text) => Marker.Replace(text, string.Empty).Replace("__", "_");
}
