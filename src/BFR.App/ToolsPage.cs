namespace BFR.App;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BFR.Core;
using BFR.Core.Licensing;
using BFR.Presentation.Tools;
using Ui = Wpf.Ui.Controls;

/// <summary>
/// Trang "Công cụ": danh sách các công cụ tệp bên trái, khung công cụ dùng chung bên phải, cộng thêm màn quản lý khu cách ly.
/// Mỗi công cụ là một <see cref="ITool"/> khai báo tuỳ chọn; trang chỉ dựng giao diện từ khai báo đó.
/// </summary>
public sealed class ToolsPage : PageBase
{
    private sealed record Entry(string Key, string Title, string Icon, ITool? Tool);

    private readonly IFileSystem _fs;
    private readonly LicenseService _license;
    private readonly ToolRunner _runner;
    private readonly string _version;
    private readonly Action<ProFeature?> _requestUpgrade;
    private readonly IMetadataProvider? _metadata;
    private readonly IImageProcessor? _images;

    private readonly ListBox _list = new();
    private readonly Grid _host = new();
    private readonly TextBlock _status = new();
    private readonly Dictionary<string, FrameworkElement> _views = new(StringComparer.OrdinalIgnoreCase);
    private QuarantineView? _quarantine;
    private ToolsHomeView? _home;
    private readonly List<string> _pending = new();
    private List<Entry> _entries = new();

