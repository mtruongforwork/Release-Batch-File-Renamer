namespace BFR.App.Controls;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BFR.Presentation.Design;

/// <summary>
/// Treemap dung lượng: mỗi ô là một mục, diện tích tỉ lệ với giá trị. Màu đi từ đầu này sang đầu kia của gradient nhấn,
/// chữ trắng (đạt tương phản trên cả hai đầu). Ô quá nhỏ không hiện chữ nhưng vẫn có chú giải khi rê chuột.
/// </summary>
public sealed class TreemapView : Grid
{
    private IReadOnlyList<TreemapItem> _items = Array.Empty<TreemapItem>();
    private readonly Canvas _canvas = new() { ClipToBounds = true };

    public TreemapView()
    {
        MinHeight = 100;
        Height = 140;
        Children.Add(_canvas);
        SizeChanged += (_, _) => Render();
        System.Windows.Automation.AutomationProperties.SetName(this, Loc.T("TreemapName"));
    }

    public void SetItems(IReadOnlyList<TreemapItem> items)
    {
        _items = items;
        Render();
    }

    private static Color ColorKey(string key) =>
        Application.Current.TryFindResource(key) is Color c ? c : Colors.SlateGray;

    private void Render()
    {
        _canvas.Children.Clear();
        if (_items.Count == 0 || ActualWidth < 10 || ActualHeight < 10) return;

        var start = ColorKey("BfrAcc1Color");
        var end = ColorKey("BfrAcc2Color");
        var cells = TreemapLayout.Compute(_items, ActualWidth, ActualHeight);

        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var t = cells.Count == 1 ? 0 : i / (double)(cells.Count - 1);
            var fill = Color.FromRgb(
                (byte)(start.R + (end.R - start.R) * t),
                (byte)(start.G + (end.G - start.G) * t),
                (byte)(start.B + (end.B - start.B) * t));

            var tile = new Border
            {
                Width = Math.Max(0, cell.Width - 3),
                Height = Math.Max(0, cell.Height - 3),
                Background = new SolidColorBrush(fill),
                CornerRadius = new CornerRadius(8),
                ToolTip = cell.Item.Label,
                Child = cell.Width > 90 && cell.Height > 34
                    ? new TextBlock
                    {
                        Text = cell.Item.Label,
                        Foreground = Brushes.White,
                        FontFamily = (FontFamily)Application.Current.FindResource("BfrFontBodySemi"),
                        FontSize = 12,
                        Margin = new Thickness(8, 6, 8, 6),
                        TextWrapping = TextWrapping.Wrap,
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }
                    : null
            };
            Canvas.SetLeft(tile, cell.X + 1.5);
            Canvas.SetTop(tile, cell.Y + 1.5);
            _canvas.Children.Add(tile);
        }
    }
}
