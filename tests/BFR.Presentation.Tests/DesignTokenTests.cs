namespace BFR.Presentation.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BFR.Presentation.Design;
using Xunit;

/// <summary>
/// Cổng chất lượng của bộ token giao diện: tương phản chữ, đủ khoá ở mọi bảng màu, không còn màu viết cứng sai chỗ.
/// Đọc thẳng các tệp XAML nên đổi màu mà làm rớt tương phản sẽ fail ngay.
/// </summary>
public class DesignTokenTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string ThemeDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "BFR.App", "Themes", "Bfr");
        return Path.GetFullPath(dir);
    }

    private static XDocument Load(string file)
    {
        var path = Path.Combine(ThemeDir(), file);
        Assert.True(File.Exists(path), $"Thiếu {file}");
        return XDocument.Load(path);
    }

    private static Dictionary<string, string> Colors(string file) =>
        Load(file).Root!.Elements()
            .Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value.Trim());

    private static Contrast.Argb C(Dictionary<string, string> map, string key) => Contrast.Argb.Parse(map[key]);

    public static IEnumerable<object[]> Themes() => new[] { new object[] { "Palette.Dark.xaml", false }, new object[] { "Palette.Light.xaml", true } };

    public static IEnumerable<object[]> ThemeAccent()
    {
        foreach (var theme in new[] { "Palette.Dark.xaml", "Palette.Light.xaml" })
            foreach (var accent in new[] { "Aurora", "Mint", "Sunset" })
                yield return new object[] { theme, accent };
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void Text_HasReadableContrastOnEverySurface(string palette, bool light)
    {
        _ = light;
        var c = Colors(palette);

        foreach (var surface in new[] { "BfrBgBaseColor", "BfrSurface1Color", "BfrSurface2Color" })
        {
            var bg = C(c, surface);
            Assert.True(Contrast.Ratio(C(c, "BfrFgColor"), bg) >= Contrast.TextMinimum, $"{palette}: chữ chính trên {surface}");
            Assert.True(Contrast.Ratio(C(c, "BfrFg2Color"), bg) >= Contrast.TextMinimum, $"{palette}: chữ phụ trên {surface}");
            Assert.True(Contrast.Ratio(C(c, "BfrFg3Color"), bg) >= Contrast.LargeOrUiMinimum, $"{palette}: chữ mờ trên {surface}");
        }
    }

    [Theory]
    [MemberData(nameof(Themes))]
    public void StatusColors_AreReadableAsTextOnTheirSoftBackground(string palette, bool light)
    {
        _ = light;
        var c = Colors(palette);
        var surface = C(c, "BfrSurface1Color");

        foreach (var name in new[] { "Ok", "Warn", "Bad" })
        {
            var soft = C(c, $"Bfr{name}SoftColor").Over(surface);
            Assert.True(Contrast.Ratio(C(c, $"Bfr{name}Color"), soft) >= Contrast.TextMinimum, $"{palette}: {name} trên nền nhạt");
            Assert.True(Contrast.Ratio(C(c, $"Bfr{name}Color"), C(c, "BfrSurface2Color")) >= Contrast.TextMinimum, $"{palette}: {name} trên thẻ");
        }
    }

    [Theory]
    [InlineData("Aurora")]
    [InlineData("Mint")]
    [InlineData("Sunset")]
    public void WhiteTextOnBothGradientStops_Reaches45(string accent)
    {
        var a = Colors($"Accent.{accent}.xaml");
        var white = Contrast.Argb.Parse("#FFFFFFFF");

        Assert.True(Contrast.Ratio(white, C(a, "BfrAcc1Color")) >= Contrast.TextMinimum, $"{accent}: đầu gradient 1");
        Assert.True(Contrast.Ratio(white, C(a, "BfrAcc2Color")) >= Contrast.TextMinimum, $"{accent}: đầu gradient 2");
    }

    [Theory]
    [MemberData(nameof(ThemeAccent))]
    public void AccentText_IsReadableOnSurfaces_ForEveryPaletteAndAccent(string palette, string accent)
    {
        var p = Colors(palette);
        var a = Colors($"Accent.{accent}.xaml");
        var fg = C(a, palette.Contains("Dark") ? "BfrAccFgDarkColor" : "BfrAccFgLightColor");

        foreach (var surface in new[] { "BfrBgBaseColor", "BfrSurface1Color", "BfrSurface2Color" })
            Assert.True(Contrast.Ratio(fg, C(p, surface)) >= Contrast.TextMinimum, $"{palette}/{accent}: chữ nhấn trên {surface}");
    }

    [Fact]
    public void BothPalettesAndHighContrast_DefineTheSameBrushKeys()
    {
        static HashSet<string> Keys(XDocument d) => d.Root!.Elements().Select(e => (string?)e.Attribute(X + "Key")).Where(k => k is not null).ToHashSet()!;

        var dark = Keys(Load("Palette.Dark.xaml"));
        var light = Keys(Load("Palette.Light.xaml"));
        var hc = Keys(Load("HighContrast.xaml"));

        Assert.Equal(dark.OrderBy(k => k), light.OrderBy(k => k));

        // Tương phản cao phải ghi đè mọi cọ (Brush) của bảng màu, nếu không sẽ còn sót màu thương hiệu.
        var brushes = Load("Palette.Dark.xaml").Root!.Elements().Where(e => e.Name.LocalName == "SolidColorBrush")
            .Select(e => (string)e.Attribute(X + "Key")!).ToList();
        foreach (var key in brushes) Assert.Contains(key, hc);
        Assert.Contains("BfrAccGradient", hc);
        Assert.Contains("BfrAccGradientSoft", hc);
    }

    [Fact]
    public void Accents_DefineTheSameKeys()
    {
        static IEnumerable<string> Keys(string file) =>
            Load(file).Root!.Elements().Select(e => (string)e.Attribute(X + "Key")!).OrderBy(k => k);

        var aurora = Keys("Accent.Aurora.xaml").ToList();
        Assert.Equal(aurora, Keys("Accent.Mint.xaml"));
        Assert.Equal(aurora, Keys("Accent.Sunset.xaml"));
    }

    [Fact]
    public void ControlsDictionary_UsesOnlyDynamicTokens_NoLiteralBrandColors()
    {
        var text = File.ReadAllText(Path.Combine(ThemeDir(), "Bfr.Controls.xaml"));

        // Template/style không được viết cứng màu thương hiệu; chỉ cho phép lớp sáng rê chuột trong suốt.
        var literals = System.Text.RegularExpressions.Regex.Matches(text, "(?<![\\w])#[0-9A-Fa-f]{6,8}\\b")
            .Select(m => m.Value.ToUpperInvariant()).Distinct().ToList();
        Assert.All(literals, l => Assert.Equal("#22FFFFFF", l));
    }

    [Theory]
    [InlineData("#FFFFFFFF", "#FF000000", 21.0)]
    [InlineData("#FF777777", "#FFFFFFFF", 4.48)]
    [InlineData("#80FFFFFF", "#FF000000", 5.3)]
    public void Ratio_MatchesKnownValues(string fg, string bg, double expected) =>
        Assert.Equal(expected, Contrast.Ratio(Contrast.Argb.Parse(fg), Contrast.Argb.Parse(bg)), 1);

    [Fact]
    public void Parse_RejectsGarbage()
    {
        Assert.Throws<FormatException>(() => Contrast.Argb.Parse("#12"));
        Assert.Equal(new Contrast.Argb(255, 1, 2, 3), Contrast.Argb.Parse("010203"));
    }
}
