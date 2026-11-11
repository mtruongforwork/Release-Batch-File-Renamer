namespace BFR.App;

using BFR.Core;
using BFR.Core.Licensing;
using BFR.Store;
using BFR.App.Infrastructure;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

public partial class App : Application
{
    private SingleInstanceLock? _lock;
    private SingleInstanceServer? _server;
    private MainWindow? _mainWindow;
    private LicenseCoordinator? _license;
    private TrayIcon? _tray;
    private int _toasts;

    /// <summary>Tệp báo lỗi nghiêm trọng (đã che đường dẫn); xoá cùng dữ liệu hoạt động.</summary>
    internal static string CrashFilePath => Path.Combine(Path.GetTempPath(), "bfr-crash.txt");

    /// <summary>True khi đang bật chạy nền (có biểu tượng khay).</summary>
    public bool BackgroundEnabled => _tray is not null;

    /// <summary>True khi người dùng đã chọn Thoát: cho phép cửa sổ chính đóng thật.</summary>
    public bool IsExiting { get; private set; }

    private bool StartHidden { get; set; }

    /// <summary>Bật/tắt biểu tượng khay và chế độ chạy nền theo cài đặt hiện tại.</summary>
    public void ApplyBackgroundSetting()
    {
        var enabled = SettingsStore.Load().RunInBackground;
        if (enabled && _tray is null)
        {
            try
            {
                _tray = new TrayIcon(
                    "Batch File Renamer",
                    Loc.T("TrayOpen"),
                    Loc.T("TrayShelf"),
                    Loc.T("TrayExit"),
                    open: () => _mainWindow?.ActivateAndForeground(),
                    shelf: () => _mainWindow?.OpenShelf(),
                    exit: ExitApplication);
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
            }
            catch
            {
                _tray = null;   // không tạo được biểu tượng khay: giữ hành vi thường
            }
        }
        else if (!enabled && _tray is not null)
        {
            _tray.Dispose();
            _tray = null;
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            // Cửa sổ chính đang ẩn trong khay thì không còn gì giữ ứng dụng sống: hiện lại.
            if (_mainWindow is { IsVisible: false }) _mainWindow.ActivateAndForeground();
        }
    }

    /// <summary>Thoát hẳn ứng dụng (kể cả khi đang chạy nền).</summary>
    public void ExitApplication()
    {
        IsExiting = true;
        _tray?.Dispose();
        _tray = null;
        _mainWindow?.Close();
        Shutdown();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            try { File.WriteAllText(CrashFilePath, LogRedactor.Redact(ev.ExceptionObject?.ToString() ?? "null")); } catch { }
        };

        DispatcherUnhandledException += (s, ev) =>
        {
            try { File.WriteAllText(CrashFilePath, LogRedactor.Redact(ev.Exception.ToString())); } catch { }
        };

        SessionEnding += (_, _) => IsExiting = true;   // không chặn đăng xuất/tắt máy khi đang ẩn trong khay
        var parsed = StartupArgs.Parse(e.Args);
        var payload = parsed.Payload;
        StartHidden = parsed.Background;

