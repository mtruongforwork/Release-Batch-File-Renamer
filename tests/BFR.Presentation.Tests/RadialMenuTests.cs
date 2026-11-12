namespace BFR.Presentation.Tests;

using System.Linq;
using BFR.Presentation.Tools;
using Xunit;

public class RadialMenuTests
{
    [Fact]
    public void Build_HasEightItems_RenameFirst_AndEveryToolOnce()
    {
        var items = RadialMenuModel.Build(ToolCatalog.Create(), vietnamese: false);

        Assert.Equal(RadialMenuModel.Count, items.Count);
        Assert.True(items[0].IsRename);
        Assert.Equal(items.Count, items.Select(i => i.Key).Distinct().Count());
        Assert.All(items, i => Assert.NotEmpty(i.Icon));
    }

    [Fact]
    public void Actions_ComeFromTheToolsOwnChoices_AndNeverIncludeCustom()
    {
        var items = RadialMenuModel.Build(ToolCatalog.Create(), vietnamese: false);
        var tools = ToolCatalog.Create();

        var organize = items.Single(i => i.Key == "organize");
        Assert.NotEmpty(organize.Actions);
        Assert.DoesNotContain(organize.Actions, a => a.Value == "custom");
        Assert.All(organize.Actions, a => Assert.Equal("template", a.OptionKey));

        // Mọi chức năng phải là một giá trị hợp lệ của đúng tuỳ chọn đó: bấm vào là đặt được.
        foreach (var item in items.Where(i => !i.IsRename))
        {
            var tool = tools.Single(t => t.Key == item.Key);
            foreach (var action in item.Actions)
            {
                var option = tool.Options.Single(o => o.Key == action.OptionKey);
                Assert.Contains(option.Choices!, c => c.Value == action.Value);
                Assert.NotNull(action.LabelKey);
            }
        }
    }

    [Fact]
    public void RenameActions_ArePresetsWithReadableLabels()
    {
        var vi = RadialMenuModel.Build(ToolCatalog.Create(), vietnamese: true)[0];
        var en = RadialMenuModel.Build(ToolCatalog.Create(), vietnamese: false)[0];

        Assert.Equal(5, en.Actions.Count);
        Assert.All(en.Actions, a => { Assert.Equal("preset", a.OptionKey); Assert.False(string.IsNullOrWhiteSpace(a.RawLabel)); });
        Assert.NotEqual(vi.Actions[0].RawLabel, en.Actions[0].RawLabel);
    }

    [Theory]
    [InlineData(0, -100, 0)]      // thẳng lên: ô 0
    [InlineData(100, 0, 2)]       // sang phải: ô 2
    [InlineData(0, 100, 4)]       // xuống: ô 4
    [InlineData(-100, 0, 6)]      // sang trái: ô 6
    [InlineData(71, -71, 1)]      // chéo phải trên: ô 1
    [InlineData(-71, -71, 7)]     // chéo trái trên: ô 7
    public void IndexAt_MapsDirectionsToWedges(double dx, double dy, int expected) =>
        Assert.Equal(expected, RadialLayout.IndexAt(dx, dy, 40, 150, 8));

    [Fact]
    public void IndexAt_IgnoresTheHubAndOutsideTheRing_AndCoversEveryAngle()
    {
        Assert.Equal(-1, RadialLayout.IndexAt(0, 0, 40, 150, 8));
        Assert.Equal(-1, RadialLayout.IndexAt(10, 10, 40, 150, 8));
        Assert.Equal(-1, RadialLayout.IndexAt(0, -200, 40, 150, 8));

        for (var degrees = 0; degrees < 360; degrees++)
        {
            var (x, y) = RadialLayout.Point(0, 0, 100, degrees);
            var index = RadialLayout.IndexAt(x, y, 40, 150, 8);
            Assert.InRange(index, 0, 7);
            // Góc giữa của ô chứa điểm không được lệch quá nửa ô.
            var delta = System.Math.Abs(((degrees - RadialLayout.CenterAngle(index, 8) + 540) % 360) - 180);
            Assert.True(delta <= 22.6, $"{degrees}° -> ô {index}");
        }
    }

    [Fact]
    public void Point_AndStep_AreConsistent()
    {
        var (x, y) = RadialLayout.Point(100, 100, 50, 0);
        Assert.Equal(100, x, 6);
        Assert.Equal(50, y, 6);

        Assert.Equal(1, RadialLayout.Step(0, 1, 8));
        Assert.Equal(7, RadialLayout.Step(0, -1, 8));
        Assert.Equal(0, RadialLayout.Step(7, 1, 8));
        Assert.Equal(0, RadialLayout.Step(-1, 1, 8));   // chưa chọn ô nào: mũi tên phải vào ô đầu
        Assert.Equal(7, RadialLayout.Step(-1, -1, 8));
    }
}
