namespace BFR.Presentation.Tools;

using System;
using System.Collections.Generic;
using System.Linq;
using BFR.Core;

/// <summary>Một chức năng trong danh sách bung ra khi bấm chuột phải vào một ô của menu tròn.</summary>
/// <param name="LabelKey">Khoá chuỗi giao diện của nhãn (công cụ); null nếu dùng <paramref name="RawLabel"/>.</param>
/// <param name="RawLabel">Nhãn đã sẵn (preset đổi tên nhanh).</param>
/// <param name="OptionKey">Tuỳ chọn của công cụ sẽ được đặt (hoặc "preset" cho ô Đổi tên).</param>
public sealed record RadialAction(string? LabelKey, string? RawLabel, string OptionKey, string Value);

/// <param name="Key">"rename" hoặc khoá công cụ (organize, duplicates...).</param>
public sealed record RadialItem(string Key, string TitleKey, string Icon, IReadOnlyList<RadialAction> Actions)
{
    public bool IsRename => Key == "rename";
}

/// <summary>
/// Nội dung menu tròn: 8 ô, mỗi ô một công cụ. Rê chuột vào ô thì ô nổi lên kèm tên; bấm chuột phải thì hiện các chức năng
/// của công cụ đó; bấm chuột trái mở công cụ.
/// </summary>
public static class RadialMenuModel
{
    /// <summary>Thứ tự ô theo chiều kim đồng hồ, bắt đầu từ trên cùng.</summary>
    public static readonly IReadOnlyList<string> ToolOrder = new[]
    {
        "organize", "duplicates", "folders", "metadata", "images", "archive", "checksum"
    };

    public const int Count = 8;

    public static IReadOnlyList<RadialItem> Build(IReadOnlyList<ITool> tools, bool vietnamese)
    {
        var items = new List<RadialItem>
        {
            new("rename", "NavRename", "",
                QuickPresets.All.Select(q => new RadialAction(null, q.Title(vietnamese), "preset", q.PresetName)).ToList())
        };

        foreach (var key in ToolOrder)
        {
            var tool = tools.FirstOrDefault(t => t.Key == key);
            if (tool is null) continue;

            var actions = new List<RadialAction>();
            var option = tool.Options.FirstOrDefault(o => o.Key == tool.QuickOptionKey && o.Kind == OptionKind.Choice);
            if (option?.Choices is not null)
            {
                // "custom" cần nhập thêm nên không đưa vào danh sách chức năng nhanh.
                foreach (var (value, labelKey) in option.Choices.Where(c => c.Value != "custom"))
                    actions.Add(new RadialAction(labelKey, null, option.Key, value));
            }

            items.Add(new RadialItem(tool.Key, tool.TitleKey, tool.Icon, actions));
        }

        return items;
    }
}

/// <summary>Hình học của menu tròn (thuần tuý, không phụ thuộc WPF): góc đo theo chiều kim đồng hồ từ hướng lên trên.</summary>
public static class RadialLayout
{
    /// <summary>Góc giữa của ô <paramref name="index"/>, độ.</summary>
    public static double CenterAngle(int index, int count) => index * 360.0 / count;

    /// <summary>Toạ độ của điểm cách tâm <paramref name="radius"/> theo góc <paramref name="angleDegrees"/>.</summary>
    public static (double X, double Y) Point(double cx, double cy, double radius, double angleDegrees)
    {
        var a = angleDegrees * Math.PI / 180.0;
        return (cx + radius * Math.Sin(a), cy - radius * Math.Cos(a));
    }

    /// <summary>Ô nằm dưới con trỏ, hoặc -1 nếu ở trong lỗ giữa hoặc ngoài vòng.</summary>
    public static int IndexAt(double dx, double dy, double innerRadius, double outerRadius, int count)
    {
        var distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < innerRadius || distance > outerRadius) return -1;

        // Góc theo chiều kim đồng hồ từ hướng lên trên, trong [0, 360).
        var angle = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
        if (angle < 0) angle += 360;

        var width = 360.0 / count;
        return (int)Math.Floor((angle + width / 2) / width) % count;
    }

    /// <summary>Ô kế tiếp theo chiều kim đồng hồ (delta = 1) hoặc ngược lại (-1), quay vòng.</summary>
    public static int Step(int current, int delta, int count) => ((current < 0 ? (delta > 0 ? -1 : 0) : current) + delta + count) % count;
}