        // Hỗ trợ lệnh đăng ký nhanh menu chuột phải từ phím tắt hoặc cài đặt
        if (payload.Paths.Contains("--register-context-menu", StringComparer.OrdinalIgnoreCase))
        {
            if (ShellIntegration.IsSupported)
            {
                var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "BatchFileRenamer.exe");
                ShellIntegration.Register(
                    exePath,
                    comServerPath: ShellIntegration.ResolveComServerPath(exePath));
            }
            Shutdown(0);
            return;
        }

        if (payload.Paths.Contains("--unregister-context-menu", StringComparer.OrdinalIgnoreCase))
        {
            if (ShellIntegration.IsSupported)
            {
                ShellIntegration.Unregister();
            }
            Shutdown(0);
            return;
        }

        _license = CreateLicenseCoordinator();

        // Khôi phục giao diện (sáng/tối) và ngôn ngữ đã lưu trước khi tạo cửa sổ.
        var settings = SettingsStore.Load();
        var theme = Enum.TryParse<ThemeManager.AppTheme>(settings.Theme, out var parsedTheme)
            ? parsedTheme
            : ThemeManager.AppTheme.System;
        ThemeManager.Apply(theme);

        if (!string.IsNullOrWhiteSpace(settings.Language) && settings.Language != "vi")
        {
            Loc.Apply(settings.Language);
        }

        _lock = new SingleInstanceLock();
        if (!_lock.IsPrimary)
        {
            // Đã có phiên ứng dụng chạy trước đó. Gửi danh sách tệp qua Named Pipe rồi thoát.
            await SingleInstanceClient.TrySendAsync(payload, timeoutMs: 3000).ConfigureAwait(false);
            Dispatcher.Invoke(() => Shutdown(0));
            return;
        }

        // C8: áp dụng thời hạn giữ dữ liệu hoạt động rồi dọn nền (chỉ phiên chính, tránh hai tiến trình cùng dọn).
        HistoryStore.DefaultRetention = TimeSpan.FromDays(AppSettings.NormalizeRetentionDays(settings.HistoryRetentionDays));
        _ = Task.Run(ActivityMaintenance.Run);

        // B1: đổi tên nhanh từ menu chuột phải — không mở cửa sổ chính, chỉ hiện thông báo kèm Hoàn tác.
        if (payload.QuickPreset is not null)
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _server = new SingleInstanceServer(OnIpcPayloadReceived);
            _server.Start();
            _ = RunQuickAsync(payload);
            return;
        }

        _mainWindow = new MainWindow(payload, _license);
        MainWindow = _mainWindow;

        // Bắt đầu lắng nghe trước khi Show để không bỏ lỡ lần mở tệp thứ hai
        // xảy ra ngay khi cửa sổ đang khởi tạo.
        _server = new SingleInstanceServer(OnIpcPayloadReceived);
        _server.Start();

        // --background: khởi động thẳng vào khay (chỉ khi đã bật chạy nền), không hiện cửa sổ.
        var startHidden = (StartHidden || AutoStart.LaunchedByStartup()) && settings.RunInBackground;
        if (startHidden)
        {
            // Cần handle để cửa sổ chính nhận IPC và hook chuột dù chưa từng hiện.
            _ = new System.Windows.Interop.WindowInteropHelper(_mainWindow).EnsureHandle();
            ApplyBackgroundSetting();
            _mainWindow.ApplyShakeSetting();
            _mainWindow.ApplyHotkeySetting();
        }
        else
        {
            _mainWindow.Show();
        }

        // Đồng bộ giấy phép Store (chạy nền, không chặn khởi động).
        _ = RefreshLicenseAsync();
    }

    /// <summary>
    /// Tạo cơ chế cấp giấy phép. Bản đóng gói MSIX dùng Microsoft Store IAP;
    /// bản portable chỉ dùng được tính năng Free (Store IAP không hoạt động ngoài Store).
    /// </summary>
    private static LicenseCoordinator CreateLicenseCoordinator()
    {
        var fs = new RealFileSystem();
        var packaged = ShellIntegration.IsPackaged;
        var devTier = LicenseService.ResolveDevTierOverride(packaged);

        var license = new LicenseService(
            new LicenseStore(fs),
            tierOverride: devTier,
            trialEnabled: true);

        var provider = packaged ? new MicrosoftStoreLicenseProvider() : null;
        return new LicenseCoordinator(license, provider);
    }

    private async Task RefreshLicenseAsync()
    {
        if (_license is null) return;

        try
        {
            await _license.RefreshAsync().ConfigureAwait(true);
        }
        catch
        {
            // Lỗi mạng/Store không được chặn ứng dụng: giữ cấp Free.
        }

        _mainWindow?.UpdateLicenseState();
    }

    /// <summary>Tạo và hiện cửa sổ chính khi ứng dụng đang chạy ở chế độ đổi tên nhanh (chưa có cửa sổ).</summary>
    private void EnsureMainWindow(IpcPayload payload)
    {
        if (_license is null || _mainWindow is not null) return;

        _mainWindow = new MainWindow(payload, _license);
        MainWindow = _mainWindow;
        ShutdownMode = BackgroundEnabled ? ShutdownMode.OnExplicitShutdown : ShutdownMode.OnLastWindowClose;
        _mainWindow.Show();
        _mainWindow.ApplyShakeSetting();
        _mainWindow.ApplyHotkeySetting();
    }

    /// <summary>Đổi tên nhanh theo preset (B1): quét tệp, áp dụng, rồi hiện thông báo có nút Hoàn tác.</summary>
    private async Task RunQuickAsync(IpcPayload payload)
    {
        var vietnamese = Loc.Current == "vi";
        try
        {
            var paths = new List<string>(payload.Paths);
            if (!string.IsNullOrWhiteSpace(payload.FileListPath))
            {
                paths.AddRange(FileListReader.ReadAndDelete(payload.FileListPath));
            }

            if (!QuickPresets.TryGet(payload.QuickPreset, out var quick, out var preset))
            {
                ShowToast(Loc.F("QuickUnknownPreset", payload.QuickPreset ?? string.Empty), null, isProblem: true);
                return;
            }

            var fs = new RealFileSystem();
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchFileRenamer");
            var history = new HistoryStore(fs, Path.Combine(dir, "history.json"));
            var renamer = new QuickRenamer(fs, Path.Combine(dir, "journal"), history);
            var limits = _license!.License;

            var result = await Task.Run(() =>
            {
                var files = new FileScanner().Scan(paths, new ScanOptions());
                return renamer.Run(files, preset, limits.MaxListRows, limits.MaxUndoEntries);
            }).ConfigureAwait(true);

            var title = quick.Title(vietnamese);
            Func<string>? undo = null;
            if (result.JournalPath is not null)
            {
                var journal = result.JournalPath;
                undo = () =>
                {
                    var n = renamer.Undo(journal);
                    history.Save(history.LoadAll().Where(e => !string.Equals(e.JournalPath, journal, StringComparison.OrdinalIgnoreCase)));
                    return Loc.F("QuickUndone", n);
                };
            }

            switch (result.Status)
            {
                case QuickRenameStatus.Done:
                    ShowToast(result.AutoResolved > 0
                        ? Loc.F("QuickDoneResolved", result.Renamed, title, result.AutoResolved)
                        : Loc.F("QuickDone", result.Renamed, title), undo, isProblem: false);
                    break;
                case QuickRenameStatus.NothingToChange:
                    ShowToast(Loc.T("QuickNothing"), null, isProblem: false);
                    break;
                case QuickRenameStatus.Blocked:
                    ShowToast(Loc.F("QuickBlocked", result.Message ?? string.Empty), null, isProblem: true);
                    break;
                default:
                    ShowToast(Loc.F("QuickFailed", result.Message ?? string.Empty), undo, isProblem: true);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowToast(Loc.F("QuickFailed", ex.Message), null, isProblem: true);
        }
    }

    private void ShowToast(string message, Func<string>? undo, bool isProblem)
    {
        var toast = new QuickToastWindow(message, undo, isProblem, _toasts);
        _toasts++;
        toast.Closed += (_, _) =>
        {
            _toasts--;
            // Chế độ đổi tên nhanh không có cửa sổ chính: thoát khi thông báo cuối đóng.
            if (_toasts == 0 && _mainWindow is null && !BackgroundEnabled) Shutdown();
        };
        toast.Show();
    }

    private void OnIpcPayloadReceived(IpcPayload payload)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (payload.QuickPreset is not null) { _ = RunQuickAsync(payload); return; }
            if (_mainWindow == null) { EnsureMainWindow(payload); return; }

            var paths = new List<string>(payload.Paths);
            if (!string.IsNullOrWhiteSpace(payload.FileListPath))
            {
                paths.AddRange(FileListReader.ReadAndDelete(payload.FileListPath));
            }

            if (!string.IsNullOrWhiteSpace(payload.PresetName))
            {
                _mainWindow.ApplyPresetByName(payload.PresetName);
            }

            if (paths.Count > 0)
            {
                _mainWindow.EnqueuePaths(paths);
            }
            else
            {
                _mainWindow.ActivateAndForeground();
            }
        });
    }


    protected override async void OnExit(ExitEventArgs e)
    {
        if (_server != null)
        {
            try
            {
                await _server.DisposeAsync().ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }

        _lock?.Dispose();
        base.OnExit(e);
    }
}
