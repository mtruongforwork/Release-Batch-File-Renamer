namespace BFR.App;

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BFR.App.Controls;

/// <summary>
/// Trang chủ kiểu Bento: một vùng thả tệp lớn dẫn vào Đổi tên, các lối tắt tới công cụ hay dùng, Theo dõi thư mục và Lịch sử.
/// Dựng hoàn toàn từ token và thành phần của bộ giao diện mới (Themes/Bfr).
/// </summary>
internal sealed class HomePage : PageBase
{
    private readonly Action<string> _navigate;
    private readonly Action<IReadOnlyList<string>> _dropFiles;
    private readonly Action<string> _openTool;
    private ContentControl? _drop;

    public HomePage(Action<string> navigate, Action<IReadOnlyList<string>> dropFiles, Action<string> openTool)
    {
        _navigate = navigate;
        _dropFiles = dropFiles;
        _openTool = openTool;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = Build() };
    }

    private static Style S(string key) => (Style)Application.Current.FindResource(key);

    private UIElement Build()
    {
        var root = new StackPanel { Margin = new Thickness(32, 24, 32, 24), MaxWidth = 1040, HorizontalAlignment = HorizontalAlignment.Left };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(new TemMascot { Width = 64, Height = 67, Margin = new Thickness(0, 0, 16, 0) });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(new TextBlock { Text = Loc.T("HomeTitle"), Style = S("BfrTitleText") });
        titles.Children.Add(new TextBlock { Text = Loc.T("HomeSubtitle"), Style = S("BfrBodyText"), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("BfrFg2"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap });
        header.Children.Add(titles);
        root.Children.Add(header);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(16) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Ô lớn: thả tệp để đổi tên.
        var hero = new Border { Style = S("BfrHeroCard"), Padding = new Thickness(16) };
        var dropContent = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        dropContent.Children.Add(new TextBlock { Text = Loc.T("HomeDropTitle"), Style = S("BfrHeadingText"), HorizontalAlignment = HorizontalAlignment.Center });
        dropContent.Children.Add(new TextBlock { Text = Loc.T("HomeDropHint"), Style = S("BfrBodyText"), Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("BfrFg2"), Margin = new Thickness(0, 4, 0, 14), HorizontalAlignment = HorizontalAlignment.Center });
        var go = new Button { Content = Loc.T("HomeDropButton"), Style = S("BfrPrimaryButton"), HorizontalAlignment = HorizontalAlignment.Center };
        go.Click += (_, _) => _navigate("Rename");
        dropContent.Children.Add(go);

        _drop = new ContentControl { Style = S("BfrDropZone"), MinHeight = 200, Content = dropContent, Focusable = false, IsTabStop = false };
        System.Windows.Automation.AutomationProperties.SetName(_drop, Loc.T("HomeDropTitle"));
        _drop.DragEnter += (_, e) => { if (e.Data.GetDataPresent(DataFormats.FileDrop)) _drop.Tag = "drag"; };
        _drop.DragLeave += (_, _) => _drop.Tag = null;
        _drop.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        _drop.Drop += (_, e) =>
        {
            _drop.Tag = null;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped && dropped.Length > 0) _dropFiles(dropped);
            e.Handled = true;
        };
        hero.Child = _drop;
        Grid.SetColumn(hero, 0);
        Grid.SetRow(hero, 0);
        Grid.SetRowSpan(hero, 3);
        grid.Children.Add(hero);

        // Công cụ nhanh.
        var tools = new Border { Style = S("BfrCard") };
        var toolsStack = new StackPanel();
        toolsStack.Children.Add(new TextBlock { Text = Loc.T("HomeToolsTitle"), Style = S("BfrHeadingText"), Margin = new Thickness(0, 0, 0, 10) });
        foreach (var (key, title) in new[] { ("organize", "ToolOrganizeTitle"), ("duplicates", "ToolDuplicatesTitle"), ("images", "ToolImagesTitle"), ("analyze", "ToolAnalyzeTitle") })
        {
            var toolKey = key;
            var button = new Button { Content = Loc.T(title), Style = S("BfrSecondaryButton"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 6) };
            button.Click += (_, _) => _openTool(toolKey);
            toolsStack.Children.Add(button);
        }

        var more = new Button { Content = Loc.T("HomeToolsAll"), Style = S("BfrGhostButton"), HorizontalAlignment = HorizontalAlignment.Left };
        more.Click += (_, _) => _navigate("Tools");
        toolsStack.Children.Add(more);
        tools.Child = toolsStack;
        Grid.SetColumn(tools, 2);
        Grid.SetRow(tools, 0);
        grid.Children.Add(tools);

        // Theo dõi thư mục và Lịch sử.
        var side = new UniformGrid { Columns = 2 };
        side.Children.Add(LinkCard("HomeWatchTitle", "HomeWatchHint", "Watch", new Thickness(0, 0, 8, 0)));
        side.Children.Add(LinkCard("HomeHistoryTitle", "HomeHistoryHint", "History", new Thickness(8, 0, 0, 0)));
        Grid.SetColumn(side, 2);
        Grid.SetRow(side, 2);
        grid.Children.Add(side);

        root.Children.Add(grid);
        return root;
    }

    private Border LinkCard(string titleKey, string hintKey, string page, Thickness margin)
    {
        var card = new Border { Style = S("BfrInteractiveCard"), Margin = margin, Padding = new Thickness(14) };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = Loc.T(titleKey), Style = S("BfrBodyText"), FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("BfrFontBodySemi") });
        stack.Children.Add(new TextBlock { Text = Loc.T(hintKey), Style = S("BfrCaptionText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        card.Child = stack;
        System.Windows.Automation.AutomationProperties.SetName(card, Loc.T(titleKey));
        card.MouseLeftButtonUp += (_, _) => _navigate(page);
        card.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space) { _navigate(page); e.Handled = true; }
        };
        return card;
    }
}
