namespace BFR.Presentation.Design;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Một phần tử của treemap: nhãn và giá trị (ví dụ số byte).</summary>
public sealed record TreemapItem(string Label, long Value);

/// <summary>Ô đã bố trí: toạ độ trong khung (0,0)-(width,height).</summary>
public sealed record TreemapCell(TreemapItem Item, double X, double Y, double Width, double Height);

/// <summary>
/// Bố trí treemap theo thuật toán squarified (Bruls, Huizing, van Wijk): ô gần vuông, diện tích tỉ lệ thuận với giá trị.
/// Thuần tính toán, không phụ thuộc WPF để kiểm thử được. Phần tử có giá trị ≤ 0 bị bỏ qua.
/// </summary>
public static class TreemapLayout
{
    public static IReadOnlyList<TreemapCell> Compute(IEnumerable<TreemapItem> items, double width, double height)
    {
        var sorted = items.Where(i => i.Value > 0).OrderByDescending(i => i.Value).ToList();
        var cells = new List<TreemapCell>();
        if (sorted.Count == 0 || width <= 0 || height <= 0) return cells;

        var total = (double)sorted.Sum(i => i.Value);
        var scale = width * height / total;
        var areas = sorted.Select(i => i.Value * scale).ToList();

        double x = 0, y = 0, w = width, h = height;
        var start = 0;

        while (start < sorted.Count)
        {
            var side = Math.Min(w, h);
            var end = start + 1;
            var rowArea = areas[start];
            var best = Worst(areas, start, end, rowArea, side);

            while (end < sorted.Count)
            {
                var nextArea = rowArea + areas[end];
                var next = Worst(areas, start, end + 1, nextArea, side);
                if (next > best) break;
                rowArea = nextArea;
                best = next;
                end++;
            }

            // Đặt hàng [start, end) dọc theo cạnh ngắn của phần còn lại.
            var thickness = rowArea / side;
            var offset = 0.0;
            for (var i = start; i < end; i++)
            {
                var length = areas[i] / thickness;
                if (w >= h)
                {
                    cells.Add(new TreemapCell(sorted[i], x, y + offset, thickness, length));
                }
                else
                {
                    cells.Add(new TreemapCell(sorted[i], x + offset, y, length, thickness));
                }

                offset += length;
            }

            if (w >= h)
            {
                x += thickness;
                w -= thickness;
            }
            else
            {
                y += thickness;
                h -= thickness;
            }

            start = end;
        }

        return cells;
    }

    /// <summary>Tỉ lệ cạnh xấu nhất của hàng [start, end) nếu đặt trên cạnh dài <paramref name="side"/>.</summary>
    private static double Worst(List<double> areas, int start, int end, double rowArea, double side)
    {
        var max = double.MinValue;
        var min = double.MaxValue;
        for (var i = start; i < end; i++)
        {
            max = Math.Max(max, areas[i]);
            min = Math.Min(min, areas[i]);
        }

        var s2 = side * side;
        var r2 = rowArea * rowArea;
        return Math.Max(s2 * max / r2, r2 / (s2 * min));
    }
}
