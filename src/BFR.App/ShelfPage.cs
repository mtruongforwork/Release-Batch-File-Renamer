namespace BFR.App;

using BFR.Core.Licensing;
using BFR.Presentation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ui = Wpf.Ui.Controls;

/// <summary>
/// Trang Shelf trong khung điều hướng: cùng <see cref="ShelfModel"/> với cửa sổ Shelf nổi,
/// nên tệp thả vào một nơi sẽ hiện ở nơi kia. Thả tệp/thư mục vào trang, rồi "Đổi tên…" để chuyển
/// cả khay sang trang Đổi tên.
/// </summary>
public sealed class ShelfPage : PageBase
{
    private sealed record Row(string Name, string Folder, string FullPath, string Glyph);

    private readonly ShelfModel _model;
    private readonly Action<IReadOnlyList<string>> _rename;
    private readonly Action<ProFeature> _onLimitReached;

    private readonly TextBlock _count = new();
    private readonly ListBox _list = new();
    private readonly TextBlock _hint = new();
    private readonly Ui.Button _clear = new();
    private readonly Ui.Button _renameButton = new();

    public ShelfPage(ShelfModel model, Action<IReadOnlyList<string>> rename, Action<ProFeature> onLimitReached)
    {
        _model = model;
        _rename = rename;
        _onLimitReached = onLimitReached;

        AllowDrop = true;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths);
            e.Handled = true;   // không để cửa sổ chính thêm tệp vào danh sách đổi tên
        };
        System.Windows.Automation.AutomationProperties.SetName(this, "Shelf page");

        var root = new Grid { Margin = new Thickness(28, 20, 28, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = Loc.T("NavShelf"),
            FontSize = (double)Application.Current.FindResource("AppFontSizeTitle"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary")
        });
        _count.Foreground = (Brush)Application.Current.FindResource("TextSecondary");
        _count.Margin = new Thickness(0, 4, 0, 0);
        header.Children.Add(_count);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // Vùng thả: danh sách ở trong, gợi ý ở giữa khi trống.
        _list.BorderThickness = new Thickness(0);
        _list.Background = Brushes.Transparent;
        _list.SelectionMode = SelectionMode.Extended;
        _list.ItemTemplate = BuildRowTemplate();
        _list.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Delete) return;
            foreach (var row in _list.SelectedItems.OfType<Row>().ToList()) _model.Remove(row.FullPath);
            e.Handled = true;
        };
        System.Windows.Automation.AutomationProperties.SetName(_list, "Shelf items");

        _hint.Text = Loc.T("ShelfHint");
        _hint.TextWrapping = TextWrapping.Wrap;
        _hint.TextAlignment = TextAlignment.Center;
        _hint.HorizontalAlignment = HorizontalAlignment.Center;
        _hint.VerticalAlignment = VerticalAlignment.Center;
        _hint.MaxWidth = 420;
        _hint.IsHitTestVisible = false;
        _hint.Foreground = (Brush)Application.Current.FindResource("TextSecondary");

        var zone = new Grid();
        zone.Children.Add(_list);
        zone.Children.Add(_hint);
        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.FindResource("Divider"),
            Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
            Child = zone
        };
        Grid.SetRow(card, 1);
        root.Children.Add(card);

        _clear.Content = Loc.T("ShelfClear");
        _clear.Appearance = Ui.ControlAppearance.Secondary;
        _clear.Click += (_, _) => _model.Clear();
        _renameButton.Content = Loc.T("ShelfRename");
        _renameButton.Appearance = Ui.ControlAppearance.Primary;
        _renameButton.Margin = new Thickness(8, 0, 0, 0);
        _renameButton.Click += (_, _) =>
        {
            if (_model.IsEmpty) return;
            _rename(_model.Snapshot());
            _model.Clear();
        };
        System.Windows.Automation.AutomationProperties.SetName(_renameButton, "Shelf rename all");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_clear);
        buttons.Children.Add(_renameButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        Content = root;
        _model.Items.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    public override void OnShown() => Refresh();

    private void AddPaths(IEnumerable<string> paths)
    {
        var result = _model.Add(paths);
        if (result.HitLimit) _onLimitReached(ProFeature.UnlimitedShelf);
    }

    private static DataTemplate BuildRowTemplate()
    {
        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Row.Glyph)));
        glyph.SetValue(TextBlock.FontFamilyProperty, Application.Current.FindResource("IconFont"));
        glyph.SetValue(TextBlock.FontSizeProperty, 16.0);
        glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        glyph.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 0, 12, 0));
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "Accent");

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Row.Name)));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");

        var folder = new FrameworkElementFactory(typeof(TextBlock));
        folder.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Row.Folder)));
        folder.SetValue(TextBlock.FontSizeProperty, 11.5);
        folder.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        folder.SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");

        var text = new FrameworkElementFactory(typeof(StackPanel));
        text.AppendChild(name);
        text.AppendChild(folder);

        var row = new FrameworkElementFactory(typeof(StackPanel));
        row.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 5, 4, 5));
        row.AppendChild(glyph);
        row.AppendChild(text);

        return new DataTemplate { VisualTree = row };
    }

    private void Refresh()
    {
        _list.ItemsSource = _model.Items.Select(p =>
        {
            var isDir = Directory.Exists(p);
            var name = Path.GetFileName(p.TrimEnd('\\', '/'));
            return new Row(name.Length > 0 ? name : p, Path.GetDirectoryName(p.TrimEnd('\\', '/')) ?? string.Empty, p, isDir ? "" : "");
        }).ToList();

        var limit = _model.Limit == int.MaxValue ? "∞" : _model.Limit.ToString();
        _count.Text = $"{_model.Count} / {limit}";
        _hint.Visibility = _model.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
        _renameButton.IsEnabled = !_model.IsEmpty;
        _clear.IsEnabled = !_model.IsEmpty;
    }
}
