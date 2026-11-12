namespace BFR.Core.Tests;

using BFR.Core;

using Xunit;

public class AutoStartCommandTests
{
    [Fact]
    public void Build_QuotesPathAndAddsBackgroundFlag() =>
        Assert.Equal("\"" + @"C:\Program Files\BFR\BatchFileRenamer.exe" + "\" --background",
            AutoStartCommand.Build(@"C:\Program Files\BFR\BatchFileRenamer.exe"));

    [Fact]
    public void Build_OutputIsParsedBackByStartupArgs()
    {
        // Lệnh trong khoá Run khi chạy sẽ truyền đúng cờ mà StartupArgs nhận diện.
        var parsed = StartupArgs.Parse(new[] { AutoStartCommand.BackgroundFlag });

        Assert.True(parsed.Background);
    }
}
