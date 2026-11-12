namespace BFR.App;

using BFR.Core;
using BFR.Core.Licensing;
using BFR.Core.WatchFolder;
using BFR.Presentation.Tools;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using BFR.App.Infrastructure;
using Ui = Wpf.Ui.Controls;
using System.Windows.Controls;
using System.Windows.Automation;

/// <summary>
/// Watch Folder nhiều thư mục (B6, Pro): mỗi quy tắc theo dõi một thư mục và chạy một quy trình
/// (đổi tên theo preset, hoặc cả workflow nhiều bước) cho tệp mới. Các quy tắc được lưu và chạy nền cùng lúc.
/// </summary>
public class WatchFolderPage : PageBase
{
    private sealed record PresetChoice(Preset Preset)
    {
        public override string ToString() => Preset.DisplayName;
    }

    private sealed record KindChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly PresetStore _presetStore;
    private readonly LicenseService _license;
    private readonly RealFileSystem _fs = new();
    private readonly string _journalDir;
    private readonly string _logPath;
    private readonly WatchRuleStore _store;
    private readonly List<WatchRule> _rules;

    private readonly ListBox _ruleList = new() { MinHeight = 90, MaxHeight = 150 };
    private readonly TextBox _nameBox = new() { Width = 240 };
    private readonly TextBox _folderBox = new() { Width = 320 };
    private readonly ComboBox _kindCombo = new() { Width = 320 };
    private readonly ComboBox _presetCombo = new() { Width = 320 };
    private readonly TextBox _workflowFileBox = new() { Width = 320 };
    private readonly CheckBox _enabled = new() { IsChecked = true };
    private readonly CheckBox _recursive = new() { IsChecked = true };
    private readonly CheckBox _dryRun = new();
    private readonly TextBox _settleBox = new() { Text = "1500", Width = 80 };
    private readonly TextBox _includeBox = new() { Width = 140 };
    private readonly TextBox _excludeBox = new() { Width = 140 };
    private readonly ListBox _logList = new();
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _logEmpty = new();
    private readonly Ui.Button _startButton = new();
    private readonly Ui.Button _stopButton = new();
    private readonly Ui.Button _addButton = new();
    private readonly Ui.Button _removeButton = new();
    private readonly List<FrameworkElement> _presetRows = new();
    private readonly List<FrameworkElement> _fileRows = new();

    private WatchFolderManager? _manager;
    private bool _loading;
    private WatchRule? Selected => _ruleList.SelectedItem as WatchRule;

