namespace BFR.Core.Tests;

using BFR.Core;

using Xunit;

/// <summary>Test cho B4: phân tích và định dạng phím tắt toàn cục.</summary>
public class HotkeySpecTests
{
    [Fact]
    public void EveryPreset_ParsesAndRoundTrips()
    {
        foreach (var preset in HotkeySpec.Presets)
        {
            Assert.True(HotkeySpec.TryParse(preset, out var spec), preset);
            Assert.Equal(preset, spec.ToString());
        }
    }

    [Fact]
    public void Default_IsOneOfThePresets() => Assert.Contains(HotkeySpec.Default, HotkeySpec.Presets);

    [Theory]
    [InlineData("alt+ctrl+space", "Ctrl+Alt+Space")]
    [InlineData("  CTRL + shift + s ", "Ctrl+Shift+S")]
    [InlineData("windows+shift+f5", "Shift+Win+F5")]
    [InlineData("Control+Alt+7", "Ctrl+Alt+7")]
    public void Parse_NormalizesCaseOrderAndAliases(string input, string expected)
    {
        Assert.True(HotkeySpec.TryParse(input, out var spec));
        Assert.Equal(expected, spec.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Space")]            // thiếu phím bổ trợ
    [InlineData("S")]
    [InlineData("Ctrl+")]            // thiếu phím chính
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+A+B")]         // hai phím chính
    [InlineData("Ctrl+Enter")]       // phím chính không được hỗ trợ
    [InlineData("Ctrl+F13")]
    [InlineData("Ctrl+F0")]
    [InlineData("Ctrl+@")]
    public void Parse_RejectsInvalidCombinations(string? input) =>
        Assert.False(HotkeySpec.TryParse(input, out _));

    [Fact]
    public void Win32Values_AreCorrect()
    {
        HotkeySpec.TryParse("Ctrl+Alt+Space", out var space);
        HotkeySpec.TryParse("Ctrl+Shift+S", out var s);
        HotkeySpec.TryParse("Win+F5", out var f5);
        HotkeySpec.TryParse("Alt+7", out var seven);

        Assert.Equal(0x3u, space.Modifiers);     // MOD_ALT | MOD_CONTROL
        Assert.Equal(0x20u, space.VirtualKey);
        Assert.Equal(0x6u, s.Modifiers);         // MOD_CONTROL | MOD_SHIFT
        Assert.Equal(0x53u, s.VirtualKey);
        Assert.Equal(0x8u, f5.Modifiers);        // MOD_WIN
        Assert.Equal(0x74u, f5.VirtualKey);
        Assert.Equal(0x37u, seven.VirtualKey);
    }

    [Theory]
    [InlineData("A", true)]
    [InlineData("z", true)]
    [InlineData("0", true)]
    [InlineData("F1", true)]
    [InlineData("f12", true)]
    [InlineData("Space", true)]
    [InlineData("F13", false)]
    [InlineData("Tab", false)]
    [InlineData("", false)]
    public void SupportedKeys(string key, bool expected) =>
        Assert.Equal(expected, HotkeySpec.IsSupportedKey(key));
}
