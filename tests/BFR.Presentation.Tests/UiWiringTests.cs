namespace BFR.Presentation.Tests;

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using Xunit;

/// <summary>
/// Kiểm tra tĩnh XAML của giao diện: bắt các lỗi nối dây mà test ViewModel không thấy.
/// </summary>
public class UiWiringTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BatchFileRenamer.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Không tìm thấy thư mục gốc repo.");
    }

    /// <summary>
    /// Một nút vừa có <c>Click=</c> vừa có <c>Command=</c> sẽ chạy HAI lần. Lỗi thật đã gặp: nút Áp dụng có
    /// <c>Click="Apply_Click"</c> (hỏi xác nhận) cùng <c>Command="{Binding ApplyCommand}"</c> (đổi tên luôn),
    /// nên bấm "Hủy" ở hộp xác nhận vẫn đổi tên tệp.
    /// </summary>
    [Fact]
    public void NoElementHasBothClickAndCommand()
    {
        var offenders = new System.Collections.Generic.List<string>();

        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "src", "BFR.App"), "*.xaml", SearchOption.AllDirectories)
                     .Where(f => !f.Contains(@"\obj\") && !f.Contains(@"\bin\")))
        {
            var text = File.ReadAllText(file);
            foreach (Match tag in Regex.Matches(text, @"<[A-Za-z][\w:.]*\s[^<>]*>", RegexOptions.Singleline))
            {
                var t = tag.Value;
                if (Regex.IsMatch(t, @"\sClick\s*=") && Regex.IsMatch(t, @"\sCommand\s*="))
                {
                    var line = text.Take(tag.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetFileName(file)}:{line}");
                }
            }
        }

        Assert.True(offenders.Count == 0, "Phần tử có cả Click và Command: " + string.Join(", ", offenders));
    }

    [Fact]
    public void MainWindow_ApplyAndUndoButtonsUseIsEnabled_NotCommand()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "BFR.App", "MainWindow.xaml"));

        Assert.DoesNotContain("Command=\"{Binding ApplyCommand}\"", xaml);
        Assert.DoesNotContain("Command=\"{Binding UndoCommand}\"", xaml);
        Assert.Contains("IsEnabled=\"{Binding CanApply}\"", xaml);
        Assert.Contains("IsEnabled=\"{Binding CanUndo}\"", xaml);
    }
}