    public WatchFolderPage(PresetStore presetStore, LicenseService license)
    {
        _presetStore = presetStore;
        _license = license;

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchFileRenamer");
        _journalDir = Path.Combine(appData, "journal");
        _logPath = Path.Combine(appData, "watch", "watch.log");
        _store = new WatchRuleStore(_fs, Path.Combine(appData, "watch", "rules.json"));
        _rules = _store.Load();

        AutomationProperties.SetName(this, "Watch folder window");
        FontFamily = (System.Windows.Media.FontFamily)Application.Current.FindResource("AppFont");
        FontSize = (double)Application.Current.FindResource("AppFontSizeBody");

        ApplyStyles();

        var root = new Grid { Margin = new Thickness(28, 20, 28, 16) };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_startButton);
        buttons.Children.Add(_stopButton);
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);

        // Danh sách quy tắc + nút thêm/xoá
        var rulesHeader = new DockPanel();
        rulesHeader.Children.Add(Header(Loc.T("WatchRulesTitle")));
        var ruleButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        ruleButtons.Children.Add(_addButton);
        ruleButtons.Children.Add(_removeButton);
        DockPanel.SetDock(ruleButtons, Dock.Right);
        rulesHeader.Children.Insert(0, ruleButtons);

        _ruleList.ItemsSource = _rules;
        _ruleList.DisplayMemberPath = null;
        _ruleList.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <StackPanel Margin="0,2">
                <TextBlock Text="{Binding Name}" FontWeight="SemiBold"
                           Foreground="{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}" />
                <TextBlock Text="{Binding Folder}" FontSize="11" Opacity="0.75" TextTrimming="CharacterEllipsis"
                           Foreground="{Binding RelativeSource={RelativeSource AncestorType=ListBoxItem}, Path=Foreground}" />
              </StackPanel>
            </DataTemplate>
            """);
        _ruleList.SelectionChanged += (_, _) => LoadSelected();
        AutomationProperties.SetName(_ruleList, "Watch rules");

        var editor = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        editor.Children.Add(Header(Loc.T("WatchOptionsTitle")));
        editor.Children.Add(Row(Loc.T("WatchRuleName"), _nameBox));
        editor.Children.Add(Row(Loc.Plain("WatchFolder"), _folderBox, BrowseButton()));
        editor.Children.Add(Row(Loc.T("WatchKind"), _kindCombo));
        var presetRow = Row(Loc.T("WatchPreset"), _presetCombo);
        var fileRow = Row(Loc.T("WatchWorkflowFile"), _workflowFileBox, WorkflowBrowseButton());
        _presetRows.Add(presetRow);
        _fileRows.Add(fileRow);
        editor.Children.Add(presetRow);
        editor.Children.Add(fileRow);
        editor.Children.Add(Row(Loc.T("WatchSettle"), _settleBox));
        editor.Children.Add(Row(Loc.T("WatchInclude"), _includeBox));
        editor.Children.Add(Row(Loc.T("WatchExclude"), _excludeBox));
        editor.Children.Add(_enabled);
        editor.Children.Add(_recursive);
        editor.Children.Add(_dryRun);

        _addButton.Click += (_, _) => AddRule();
        _removeButton.Click += (_, _) => RemoveRule();
        _startButton.Click += (_, _) => Start();
        _stopButton.Click += (_, _) => Stop();

        // Nhật ký
        _logList.ItemsSource = new List<string>();
        AutomationProperties.SetName(_logList, "Watch folder log");
        _logEmpty.Text = Loc.T("WatchFolderEmpty");
        _logEmpty.TextWrapping = TextWrapping.Wrap;
        _logEmpty.TextAlignment = TextAlignment.Center;
        _logEmpty.HorizontalAlignment = HorizontalAlignment.Center;
        _logEmpty.VerticalAlignment = VerticalAlignment.Center;
        _logEmpty.Margin = new Thickness(24);
        _logEmpty.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary");

        var logGrid = new Grid { MinHeight = 120 };
        logGrid.Children.Add(_logList);
        logGrid.Children.Add(_logEmpty);

        var content = new DockPanel { Margin = new Thickness(0, 12, 0, 0), Height = 150 };
        DockPanel.SetDock(_status, Dock.Bottom);
        content.Children.Add(_status);
        content.Children.Add(logGrid);
        Grid.SetRow(content, 1);

        var top = new StackPanel();
        top.Children.Add(rulesHeader);
        top.Children.Add(_ruleList);
        top.Children.Add(editor);
        var scroll = new ScrollViewer { Content = top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scroll, 0);
        root.Children.Add(scroll);
        root.Children.Add(content);
        Content = root;

        FillKinds();
        RefreshPresets();
        WireEditorEvents();

        if (_rules.Count > 0) _ruleList.SelectedIndex = 0;
        else UpdateEditorState();
        _stopButton.IsEnabled = false;
    }

    private void ApplyStyles()
    {
        foreach (var box in new[] { _nameBox, _folderBox, _workflowFileBox, _settleBox, _includeBox, _excludeBox })
            box.Style = (Style)Application.Current.FindResource("AppTextBoxStyle");
        foreach (var combo in new[] { _kindCombo, _presetCombo })
            combo.Style = (Style)Application.Current.FindResource("AppComboBoxStyle");
        _ruleList.Style = (Style)Application.Current.FindResource("AppListBoxStyle");
        _logList.Style = (Style)Application.Current.FindResource("AppListBoxStyle");
        foreach (var check in new[] { _enabled, _recursive, _dryRun })
            check.Style = (Style)Application.Current.FindResource("AppCheckBoxStyle");
        _enabled.Content = Loc.T("WatchRuleEnabled");
        _recursive.Content = Loc.T("WatchRecursive");
        _dryRun.Content = Loc.T("WatchDryRun");
        _status.Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary");

        Decorate(_startButton, Loc.T("WatchStart"), Ui.ControlAppearance.Primary);
        Decorate(_stopButton, Loc.T("WatchStop"), Ui.ControlAppearance.Secondary);
        Decorate(_addButton, Loc.T("WatchAddRule"), Ui.ControlAppearance.Secondary);
        Decorate(_removeButton, Loc.T("WatchRemoveRule"), Ui.ControlAppearance.Secondary);
    }

    private static void Decorate(Ui.Button button, string text, Ui.ControlAppearance appearance)
    {
        button.Content = text;
        button.Appearance = appearance;
        button.Margin = new Thickness(0, 0, 6, 0);
        button.Padding = new Thickness(12, 6, 12, 6);
    }

    private static TextBlock Header(string text) => new()
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 0, 0, 8),
        Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextPrimary")
    };

    private static StackPanel Row(string label, UIElement element, UIElement? extra = null)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Width = 150,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("TextSecondary")
        });
        panel.Children.Add(element);
        if (extra is not null) panel.Children.Add(extra);
        return panel;
    }

    private Ui.Button BrowseButton()
    {
        var button = new Ui.Button { Content = "…", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4), Appearance = Ui.ControlAppearance.Secondary, ToolTip = Loc.T("WatchBrowseButton") };
        button.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Title = Loc.Plain("WatchFolder") };
            if (dialog.ShowDialog() == true) _folderBox.Text = dialog.FolderName;
        };
        return button;
    }

    private Ui.Button WorkflowBrowseButton()
    {
        var button = new Ui.Button { Content = "…", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4), Appearance = Ui.ControlAppearance.Secondary };
        button.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Filter = "Workflow|*.json" };
            if (dialog.ShowDialog() == true) _workflowFileBox.Text = dialog.FileName;
        };
        return button;
    }

    // ----------------------------------------------------------------- Quy tắc

    private void FillKinds()
    {
        _kindCombo.ItemsSource = new List<KindChoice>
        {
            new("preset", Loc.T("WatchKindPreset")),
            new("photos", Loc.T("ToolWorkflowPhotos")),
            new("tidy", Loc.T("ToolWorkflowTidy")),
            new("file", Loc.T("ToolWorkflowFromFile"))
        };
        _kindCombo.SelectedIndex = 0;
    }

    private void RefreshPresets()
    {
        var presets = _presetStore.LoadAll().Concat(BuiltInPresets.All).Select(p => new PresetChoice(p)).ToList();
        _presetCombo.ItemsSource = presets;
        if (presets.Count > 0) _presetCombo.SelectedIndex = 0;
    }

    private void WireEditorEvents()
    {
        _nameBox.TextChanged += (_, _) => Commit();
        _folderBox.TextChanged += (_, _) => Commit();
        _workflowFileBox.TextChanged += (_, _) => Commit();
        _settleBox.TextChanged += (_, _) => Commit();
        _includeBox.TextChanged += (_, _) => Commit();
        _excludeBox.TextChanged += (_, _) => Commit();
        _kindCombo.SelectionChanged += (_, _) => { UpdateEditorState(); Commit(); };
        _presetCombo.SelectionChanged += (_, _) => Commit();
        foreach (var c in new[] { _enabled, _recursive, _dryRun }) c.Click += (_, _) => Commit();
    }

    private void AddRule()
    {
        if (_rules.Count >= WatchRule.MaxRules)
        {
            _status.Text = Loc.F("CoreWatchTooMany", WatchRule.MaxRules);
            return;
        }

        var rule = new WatchRule { Name = Loc.F("WatchRuleDefaultName", _rules.Count + 1), Folder = string.Empty };
        rule.Steps.Add(new RenameStep(BuiltInPresets.All.First()));
        _rules.Add(rule);
        RefreshList();
        _ruleList.SelectedItem = rule;
    }

    private void RemoveRule()
    {
        if (Selected is not { } rule) return;
        _rules.Remove(rule);
        RefreshList();
        if (_rules.Count > 0) _ruleList.SelectedIndex = 0;
        Save();
    }

    private void RefreshList()
    {
        var selected = _ruleList.SelectedItem;
        _ruleList.ItemsSource = null;
        _ruleList.ItemsSource = _rules;
        if (selected is not null && _rules.Contains(selected)) _ruleList.SelectedItem = selected;
        UpdateEditorState();
    }

    private void LoadSelected()
    {
        _loading = true;
        try
        {
            if (Selected is not { } rule)
            {
                UpdateEditorState();
                return;
            }

            _nameBox.Text = rule.Name;
            _folderBox.Text = rule.Folder;
            _enabled.IsChecked = rule.Enabled;
            _recursive.IsChecked = rule.Recursive;
            _dryRun.IsChecked = rule.DryRun;
            _settleBox.Text = rule.SettleDelayMs.ToString();
            _includeBox.Text = rule.IncludeGlob ?? string.Empty;
            _excludeBox.Text = rule.ExcludeGlob ?? string.Empty;

            // Suy ra kiểu quy trình từ các bước đã lưu.
            if (rule.Steps is [RenameStep rename])
            {
                _kindCombo.SelectedIndex = 0;
                var presets = _presetCombo.ItemsSource as List<PresetChoice>;
                var match = presets?.FirstOrDefault(p => p.Preset.Name == rename.Preset.Name);
                if (match is not null) _presetCombo.SelectedItem = match;
            }
            else
            {
                var template = WorkflowTool.Templates().FirstOrDefault(t => t.Definition.Steps.SequenceEqual(rule.Steps)).Value;
                _kindCombo.SelectedIndex = template switch { "photos" => 1, "tidy" => 2, _ => 3 };
            }
        }
        finally
        {
            _loading = false;
        }
        UpdateEditorState();
    }

    private void UpdateEditorState()
    {
        var has = Selected is not null;
        foreach (var c in new UIElement[] { _nameBox, _folderBox, _kindCombo, _presetCombo, _workflowFileBox, _enabled, _recursive, _dryRun, _settleBox, _includeBox, _excludeBox })
            c.IsEnabled = has;
        _removeButton.IsEnabled = has && _manager is null;

        var kind = (_kindCombo.SelectedItem as KindChoice)?.Value ?? "preset";
        foreach (var row in _presetRows) row.Visibility = kind == "preset" ? Visibility.Visible : Visibility.Collapsed;
        foreach (var row in _fileRows) row.Visibility = kind == "file" ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Ghi giá trị trên màn hình vào quy tắc đang chọn.</summary>
    private void Commit()
    {
        if (_loading || Selected is not { } rule) return;

        rule.Name = _nameBox.Text.Trim();
        rule.Folder = _folderBox.Text.Trim();
        rule.Enabled = _enabled.IsChecked == true;
        rule.Recursive = _recursive.IsChecked == true;
        rule.DryRun = _dryRun.IsChecked == true;
        rule.SettleDelayMs = int.TryParse(_settleBox.Text, out var settle) ? settle : 1500;
        rule.IncludeGlob = string.IsNullOrWhiteSpace(_includeBox.Text) ? null : _includeBox.Text.Trim();
        rule.ExcludeGlob = string.IsNullOrWhiteSpace(_excludeBox.Text) ? null : _excludeBox.Text.Trim();

        var kind = (_kindCombo.SelectedItem as KindChoice)?.Value ?? "preset";
        switch (kind)
        {
            case "photos":
            case "tidy":
                rule.Steps = WorkflowTool.Templates().First(t => t.Value == kind).Definition.Steps.ToList();
                break;
            case "file":
                // Tệp workflow được nạp và kiểm tra khi bắt đầu theo dõi.
                break;
            default:
                if (_presetCombo.SelectedItem is PresetChoice choice) rule.Steps = new List<WorkflowStep> { new RenameStep(choice.Preset) };
                break;
        }

        UpdateEditorState();
        var index = _ruleList.SelectedIndex;
        // Cập nhật tiêu đề trong danh sách mà không mất lựa chọn.
        _ruleList.Items.Refresh();
        _ruleList.SelectedIndex = index;
    }

    private void Save()
    {
        try { _store.Save(_rules); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status.Text = ex.Message;
        }
    }

    // ----------------------------------------------------------------- Chạy

    private List<WatchRule>? BuildRunnableRules()
    {
        var rules = new List<WatchRule>();
        foreach (var rule in _rules)
        {
            var copy = new WatchRule
            {
                Id = rule.Id, Name = rule.Name, Enabled = rule.Enabled, Folder = rule.Folder, Recursive = rule.Recursive,
                IncludeGlob = rule.IncludeGlob, ExcludeGlob = rule.ExcludeGlob, SettleDelayMs = rule.SettleDelayMs,
                MaxFilesPerBatch = rule.MaxFilesPerBatch, DryRun = rule.DryRun, Steps = rule.Steps.ToList()
            };

            // Quy tắc dùng tệp workflow: nạp và kiểm tra ngay để báo lỗi rõ ràng trước khi chạy nền.
            if (rule.Enabled && IsFileKind(rule))
            {
                try { copy.Steps = WorkflowSerializer.Parse(File.ReadAllText(_workflowFileBox.Text)).Steps; }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException or ArgumentException or IOException)
                {
                    _status.Text = Loc.F("CoreToolWorkflowBad", ex.Message);
                    return null;
                }
            }
            rules.Add(copy);
        }
        return rules;
    }

    private bool IsFileKind(WatchRule rule) =>
        ReferenceEquals(rule, Selected) && (_kindCombo.SelectedItem as KindChoice)?.Value == "file";

    private void Start()
    {
        if (!_license.Allows(ProFeature.UnlimitedRecursion))
        {
            MessageBox.Show(FeatureMessages.For(ProFeature.UnlimitedRecursion), Loc.T("ProLockedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!_license.Allows(ProFeature.Workflow))
        {
            MessageBox.Show(FeatureMessages.For(ProFeature.Workflow), Loc.T("ProLockedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (_rules.Count(r => r.Enabled) == 0)
        {
            MessageBox.Show(Loc.T("WatchFolderMissing"), Loc.T("WatchTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var runnable = BuildRunnableRules();
        if (runnable is null) return;

        var errors = WatchFolderManager.Validate(runnable);
        if (errors.Count > 0)
        {
            _status.Text = string.Join(Environment.NewLine, errors.Take(3));
            return;
        }

        foreach (var rule in runnable.Where(r => r.Enabled))
        {
            if (!Directory.Exists(rule.Folder))
            {
                _status.Text = Loc.T("WatchFolderMissing") + " " + rule.Folder;
                return;
            }
        }

        Save();

        try
        {
            Stop();

            var metadata = new CompositeMetadataProvider(new ExifMetadataReader(), new MediaMetadataReader(), new AudioTagReader());
            _manager = new WatchFolderManager(_fs, new WorkflowWatchProcessor(_fs, _journalDir, metadata));
            _manager.Logged += (_, e) => AppendLog(e.Rule, e.Entry);
            _manager.BatchProcessed += (_, e) => Dispatcher.Invoke(() =>
                _status.Text = $"{DisplayName(e.Rule)}: " + Loc.F("WatchBatchDone", e.Result.Renamed, e.Result.Unchanged, e.Result.Skipped, e.Result.Failed));
            _manager.Start(runnable);

            _startButton.IsEnabled = false;
            _stopButton.IsEnabled = true;
            UpdateEditorState();
            _status.Text = Loc.F("WatchRunning", _manager.RunningCount);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            _manager?.Dispose();
            _manager = null;
            MessageBox.Show(ex.Message, Loc.T("WatchTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Stop()
    {
        if (_manager is null) return;

        _manager.Dispose();
        _manager = null;
        _startButton.IsEnabled = true;
        _stopButton.IsEnabled = false;
        UpdateEditorState();
    }

    private static string DisplayName(WatchRule rule) => string.IsNullOrWhiteSpace(rule.Name) ? rule.Folder : rule.Name;

    private void AppendLog(WatchRule rule, WatchFolderLogEntry entry)
    {
        var line = $"[{DisplayName(rule)}] {entry.Format()}";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
            File.AppendAllText(_logPath, LogRedactor.Redact(line) + Environment.NewLine);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nhật ký là thông tin phụ: không làm hỏng chức năng.
        }

        Dispatcher.Invoke(() =>
        {
            if (_logList.ItemsSource is List<string> items)
            {
                items.Add(line);
                while (items.Count > 500) items.RemoveAt(0);
                _logList.Items.Refresh();
                _logEmpty.Visibility = items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
        });
    }

    /// <summary>Dịch vụ theo dõi chạy nền ngay cả khi người dùng chuyển sang trang khác; chỉ dừng khi cửa sổ chính đóng.</summary>
    public override void Shutdown() => Stop();
}
