namespace BFR.Shell;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

/// <summary>
/// COM server được Explorer gọi cho cả menu Windows 11 (desktop4/desktop10)
/// và menu cổ điển qua giá trị <c>ExplorerCommandHandler</c>.
///
/// Explorer truyền toàn bộ lựa chọn trong một <see cref="IShellItemArray"/>.
/// Handler ghi danh sách ra tệp tạm rồi mở BFR bằng <c>--file-list</c>, nhờ đó
/// không bị giới hạn độ dài dòng lệnh khi chọn hàng nghìn tệp.
/// </summary>
[ComVisible(true)]
[Guid(ShellIntegration.ExplorerCommandClsid)]
[ClassInterface(ClassInterfaceType.None)]
[ProgId(ShellIntegration.ExplorerCommandProgId)]
public sealed class ExplorerCommand : IExplorerCommand
{
    private const string VietnameseTitle = "Đổi tên hàng loạt với BFR";
    private const string EnglishTitle = "Batch Rename with BFR";
    private const string VietnameseToolTip = "Mở Batch File Renamer với các tệp/thư mục đã chọn";
    private const string EnglishToolTip = "Open Batch File Renamer with the selected files/folders";

    public int GetTitle(IShellItemArray? psiItemArray, out IntPtr ppszName)
    {
        ppszName = Marshal.StringToCoTaskMemUni(IsVietnamese() ? VietnameseTitle : EnglishTitle);
        return HResult.S_OK;
    }

    public int GetIcon(IShellItemArray? psiItemArray, out IntPtr ppszIcon)
    {
        var app = ExplorerCommandLauncher.FindAppExecutable();
        ppszIcon = app is null
            ? IntPtr.Zero
            : Marshal.StringToCoTaskMemUni($"\"{app}\",0");
        return HResult.S_OK;
    }

    public int GetToolTip(IShellItemArray? psiItemArray, out IntPtr ppszInfotip)
    {
        ppszInfotip = Marshal.StringToCoTaskMemUni(IsVietnamese() ? VietnameseToolTip : EnglishToolTip);
        return HResult.S_OK;
    }

    public int GetCanonicalName(out Guid pguidCommandName)
    {
        pguidCommandName = Guid.Parse(ShellIntegration.ExplorerCommandClsid);
        return HResult.S_OK;
    }

    public int GetState(IShellItemArray? psiItemArray, int fOkToBeSlow, out uint pCmdState)
    {
        // ECS_ENABLED = 0. Menu cũng hiển thị khi bấm chuột phải vào nền thư mục
        // (khi đó IShellItemArray rỗng và ứng dụng chỉ mở cửa sổ chính).
        pCmdState = 0;
        return HResult.S_OK;
    }

    public int GetFlags(out uint pFlags)
    {
        // ECF_HASSUBCOMMANDS = 1: menu cha mở ra menu con (Mở trong BFR + các preset đổi tên nhanh).
        pFlags = 1;
        return HResult.S_OK;
    }

    public int EnumSubCommands(out IntPtr ppEnum)
    {
        var commands = ExplorerSubCommand.BuildAll();
        ppEnum = Marshal.GetComInterfaceForObject(new EnumExplorerCommand(commands), typeof(IEnumExplorerCommandServer));
        return HResult.S_OK;
    }

    public int Invoke(IShellItemArray? psiItemArray, IntPtr pbc)
    {
        try
        {
            var paths = CollectPaths(psiItemArray);
            return ExplorerCommandLauncher.Launch(paths);
        }
        catch
        {
            return HResult.E_FAIL;
        }
    }

    private static bool IsVietnamese() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("vi", StringComparison.OrdinalIgnoreCase);

