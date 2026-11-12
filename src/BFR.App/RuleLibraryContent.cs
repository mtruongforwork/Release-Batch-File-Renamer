namespace BFR.App;

using BFR.Presentation;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Ui = Wpf.Ui.Controls;

/// <summary>
/// Thư viện quy tắc (A5): lưới thẻ có biểu tượng, tên và mô tả ngắn, có ô tìm kiếm.
/// Thay cho menu chữ "+ Thêm quy tắc". Bấm một thẻ phát sự kiện <see cref="RuleChosen"/>.
/// </summary>
public sealed class RuleLibraryContent : UserControl
{
    private static readonly (string Type, string Glyph)[] Glyphs =
    {
        ("FindReplace", "↔"), ("Numbering", "#"), ("Trim", "✂"), ("Insert", "＋"), ("Case", "Aa"),
        ("Sanitize", "✓"), ("Date", "◷"), ("Extension", ".ext"), ("Template", "{ }"),
        ("ListMap", "☰"), ("ReplaceDict", "⇄")
    };

    private readonly WrapPanel _cards = new() { Margin = new Thickness(0, 12, 0, 0) };
    private readonly Ui.TextBox _search = new() { PlaceholderText = string.Empty, Icon = new Ui.SymbolIcon(Ui.SymbolRegular.Search24) };

    /// <summary>Người dùng chọn một loại quy tắc (khoá như "FindReplace").</summary>
    public event EventHandler<string>? RuleChosen;

    public RuleLibraryContent()
    {
        Width = 700;
        System.Windows.Automation.AutomationProperties.SetName(this, "Rule library");
        System.Windows.Automation.AutomationProperties.SetName(_search, "Search rules");

        _search.TextChanged += (_, _) => Rebuild();

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 420,
            Content = _cards
        };

        var root = new StackPanel();
        root.Children.Add(_search);
        root.Children.Add(scroll);
        Content = root;

        Rebuild();
        Loaded += (_, _) => _search.Focus();
    }

    private void Rebuild()
    {
        _cards.Children.Clear();
        var strings = new LocStringProvider();
        var query = _search.Text.Trim();

        foreach (var type in RuleCardViewModel.AllRuleTypes)
        {
            var title = RuleTitles.Get(type, null, strings);
            var description = Loc.T("RuleDesc" + type);
            if (query.Length > 0
                && !title.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                && !description.Contains(query, StringComparison.CurrentCultureIgnoreCase)) continue;

            _cards.Children.Add(BuildCard(type, title, description));
        }
    }

    private UIElement BuildCard(string type, string title, string description)
    {
        var glyph = Glyphs.FirstOrDefault(g => g.Type == type).Glyph ?? "•";

        var icon = new Border
        {
            Width = 40,
            Height = 40,
            CornerRadius = new CornerRadius(8),
            Background = (Brush)Application.Current.FindResource("AccentSoft"),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = glyph,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.FindResource("Accent"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };

        var text = new StackPanel { Width = 134 };
        text.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary")
        });
        text.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 52,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = (Brush)Application.Current.FindResource("TextSecondary")
        });

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(icon);
        row.Children.Add(text);

        // ui:Button: có focus bàn phím, Enter/Space, và UI Automation (Border thì không).
        var card = new Ui.Button
        {
            Width = 212,
            Height = 96,
            Margin = new Thickness(0, 0, 10, 10),
            Padding = new Thickness(12),
            Appearance = Ui.ControlAppearance.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.FindResource("Divider"),
            Background = (Brush)Application.Current.FindResource("SurfaceBackground"),
            Content = row
        };
        System.Windows.Automation.AutomationProperties.SetName(card, "Rule card " + type);
        card.Click += (_, _) => RuleChosen?.Invoke(this, type);

        return card;
    }
}
