namespace BFR.App;

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BFR.App.Infrastructure;
using BFR.Presentation.Design;

/// <summary>Cửa sổ Gallery (dev): xem token và thành phần của giao diện mới, đổi sáng/tối và bảng màu tại chỗ.</summary>
public partial class BfrGalleryWindow : Window
{
    private static readonly string[] SwatchKeys =
    {
        "BfrBgBase", "BfrSurface1", "BfrSurface2", "BfrFg", "BfrFg2", "BfrFg3",
        "BfrOk", "BfrWarn", "BfrBad", "BfrAcc1", "BfrAcc2", "BfrAccFg"
    };

    private bool _ready;

    public BfrGalleryWindow()
    {
        InitializeComponent();
        ThemeDark.IsChecked = BfrTheme.IsDark;
        ThemeLight.IsChecked = !BfrTheme.IsDark;
        AccentAurora.IsChecked = BfrTheme.CurrentAccent == BfrTheme.Accent.Aurora;
        AccentMint.IsChecked = BfrTheme.CurrentAccent == BfrTheme.Accent.Mint;
        AccentSunset.IsChecked = BfrTheme.CurrentAccent == BfrTheme.Accent.Sunset;
        HighContrastToggle.IsChecked = BfrTheme.HighContrastOverride == true;

        _ready = true;
        Rebuild();
    }

    private void Theme_Checked(object sender, RoutedEventArgs e) => Reapply();

    private void Accent_Checked(object sender, RoutedEventArgs e) => Reapply();

    private void HighContrast_Click(object sender, RoutedEventArgs e)
    {
        BfrTheme.HighContrastOverride = HighContrastToggle.IsChecked == true ? true : null;
        Reapply();
    }

    private void Reapply()
    {
        if (!_ready) return;

        var accent = AccentMint.IsChecked == true ? BfrTheme.Accent.Mint
            : AccentSunset.IsChecked == true ? BfrTheme.Accent.Sunset
            : BfrTheme.Accent.Aurora;

        BfrTheme.Apply(ThemeDark.IsChecked == true, accent);
        LegacyBridge.Apply();
        Rebuild();
    }

    /// <summary>
    /// Màu hiện hành của một token. Cọ (Brush) có Color là DynamicResource chưa được giải quyết khi đọc thẳng,
    /// nên đọc từ khoá màu (…Color) tương ứng; riêng chữ nhấn chọn biến thể theo sáng/tối.
    /// </summary>
    private static Color ColorOf(string key)
    {
        var colorKey = key == "BfrAccFg" ? (BfrTheme.IsDark ? "BfrAccFgDarkColor" : "BfrAccFgLightColor") : key + "Color";
        if (Application.Current.TryFindResource(colorKey) is Color c) return c;
        return Application.Current.TryFindResource(key) is SolidColorBrush brush ? brush.Color : Colors.Magenta;
    }

    private static string Hex(Color c) => $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Contrast.Argb Argb(Color c) => new(c.A, c.R, c.G, c.B);

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    /// <summary>Dựng lại bảng mẫu màu và bảng tương phản theo theme đang áp dụng.</summary>
    private void Rebuild()
    {
        var surface1 = Argb(ColorOf("BfrSurface1"));

        Swatches.Children.Clear();
        foreach (var key in SwatchKeys)
        {
            var color = ColorOf(key);
            var stack = new StackPanel { Width = 150, Margin = new Thickness(0, 0, 12, 12) };
            stack.Children.Add(new Border
            {
                Height = 44,
                CornerRadius = (CornerRadius)Application.Current.FindResource("BfrRadiusControl"),
                Background = new SolidColorBrush(color),
                BorderBrush = Res("BfrLine"),
                BorderThickness = new Thickness(1)
            });
            stack.Children.Add(new TextBlock { Text = key, Style = (Style)FindResource("BfrBodyText"), FontFamily = (FontFamily)FindResource("BfrFontBodySemi"), Margin = new Thickness(0, 6, 0, 0) });
            stack.Children.Add(new TextBlock { Text = Hex(color), Style = (Style)FindResource("BfrMonoText") });
            Swatches.Children.Add(stack);
        }

        var rows = new (string Label, string Fg, string Bg, double Min)[]
        {
            ("Chữ chính trên nền", "BfrFg", "BfrBgBase", Contrast.TextMinimum),
            ("Chữ chính trên thẻ", "BfrFg", "BfrSurface2", Contrast.TextMinimum),
            ("Chữ phụ trên thẻ", "BfrFg2", "BfrSurface2", Contrast.TextMinimum),
            ("Chữ mờ trên thẻ", "BfrFg3", "BfrSurface2", Contrast.LargeOrUiMinimum),
            ("Chữ nhấn trên thẻ", "BfrAccFg", "BfrSurface2", Contrast.TextMinimum),
            ("Trắng trên gradient (đầu 1)", "BfrOnAccent", "BfrAcc1", Contrast.TextMinimum),
            ("Trắng trên gradient (đầu 2)", "BfrOnAccent", "BfrAcc2", Contrast.TextMinimum),
            ("Thành công trên thẻ", "BfrOk", "BfrSurface2", Contrast.TextMinimum),
            ("Cảnh báo trên thẻ", "BfrWarn", "BfrSurface2", Contrast.TextMinimum),
            ("Lỗi trên thẻ", "BfrBad", "BfrSurface2", Contrast.TextMinimum)
        };

        ContrastRows.Items.Clear();
        foreach (var (label, fg, bg, min) in rows)
        {
            var bgColor = Argb(ColorOf(bg));
            var ratio = Contrast.Ratio(Argb(ColorOf(fg)), bgColor.A == 255 ? bgColor : bgColor.Over(surface1));
            var pass = ratio >= min;

            var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });

            grid.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("BfrBodyText"), VerticalAlignment = VerticalAlignment.Center });

            var ratioText = new TextBlock { Text = $"{ratio:0.0}:1  (cần {min:0.0})", Style = (Style)FindResource("BfrMonoText"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            Grid.SetColumn(ratioText, 1);
            grid.Children.Add(ratioText);

            var chip = new ContentControl { Content = pass ? "Đạt" : "Chưa đạt", Style = (Style)FindResource(pass ? "BfrChipOk" : "BfrChipBad"), HorizontalAlignment = HorizontalAlignment.Left };
            Grid.SetColumn(chip, 2);
            grid.Children.Add(chip);

            ContrastRows.Items.Add(grid);
        }
    }
}