    internal static List<string> CollectPaths(IShellItemArray? itemArray)
    {
        var paths = new List<string>();
        if (itemArray is null) return paths;

        if (itemArray.GetCount(out var count) != HResult.S_OK) return paths;

        for (uint i = 0; i < count; i++)
        {
            if (itemArray.GetItemAt(i, out var item) != HResult.S_OK || item is null) continue;

            IntPtr namePtr = IntPtr.Zero;
            try
            {
                var hr = item.GetDisplayName(SIGDN.FileSysPath, out namePtr);
                if (hr != HResult.S_OK || namePtr == IntPtr.Zero)
                {
                    namePtr = IntPtr.Zero;
                    hr = item.GetDisplayName(SIGDN.DesktopAbsoluteParsing, out namePtr);
                }

                if (hr == HResult.S_OK && namePtr != IntPtr.Zero)
                {
                    var path = Marshal.PtrToStringUni(namePtr);
                    if (!string.IsNullOrWhiteSpace(path)) paths.Add(path);
                }
            }
            finally
            {
                if (namePtr != IntPtr.Zero) Marshal.FreeCoTaskMem(namePtr);
                try
                {
                    if (Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
                }
                catch
                {
                    // Explorer sở hữu vòng đời của item; bỏ qua lỗi release.
                }
            }
        }

        return paths;
    }
}

/// <summary>
/// Phần logic không phụ thuộc COM để dễ kiểm thử: tìm exe, ghi danh sách và khởi chạy.
/// </summary>
public static class ExplorerCommandLauncher
{
    public const string AppExecutableName = "BatchFileRenamer.exe";
    public const string FileListArgument = "--file-list";

    public static string? FindAppExecutable()
    {
        var overridePath = Environment.GetEnvironmentVariable("BFR_APP_PATH");
        var candidates = new[]
        {
            overridePath,
            Path.Combine(AppContext.BaseDirectory, AppExecutableName),
            Path.Combine(Path.GetDirectoryName(typeof(ExplorerCommand).Assembly.Location) ?? string.Empty, AppExecutableName)
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    public static string WriteFileList(IEnumerable<string> paths)
    {
        var directory = Path.Combine(Path.GetTempPath(), "BatchFileRenamer", "shell");
        Directory.CreateDirectory(directory);

        var filePath = Path.Combine(directory, $"shell-{Guid.NewGuid():N}.txt");
        var cleaned = paths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        File.WriteAllLines(filePath, cleaned, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return filePath;
    }

    public const string QuickArgument = "--quick";
    public const string ToolArgument = "--tool";

    /// <param name="quickKey">Khoá preset đổi tên nhanh; null = mở BFR bình thường.</param>
    /// <param name="toolKey">Khoá công cụ tệp (organize, duplicates...); mở trang Công cụ với các tệp đã chọn.</param>
    public static int Launch(IReadOnlyCollection<string> paths, string? quickKey = null, string? toolKey = null)
    {
        var app = FindAppExecutable();
        if (app is null) return HResult.E_FILE_NOT_FOUND;

        // Đổi tên nhanh mà không có tệp nào được chọn (ví dụ bấm vào nền thư mục): không làm gì.
        if ((quickKey is not null || toolKey is not null) && paths.Count == 0) return HResult.S_OK;

        string? listPath = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = app,
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(app) ?? AppContext.BaseDirectory
            };

            if (paths.Count > 0)
            {
                listPath = WriteFileList(paths);
                startInfo.ArgumentList.Add(FileListArgument);
                startInfo.ArgumentList.Add(listPath);
            }

            if (quickKey is not null)
            {
                startInfo.ArgumentList.Add(QuickArgument);
                startInfo.ArgumentList.Add(quickKey);
            }

            if (toolKey is not null)
            {
                startInfo.ArgumentList.Add(ToolArgument);
                startInfo.ArgumentList.Add(toolKey);
            }

            Process.Start(startInfo);
            return HResult.S_OK;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
        {
            // Không để lại tệp danh sách mồ côi nếu tiến trình không khởi chạy được.
            if (listPath is not null)
            {
                try { File.Delete(listPath); } catch { /* ignore */ }
            }
            return HResult.E_FAIL;
        }
    }
}
