namespace BFR.App;

using BFR.Presentation.Tools;
using BFR.Core;
using BFR.App.Infrastructure;
using BFR.Core.Licensing;
using BFR.Core.Reports;
using BFR.Presentation;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using FluentWindow = Wpf.Ui.Controls.FluentWindow;

/// <summary>
/// Cửa sổ chính (bố cục v2.0 theo docs/Plan/BFR_UIUX_REBUILD_PLAN.md):
/// CommandBar 1 hàng · Rule Builder bên trái · bảng xem trước bên phải · thanh hành động dưới.
///
/// Code-behind chỉ làm việc nối view ↔ <see cref="MainViewModel"/>; toàn bộ logic đổi tên
/// vẫn nằm trong BFR.Core nên kết quả test INV-1..INV-6 không đổi.
/// </summary>
public partial class MainWindow : FluentWindow
{
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    private const int SW_RESTORE = 9;

    private readonly MainViewModel _vm;
    private readonly LicenseCoordinator _license;
    private readonly RealFileSystem _fs = new();
    private readonly DispatcherTimer _collectorTimer;
    private readonly List<string> _collectorBuffer = new();
    private readonly List<string> _renamedPaths = new();

    public MainWindow() : this(null, null)
    {
    }

    public MainWindow(IpcPayload? initialPayload, LicenseCoordinator? license)
    {
        InitializeComponent();

        _license = license ?? new LicenseCoordinator(
            new LicenseService(new LicenseStore(_fs), trialEnabled: false));
        _vm = new MainViewModel(_license, initialPayload, null, new LocStringProvider(), new WpfUiDispatcher());
        DataContext = _vm;
        _shelfModel = new ShelfModel(_license.License);

        _collectorTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _collectorTimer.Tick += (_, _) => OnCollectorTimeout();

        KeyDown += MainWindow_KeyDown;
        Loaded += (_, _) =>
        {
            ThemeManager.RefreshBackdrop(this);
            UpdateLicenseState();
        };

        PresetCombo.ItemsSource = _vm.Presets;
        if (_vm.SelectedPreset is not null)
        {
            // Giữ preset do Explorer/IPC chỉ định (không bị combobox ghi đè).
            PresetCombo.SelectedItem = _vm.SelectedPreset;
        }
        else if (_vm.Presets.Count > 0)
        {
            PresetCombo.SelectedIndex = 0;
        }
        SortCombo.SelectedIndex = 0;
        Loaded += (_, _) =>
        {
            ApplyShakeSetting();
            ApplyHotkeySetting();
            // Dev: BFR_GALLERY=2 mở thẳng Gallery khi khởi động (dùng cho chụp ảnh tự động).
            if (Environment.GetEnvironmentVariable("BFR_GALLERY") == "2")
            {
                try { new BfrGalleryWindow { Owner = this }.Show(); }
                catch (Exception ex) { System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bfr-gallery-error.txt"), ex.ToString()); }
            }
        };
        Closed += (_, _) =>
        {
            _shakeHook?.Dispose();
            _hotkey?.Dispose();
            foreach (var page in _pages.Values) page.Shutdown();
            _shakeHook = null;
            _shelf?.Close();
        };
        ShelfButtonText.Text = Loc.T("ShelfButton");
        ShelfButton.ToolTip = Loc.T("ShelfTooltip");

        _vm.ProLimitReached += (_, feature) => ShowUpgradeDialog(feature);
        Loaded += (_, _) => (Application.Current as App)?.ApplyBackgroundSetting();

        if (_vm.IsEmptyStateVisible) _ = _vm.RebuildAsync();
    }

    // ----------------------------------------------------------------- API cho App

    public void ApplyPresetByName(string name)
    {
        var preset = _vm.Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (preset is not null) PresetCombo.SelectedItem = preset;
    }

    public void EnqueuePaths(IEnumerable<string> paths)
    {
        lock (_collectorBuffer)
        {
            foreach (var p in paths)
            {
                if (!string.IsNullOrWhiteSpace(p)) _collectorBuffer.Add(p);
            }
        }

        _collectorTimer.Stop();
        _collectorTimer.Start();
        ActivateAndForeground();
    }

    public void UpdateLicenseState()
    {
        var status = _license.Status;
        TierBadgeText.Text = status.IsTrial ? Loc.T("TierTrial") : Loc.T(status.IsPro ? "TierPro" : "TierFree");
        UpgradeButton.Visibility = status.IsPro ? Visibility.Collapsed : Visibility.Visible;
        TierBadge.ToolTip = status.IsPro ? Loc.T("LicenseInfoTitle") : Loc.T("UpgradeHeader");
    }

    /// <summary>Bấm badge cấp: nếu đã là Pro thì mở màn hình thông tin license.</summary>
    private void TierBadge_Click(object sender, RoutedEventArgs e)
    {
        // Đã là Pro: hộp thoại hiện thông tin license; Free/dùng thử: hộp thoại nâng cấp.
        ShowUpgradeDialog(contextFeature: null);
    }

    public void ActivateAndForeground()
    {
        try
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            Activate();
            Topmost = true;
            Topmost = false;
            Focus();

            var handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero)
            {
                ShowWindow(handle, SW_RESTORE);
                SetForegroundWindow(handle);
            }
        }
        catch
        {
            // ignored
        }
    }

    private void OnCollectorTimeout()
    {
        _collectorTimer.Stop();
        List<string> batch;
        lock (_collectorBuffer)
        {
            batch = _collectorBuffer.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            _collectorBuffer.Clear();
        }

        if (batch.Count == 0) return;
        NavigateTo("Rename");
        _vm.AddPaths(batch);
    }

    // ----------------------------------------------------------------- Thanh lệnh

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Multiselect = true, Title = Loc.T("AddFiles") };
        if (dialog.ShowDialog() == true) _vm.AddPaths(dialog.FileNames);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = Loc.T("AddFolder") };
        if (dialog.ShowDialog() == true) _vm.AddPaths(new[] { dialog.FolderName });
    }

    private void ClearList_Click(object sender, RoutedEventArgs e) => _vm.ClearList();

    /// <summary>Gửi các tệp đang có trong danh sách sang trang Công cụ (Trang chủ của Công cụ để chọn công cụ).</summary>
    private void SendToTools_Click(object sender, RoutedEventArgs e) => SendToTools(_vm.Rows.Select(r => r.File.FullPath).ToList());

    /// <summary>Mở một công cụ theo khoá (từ menu Explorer hoặc dòng lệnh) và đưa các tệp vào đó.</summary>
    public void OpenTool(string toolKey, IReadOnlyList<string> paths, IReadOnlyDictionary<string, string>? values = null)
    {
        NavigateTo("Tools");
        if (GetPage("Tools") is ToolsPage tools) tools.OpenTool(toolKey, paths, values);
    }

    // ----------------------------------------------------------------- Menu tròn

    private GlobalHotkey? _radialHotkey;
    private RadialMenuWindow? _radial;

    /// <summary>Mở menu tròn tại toạ độ màn hình (vật lý). Đang mở thì đóng lại.</summary>
    public void ShowRadial(int x, int y)
    {
        if (_radial is not null)
        {
            _radial.Close();
            return;
        }

        var items = RadialMenuModel.Build(ToolCatalog.Create(), Loc.Current == "vi");
        _radial = new RadialMenuWindow(items, OnRadialChosen);
        _radial.Closed += (_, _) => _radial = null;
        _radial.ShowAt(x, y);
    }

    private void ShowRadialAtCursor()
    {
        var (x, y) = GlobalHotkey.CursorPosition();
        ShowRadial(x, y);
    }

    private void OnRadialChosen(RadialItem item, RadialAction? action)
    {
        ActivateAndForeground();

        if (item.IsRename)
        {
            NavigateTo("Rename");
            if (action is not null) ApplyPresetByName(action.Value);
            return;
        }

        var values = action is null
            ? null
            : new Dictionary<string, string> { [action.OptionKey] = action.Value };
        OpenTool(item.Key, Array.Empty<string>(), values);
    }

    /// <summary>Đăng ký/gỡ phím tắt toàn cục mở menu tròn theo cài đặt.</summary>
    private void ApplyRadialHotkeySetting()
    {
        var settings = SettingsStore.Load();
        BFR.Core.HotkeySpec? wanted = null;
        if (settings.RadialHotkeyEnabled && BFR.Core.HotkeySpec.TryParse(settings.RadialHotkey, out var parsed)) wanted = parsed;

        if (_radialHotkey is not null && (wanted is null || _radialHotkey.Spec != wanted))
        {
            _radialHotkey.Dispose();
            _radialHotkey = null;
        }

        if (wanted is null || _radialHotkey is not null) return;

        try
        {
            _radialHotkey = new GlobalHotkey(wanted, () => Dispatcher.BeginInvoke(ShowRadialAtCursor));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(Loc.F("HotkeyRegisterFailed", wanted.ToString()), "Batch File Renamer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void SendToTools(IReadOnlyList<string> paths)
    {
        NavigateTo("Tools");
        if (GetPage("Tools") is ToolsPage tools) tools.AddPaths(paths);
    }

    private ShelfWindow? _shelf;
    private ShelfModel _shelfModel = null!;
    private readonly Dictionary<string, PageBase> _pages = new();
    private string _currentPage = "Rename";
    private bool _navigating;
    private MouseHook? _shakeHook;
    private GlobalHotkey? _hotkey;

    /// <summary>Đăng ký/gỡ phím tắt toàn cục mở Shelf theo cài đặt hiện tại (B4).</summary>
    public void ApplyHotkeySetting()
    {
        ApplyRadialHotkeySetting();
        var settings = SettingsStore.Load();
        BFR.Core.HotkeySpec? wanted = null;
        if (settings.ShelfHotkeyEnabled && BFR.Core.HotkeySpec.TryParse(settings.ShelfHotkey, out var parsed)) wanted = parsed;

        if (_hotkey is not null && (wanted is null || _hotkey.Spec != wanted))
        {
            _hotkey.Dispose();
            _hotkey = null;
        }

        if (wanted is null || _hotkey is not null) return;

        try
        {
            _hotkey = new GlobalHotkey(wanted, () => Dispatcher.BeginInvoke(() =>
            {
                var (x, y) = GlobalHotkey.CursorPosition();
                OpenShelfAt(x, y, autoClose: false);
            }));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Tổ hợp đang được ứng dụng khác dùng: báo để người dùng chọn tổ hợp khác.
            MessageBox.Show(Loc.F("HotkeyRegisterFailed", wanted.ToString()), "Batch File Renamer",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Bật/tắt hook lắc chuột theo cài đặt hiện tại.</summary>
    public void ApplyShakeSetting()
    {
        var enabled = SettingsStore.Load().ShakeToOpenShelf;
        if (enabled && _shakeHook is null)
        {
            try
            {
                _shakeHook = new MouseHook(new ShakeDetector());
                _shakeHook.Shaken += (x, y) => Dispatcher.BeginInvoke(() => OpenShelfAt(x, y));
            }
            catch
            {
                _shakeHook = null;   // không cài được hook: bỏ qua, không làm hỏng ứng dụng
            }
        }
        else if (!enabled && _shakeHook is not null)
        {
            _shakeHook.Dispose();
            _shakeHook = null;
        }
    }

    /// <summary>Mở Shelf ngay tại con trỏ (toạ độ vật lý) để thả tệp đang kéo vào.</summary>
    private void OpenShelfAt(int screenX, int screenY, bool autoClose = true)
    {
        var created = OpenShelf(activate: false);
        if (_shelf is null) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var vx = screenX / dpi.DpiScaleX;
        var vy = screenY / dpi.DpiScaleY;
        var maxLeft = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - _shelf.Width;
        var maxTop = SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - _shelf.Height;
        _shelf.Left = Math.Clamp(vx - _shelf.Width / 2, SystemParameters.VirtualScreenLeft, Math.Max(SystemParameters.VirtualScreenLeft, maxLeft));
        _shelf.Top = Math.Clamp(vy - 80, SystemParameters.VirtualScreenTop, Math.Max(SystemParameters.VirtualScreenTop, maxTop));
        // Mở bằng lắc mà không thả gì thì tự đóng, tránh để lại cửa sổ thừa.
        if (created && autoClose) _shelf.AutoCloseWhenUnused(TimeSpan.FromSeconds(6));
    }

    private void Shelf_Click(object sender, RoutedEventArgs e) => OpenShelf();

    /// <summary>Mở (hoặc đưa lên trước) khay tệp gom từ nhiều thư mục.</summary>
    public bool OpenShelf(IEnumerable<string>? paths = null, bool activate = true)
    {
        var created = _shelf is null;
        if (created)
        {
            _shelf = new ShelfWindow(
                _shelfModel,
                batch => EnqueuePaths(batch),
                feature => ShowUpgradeDialog(feature))
            {
                Topmost = true
            };
            _shelf.ShowActivated = activate;
            // Cửa sổ chính đang minimize thì Left/Top không dùng được: lấy vùng đã lưu trước khi thu nhỏ.
            var area = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            _shelf.Left = Math.Max(0, area.Right - _shelf.Width - 24);
            _shelf.Top = Math.Max(0, area.Top + 96);
            _shelf.Closed += (_, _) => _shelf = null;
            _shelf.Show();
        }
        else
        {
            if (activate) _shelf?.Activate();
        }

        if (paths is not null) _shelf?.AddPaths(paths);
        return created;
    }

    private void SortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SortCombo.SelectedItem is ComboBoxItem { Tag: string tag }
            && Enum.TryParse<SortBy>(tag, out var sort))
        {
            _vm.SortBy = sort;
            _ = _vm.RebuildAsync();
        }
    }

    private void Filter_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag }
            && Enum.TryParse<PreviewFilter>(tag, out var filter))
        {
            _vm.Filter = filter;
        }
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetCombo.SelectedItem is Preset preset) _vm.LoadPreset(preset);
    }

    /// <summary>Menu "⋯ Thêm": gom mọi tính năng nâng cao ra khỏi luồng chính.</summary>
    private void MoreMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("AppContextMenuStyle") };

        menu.Items.Add(MenuItem(Loc.T("MenuWatchFolder"), WatchFolder_Click));
        menu.Items.Add(MenuItem(Loc.T("MenuContextMenu"), ContextMenu_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Loc.T("MenuHistory"), History_Click));
        menu.Items.Add(MenuItem(Loc.T("MenuExportReport"), ExportReport_Click));
        menu.Items.Add(MenuItem(Loc.T("MenuPresetLibrary"), PresetLibrary_Click));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Loc.T("MenuImportExportPreset"), ImportExportPreset_Click));
        menu.Items.Add(MenuItem(Loc.T("MenuCheckUpdate"), CheckUpdate_Click));
        menu.Items.Add(MenuItem(Loc.T("MenuRateApp"), RateApp_Click));

        menu.PlacementTarget = (FrameworkElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static MenuItem MenuItem(string header, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = header, Style = (Style)Application.Current.FindResource("AppMenuItemStyle") };
        item.Click += handler;
        return item;
    }

    // ----------------------------------------------------------------- Rule Builder

    private void PresetMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("AppContextMenuStyle") };
        menu.Items.Add(MenuItem(Loc.T("RuleMenuSave"), SavePreset_Click));
        menu.Items.Add(MenuItem(Loc.T("RuleMenuImport"), ImportPreset_Click));
        menu.Items.Add(MenuItem(Loc.T("RuleMenuExport"), ExportPreset_Click));
        menu.Items.Add(MenuItem(Loc.T("RuleMenuManage"), PresetLibrary_Click));

        menu.PlacementTarget = (FrameworkElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Nhập danh sách từ tệp CSV/TSV vào ô tham số nhiều dòng (tính năng Pro).</summary>
    private void ImportListFile_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleParameterViewModel parameter) return;
        if (!_vm.CanImportListFile()) return;

        var dialog = new OpenFileDialog { Filter = Loc.T("ListImportFilter"), Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            parameter.TextValue = System.IO.File.ReadAllText(dialog.FileName);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(ex.Message, "Batch File Renamer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Thêm quy tắc: thư viện thẻ có tìm kiếm trong hộp thoại nổi (thay cho menu chữ).</summary>
    private async void AddRule_Click(object sender, RoutedEventArgs e)
    {
        var library = new RuleLibraryContent();
        var dialog = new Wpf.Ui.Controls.ContentDialog(DialogHost)
        {
            Title = Loc.T("RuleLibraryTitle"),
            Content = library,
            CloseButtonText = Loc.T("CloseDialog"),
            DialogMaxWidth = 800
        };

        string? chosen = null;
        library.RuleChosen += (_, type) =>
        {
            chosen = type;
            dialog.Hide();
        };

        ActivateAndForeground();
        await dialog.ShowAsync();

        if (chosen is not null) _vm.AddRule(chosen);
    }

    private void RuleToggleExpand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RuleCardViewModel card }) card.IsExpanded = !card.IsExpanded;
    }

    private void RuleDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: RuleCardViewModel card }) _vm.RemoveRule(card);
    }

    private void RuleMoreMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RuleCardViewModel card } btn) return;

        var menu = new ContextMenu { Style = (Style)FindResource("AppContextMenuStyle") };

        var expandItem = new MenuItem
        {
            Header = card.IsExpanded ? Loc.T("RuleCollapse") : Loc.T("RuleEditHint"),
            Style = (Style)FindResource("AppMenuItemStyle")
        };
        System.Windows.Automation.AutomationProperties.SetName(expandItem, "Rule menu expand");
        expandItem.Click += (_, _) => card.IsExpanded = !card.IsExpanded;
        menu.Items.Add(expandItem);

        var upItem = new MenuItem
        {
            Header = Loc.T("RuleMoveUp"),
            Style = (Style)FindResource("AppMenuItemStyle"),
            IsEnabled = card.MoveUpCommand.CanExecute(null)
        };
        System.Windows.Automation.AutomationProperties.SetName(upItem, "Rule menu move up");
        upItem.Click += (_, _) => card.MoveUpCommand.Execute(null);
        menu.Items.Add(upItem);

        var downItem = new MenuItem
        {
            Header = Loc.T("RuleMoveDown"),
            Style = (Style)FindResource("AppMenuItemStyle"),
            IsEnabled = card.MoveDownCommand.CanExecute(null)
        };
        System.Windows.Automation.AutomationProperties.SetName(downItem, "Rule menu move down");
        downItem.Click += (_, _) => card.MoveDownCommand.Execute(null);
        menu.Items.Add(downItem);

        if (card.CanOpenRegexTester)
        {
            var regexItem = new MenuItem
            {
                Header = Loc.T("RegexTesterTitle"),
                Style = (Style)FindResource("AppMenuItemStyle")
            };
            regexItem.Click += (_, _) => OpenRegexTesterFor(card);
            menu.Items.Add(regexItem);
        }

        menu.Items.Add(new Separator());

        var deleteItem = new MenuItem
        {
            Header = Loc.T("RuleDelete"),
            Style = (Style)FindResource("AppMenuItemStyle")
        };
        deleteItem.Click += (_, _) => _vm.RemoveRule(card);
        menu.Items.Add(deleteItem);

        menu.PlacementTarget = btn;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ---------------------------------------------------------------- Kéo-thả đổi thứ tự quy tắc

    private void RuleDragStart_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        if (element.DataContext is not RuleCardViewModel card) return;
        if (e.ChangedButton != MouseButton.Left) return;

        var data = new DataObject(typeof(RuleCardViewModel), card);
        DragDrop.DoDragDrop(element, data, DragDropEffects.Move);
    }

    private void RuleCard_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(RuleCardViewModel))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void RuleCard_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(RuleCardViewModel)) is not RuleCardViewModel dragged) return;
        if (sender is not FrameworkElement { DataContext: RuleCardViewModel target }) return;
        if (ReferenceEquals(dragged, target)) return;

        _vm.MoveRuleTo(dragged, _vm.Rules.IndexOf(target));
        e.Handled = true;
    }

    // ---------------------------------------------------------------- Trình thử Regex

    private void RuleRegexTester_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: RuleCardViewModel card }) return;
        OpenRegexTesterFor(card);
    }

    private void OpenRegexTesterFor(RuleCardViewModel card)
    {
        var vm = new RegexTesterViewModel(
            new LocStringProvider(),
            new WpfUiDispatcher(),
            (pattern, replacement, ignoreCase, replaceAll) =>
                ApplyRegexToRule(card, pattern, replacement, ignoreCase, replaceAll));

        // Điền sẵn giá trị hiện tại của quy tắc.
        vm.Pattern = ReadParameter(card, "pattern");
        vm.Replacement = ReadParameter(card, "replacement");
        vm.IgnoreCase = ReadToggle(card, "ignoreCase");
        vm.Recalculate();

        new RegexTesterWindow(vm, () => _vm.SelectedRow?.OldName, this).ShowDialog();
    }

    private static string ReadParameter(RuleCardViewModel card, string key) =>
        card.Parameters.FirstOrDefault(p => p.Key == key)?.TextValue ?? string.Empty;

    private static bool ReadToggle(RuleCardViewModel card, string key) =>
        card.Parameters.FirstOrDefault(p => p.Key == key)?.BoolValue ?? false;

    private static void ApplyRegexToRule(
        RuleCardViewModel card,
        string pattern,
        string replacement,
        bool ignoreCase,
        bool replaceAll)
    {
        foreach (var parameter in card.Parameters)
        {
            switch (parameter.Key)
            {
                case "pattern":
                    parameter.TextValue = pattern;
                    break;
                case "replacement":
                    parameter.TextValue = replacement;
                    break;
                case "ignoreCase":
                    parameter.BoolValue = ignoreCase;
                    break;
                case "replaceAll":
                    parameter.BoolValue = replaceAll;
                    break;
                case "scope":
                    parameter.ChoiceValue = "Both";
                    break;
                case "isRegex":
                    parameter.BoolValue = true;
                    break;
            }
        }
    }

    private void SuggestPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string name })
        {
            var preset = _vm.Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (preset is not null) PresetCombo.SelectedItem = preset;
        }
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        var name = _vm.SelectedPreset?.Name;
        if (string.IsNullOrWhiteSpace(name)) return;

        var dialog = new SaveFileDialog
        {
            Filter = "BFR Preset (*.bfrpreset)|*.bfrpreset",
            FileName = name + ".bfrpreset"
        };
        if (dialog.ShowDialog() != true) return;

        var preset = new Preset
        {
            Name = Path.GetFileNameWithoutExtension(dialog.FileName),
            Description = _vm.SelectedPreset?.Description ?? string.Empty,
            Options = _vm.SelectedPreset?.Options ?? new PresetOptions(),
            Rules = _vm.Rules.Select(r => r.Definition).ToList()
        };

        new PresetStore(_fs).Export(preset, dialog.FileName);
        StatusText.Text = Loc.F("SavedPreset", dialog.FileName);
    }

    private void ImportPreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "BFR Preset (*.bfrpreset)|*.bfrpreset", Multiselect = false };
        if (dialog.ShowDialog() != true) return;

        Preset preset;
        try
        {
            preset = new PresetStore(_fs).Import(dialog.FileName);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, Loc.T("WindowTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        PresetCombo.SelectedItem = null;
        _vm.LoadPreset(preset);
        StatusText.Text = Loc.F("ImportedPreset", preset.Name);
        _ = _vm.RebuildAsync();
    }

    private void ExportPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPreset is null) return;

        var dialog = new SaveFileDialog
        {
            Filter = "BFR Preset (*.bfrpreset)|*.bfrpreset",
            FileName = _vm.SelectedPreset.Name + ".bfrpreset"
        };
        if (dialog.ShowDialog() != true) return;

        new PresetStore(_fs).Export(_vm.SelectedPreset, dialog.FileName);
        StatusText.Text = Loc.F("SavedPreset", dialog.FileName);
    }

    private void ImportExportPreset_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Style = (Style)FindResource("AppContextMenuStyle") };
        menu.Items.Add(MenuItem(Loc.T("RuleMenuImport"), ImportPreset_Click));
        menu.Items.Add(MenuItem(Loc.T("RuleMenuExport"), ExportPreset_Click));

        menu.PlacementTarget = (FrameworkElement)sender;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    // ----------------------------------------------------------------- Áp dụng / hoàn tác

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        var summary = _vm.BuildApplySummary();
        if (summary.FileCount == 0) return;

        var confirmed = await ShowDialogAsync(
            ApplyConfirmContent.Title(summary),
            ApplyConfirmContent.Build(summary),
            primaryText: Loc.T("ConfirmApplyButton"),
            closeText: Loc.T("ConfirmCancel"));
        if (!confirmed) return;

        await _vm.ApplyAsync();
        CheckAndPromptStoreRating();
    }

    private async void Undo_Click(object sender, RoutedEventArgs e)
    {
        await _vm.UndoLastAsync();
        SyncSourcesAfterUndo();
    }

    private void CancelWork_Click(object sender, RoutedEventArgs e) => _vm.CancelWork();

    private void SyncSourcesAfterUndo()
    {
        // Đường dẫn đã hoàn tác được tính lại ở lượt preview kế tiếp.
    }

    // ----------------------------------------------------------------- Command Palette (Ctrl+K)

    /// <summary>Mở bảng lệnh nhanh.</summary>
    private void ShowCommandPalette()
    {
        var vm = new CommandPaletteViewModel(
            BuildCommandRegistry(),
            new LocStringProvider(),
            new WpfUiDispatcher());

        new CommandPaletteWindow(vm, this).ShowDialog();
    }

    private CommandRegistry BuildCommandRegistry()
    {
        var registry = new CommandRegistry();
        var files = Loc.T("CommandCategoryFile");
        var rules = Loc.T("CommandCategoryRules");
        var tools = Loc.T("CommandCategoryTools");
        var app = Loc.T("CommandCategoryApp");

        registry.Register(Loc.T("CommandAddFiles"), files, () => AddFiles_Click(this, new RoutedEventArgs()), "Ctrl+O");
        registry.Register(Loc.T("CommandAddFolder"), files, () => AddFolder_Click(this, new RoutedEventArgs()), "Ctrl+Shift+O");
        registry.Register(Loc.T("ShelfCommand"), files, () => OpenShelf(), "Ctrl+Shift+S");
        registry.Register(Loc.T("CommandClearList"), files, () => ClearList_Click(this, new RoutedEventArgs()));
        registry.Register(Loc.T("CommandSendToTools"), files, () => SendToTools_Click(this, new RoutedEventArgs()));
        registry.Register(Loc.T("CommandRadial"), tools, () => ShowRadialAtCursor());

        // Gallery của giao diện mới: chỉ có khi dev đặt BFR_GALLERY=1 (không hiện với người dùng).
        if (Environment.GetEnvironmentVariable("BFR_GALLERY") == "1")
            registry.Register("Gallery (dev)", tools, () => new BfrGalleryWindow { Owner = this }.Show());
        registry.Register(Loc.T("CommandApply"), files, () => Apply_Click(this, new RoutedEventArgs()), "Ctrl+Enter");
        registry.Register(Loc.T("CommandUndo"), files, () => Undo_Click(this, new RoutedEventArgs()), "Ctrl+Z");
        registry.Register(Loc.T("CommandSavePreset"), rules, () => SavePreset_Click(this, new RoutedEventArgs()), "Ctrl+S");
        registry.Register(Loc.T("CommandImportExportPreset"), rules, () => ImportExportPreset_Click(this, new RoutedEventArgs()));
        registry.Register(Loc.T("CommandPresetLibrary"), rules, () => PresetLibrary_Click(this, new RoutedEventArgs()));
        registry.Register(Loc.T("CommandHistory"), tools, () => History_Click(this, new RoutedEventArgs()));
        registry.Register(Loc.T("CommandWatchFolder"), tools, () => WatchFolder_Click(this, new RoutedEventArgs()));
        registry.Register(Loc.T("CommandContextMenu"), tools, () => ContextMenu_Click(this, new RoutedEventArgs()));
        registry.Register(Loc.T("CommandExportReport"), tools, () => ExportReport_Click(this, new RoutedEventArgs()));
        registry.Register(Loc.T("CommandRegexTester"), tools, () => OpenRegexTester());
        registry.Register(Loc.T("CommandSettings"), app, () => Settings_Click(this, new RoutedEventArgs()));
        registry.Register(Loc.T("CommandUpgrade"), app, () => ShowUpgradeDialog(contextFeature: null));
        registry.Register(Loc.T("CommandCheckUpdate"), app, () => CheckUpdate_Click(this, new RoutedEventArgs()));
        registry.Register(Loc.T("CommandToggleTheme"), app, ToggleTheme);

        return registry;
    }

    /// <summary>Đổi vòng giao diện: Hệ thống → Sáng → Tối → Hệ thống.</summary>
    private void ToggleTheme()
    {
        var settings = SettingsStore.Load();
        var next = settings.Theme switch
        {
            "System" => "Light",
            "Light" => "Dark",
            _ => "System"
        };

        settings.Theme = next;
        SettingsStore.Save(settings);

        if (Enum.TryParse<ThemeManager.AppTheme>(next, out var theme))
        {
            ThemeManager.Apply(theme);
        }
    }

    /// <summary>Mở Trình thử Regex từ Command Palette (dùng quy tắc Tìm và thay đầu tiên).</summary>
    private void OpenRegexTester()
    {
        var card = _vm.Rules.FirstOrDefault(r => r.CanOpenRegexTester);
        if (card is null)
        {
            MessageBox.Show(Loc.T("RegexTesterNeedFindReplace"), Loc.T("RegexTesterTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        OpenRegexTesterFor(card);
    }

    // ----------------------------------------------------------------- Hộp thoại nổi (ContentDialog)

    /// <summary>
    /// Hiện hộp thoại nổi ngay trong cửa sổ (không mở cửa sổ mới). Trả về true nếu người dùng chọn nút chính.
    /// Cửa sổ chính được đưa lên trước nếu đang ẩn trong khay.
    /// </summary>
    private async System.Threading.Tasks.Task<bool> ShowDialogAsync(
        string title, object content, string primaryText, string closeText, bool showFooter = true)
    {
        ActivateAndForeground();

        var dialog = new Wpf.Ui.Controls.ContentDialog(DialogHost)
        {
            Title = title,
            Content = content,
            PrimaryButtonText = primaryText,
            CloseButtonText = closeText,
            DefaultButton = Wpf.Ui.Controls.ContentDialogButton.Primary,
            IsFooterVisible = showFooter
        };

        var result = await dialog.ShowAsync();
        return result == Wpf.Ui.Controls.ContentDialogResult.Primary;
    }

    // ----------------------------------------------------------------- Điều hướng (v1.4)

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string key }) ShowPage(key);
    }

    private RadioButton NavButton(string key) => key switch
    {
        "Tools" => NavTools,
        "Shelf" => NavShelf,
        "History" => NavHistory,
        "Presets" => NavPresets,
        "Watch" => NavWatch,
        "Settings" => NavSettings,
        _ => NavRename
    };

    /// <summary>Chuyển sang một trang và đồng bộ rail điều hướng.</summary>
    public void NavigateTo(string key)
    {
        var button = NavButton(key);
        if (button.IsChecked != true) button.IsChecked = true;   // kích hoạt Nav_Checked → ShowPage
        else ShowPage(key);
    }

    private void ShowPage(string key)
    {
        if (_navigating) return;

        // Watch Folder là tính năng Pro: Free thấy hộp thoại nâng cấp và ở lại trang hiện tại.
        if (key == "Watch" && !EnsurePro(ProFeature.UnlimitedRecursion))
        {
            _navigating = true;
            NavButton(_currentPage).IsChecked = true;
            _navigating = false;
            return;
        }

        if (key == _currentPage) return;

        var leaving = _currentPage;
        _currentPage = key;

        if (leaving != "Rename" && _pages.TryGetValue(leaving, out var old))
        {
            old.Visibility = Visibility.Collapsed;
            old.OnHidden();
        }

        RenamePage.Visibility = key == "Rename" ? Visibility.Visible : Visibility.Collapsed;
        if (key == "Rename") return;

        var page = GetPage(key);
        page.Visibility = Visibility.Visible;
        page.OnShown();
    }

    private PageBase GetPage(string key)
    {
        // Trang Cài đặt dựng từ cài đặt hiện tại nên tạo mới mỗi lần mở.
        if (key == "Settings" && _pages.Remove(key, out var stale)) PageHost.Children.Remove(stale);
        if (_pages.TryGetValue(key, out var existing)) return existing;

        string AppData(string name) => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchFileRenamer", name);

        PageBase page;
        switch (key)
        {
            case "Shelf":
                page = new ShelfPage(_shelfModel, batch => { NavigateTo("Rename"); EnqueuePaths(batch); }, feature => ShowUpgradeDialog(feature));
                break;
            case "History":
                var history = new HistoryPage(new HistoryStore(_fs, AppData("history.json")), _fs, _license.License);
                history.Left += (_, _) => _ = _vm.RebuildAsync();
                page = history;
                break;
            case "Presets":
                var presets = new PresetsPage(new PresetStore(_fs, AppData("presets")), () => _vm.SelectedPreset ?? new Preset(), _license.License);
                presets.Left += (_, _) => _vm.RefreshPresets();
                presets.PresetApplied += (_, preset) =>
                {
                    _vm.RefreshPresets();
                    PresetCombo.SelectedItem = null;
                    _vm.LoadPreset(preset);
                    _ = _vm.RebuildAsync();
                    NavigateTo("Rename");
                };
                page = presets;
                break;
            case "Tools":
                page = new ToolsPage(_fs, _license.License, new HistoryStore(_fs, AppData("history.json")), AppData("journal"), AppVersion.Current,
                    new CompositeMetadataProvider(new ExifMetadataReader(), new MediaMetadataReader(), new AudioTagReader()),
                    new BFR.Media.WicImageProcessor(), feature => ShowUpgradeDialog(feature));
                break;
            case "Watch":
                page = new WatchFolderPage(new PresetStore(_fs, AppData("presets")), _license.License);
                break;
            default:
                var settings = new SettingsPage(_license);
                settings.UpgradeRequested += (_, _) => ShowUpgradeDialog(contextFeature: null);
                settings.SettingsLeft += (_, _) => ApplySettingsChanges();
                page = settings;
                break;
        }

        page.CloseRequested += (_, _) => NavigateTo("Rename");
        page.Visibility = Visibility.Collapsed;
        PageHost.Children.Add(page);
        _pages[key] = page;
        return page;
    }

    /// <summary>Áp dụng ngay các thay đổi trong Cài đặt khi người dùng rời trang (khay, hook, phím tắt, giấy phép).</summary>
    private void ApplySettingsChanges()
    {
        (Application.Current as App)?.ApplyBackgroundSetting();
        ApplyShakeSetting();
        ApplyHotkeySetting();
        UpdateLicenseState();
        _ = _vm.RebuildAsync();
    }

    // ----------------------------------------------------------------- Màn phụ

    private void History_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("History");
    }

    private void PresetLibrary_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("Presets");
    }

    private void WatchFolder_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("Watch");
    }

    private void ExportReport_Click(object sender, RoutedEventArgs e)
    {
        if (!EnsurePro(ProFeature.ReportExport)) return;

        var report = _vm.LastReport;
        if (report.Count == 0)
        {
            MessageBox.Show(Loc.T("ReportNoData"), Loc.T("WindowTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV (*.csv)|*.csv|JSON (*.json)|*.json",
            FileName = "bfr-report-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv"
        };
        if (dialog.ShowDialog() != true) return;

        var format = Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? ReportFormat.Json
            : ReportFormat.Csv;

        BFR.Core.Reports.ReportExporter.Export(report, dialog.FileName, format);
        StatusText.Text = Loc.F("ReportSaved", dialog.FileName);
    }

    private void ContextMenu_Click(object sender, RoutedEventArgs e)
    {
        if (!ShellIntegration.IsSupported) return;

        if (ShellIntegration.IsPackaged)
        {
            MessageBox.Show(Loc.T("ContextMenuPackaged"), Loc.T("WindowTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var registered = ShellIntegration.IsRegistered();
        var prompt = registered ? Loc.T("ContextMenuToggleDisable") : Loc.T("ContextMenuToggleEnable");
        if (MessageBox.Show(prompt, Loc.T("WindowTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        if (registered)
        {
            ShellIntegration.Unregister();
            MessageBox.Show(Loc.T("ContextMenuDisabled"), Loc.T("WindowTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "BatchFileRenamer.exe");
            ShellIntegration.Register(exePath, comServerPath: ShellIntegration.ResolveComServerPath(exePath));

            MessageBox.Show(
                ShellIntegration.IsComRegistered() ? Loc.T("ContextMenuEnabled") : Loc.T("ContextMenuFallback"),
                Loc.T("WindowTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        NavigateTo("Settings");
    }

    /// <summary>
    /// Mở hộp thoại nâng cấp Pro (dialog tùy chỉnh, không dùng MessageBox).
    /// Nếu người dùng đã là Pro thì mở màn hình thông tin license.
    /// </summary>
    private void Upgrade_Click(object sender, RoutedEventArgs e)
    {
        ShowUpgradeDialog(contextFeature: null);
    }

    /// <summary>Mở hộp thoại nâng cấp, kèm dòng bối cảnh nêu tính năng đang cần Pro.</summary>
    private void ShowUpgradeDialog(ProFeature? contextFeature)
    {
        _ = ShowUpgradeDialogAsync(contextFeature);
    }

    /// <summary>
    /// Hộp thoại nâng cấp Pro hiện ngay trong cửa sổ (ContentDialog). Nếu đã là Pro (không phải dùng thử)
    /// thì hiện thông tin license thay vì hộp nâng cấp.
    /// </summary>
    private async System.Threading.Tasks.Task ShowUpgradeDialogAsync(ProFeature? contextFeature)
    {
        if (_license.Status.IsPro && !_license.Status.IsTrial)
        {
            await ShowDialogAsync(Loc.T("LicenseInfoTitle"), LicenseInfoContent.Build(_license.License, _license.Status),
                primaryText: string.Empty, closeText: Loc.T("SettingsClose"));
            return;
        }

        var settings = SettingsStore.Load();
        var vm = new UpgradeViewModel(
            _license,
            new BFR.Store.StorePriceProvider(),
            contextFeature,
            settings.ProPriceText,
            new LocStringProvider());

        var view = new UpgradeView(vm);
        ActivateAndForeground();
        var dialog = new Wpf.Ui.Controls.ContentDialog(DialogHost)
        {
            Content = view,
            IsFooterVisible = false
        };
        view.CloseRequested += (_, _) => dialog.Hide();

        await dialog.ShowAsync();

        UpdateLicenseState();
        _ = _vm.RebuildAsync();
    }

    private async void CheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = Loc.T("UpdateChecking");

        var result = await UpdateRunner.CheckAsync();

        if (result.Status != UpdateCheckStatus.UpdateAvailable)
        {
            MessageBox.Show(result.Message, Loc.T("UpdateTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            StatusText.Text = result.Message;
            return;
        }

        if (MessageBox.Show(result.Message, Loc.T("UpdateTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes
            && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = result.ReleaseUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Không mở được trình duyệt: bỏ qua.
            }
        }
    }

    private bool HasUsedTrial()
    {
        try
        {
            return new LicenseStore(_fs).Load().TrialConsumed;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// Kiểm tra tính năng Pro. Nếu bị khóa thì mở hộp thoại nâng cấp với dòng bối cảnh
    /// nêu đúng tính năng đó cần Pro (yêu cầu mục 4) — không dùng MessageBox.
    /// </summary>
    private bool EnsurePro(ProFeature feature)
    {
        var gate = _license.License.Check(feature);
        if (gate.Allowed) return true;

        ShowUpgradeDialog(feature);
        return false;
    }

    // ----------------------------------------------------------------- Khác

    private void PreviewGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _vm.SelectedRow = PreviewGrid.SelectedItem as PreviewRowViewModel;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            NavigateTo("Rename");
            _vm.AddPaths(paths);
        }
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S)
        {
            OpenShelf();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.O:
                    if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift)) AddFolder_Click(this, new RoutedEventArgs());
                    else AddFiles_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.Z:
                    Undo_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.S:
                    SavePreset_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.F:
                    FocusSearch();
                    e.Handled = true;
                    break;
                case Key.K:
                    ShowCommandPalette();
                    e.Handled = true;
                    break;
                case Key.Enter:
                    if (_vm.CanApply) Apply_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }
        else if (e.Key == Key.F5)
        {
            _ = _vm.RebuildAsync();
            e.Handled = true;
        }
    }

    private void FocusSearch()
    {
        // Ô tìm kiếm nằm ở hàng lọc của bảng xem trước.
        var search = FindVisualChild<TextBox>(this, "PreviewSearchBox");
        search?.Focus();
    }

    private async void RateApp_Click(object sender, RoutedEventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        await BFR.Store.StoreRatingService.RequestRateAndReviewAsync(hwnd);
    }

    private async void CheckAndPromptStoreRating()
    {
        var settings = SettingsStore.Load();
        settings.SuccessfulRenameCount++;
        SettingsStore.Save(settings);

        if (!settings.HasPromptedRating && settings.SuccessfulRenameCount >= 2)
        {
            settings.HasPromptedRating = true;
            SettingsStore.Save(settings);

            var answer = MessageBox.Show(
                this,
                Loc.T("RateAppPrompt"),
                Loc.T("RateAppTitle"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (answer == MessageBoxResult.Yes)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                await BFR.Store.StoreRatingService.RequestRateAndReviewAsync(hwnd);
            }
        }
    }

    // ----------------------------------------------------------------- Preview Context Menu

    private PreviewRowViewModel? GetContextMenuRow(object sender)
    {
        if (sender is MenuItem menuItem && menuItem.Parent is ContextMenu contextMenu)
        {
            if (contextMenu.PlacementTarget is DataGridRow row && row.DataContext is PreviewRowViewModel vm)
            {
                return vm;
            }
        }
        return PreviewGrid.SelectedItem as PreviewRowViewModel;
    }

    private void ContextMenu_OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuRow(sender) is not { } row) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = row.File.FullPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("WindowTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ContextMenu_OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuRow(sender) is not { } row) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{row.File.FullPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, Loc.T("WindowTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ContextMenu_CopyOldName_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuRow(sender) is { } row)
        {
            Clipboard.SetText(row.OldName);
        }
    }

    private void ContextMenu_CopyNewName_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuRow(sender) is { } row)
        {
            Clipboard.SetText(row.NewName);
        }
    }

    private void ContextMenu_ToggleSelect_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuRow(sender) is { } row)
        {
            row.IsSelected = !row.IsSelected;
        }
    }

    private void ContextMenu_RemoveFromList_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextMenuRow(sender) is { } row)
        {
            _vm.RemoveRow(row);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        if (parent is T typed && typed.Name == name) return typed;

        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var found = FindVisualChild<T>(VisualTreeHelper.GetChild(parent, i), name);
            if (found is not null) return found;
        }

        return null;
    }

    /// <summary>Chạy nền: đóng cửa sổ chỉ ẩn đi, trừ khi người dùng chọn Thoát ở khay.</summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (Application.Current is App { BackgroundEnabled: true, IsExiting: false })
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }
}