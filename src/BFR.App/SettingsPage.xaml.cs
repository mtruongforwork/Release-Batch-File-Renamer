namespace BFR.App;

using BFR.Core.Licensing;
using BFR.App.Infrastructure;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Ui = Wpf.Ui.Controls;

/// <summary>
/// Trang Cài đặt (v1.4: một trang trong khung điều hướng): ngôn ngữ, giao diện, Shelf/chạy nền,
/// phím tắt, cập nhật, giấy phép Pro, quyền riêng tư. Mỗi nhóm là một Card của WPF-UI,
/// tuỳ chọn bật/tắt dùng ToggleSwitch.
/// </summary>
public class SettingsPage : PageBase
{
    /// <summary>Người dùng bấm "Nâng cấp" trong nhóm giấy phép.</summary>
    public event EventHandler? UpgradeRequested;

    /// <summary>Trang bị rời đi: cửa sổ chính áp dụng các thay đổi (khay, hook, phím tắt...).</summary>
    public event EventHandler? SettingsLeft;

    public override void OnHidden() => SettingsLeft?.Invoke(this, EventArgs.Empty);

    private readonly LicenseCoordinator _license;
    private readonly AppSettings _settings;

    public SettingsPage(LicenseCoordinator license)
    {
        _license = license;
        _settings = SettingsStore.Load();

        AutomationProperties.SetName(this, "Settings window");

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new TextBlock
        {
            Text = Loc.T("SettingsTitle"),
            FontSize = (double)Application.Current.FindResource("AppFontSizeTitle"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("TextPrimary"),
            Margin = new Thickness(28, 20, 28, 6)
        };
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var content = new StackPanel { Margin = new Thickness(28, 4, 28, 16), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };

        // ---- Giao diện & ngôn ngữ ----
        var look = Card(content, Loc.T("SettingsLanguage") + " · " + Loc.T("SettingsTheme"));

        var language = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(language, "Language selector");
        foreach (var code in new[] { "vi", "en", "fr", "zh-Hans" })
        {
            language.Items.Add(new ComboBoxItem { Content = LanguageName(code), Tag = code });
        }
        SelectByTag(language, _settings.Language);
        language.SelectionChanged += (_, _) =>
        {
            if (language.SelectedItem is ComboBoxItem { Tag: string code })
            {
                Loc.Apply(code);
                _settings.Language = code;
                SettingsStore.Save(_settings);
            }
        };
        look.Children.Add(Caption(Loc.T("SettingsLanguage")));
        look.Children.Add(language);

        var theme = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(theme, "Theme selector");
        theme.Items.Add(new ComboBoxItem { Content = Loc.T("ThemeSystem"), Tag = "System" });
        theme.Items.Add(new ComboBoxItem { Content = Loc.T("ThemeLight"), Tag = "Light" });
        theme.Items.Add(new ComboBoxItem { Content = Loc.T("ThemeDark"), Tag = "Dark" });
        SelectByTag(theme, _settings.Theme);
        theme.SelectionChanged += (_, _) =>
        {
            if (theme.SelectedItem is ComboBoxItem { Tag: string tag }
                && Enum.TryParse<ThemeManager.AppTheme>(tag, out var value))
            {
                _settings.Theme = tag;
                SettingsStore.Save(_settings);
                ThemeManager.Apply(value);
            }
        };
        var themeCaption = Caption(Loc.T("SettingsTheme"));
        themeCaption.Margin = new Thickness(0, 12, 0, 4);
        look.Children.Add(themeCaption);
        look.Children.Add(theme);

        // ---- Shelf & chạy nền ----
        var shelf = Card(content, Loc.T("SettingsShelf"));
        shelf.Children.Add(Toggle(
            Loc.T("SettingsShake"),
            _settings.ShakeToOpenShelf,
            on => _settings.ShakeToOpenShelf = on));
        shelf.Children.Add(Caption(Loc.T("SettingsShakeHint")));
        var hotkeyToggle = Toggle(Loc.T("SettingsHotkey"), _settings.ShelfHotkeyEnabled, on => _settings.ShelfHotkeyEnabled = on);
        hotkeyToggle.Margin = new Thickness(0, 12, 0, 0);
        shelf.Children.Add(hotkeyToggle);

        var hotkeyBox = new ComboBox { MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(hotkeyBox, "Shelf hotkey");
        foreach (var h in BFR.Core.HotkeySpec.Presets) hotkeyBox.Items.Add(h);
        hotkeyBox.SelectedItem = BFR.Core.HotkeySpec.TryParse(_settings.ShelfHotkey, out var currentHotkey)
            ? currentHotkey.ToString()
            : BFR.Core.HotkeySpec.Default;
        hotkeyBox.SelectionChanged += (_, _) =>
        {
            if (hotkeyBox.SelectedItem is string chosen) { _settings.ShelfHotkey = chosen; SettingsStore.Save(_settings); }
        };
        shelf.Children.Add(hotkeyBox);
        shelf.Children.Add(Caption(Loc.T("SettingsHotkeyHint")));

        // Menu tròn các công cụ: phím tắt toàn cục riêng, mặc định tắt.
        var radialToggle = Toggle(Loc.T("SettingsRadial"), _settings.RadialHotkeyEnabled, on => _settings.RadialHotkeyEnabled = on);
        radialToggle.Margin = new Thickness(0, 12, 0, 0);
        shelf.Children.Add(radialToggle);
        var radialBox = new ComboBox { MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        AutomationProperties.SetName(radialBox, "Radial menu hotkey");
        foreach (var h in BFR.Core.HotkeySpec.Presets) radialBox.Items.Add(h);
        radialBox.SelectedItem = BFR.Core.HotkeySpec.TryParse(_settings.RadialHotkey, out var currentRadial)
            ? currentRadial.ToString()
            : "Ctrl+Alt+Q";
        radialBox.SelectionChanged += (_, _) =>
        {
            if (radialBox.SelectedItem is string chosen) { _settings.RadialHotkey = chosen; SettingsStore.Save(_settings); }
        };
        shelf.Children.Add(radialBox);
        shelf.Children.Add(Caption(Loc.T("SettingsRadialHint")));

        var bg = Toggle(
            Loc.T("SettingsRunInBackground"),
            _settings.RunInBackground,
            on => _settings.RunInBackground = on);
        bg.Margin = new Thickness(0, 12, 0, 0);
        shelf.Children.Add(bg);

        // Khởi động cùng Windows: chỉ có nghĩa khi bật chạy nền (ứng dụng khởi động ẩn trong khay).
        Ui.ToggleSwitch? startup = null;
        var startupHint = Caption(Loc.T("SettingsStartupBlocked"));
        startupHint.Visibility = Visibility.Collapsed;
        var openStartupApps = new Ui.Button
        {
            Content = Loc.T("SettingsOpenStartupApps"),
            Appearance = Ui.ControlAppearance.Secondary,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };
        openStartupApps.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ms-settings:startupapps") { UseShellExecute = true }); }
            catch { /* không mở được trang cài đặt: bỏ qua */ }
        };

        var suppress = false;   // đặt IsChecked bằng code không được kích hoạt lại logic bật/tắt
        startup = Toggle(Loc.T("SettingsStartWithWindows"), false, _ => { });
        startup.Margin = new Thickness(0, 12, 0, 0);
        startup.Checked += async (_, _) =>
        {
            if (suppress) return;
            // Khởi động ẩn chỉ hợp lý khi có biểu tượng khay: tự bật chạy nền.
            if (bg.IsChecked != true) bg.IsChecked = true;
            var state = await AutoStart.SetEnabledAsync(true);
            if (state != AutoStartState.Enabled)
            {
                suppress = true; startup.IsChecked = false; suppress = false;
                var blocked = state is AutoStartState.DisabledByUser or AutoStartState.DisabledByPolicy;
                startupHint.Visibility = openStartupApps.Visibility = blocked ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                startupHint.Visibility = openStartupApps.Visibility = Visibility.Collapsed;
            }
        };
        startup.Unchecked += async (_, _) =>
        {
            if (suppress) return;
            await AutoStart.SetEnabledAsync(false);
        };
        // Tắt chạy nền thì cũng tắt khởi động cùng Windows.
        bg.Unchecked += (_, _) => { if (startup.IsChecked == true) startup.IsChecked = false; };
        shelf.Children.Add(startup);
        shelf.Children.Add(startupHint);
        shelf.Children.Add(openStartupApps);
        Loaded += async (_, _) =>
        {
            var current = await AutoStart.GetStateAsync();
            suppress = true; startup.IsChecked = current == AutoStartState.Enabled; suppress = false;
            if (current is AutoStartState.DisabledByUser or AutoStartState.DisabledByPolicy)
            {
                startupHint.Visibility = openStartupApps.Visibility = Visibility.Visible;
            }
        };

        // ---- Cập nhật ----
        var updates = Card(content, Loc.T("SettingsUpdates"));
        updates.Children.Add(Toggle(Loc.T("SettingsCheckUpdates"), _settings.CheckForUpdates,
            on => _settings.CheckForUpdates = on));

        var urlCaption = Caption(Loc.T("SettingsUpdateUrl"));
        urlCaption.Margin = new Thickness(0, 12, 0, 4);
        updates.Children.Add(urlCaption);
        var url = new TextBox { Text = _settings.UpdateFeedUrl };
        AutomationProperties.SetName(url, "Update feed url");
        url.TextChanged += (_, _) => { _settings.UpdateFeedUrl = url.Text; SettingsStore.Save(_settings); };
        updates.Children.Add(url);

        var checkNow = new Ui.Button
        {
            Content = Loc.T("SettingsCheckUpdateNow"),
            Appearance = Ui.ControlAppearance.Secondary,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0)
        };
        AutomationProperties.SetName(checkNow, "Check for updates now");
        updates.Children.Add(checkNow);

        var updateStatus = new TextBlock
        {
            Text = string.Empty,
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        AutomationProperties.SetName(updateStatus, "Update check result");
        updates.Children.Add(updateStatus);

        checkNow.Click += async (_, _) =>
        {
            checkNow.IsEnabled = false;
            checkNow.Content = Loc.T("SettingsUpdateChecking");
            updateStatus.Text = Loc.T("SettingsUpdateChecking");

            try
            {
                var result = await UpdateRunner.CheckAsync();
                updateStatus.Text = result.Message;

                if (result.Status == UpdateCheckStatus.UpdateAvailable
                    && !string.IsNullOrWhiteSpace(result.ReleaseUrl))
                {
                    var open = new Ui.Button
                    {
                        Content = Loc.T("UpdateDownload"),
                        Appearance = Ui.ControlAppearance.Primary,
                        HorizontalAlignment = HorizontalAlignment.Left,
                        Margin = new Thickness(0, 8, 0, 0)
                    };
                    open.Click += (_, _) =>
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
                            // Bỏ qua nếu không mở được trình duyệt.
                        }
                    };
                    updates.Children.Add(open);
                }
            }
            finally
            {
                checkNow.IsEnabled = true;
                checkNow.Content = Loc.T("SettingsCheckUpdateNow");
            }
        };

        // ---- Giấy phép ----
        var licenseCard = Card(content, Loc.T("SettingsLicense"));
        var status = _license.Status;
        var licenseText = status.IsTrial
            ? Loc.F("SettingsLicenseTrial", (int)Math.Ceiling((_license.License.TrialRemaining() ?? TimeSpan.Zero).TotalDays))
            : status.IsPro
                ? Loc.T("SettingsLicensePro")
                : Loc.T("SettingsLicenseFree");

        licenseCard.Children.Add(new TextBlock
        {
            Text = licenseText,
            Foreground = (Brush)Application.Current.FindResource("TextSecondary"),
            TextWrapping = TextWrapping.Wrap
        });

        if (!status.IsPro)
        {
            var upgrade = new Ui.Button
            {
                Content = Loc.T("Upgrade"),
                Appearance = Ui.ControlAppearance.Primary,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            };
            upgrade.Click += (_, _) => UpgradeRequested?.Invoke(this, EventArgs.Empty);
            licenseCard.Children.Add(upgrade);
        }

        // ---- Quyền riêng tư & giới thiệu ----
        var about = Card(content, Loc.T("SettingsPrivacy") + " · " + Loc.T("SettingsAbout"));
        about.Children.Add(Caption(Loc.T("SettingsPrivacyHint")));

        // C8: thời hạn giữ lịch sử/journal và xoá dữ liệu hoạt động theo yêu cầu.
        var retentionCaption = Caption(Loc.T("SettingsRetention"));
        retentionCaption.Margin = new Thickness(0, 12, 0, 4);
        about.Children.Add(retentionCaption);

        var retention = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(retention, "History retention");
        foreach (var days in AppSettings.RetentionChoices)
            retention.Items.Add(new ComboBoxItem { Content = Loc.F("SettingsRetentionDays", days), Tag = days.ToString() });
        SelectByTag(retention, AppSettings.NormalizeRetentionDays(_settings.HistoryRetentionDays).ToString());
        retention.SelectionChanged += (_, _) =>
        {
            if (retention.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out var days))
            {
                _settings.HistoryRetentionDays = AppSettings.NormalizeRetentionDays(days);
                SettingsStore.Save(_settings);
                BFR.Core.HistoryStore.DefaultRetention = TimeSpan.FromDays(_settings.HistoryRetentionDays);
            }
        };
        about.Children.Add(retention);

