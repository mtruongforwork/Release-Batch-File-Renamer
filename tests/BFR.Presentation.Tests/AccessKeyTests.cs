namespace BFR.Presentation.Tests;

using BFR.Presentation;

using Xunit;

public class AccessKeyTests
{
    [Theory]
    [InlineData("_Theo dõi thư mục", "Theo dõi thư mục")]
    [InlineData("_Watch folder", "Watch folder")]
    [InlineData("监视文件夹(_W)", "监视文件夹")]
    [InlineData("Không có phím tắt", "Không có phím tắt")]
    [InlineData("Tên__tệp", "Tên_tệp")]
    public void Strip_RemovesMenuAccessKeys(string input, string expected) =>
        Assert.Equal(expected, AccessKey.Strip(input));
}
