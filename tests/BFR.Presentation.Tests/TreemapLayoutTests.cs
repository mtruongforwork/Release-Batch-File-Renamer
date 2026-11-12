namespace BFR.Presentation.Tests;

using System.Linq;
using BFR.Presentation.Design;
using Xunit;

public class TreemapLayoutTests
{
    private static TreemapItem[] Sample() => new[]
    {
        new TreemapItem("a", 600), new TreemapItem("b", 300), new TreemapItem("c", 200),
        new TreemapItem("d", 100), new TreemapItem("e", 50), new TreemapItem("f", 50)
    };

    [Fact]
    public void AreasAreProportionalAndFillTheFrame()
    {
        var cells = TreemapLayout.Compute(Sample(), 800, 400);
        var total = Sample().Sum(i => i.Value);

        Assert.Equal(Sample().Length, cells.Count);
        Assert.Equal(800 * 400, cells.Sum(c => c.Width * c.Height), 3);
        foreach (var cell in cells)
            Assert.Equal(cell.Item.Value / (double)total * 800 * 400, cell.Width * cell.Height, 3);
    }

    [Fact]
    public void CellsStayInsideTheFrameAndDoNotOverlap()
    {
        var cells = TreemapLayout.Compute(Sample(), 500, 700);

        foreach (var c in cells)
        {
            Assert.True(c.X >= -1e-9 && c.Y >= -1e-9);
            Assert.True(c.X + c.Width <= 500 + 1e-6 && c.Y + c.Height <= 700 + 1e-6);
        }

        for (var i = 0; i < cells.Count; i++)
        for (var j = i + 1; j < cells.Count; j++)
        {
            var a = cells[i];
            var b = cells[j];
            var overlapX = System.Math.Min(a.X + a.Width, b.X + b.Width) - System.Math.Max(a.X, b.X);
            var overlapY = System.Math.Min(a.Y + a.Height, b.Y + b.Height) - System.Math.Max(a.Y, b.Y);
            Assert.False(overlapX > 1e-6 && overlapY > 1e-6, $"{a.Item.Label} chồng {b.Item.Label}");
        }
    }

    [Fact]
    public void IgnoresNonPositiveValuesAndEmptyInput()
    {
        Assert.Empty(TreemapLayout.Compute(new[] { new TreemapItem("x", 0), new TreemapItem("y", -5) }, 100, 100));
        Assert.Empty(TreemapLayout.Compute(Sample(), 0, 100));
        Assert.Single(TreemapLayout.Compute(new[] { new TreemapItem("only", 7) }, 100, 50));
    }

    [Fact]
    public void LargestItemGetsTheLargestCell()
    {
        var cells = TreemapLayout.Compute(Sample(), 640, 360);
        var biggest = cells.OrderByDescending(c => c.Width * c.Height).First();
        Assert.Equal("a", biggest.Item.Label);
    }
}
