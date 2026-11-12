namespace BFR.App;

using BFR.Core;
using BFR.Core.Licensing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using BFR.App.Infrastructure;
using Ui = Wpf.Ui.Controls;
using System.Windows.Controls;
using System.Windows.Automation;

/// <summary>
/// Cửa sổ Thư viện preset: lưu preset hiện tại vào thư viện cục bộ, áp dụng hoặc xoá.
/// Bản Free lưu tối đa 3 preset; Pro lưu không giới hạn (PHẦN 3.6).
/// </summary>
public class PresetsPage : PageBase
{
    private readonly PresetStore _store;
    private readonly LicenseService _license;
    private readonly ListBox _list;
    private readonly TextBlock _status;
    private readonly TextBox _search;
    private readonly TextBlock _empty;
    private List<Preset> _all = new();

    private readonly Func<Preset> _currentProvider;

    /// <summary>Người dùng bấm "Áp dụng" trên một preset: cửa sổ chính nạp preset rồi quay về trang Đổi tên.</summary>
    public event EventHandler<Preset>? PresetApplied;

    /// <summary>Danh sách preset có thể đã đổi (lưu/xoá): cửa sổ chính nạp lại danh sách ở combobox.</summary>
    public event EventHandler? Left;

    public override void OnShown()
    {
        Refresh();
        UpdateStatus();
    }

    public override void OnHidden() => Left?.Invoke(this, EventArgs.Empty);

