namespace BFR.Core.Tests;

using BFR.Core;

using System;
using System.Collections.Generic;

using Xunit;

/// <summary>Chuỗi do Core sinh ra phải theo ngôn ngữ giao diện khi ứng dụng gắn bộ tra cứu.</summary>
public class CoreTextTests
{
    private static PreviewItem Item(string oldName, string newName)
    {
        var file = new FileItem($@"C:\d\{oldName}", @"C:\d", oldName, "txt", false, 1, DateTime.UtcNow, DateTime.UtcNow, 0);
        return new PreviewItem(file, oldName + ".txt", newName, PreviewStatus.Ok, new List<string>());
    }

    [Fact]
    public void WithoutResolver_FallsBackToVietnamese()
    {
        Assert.Equal("Trùng tên trong lô: 'a.txt'", CoreText.Get("CoreDuplicateInBatch", "Trùng tên trong lô: '{0}'", "a.txt"));
    }

    [Fact]
    public void ConflictDetail_UsesTheResolvedLanguage()
    {
        using var _ = CoreText.UseScoped(key => key == "CoreDuplicateInBatch" ? "Duplicate name in the batch: '{0}'" : null);

        var conflicts = new ConflictDetector().Detect(
            new[] { Item("1", "a.txt"), Item("2", "a.txt") },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal("Duplicate name in the batch: 'a.txt'", conflicts[0]!.Message);
    }

    [Fact]
    public void BuiltInPresetNames_AreTranslatedForDisplayOnly()
    {
        var preset = BuiltInPresets.All[0];
        var original = preset.Name;
        using var _ = CoreText.UseScoped(key => key == "PresetBuiltinCleanName" ? "Clean up file names" : null);

        Assert.Equal("Clean up file names", preset.DisplayName);
        Assert.Equal(original, preset.Name);   // khoá ổn định không đổi
        Assert.Equal("Custom", new Preset { Name = "Custom" }.DisplayName);   // preset của người dùng giữ nguyên
    }
}