    public ToolsPage(
        IFileSystem fs, LicenseService license, HistoryStore history, string journalDir, string version,
        IMetadataProvider? metadata, IImageProcessor? images, Action<ProFeature?> requestUpgrade)
    {
        _fs = fs;
        _license = license;
        _version = version;
        _metadata = metadata;
        _images = images;
        _requestUpgrade = requestUpgrade;
        _runner = new ToolRunner(fs, journalDir, history, version);

        System.Windows.Automation.AutomationProperties.SetName(this, "Tools page");
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("AppFont");
        FontSize = (double)Application.Current.FindResource("AppFontSizeBody");

        var entries = new List<Entry> { new("home", Loc.T("ToolsHomeTitle"), "", null) };
        entries.AddRange(ToolCatalog.Create().Select(t => new Entry(t.Key, Loc.T(t.TitleKey), t.Icon, t)));
        entries.Add(new Entry("quarantine", Loc.T("ToolQuarantineTitle"), "", null));
        _entries = entries;

        _list.ItemsSource = entries;
        _list.Style = (Style)Application.Current.FindResource("AppListBoxStyle");
        _list.ItemTemplate = BuildItemTemplate();
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is Entry entry) Show(entry);
        };
        System.Windows.Automation.AutomationProperties.SetName(_list, "Tool list");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(236) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new Border
        {
            Child = _list,
            Padding = new Thickness(10, 16, 6, 10),
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("Divider")
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(_host, 1);
        grid.Children.Add(left);
        grid.Children.Add(_host);

        Content = grid;
        _list.SelectedIndex = 0;
    }

    private static DataTemplate BuildItemTemplate()
    {
        return (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Orientation="Horizontal" Margin="0,4">
                <TextBlock Text="{Binding Icon}" FontFamily="Segoe MDL2 Assets" FontSize="16" Width="26" VerticalAlignment="Center"
                           Foreground="{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}" />
                <TextBlock Text="{Binding Title}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" MaxWidth="150"
                           Foreground="{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}" />
              </StackPanel>
            </DataTemplate>
            """);
    }

    private void Show(Entry entry)
    {
        foreach (var v in _views.Values) v.Visibility = Visibility.Collapsed;

        if (!_views.TryGetValue(entry.Key, out var view))
        {
            if (entry.Tool is not null)
            {
                view = new ToolView(entry.Tool, () => (_fs, _license, _runner, _version, _metadata, _images), _requestUpgrade, s => _status.Text = s);
            }
            else if (entry.Key == "home")
            {
                _home = new ToolsHomeView(ToolCatalog.Create(), OpenTool, AddPending);
                _home.SetPending(_pending.Count);
                view = _home;
            }
            else
            {
                _quarantine = new QuarantineView(_fs, _license);
                view = _quarantine;
            }
            _views[entry.Key] = view;
            _host.Children.Add(view);
        }

        view.Visibility = Visibility.Visible;
        if (view is QuarantineView q) q.Refresh();
    }

    /// <summary>
    /// Gửi tệp tới Công cụ (từ danh sách Đổi tên, Shelf...). Nếu đang mở một công cụ thì thêm thẳng vào đó;
    /// nếu không, giữ lại ở Trang chủ để người dùng chọn công cụ.
    /// </summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        if (_list.SelectedItem is Entry { Tool: not null } entry && _views.TryGetValue(entry.Key, out var view) && view is ToolView toolView)
        {
            toolView.AddPaths(paths);
            return;
        }

        AddPending(paths);
        _list.SelectedIndex = 0;
    }

    /// <summary>Mở công cụ theo khoá và đưa tệp vào đó (khoá lạ: về Trang chủ và giữ tệp lại).</summary>
    public void OpenTool(string key, IEnumerable<string> paths)
    {
        AddPending(paths);
        OpenTool(KnownTools.IsKnown(key) ? key : "home");
    }

    private void AddPending(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!string.IsNullOrWhiteSpace(p) && !_pending.Contains(p, StringComparer.OrdinalIgnoreCase)) _pending.Add(p);
        }
        _home?.SetPending(_pending.Count);
    }

    /// <summary>Mở một công cụ từ Trang chủ và đưa các tệp đang chờ vào đó.</summary>
    private void OpenTool(string key)
    {
        var entry = _entries.FirstOrDefault(e => e.Key == key);
        if (entry is null) return;

        if (key == "home")
        {
            _list.SelectedItem = entry;
            _home?.SetPending(_pending.Count);
            return;
        }

        var carry = _pending.ToList();
        _pending.Clear();
        _home?.SetPending(0);
        _list.SelectedItem = entry;
        if (carry.Count > 0 && _views.TryGetValue(key, out var view) && view is ToolView toolView) toolView.AddPaths(carry);
    }

    public override void OnShown() => _quarantine?.Refresh();

    public override void Shutdown()
    {
        foreach (var v in _views.Values.OfType<ToolView>()) v.Shutdown();
    }
}

/// <summary>Khu cách ly: xem dung lượng, mở thư mục, dọn mục hết hạn. Khôi phục từng lần "xoá" thực hiện bằng Hoàn tác trong Lịch sử.</summary>
internal sealed class QuarantineView : UserControl
{
    private readonly IFileSystem _fs;
    private readonly LicenseService _license;
    private readonly TextBlock _usage = new();
    private readonly TextBlock _result = new() { Margin = new Thickness(0, 8, 0, 0) };

    public QuarantineView(IFileSystem fs, LicenseService license)
    {
        _fs = fs;
        _license = license;

        var panel = new StackPanel { Margin = new Thickness(24, 18, 24, 14) };
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("ToolQuarantineTitle"),
            FontSize = (double)Application.Current.FindResource("AppFontSizeTitle"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary")
        });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.F("ToolQuarantineDesc", QuarantineStore.DefaultRetentionDays),
            Style = (Style)Application.Current.FindResource("CaptionTextStyle"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 12),
            MaxWidth = 560,
            HorizontalAlignment = HorizontalAlignment.Left
        });

        _usage.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary");
        panel.Children.Add(_usage);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        var open = new Ui.Button { Content = Loc.T("ToolQuarantineOpen"), Appearance = Ui.ControlAppearance.Secondary, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
        open.Click += (_, _) => Open();
        var purge = new Ui.Button { Content = Loc.F("ToolQuarantinePurge", QuarantineStore.DefaultRetentionDays), Appearance = Ui.ControlAppearance.Secondary, Padding = new Thickness(14, 6, 14, 6) };
        purge.Click += (_, _) => Purge();
        buttons.Children.Add(open);
        buttons.Children.Add(purge);
        panel.Children.Add(buttons);

        _result.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary");
        panel.Children.Add(_result);

        Content = panel;
    }

    public void Refresh()
    {
        var (files, bytes) = new QuarantineStore(_fs).GetUsage();
        _usage.Text = Loc.F("ToolQuarantineUsage", files, FormatSize(bytes));
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:0.#} {units[i]}";
    }

    private void Open()
    {
        var root = new QuarantineStore(_fs).Root;
        try
        {
            Directory.CreateDirectory(root);
            Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            _result.Text = ex.Message;
        }
    }

    private void Purge()
    {
        // Không bao giờ dọn tệp mới hơn thời hạn tối thiểu: khu cách ly là lưới an toàn cuối cùng.
        var removed = new QuarantineStore(_fs).Purge(TimeSpan.FromDays(QuarantineStore.DefaultRetentionDays));
        _result.Text = Loc.F("ToolQuarantinePurged", removed);
        Refresh();
    }
}
