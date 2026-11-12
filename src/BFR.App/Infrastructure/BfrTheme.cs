namespace BFR.App.Infrastructure;

using System;
using System.Linq;
using System.Windows;

/// <summary>
/// Nạp và đổi bộ token giao diện mới (Themes/Bfr): bảng màu sáng/tối, bảng màu nhấn Aurora/Mint/Sunset,
/// chế độ tương phản cao và chế độ giảm chuyển động. Chỉ chọn ResourceDictionary tương ứng; mọi màu nằm trong XAML.
/// </summary>
public static class BfrTheme
{
    public enum Accent
    {
        Aurora,
        Mint,
        Sunset
    }

    private const string Root = "Themes/Bfr/";

    private static bool _hooked;

    public static Accent CurrentAccent { get; private set; } = Accent.Aurora;

    public static bool IsDark { get; private set; } = true;

    public static bool IsHighContrast { get; private set; }

    /// <summary>Giảm chuyển động: Windows tắt hiệu ứng hoạt hình hoặc đang ở tương phản cao.</summary>
    public static bool ReduceMotion { get; private set; }

    /// <summary>Ghi đè tạm để xem thử tương phản cao trong Gallery mà không cần bật chế độ của Windows.</summary>
    public static bool? HighContrastOverride { get; set; }

    public static Accent ParseAccent(string? name) =>
        Enum.TryParse<Accent>(name, ignoreCase: true, out var accent) ? accent : Accent.Aurora;

    /// <summary>Áp dụng bảng màu, bảng màu nhấn và các chế độ trợ năng. An toàn khi gọi nhiều lần.</summary>
    public static void Apply(bool dark, Accent accent)
    {
        var app = Application.Current;
        if (app is null) return;

        IsDark = dark;
        CurrentAccent = accent;
        IsHighContrast = HighContrastOverride ?? SystemParameters.HighContrast;

        var merged = app.Resources.MergedDictionaries;

        // Đổi chế độ giảm chuyển động thì nạp lại từ điển thành phần để thời lượng mới có hiệu lực.
        var reduce = IsHighContrast || !SystemParameters.ClientAreaAnimation;
        if (reduce != ReduceMotion)
        {
            ReduceMotion = reduce;
            Remove(merged, "Bfr.Controls.xaml");
        }

        Ensure(merged, "Tokens.xaml");
        Replace(merged, "Palette.", $"Palette.{(dark ? "Dark" : "Light")}.xaml");
        Replace(merged, "Accent.", $"Accent.{accent}.xaml");
        Ensure(merged, "Bfr.Controls.xaml");

        // Tương phản cao nạp sau cùng để ghi đè mọi khoá Bfr; tắt thì gỡ ra.
        Remove(merged, "HighContrast.xaml");
        if (IsHighContrast) merged.Add(Load("HighContrast.xaml"));

        if (_hooked) return;
        _hooked = true;
        SystemParameters.StaticPropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SystemParameters.HighContrast) or nameof(SystemParameters.ClientAreaAnimation))
                Application.Current?.Dispatcher.BeginInvoke(() => Apply(IsDark, CurrentAccent));
        };
    }

    private static ResourceDictionary Load(string file) => new() { Source = new Uri(Root + file, UriKind.Relative) };

    private static bool Matches(ResourceDictionary d, string fileOrPrefix)
    {
        var source = d.Source?.OriginalString ?? string.Empty;
        var name = source[(source.LastIndexOf('/') + 1)..];
        return name.StartsWith(fileOrPrefix, StringComparison.OrdinalIgnoreCase) && source.Contains("Themes/Bfr", StringComparison.OrdinalIgnoreCase);
    }

    private static void Ensure(System.Collections.ObjectModel.Collection<ResourceDictionary> merged, string file)
    {
        if (!merged.Any(d => Matches(d, file))) merged.Add(Load(file));
    }

    private static void Remove(System.Collections.ObjectModel.Collection<ResourceDictionary> merged, string prefix)
    {
        foreach (var d in merged.Where(d => Matches(d, prefix)).ToList()) merged.Remove(d);
    }

    /// <summary>Thay từ điển có tiền tố <paramref name="prefix"/> bằng <paramref name="file"/>, giữ nguyên vị trí nếu đã có.</summary>
    private static void Replace(System.Collections.ObjectModel.Collection<ResourceDictionary> merged, string prefix, string file)
    {
        var existing = merged.FirstOrDefault(d => Matches(d, prefix));
        var fresh = Load(file);

        if (existing is null)
        {
            merged.Add(fresh);
            return;
        }

        var index = merged.IndexOf(existing);
        merged.Remove(existing);
        merged.Insert(index, fresh);
    }
}
