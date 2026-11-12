namespace BFR.App.Infrastructure;

using BFR.Core;
using Microsoft.Win32;
using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;

/// <summary>Trạng thái mục khởi động cùng Windows.</summary>
public enum AutoStartState
{
    Disabled,
    Enabled,
    /// <summary>Người dùng đã tắt trong Task Manager / Cài đặt Windows; ứng dụng không bật lại được.</summary>
    DisabledByUser,
    DisabledByPolicy,
    Unavailable
}

/// <summary>
/// Khởi động cùng Windows.
/// Bản đóng gói MSIX dùng <c>StartupTask</c> (khai báo trong AppxManifest); ghi khoá Run của HKCU
/// trong gói bị ảo hoá theo từng ứng dụng nên không dùng được. Bản portable ghi khoá Run
/// với cờ <c>--background</c>.
/// </summary>
public static class AutoStart
{
    public const string TaskId = "BFRStartup";
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "BatchFileRenamer";

    public static async Task<AutoStartState> GetStateAsync()
    {
        try
        {
            if (ShellIntegration.IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                return Map(task.State);
            }

            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string ? AutoStartState.Enabled : AutoStartState.Disabled;
        }
        catch
        {
            return AutoStartState.Unavailable;
        }
    }

    public static async Task<AutoStartState> SetEnabledAsync(bool enable)
    {
        try
        {
            if (ShellIntegration.IsPackaged)
            {
                var task = await StartupTask.GetAsync(TaskId);
                if (enable) return Map(await task.RequestEnableAsync());
                task.Disable();
                return AutoStartState.Disabled;
            }

            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (enable)
            {
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return AutoStartState.Unavailable;
                key.SetValue(RunValueName, AutoStartCommand.Build(exe));
                return AutoStartState.Enabled;
            }

            key.DeleteValue(RunValueName, throwOnMissingValue: false);
            return AutoStartState.Disabled;
        }
        catch
        {
            return AutoStartState.Unavailable;
        }
    }

    /// <summary>
    /// True nếu tiến trình được Windows khởi động qua StartupTask (chỉ bản MSIX; bản portable
    /// nhận cờ --background từ khoá Run nên không cần kiểm tra này).
    /// </summary>
    public static bool LaunchedByStartup()
    {
        try
        {
            if (!ShellIntegration.IsPackaged) return false;
            return AppInstance.GetActivatedEventArgs()?.Kind == Windows.ApplicationModel.Activation.ActivationKind.StartupTask;
        }
        catch
        {
            return false;
        }
    }

    private static AutoStartState Map(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => AutoStartState.Enabled,
        StartupTaskState.DisabledByUser => AutoStartState.DisabledByUser,
        StartupTaskState.DisabledByPolicy => AutoStartState.DisabledByPolicy,
        _ => AutoStartState.Disabled
    };
}
