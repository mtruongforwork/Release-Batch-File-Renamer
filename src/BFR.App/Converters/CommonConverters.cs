namespace BFR.App.Converters;

using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

/// <summary>Đảo ngược trạng thái logic để hiện/ẩn (bool -> Visibility).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Collapsed;
}

/// <summary>True khi chuỗi khác rỗng (dùng cho InfoBar).</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Hiện khi số lượng = 0 (dùng cho empty state của Rule Builder).</summary>
public sealed class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Chọn control hiển thị theo loại tham số (Toggle/Text/Choice).</summary>
public sealed class ParameterKindToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var kind = value?.ToString() ?? string.Empty;
        var wanted = parameter?.ToString() ?? string.Empty;

        return kind switch
        {
            "Toggle" => wanted == "Toggle" ? Visibility.Visible : Visibility.Collapsed,
            "Text" or "Number" => wanted == "Text" ? Visibility.Visible : Visibility.Collapsed,
            "Choice" => wanted == "Choice" ? Visibility.Visible : Visibility.Collapsed,
            "Multiline" => wanted == "Multiline" ? Visibility.Visible : Visibility.Collapsed,
            _ => Visibility.Collapsed
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Hiện khi giá trị null (dùng cho empty state của Rule Trace).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Hiện khi giá trị khác null.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
