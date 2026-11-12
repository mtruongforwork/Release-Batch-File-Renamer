namespace BFR.App;

using System;
using System.Globalization;
using System.Linq;
using System.Windows;

/// <summary>
/// Chuyển ngôn ngữ giao diện bằng cách thay ResourceDictionary (vi/en).
/// </summary>
public static class Loc
{
    public static string Current { get; private set; } = "vi";
    public static bool IsEnglish => Current == "en";

    /// <summary>Chọn chuỗi theo ngôn ngữ hiện tại cho text sinh động trong code-behind.</summary>
    public static string L(string vi, string en) => IsEnglish ? en : vi;

    public static void Apply(string language)
    {
        var dictionary = new ResourceDictionary
        {
            Source = new Uri($"Resources/Strings.{language}.xaml", UriKind.Relative)
        };

        var app = Application.Current;
        if (app is null) return;

        var existing = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString.Contains("Strings.", StringComparison.OrdinalIgnoreCase) == true);

        if (existing is not null) app.Resources.MergedDictionaries.Remove(existing);
        app.Resources.MergedDictionaries.Add(dictionary);
        Current = language;

        // Ảnh chụp bất biến các chuỗi của Core: Core chạy trên luồng nền nên không tra ResourceDictionary trực tiếp.
        var snapshot = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in dictionary.Keys)
        {
            if (key is string name
                && (name.StartsWith("Core", StringComparison.Ordinal) || name.StartsWith("PresetBuiltin", StringComparison.Ordinal))
                && dictionary[name] is string value)
            {
                snapshot[name] = value;
            }
        }
        BFR.Core.CoreText.Resolver = key => snapshot.TryGetValue(key, out var text) ? text : null;
    }

    public static string T(string key) =>
        Application.Current?.TryFindResource(key) as string ?? key;

    /// <summary>
    /// Chuỗi dùng làm nhãn thường: bỏ ký hiệu phím tắt của menu ("_Mở", "打开(_O)").
    /// Các khoá như WatchFolder viết cho MenuItem nên có "_"; trong TextBlock nó hiện nguyên văn.
    /// </summary>
    public static string Plain(string key) => BFR.Presentation.AccessKey.Strip(T(key));

    public static string F(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);
}
