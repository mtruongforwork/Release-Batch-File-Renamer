namespace BFR.App;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using BFR.Core;
using BFR.Core.Licensing;
using BFR.Presentation.Tools;
using Microsoft.Win32;
using Ui = Wpf.Ui.Controls;

/// <summary>
/// Giao diện chung của một công cụ: vùng đầu vào (kéo thả, thêm tệp/thư mục), tuỳ chọn dựng từ <see cref="ToolOption"/>,
/// nút Xem trước/Áp dụng/Huỷ, danh sách kết quả và cảnh báo. Mọi công cụ dùng chung một khung này.
/// </summary>
internal sealed class ToolView : UserControl
{
    private const string RowTemplateXaml = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Grid Margin="0,3" Background="Transparent">
            <Grid.ColumnDefinitions>
              <ColumnDefinition Width="3*" />
              <ColumnDefinition Width="2*" />
            </Grid.ColumnDefinitions>
            <TextBlock x:Name="Primary" Text="{Binding Primary}" TextTrimming="CharacterEllipsis"
                       Foreground="{DynamicResource TextPrimary}" ToolTip="{Binding Detail}" />
            <TextBlock x:Name="Secondary" Grid.Column="1" Margin="12,0,0,0" Text="{Binding Secondary}" TextTrimming="CharacterEllipsis"
                       Foreground="{DynamicResource TextSecondary}" ToolTip="{Binding Secondary}" />
          </Grid>
          <DataTemplate.Triggers>
            <DataTrigger Binding="{Binding State}" Value="Header">
              <Setter TargetName="Primary" Property="FontWeight" Value="SemiBold" />
              <Setter TargetName="Primary" Property="Foreground" Value="{DynamicResource Accent}" />
            </DataTrigger>
            <DataTrigger Binding="{Binding State}" Value="Warning">
              <Setter TargetName="Secondary" Property="Foreground" Value="{DynamicResource Warning}" />
            </DataTrigger>
            <DataTrigger Binding="{Binding State}" Value="Error">
              <Setter TargetName="Secondary" Property="Foreground" Value="{DynamicResource Error}" />
              <Setter TargetName="Primary" Property="Foreground" Value="{DynamicResource Error}" />
            </DataTrigger>
            <DataTrigger Binding="{Binding State}" Value="Ok">
              <Setter TargetName="Secondary" Property="Foreground" Value="{DynamicResource Success}" />
            </DataTrigger>
          </DataTemplate.Triggers>
        </DataTemplate>
        """;

    private sealed record Choice(string Value, string Label)
    {
        // Ô chọn dùng mẫu giao diện riêng nên hiển thị ToString() của mục đang chọn.
        public override string ToString() => Label;
    }

    private readonly ITool _tool;
    private readonly Func<(IFileSystem Fs, LicenseService License, ToolRunner Runner, string Version, IMetadataProvider? Metadata, IImageProcessor? Images)> _services;
    private readonly Action<ProFeature?> _requestUpgrade;
    private readonly Action<string> _statusChanged;

    private readonly List<string> _paths = new();
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrameworkElement> _optionRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action<string>> _setters = new(StringComparer.OrdinalIgnoreCase);

    private readonly TextBlock _inputSummary = new();
    private readonly ListBox _inputList = new() { MaxHeight = 96 };
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly ItemsControl _messages = new();
    private readonly ListBox _results = new();
    private readonly BFR.App.Controls.TreemapView _treemap = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
    private readonly TextBlock _empty = new();
    private readonly ProgressBar _progress = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    private readonly Ui.Button _previewButton = new();
    private readonly Ui.Button _applyButton = new();
    private readonly Ui.Button _cancelButton = new();
    private readonly Ui.Button _exportButton = new();
    private readonly Ui.Button _upgradeButton = new();
    private readonly Ui.Button _undoButton = new();
    private readonly List<ContentControl> _stepDots = new();
    private readonly List<Border> _stepLines = new();

    private ToolPlan? _plan;
    private CancellationTokenSource? _cts;
    private string? _lastJournal;
    private bool _busy;

    public ToolView(
        ITool tool,
        Func<(IFileSystem, LicenseService, ToolRunner, string, IMetadataProvider?, IImageProcessor?)> services,
        Action<ProFeature?> requestUpgrade,
        Action<string> statusChanged)
    {
        _tool = tool;
        _services = services;
        _requestUpgrade = requestUpgrade;
        _statusChanged = statusChanged;

        foreach (var option in tool.Options) _values[option.Key] = option.Default;

        AllowDrop = true;
        Drop += OnDrop;
        DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };

        Content = BuildLayout();
        UpdateButtons();
        RefreshVisibility();
    }

    private static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

    private static Style AppStyle(string key) => (Style)Application.Current.FindResource(key);

    // ----------------------------------------------------------------- Dựng giao diện

    private UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(24, 18, 24, 14), LastChildFill = true };

        // Tiêu đề
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        header.Children.Add(new TextBlock
        {
            Text = Loc.T(_tool.TitleKey),
            FontSize = (double)Application.Current.FindResource("AppFontSizeTitle"),
            FontWeight = FontWeights.SemiBold,
            Foreground = Res("TextPrimary")
        });
        header.Children.Add(new TextBlock
        {
            Text = Loc.T(_tool.DescriptionKey),
            Style = AppStyle("CaptionTextStyle"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        // Thanh bước: Chọn tệp → Tuỳ chọn → Xem trước → Áp dụng (công cụ chỉ đọc không có bước cuối).
        var stepper = BuildStepper();
        DockPanel.SetDock(stepper, Dock.Top);
        root.Children.Add(stepper);

        // Thanh hành động (dưới cùng)
        var actions = BuildActions();
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        // Phần trên: đầu vào + tuỳ chọn (cuộn được khi nhiều)
        var top = new StackPanel();
        top.Children.Add(BuildInput());
        top.Children.Add(BuildOptions());
        var topScroll = new ScrollViewer
        {
            Content = top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = _tool.ReadOnly ? 250 : 330
        };
        DockPanel.SetDock(topScroll, Dock.Top);
        root.Children.Add(topScroll);

        // Tóm tắt + cảnh báo
        var info = new StackPanel { Margin = new Thickness(0, 10, 0, 6) };
        _summary.Foreground = Res("TextPrimary");
        info.Children.Add(_summary);
        _messages.ItemTemplate = (DataTemplate)XamlReader.Parse("""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
              <TextBlock Text="{Binding}" TextWrapping="Wrap" Margin="0,2,0,0" Foreground="{DynamicResource Warning}" />
            </DataTemplate>
            """);
        info.Children.Add(_messages);
        DockPanel.SetDock(info, Dock.Top);
        root.Children.Add(info);

        DockPanel.SetDock(_treemap, Dock.Top);
        root.Children.Add(_treemap);

        // Danh sách kết quả
        _results.Style = AppStyle("AppListBoxStyle");
        _results.ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplateXaml);
        _results.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(_results, ScrollBarVisibility.Disabled);
        VirtualizingPanel.SetIsVirtualizing(_results, true);
        _results.SelectionChanged += OnRowSelected;
        AutomationHelper(_results, "Tool results");

        _empty.Text = Loc.T("ToolsEmptyResults");
        _empty.TextWrapping = TextWrapping.Wrap;
        _empty.TextAlignment = TextAlignment.Center;
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.Margin = new Thickness(24);
        _empty.Foreground = Res("TextSecondary");

        var resultGrid = new Grid();
        resultGrid.Children.Add(_results);
        resultGrid.Children.Add(_empty);
        root.Children.Add(resultGrid);

        return root;
    }

    private static void AutomationHelper(DependencyObject element, string name) =>
        System.Windows.Automation.AutomationProperties.SetName(element, name);

    private UIElement BuildInput()
    {
        var card = new Border
        {
            Style = AppStyle("CardStyle"),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var panel = new StackPanel();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var hint = new TextBlock
        {
            Text = Loc.T(_tool.AcceptsFiles ? "ToolsDropFilesHint" : "ToolsDropFolderHint"),
            Foreground = Res("TextSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 380
        };
        buttons.Children.Add(hint);

        if (_tool.AcceptsFiles)
        {
            var addFiles = MakeButton(Loc.Plain("AddFiles"));
            addFiles.Click += (_, _) =>
            {
                var dialog = new OpenFileDialog { Multiselect = true };
                if (dialog.ShowDialog() == true) AddPaths(dialog.FileNames);
            };
            buttons.Children.Add(addFiles);
        }

        var addFolder = MakeButton(Loc.Plain("AddFolder"));
        addFolder.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog { Multiselect = true };
            if (dialog.ShowDialog() == true) AddPaths(dialog.FolderNames);
        };
        buttons.Children.Add(addFolder);

        var clear = MakeButton(Loc.Plain("ClearList"));
        clear.Click += (_, _) => { _paths.Clear(); RefreshInput(); ResetPlan(); };
        buttons.Children.Add(clear);

        panel.Children.Add(buttons);

        _inputSummary.Margin = new Thickness(0, 8, 0, 4);
        _inputSummary.Foreground = Res("TextSecondary");
        panel.Children.Add(_inputSummary);

        _inputList.Style = AppStyle("AppListBoxStyle");
        _inputList.Visibility = Visibility.Collapsed;
        AutomationHelper(_inputList, "Tool input list");
        panel.Children.Add(_inputList);

        card.Child = panel;
        RefreshInput();
        return card;
    }

    private UIElement BuildOptions()
    {
        var panel = new StackPanel { Margin = new Thickness(2, 4, 2, 0) };

        foreach (var option in _tool.Options)
        {
            var row = BuildOption(option);
            _optionRows[option.Key] = row;
            panel.Children.Add(row);
        }

        return panel;
    }

    private FrameworkElement BuildOption(ToolOption option)
    {
        var row = new StackPanel { Margin = new Thickness(0, 3, 0, 3) };
        var line = new StackPanel { Orientation = Orientation.Horizontal };

        if (option.Kind == OptionKind.Toggle)
        {
            var check = new CheckBox
            {
                Content = Loc.T(option.LabelKey),
                Style = AppStyle("AppCheckBoxStyle"),
                IsChecked = string.Equals(option.Default, "true", StringComparison.OrdinalIgnoreCase)
            };
            check.Click += (_, _) => SetValue(option.Key, check.IsChecked == true ? "true" : "false");
            _setters[option.Key] = v => { check.IsChecked = string.Equals(v, "true", StringComparison.OrdinalIgnoreCase); SetValue(option.Key, check.IsChecked == true ? "true" : "false"); };
            line.Children.Add(check);
        }
        else
        {
            line.Children.Add(new TextBlock
            {
                Text = Loc.T(option.LabelKey),
                Width = 190,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("TextSecondary"),
                TextWrapping = TextWrapping.Wrap
            });
            var editor = BuildEditor(option);
            NameForNarrator(editor, Loc.T(option.LabelKey));
            line.Children.Add(editor);
        }

        row.Children.Add(line);

        if (option.HintKey is not null)
        {
            row.Children.Add(new TextBlock
            {
                Text = Loc.T(option.HintKey),
                Style = AppStyle("CaptionTextStyle"),
                Margin = new Thickness(option.Kind == OptionKind.Toggle ? 24 : 190, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 520
            });
        }

        return row;
    }

    /// <summary>Gán tên cho điều khiển để Narrator đọc đúng nhãn (ô văn bản, danh sách chọn, ô thư mục/tệp).</summary>
    private static void NameForNarrator(FrameworkElement editor, string label)
    {
        if (editor is Panel panel)
        {
            foreach (var child in panel.Children.OfType<TextBox>()) AutomationHelper(child, label);
            foreach (var child in panel.Children.OfType<Ui.Button>()) AutomationHelper(child, label + " …");
        }
        else
        {
            AutomationHelper(editor, label);
        }
    }

    private FrameworkElement BuildEditor(ToolOption option)
    {
        switch (option.Kind)
        {
            case OptionKind.Choice:
            {
                var combo = new ComboBox { Style = AppStyle("AppComboBoxStyle"), MinWidth = 280, DisplayMemberPath = nameof(Choice.Label) };
                var items = option.Choices!.Select(c => new Choice(c.Value, Loc.T(c.LabelKey))).ToList();
                combo.ItemsSource = items;
                combo.SelectedItem = items.FirstOrDefault(i => i.Value == option.Default) ?? items.FirstOrDefault();
                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is Choice c) SetValue(option.Key, c.Value);
                };
                _setters[option.Key] = v =>
                {
                    if (items.FirstOrDefault(i => i.Value == v) is { } match) combo.SelectedItem = match;
                };
                return combo;
            }
            case OptionKind.MultiLine:
            {
                var box = new TextBox
                {
                    Style = AppStyle("AppTextBoxStyle"),
                    Text = option.Default,
                    AcceptsReturn = true,
                    Width = 320,
                    Height = 110,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    TextWrapping = TextWrapping.NoWrap
                };
                box.TextChanged += (_, _) => SetValue(option.Key, box.Text);
                _setters[option.Key] = v => box.Text = v;
                return box;
            }
            case OptionKind.Folder:
            case OptionKind.File:
            {
                var box = new TextBox { Style = AppStyle("AppTextBoxStyle"), Text = option.Default, Width = 300 };
                box.TextChanged += (_, _) => SetValue(option.Key, box.Text);
                _setters[option.Key] = v => box.Text = v;
                var browse = MakeButton("…");
                browse.Margin = new Thickness(4, 0, 0, 0);
                browse.Click += (_, _) =>
                {
                    if (option.Kind == OptionKind.Folder)
                    {
                        var dialog = new OpenFolderDialog();
                        if (dialog.ShowDialog() == true) box.Text = dialog.FolderName;
                    }
                    else
                    {
                        var dialog = new OpenFileDialog { Filter = option.Filter ?? "*|*.*" };
                        if (dialog.ShowDialog() == true) box.Text = dialog.FileName;
                    }
                };
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(box);
                panel.Children.Add(browse);
                return panel;
            }
            default:
            {
                var box = new TextBox
                {
                    Style = AppStyle("AppTextBoxStyle"),
                    Text = option.Default,
                    Width = option.Kind == OptionKind.Number ? 100 : 320
                };
                box.TextChanged += (_, _) => SetValue(option.Key, box.Text);
                _setters[option.Key] = v => box.Text = v;
                return box;
            }
        }
    }

    /// <summary>Đặt sẵn giá trị tuỳ chọn (ví dụ chọn chức năng từ menu tròn). Khoá lạ bị bỏ qua.</summary>
    public void ApplyValues(IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, value) in values)
        {
            if (_setters.TryGetValue(key, out var set)) set(value);
        }
    }

    private UIElement BuildActions()
    {
        var bar = new DockPanel { Margin = new Thickness(0, 10, 0, 0), LastChildFill = false };

        _previewButton.Content = Loc.T("ToolPreview");
        _previewButton.Appearance = Ui.ControlAppearance.Secondary;
        _previewButton.Click += async (_, _) => await PreviewAsync();

        _applyButton.Content = Loc.T("ToolApply");
        _applyButton.Appearance = Ui.ControlAppearance.Primary;
        _applyButton.FontWeight = FontWeights.SemiBold;
        _applyButton.Click += async (_, _) => await ApplyAsync();

        _cancelButton.Content = Loc.T("ToolCancel");
        _cancelButton.Appearance = Ui.ControlAppearance.Secondary;
        _cancelButton.Click += (_, _) => _cts?.Cancel();

        _exportButton.Content = Loc.T("ToolExport");
        _exportButton.Appearance = Ui.ControlAppearance.Secondary;
        _exportButton.Click += (_, _) => Export();

        _upgradeButton.Content = Loc.T("ToolUpgrade");
        _upgradeButton.Appearance = Ui.ControlAppearance.Caution;
        _upgradeButton.Click += (_, _) => _requestUpgrade(_plan?.UpgradeFeature);

        _undoButton.Content = Loc.T("ToolUndoLast");
        _undoButton.Appearance = Ui.ControlAppearance.Secondary;
        _undoButton.Click += (_, _) => UndoLast();

        foreach (var b in new[] { _previewButton, _applyButton, _cancelButton, _exportButton, _upgradeButton, _undoButton })
        {
            b.Margin = new Thickness(0, 0, 8, 0);
            b.Padding = new Thickness(14, 6, 14, 6);
            DockPanel.SetDock(b, Dock.Left);
            bar.Children.Add(b);
        }

        AutomationHelper(_previewButton, "Tool preview");
        AutomationHelper(_applyButton, "Tool apply");

        var host = new StackPanel();
        host.Children.Add(bar);
        host.Children.Add(_progress);
        return host;
    }

    private static Ui.Button MakeButton(string text) => new()
    {
        Content = text,
        Appearance = Ui.ControlAppearance.Secondary,
        Margin = new Thickness(0, 0, 6, 0),
        Padding = new Thickness(12, 5, 12, 5)
    };

    // ----------------------------------------------------------------- Đầu vào

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] dropped) AddPaths(dropped);
        e.Handled = true;
    }

    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var p in paths)
        {
            if (!string.IsNullOrWhiteSpace(p) && !_paths.Contains(p, StringComparer.OrdinalIgnoreCase)) _paths.Add(p);
        }
        RefreshInput();
        ResetPlan();
    }

    private void RefreshInput()
    {
        _inputSummary.Text = _paths.Count == 0 ? Loc.T("ToolsNoInput") : Loc.F("ToolsInputCount", _paths.Count);
        _inputList.ItemsSource = null;
        _inputList.ItemsSource = _paths.Take(200).ToList();
        _inputList.Visibility = _paths.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        UpdateButtons();
    }

    // ----------------------------------------------------------------- Tuỳ chọn

    private void SetValue(string key, string value)
    {
        _values[key] = value;
        RefreshVisibility();
        ResetPlan();
    }

    private void RefreshVisibility()
    {
        foreach (var option in _tool.Options)
        {
            if (!_optionRows.TryGetValue(option.Key, out var row)) continue;
            row.Visibility = IsOptionVisible(option) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private bool IsOptionVisible(ToolOption option)
    {
        if (option.VisibleWhen is null) return true;
        var parts = option.VisibleWhen.Split('=', 2);
        return _values.TryGetValue(parts[0], out var current) && parts[1].Split('|').Contains(current, StringComparer.OrdinalIgnoreCase);
    }

    // ----------------------------------------------------------------- Kế hoạch

    private void ResetPlan()
    {
        if (_busy) return;
        DisposePlan();
        _plan = null;
        _results.ItemsSource = null;
        _treemap.Visibility = Visibility.Collapsed;
        _messages.ItemsSource = null;
        _summary.Text = string.Empty;
        _empty.Visibility = Visibility.Visible;
        UpdateButtons();
    }

    private void DisposePlan()
    {
        try { _plan?.Cleanup?.Invoke(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private ToolContext NewContext()
    {
        var (fs, license, _, _, metadata, images) = _services();
        var temp = Path.Combine(PathGuard.BfrTempRoot, "tool-" + Guid.NewGuid().ToString("N"));
        return new ToolContext(_paths.ToList(), new Dictionary<string, string>(_values), fs, license, temp, metadata, images);
    }

    private async System.Threading.Tasks.Task PreviewAsync()
    {
        if (_busy) return;
        DisposePlan();
        _plan = null;

        SetBusy(true);
        _cts = new CancellationTokenSource();
        var progress = new Progress<string>(text => _statusChanged(text));

        try
        {
            var plan = await _tool.PlanAsync(NewContext(), _cts.Token, progress);
            ShowPlan(plan);
        }
        catch (OperationCanceledException)
        {
            _summary.Text = Loc.T("ToolCancelled");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _summary.Text = ex.Message;
            _plan = null;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void ShowPlan(ToolPlan plan)
    {
        _plan = plan;
        _results.ItemsSource = plan.Rows;
        _treemap.SetItems(plan.Treemap ?? Array.Empty<BFR.Presentation.Design.TreemapItem>());
        _treemap.Visibility = plan.Treemap is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        _messages.ItemsSource = plan.Messages.Take(20).ToList();
        _summary.Text = plan.Summary;
        _empty.Visibility = plan.Rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _empty.Text = plan.Rows.Count == 0 ? Loc.T("ToolsNothingToShow") : string.Empty;
        UpdateButtons();
    }

    private void OnRowSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_busy || _results.SelectedItem is not ToolRow row) return;

        var updated = _tool.OnRowActivated(row);
        if (updated is null) return;

        ShowPlan(updated); // kế hoạch cũ và mới dùng chung tệp tạm (nếu có) nên không dọn kế hoạch cũ ở đây
    }

    // ----------------------------------------------------------------- Áp dụng

    private async System.Threading.Tasks.Task ApplyAsync()
    {
        if (_busy || _plan is not { CanApply: true } plan) return;

        var (_, license, runner, _, _, _) = _services();
        var message = Loc.F("ToolConfirmApply", plan.Operations.Count);
        if (MessageBox.Show(message, Loc.T(_tool.TitleKey), MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes) return;

        SetBusy(true);
        _cts = new CancellationTokenSource();
        try
        {
            var token = _cts.Token;
            var label = _tool.Key;
            var result = await System.Threading.Tasks.Task.Run(() => runner.Execute(plan, label, license.MaxUndoEntries, token));

            _lastJournal = result.JournalPath;
            _plan = null; // đã áp dụng: dọn tệp tạm xong trong runner, bắt buộc xem trước lại
            _results.ItemsSource = null;
            _messages.ItemsSource = result.Errors.Take(10).Select(e => $"{e.Step}: {e.Error}").ToList();
            _summary.Text = Loc.F("ToolDone", result.Succeeded, result.Total) + (result.Failed > 0 ? " " + Loc.F("ToolDoneFailed", result.Failed) : string.Empty);
            _empty.Text = Loc.T("ToolUndoHint");
            _empty.Visibility = Visibility.Visible;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _summary.Text = ex.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UndoLast()
    {
        if (_lastJournal is null) return;
        var (_, _, runner, _, _, _) = _services();
        var result = runner.Undo(_lastJournal);
        _summary.Text = Loc.F("UndoMessage", result.Undone, result.Total) + (result.Failed > 0 ? Loc.F("UndoPartial", result.Failed) : string.Empty);
        _lastJournal = null;
        UpdateButtons();
    }

    private void Export()
    {
        if (_plan?.Export is not { } export) return;

        var (_, license, _, _, _, _) = _services();
        if (export.RequiresFeature is { } feature && !license.Allows(feature))
        {
            _requestUpgrade(feature);
            return;
        }

        var dialog = new SaveFileDialog { FileName = export.SuggestedName, Filter = export.FileFilter };
        if (dialog.ShowDialog() != true) return;

        try { File.WriteAllText(dialog.FileName, export.Content); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, Loc.T(_tool.TitleKey), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private FrameworkElement BuildStepper()
    {
        var keys = _tool.ReadOnly ? new[] { "StepFiles", "StepOptions", "StepReview" } : new[] { "StepFiles", "StepOptions", "StepReview", "StepApply" };
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        for (var i = 0; i < keys.Length; i++)
        {
            if (i > 0)
            {
                var line = new Border { Style = AppStyle("BfrStepLine"), Width = 28 };
                _stepLines.Add(line);
                strip.Children.Add(line);
            }

            var dot = new ContentControl { Style = AppStyle("BfrStepDot"), Content = (i + 1).ToString() };
            _stepDots.Add(dot);
            strip.Children.Add(dot);
            strip.Children.Add(new TextBlock
            {
                Text = Loc.T(keys[i]),
                Style = AppStyle("BfrCaptionText"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            });
        }

        System.Windows.Automation.AutomationProperties.SetName(strip, Loc.T("StepsName"));
        return strip;
    }

    /// <summary>Bước hiện tại suy ra từ trạng thái: chưa có tệp → 1; chưa xem trước → 2; đã có kế hoạch → 3; đã áp dụng → 4 (xong).</summary>
    private void UpdateStepper()
    {
        if (_stepDots.Count == 0) return;

        var hasInput = _paths.Count > 0 || NeedsNoInput();
        var current = !hasInput ? 0 : _plan is null ? 1 : _lastJournal is not null && !_tool.ReadOnly ? _stepDots.Count : 2;

        for (var i = 0; i < _stepDots.Count; i++)
        {
            _stepDots[i].Tag = i < current ? "done" : i == current ? "current" : null;
            if (i > 0) _stepLines[i - 1].Tag = i <= current ? "done" : null;
        }
    }

    // ----------------------------------------------------------------- Trạng thái nút

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateButtons();
        if (!busy) _statusChanged(string.Empty);
    }

    private void UpdateButtons()
    {
        _previewButton.IsEnabled = !_busy && (_paths.Count > 0 || NeedsNoInput());
        _applyButton.IsEnabled = !_busy && _plan is { CanApply: true };
        _applyButton.Visibility = _tool.ReadOnly ? Visibility.Collapsed : Visibility.Visible;
        _cancelButton.Visibility = _busy ? Visibility.Visible : Visibility.Collapsed;
        _exportButton.Visibility = _plan?.Export is not null ? Visibility.Visible : Visibility.Collapsed;
        _upgradeButton.Visibility = _plan?.UpgradeFeature is not null ? Visibility.Visible : Visibility.Collapsed;
        _undoButton.Visibility = _lastJournal is not null && !_busy ? Visibility.Visible : Visibility.Collapsed;
        UpdateStepper();
    }

    /// <summary>Kiểm tra tệp checksum không cần danh sách đầu vào: chỉ cần chọn tệp .sha256.</summary>
    private bool NeedsNoInput() =>
        _tool.Key == "checksum" && _values.TryGetValue("mode", out var mode) && mode == "verify";

    public void Shutdown()
    {
        _cts?.Cancel();
        DisposePlan();
    }
}