        var erase = new Ui.Button
        {
            Content = Loc.T("SettingsEraseActivity"),
            Appearance = Ui.ControlAppearance.Secondary,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 10, 0, 0)
        };
        AutomationProperties.SetName(erase, "Clear history and undo data");
        var eraseStatus = Caption(string.Empty);
        eraseStatus.Margin = new Thickness(0, 6, 0, 0);
        erase.Click += (_, _) =>
        {
            var answer = MessageBox.Show(Window.GetWindow(this), Loc.T("SettingsEraseConfirm"),
                Loc.T("SettingsEraseActivity"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            var result = ActivityMaintenance.EraseAll();
            eraseStatus.Text = result.Failed == 0
                ? Loc.F("SettingsEraseDone", result.FilesDeleted)
                : Loc.F("SettingsEraseDoneFailed", result.FilesDeleted, result.Failed);
        };
        about.Children.Add(erase);
        about.Children.Add(eraseStatus);

        about.Children.Add(Caption(Loc.F("SettingsVersion", BFR.Core.AppVersion.Current)));

        var rateButton = new Ui.Button
        {
            Content = Loc.T("RateApp"),
            Appearance = Ui.ControlAppearance.Secondary,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 8, 0, 0)
        };
        AutomationProperties.SetName(rateButton, "Rate this app");
        rateButton.Click += async (_, _) =>
        {
            var owner = Window.GetWindow(this);
            var hwnd = owner is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(owner).Handle;
            await BFR.Store.StoreRatingService.RequestRateAndReviewAsync(hwnd);
        };
        about.Children.Add(rateButton);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        Content = root;
    }

    /// <summary>Thêm một Card có tiêu đề vào <paramref name="parent"/> và trả về StackPanel bên trong.</summary>
    private static StackPanel Card(StackPanel parent, string title)
    {
        var inner = new StackPanel();
        inner.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)Application.Current.FindResource("PanelHeaderTextStyle"),
            Margin = new Thickness(0, 0, 0, 8)
        });

        parent.Children.Add(new Ui.Card
        {
            Content = inner,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(16),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        });
        return inner;
    }

    /// <summary>Công tắc bật/tắt: lưu ngay vào cài đặt khi đổi.</summary>
    private Ui.ToggleSwitch Toggle(string text, bool isOn, Action<bool> set)
    {
        // Content là TextBlock có xuống dòng: ToggleSwitch không tự wrap chuỗi dài (tiếng Pháp, tiếng Trung).
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 };
        var toggle = new Ui.ToggleSwitch { Content = label, IsChecked = isOn };
        AutomationProperties.SetName(toggle, text);
        toggle.Checked += (_, _) => { set(true); SettingsStore.Save(_settings); };
        toggle.Unchecked += (_, _) => { set(false); SettingsStore.Save(_settings); };
        return toggle;
    }

    private static void SelectByTag(ComboBox combo, string? tag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag as string, tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }

        combo.SelectedIndex = 0;
    }

    private static string LanguageName(string code) => code switch
    {
        "en" => "English",
        "fr" => "Français",
        "zh-Hans" => "简体中文",
        _ => "Tiếng Việt"
    };

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        Style = (Style)Application.Current.FindResource("CaptionTextStyle"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 4)
    };
}
