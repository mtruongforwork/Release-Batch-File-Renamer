namespace BFR.Core.Tests;

using BFR.Core;

using Xunit;

public class StartupArgsTests
{
    [Fact]
    public void Background_IsNotTreatedAsPath()
    {
        var r = StartupArgs.Parse(new[] { "--background" });

        Assert.True(r.Background);
        Assert.Empty(r.Payload.Paths);
    }

    [Fact]
    public void Background_IsCaseInsensitive_AndCanMixWithPaths()
    {
        var r = StartupArgs.Parse(new[] { @"C:\a.txt", "--BACKGROUND", @"C:\b.txt" });

        Assert.True(r.Background);
        Assert.Equal(new[] { @"C:\a.txt", @"C:\b.txt" }, r.Payload.Paths);
    }

    [Fact]
    public void NoFlags_MeansForeground()
    {
        var r = StartupArgs.Parse(new[] { @"C:\a.txt" });

        Assert.False(r.Background);
        Assert.Single(r.Payload.Paths);
    }

    [Fact]
    public void PresetAndFileList_AreReadWithTheirValues()
    {
        var r = StartupArgs.Parse(new[] { "--preset", "Numbering", "--file-list", @"C:\list.txt", "--background" });

        Assert.Equal("Numbering", r.Payload.PresetName);
        Assert.Equal(@"C:\list.txt", r.Payload.FileListPath);
        Assert.Empty(r.Payload.Paths);
        Assert.True(r.Background);
    }

    [Fact]
    public void ContextMenuRegistrationFlags_StayInPaths_ForTheAppToHandle()
    {
        var r = StartupArgs.Parse(new[] { "--register-context-menu" });

        Assert.Contains("--register-context-menu", r.Payload.Paths);
    }

    [Fact]
    public void Quick_IsReadWithItsKey_AndNotTreatedAsPath()
    {
        var r = StartupArgs.Parse(new[] { "--quick", "number", "--file-list", @"C:\list.txt" });

        Assert.Equal("number", r.Payload.QuickPreset);
        Assert.Equal(@"C:\list.txt", r.Payload.FileListPath);
        Assert.Empty(r.Payload.Paths);
        Assert.False(r.Background);
    }

    [Fact]
    public void Quick_WithoutKey_IsIgnored()
    {
        var r = StartupArgs.Parse(new[] { "--quick" });

        Assert.Null(r.Payload.QuickPreset);
        Assert.Empty(r.Payload.Paths);
    }

    [Fact]
    public void FlagWithoutValue_IsIgnored()
    {
        var r = StartupArgs.Parse(new[] { "--preset" });

        Assert.Null(r.Payload.PresetName);
        Assert.Empty(r.Payload.Paths);
    }
}