    public PresetsPage(PresetStore store, Func<Preset> currentProvider, LicenseService license)
    {
        _store = store;
        _currentProvider = currentProvider;
        _license = license;

        AutomationProperties.SetName(this, "Preset library window");

        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("AppFont");
        FontSize = (double)Application.Current.FindResource("AppFontSizeBody");

        var root = new DockPanel { Margin = new Thickness(28, 20, 28, 16) };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0)
        };
        DockPanel.SetDock(buttons, Dock.Bottom);

        var saveButton = MakeButton(Loc.T("LibrarySave"), "SecondaryButtonStyle");
        saveButton.Click += (_, _) => SaveCurrent(_currentProvider());
        var applyButton = MakeButton(Loc.T("LibraryApply"), "PrimaryButtonStyle");
        applyButton.Click += (_, _) => ApplySelected();
        var shareButton = MakeButton(Loc.T("SharePreset"), "SecondaryButtonStyle");
        shareButton.Click += (_, _) => ShareSelected();
        var importButton = MakeButton(Loc.T("ImportFromClipboard"), "SecondaryButtonStyle");
        importButton.Click += (_, _) => ImportFromClipboard();
        var deleteButton = MakeButton(Loc.T("LibraryDelete"), "SecondaryButtonStyle");
        deleteButton.Click += (_, _) => DeleteSelected();

        buttons.Children.Add(saveButton);
        buttons.Children.Add(applyButton);
        buttons.Children.Add(shareButton);
        buttons.Children.Add(importButton);
        buttons.Children.Add(deleteButton);

        // Ô tìm kiếm preset
        _search = new TextBox
        {
            Margin = new Thickness(0, 0, 0, 10),
            Style = (Style)Application.Current.FindResource("AppTextBoxStyle"),
            ToolTip = Loc.T("PresetLibrarySearch")
        };
        _search.TextChanged += (_, _) => ApplyFilter();
        AutomationProperties.SetName(_search, "Search presets");

        _list = new ListBox
        {
            Style = (Style)Application.Current.FindResource("AppListBoxStyle"),
            DisplayMemberPath = nameof(Preset.DisplayName)
        };
        _list.SelectionChanged += (_, _) => UpdateStatus();
        AutomationProperties.SetName(_list, "Saved presets");

        _empty = new TextBlock
        {
            Text = Loc.T("PresetLibraryEmpty"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(24),
            Visibility = Visibility.Collapsed
        };
        AutomationProperties.SetName(_empty, "Preset library empty");

        _status = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary")
        };

        var listGrid = new Grid();
        listGrid.Children.Add(_list);
        listGrid.Children.Add(_empty);

        var content = new DockPanel();
        DockPanel.SetDock(_status, Dock.Bottom);
        content.Children.Add(_status);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(new TextBlock
        {
            Text = Loc.T("LibraryTitle"),
            FontSize = (double)Application.Current.FindResource("AppFontSizeTitle"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary")
        });
        header.Children.Add(_search);
        DockPanel.SetDock(header, Dock.Top);
        content.Children.Add(header);

        // DockPanel: phần tử con đầu được đặt sát mép, phần tử cuối chiếm phần còn lại.
        content.Children.Add(listGrid);   // phần tử cuối: chiếm phần còn lại của DockPanel
        root.Children.Add(buttons);
        root.Children.Add(content);

        Content = root;

        Refresh();
        UpdateStatus();
    }

    /// <summary>Tạo nút theo style của theme hiện tại.</summary>
    private static Ui.Button MakeButton(string content, string styleKey)
    {
        var button = new Ui.Button
        {
            Content = content,
            Appearance = DialogChrome.AppearanceFor(styleKey),
            Margin = new Thickness(0, 0, 6, 0),
            Padding = new Thickness(12, 6, 12, 6)
        };

        return button;
    }

    /// <summary>Lọc danh sách preset theo từ khoá.</summary>
    private void ApplyFilter()
    {
        var query = _search.Text.Trim();
        _list.ItemsSource = query.Length == 0
            ? _all
            : _all.Where(p => p.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)).ToList();

        _empty.Visibility = _list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Refresh()
    {
        _all = _store.LoadAll().ToList();
        ApplyFilter();
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        var count = _store.LoadAll().Count;
        if (count == 0)
        {
            _status.Text = Loc.T("LibraryEmpty");
            return;
        }

        var max = _license.MaxSavedPresets;
        _status.Text = max == int.MaxValue
            ? $"{count} preset"
            : $"{count}/{max} preset — " + Loc.T("LibraryLimitHint");
    }

    private void SaveCurrent(Preset current)
    {
        var existing = _store.LoadAll();
        var alreadyExists = existing.Any(p => p.Name.Equals(current.Name, StringComparison.OrdinalIgnoreCase));
        if (!alreadyExists)
        {
            var gate = _license.CheckPresetCount(existing.Count + 1);
            if (!gate.Allowed)
            {
                MessageBox.Show(gate.Message, Loc.T("ProLockedTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
        }

        _store.Save(current);
        Refresh();
        _status.Text = Loc.F("LibrarySaved", current.Name);
    }

    private void ApplySelected()
    {
        if (_list.SelectedItem is Preset preset)
        {
            PresetApplied?.Invoke(this, preset);
        }
    }

    private void DeleteSelected()
    {
        if (_list.SelectedItem is not Preset preset) return;

        _store.Delete(preset.Name);
        Refresh();
    }

    /// <summary>Chia sẻ preset: chép JSON vào bảng nhớ tạm để gửi cho người khác.</summary>
    private void ShareSelected()
    {
        if (_list.SelectedItem is not Preset preset) return;

        try
        {
            Clipboard.SetText(_store.ToJson(preset));
            _status.Text = Loc.T("PresetCopied");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("LibraryTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Nhập preset từ bảng nhớ tạm (preset được chia sẻ).</summary>
    private void ImportFromClipboard()
    {
        try
        {
            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            var preset = _store.FromJson(text);
            var existing = _store.LoadAll();
            var gate = _license.CheckPresetCount(existing.Count + 1);
            if (!gate.Allowed)
            {
                MessageBox.Show(gate.Message, Loc.T("ProLockedTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _store.Save(preset);
            Refresh();
            _status.Text = Loc.F("PresetImported", preset.DisplayName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, Loc.T("LibraryTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
