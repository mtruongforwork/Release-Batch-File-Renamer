namespace BFR.Core.Tests;

using BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Xunit;

/// <summary>Test cho B3: tự xử lý trùng tên bằng hậu tố " (2)", " (3)"…</summary>
public class ConflictResolverTests
{
    private static PreviewItem Item(string oldName, string newName, string dir = @"C:\a")
    {
        var name = Path.GetFileNameWithoutExtension(oldName);
        var ext = Path.GetExtension(oldName).TrimStart('.');
        var file = new FileItem(Path.Combine(dir, oldName), dir, name, ext, false, 1, DateTime.UtcNow, DateTime.UtcNow, 0);
        return new PreviewItem(file, oldName, newName, PreviewStatus.Ok, new List<string>());
    }

    private static IReadOnlySet<string>? None(PreviewItem _) => null;

    private static IReadOnlySet<string> Set(params string[] names) =>
        new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void DuplicatesInBatch_GetIncreasingSuffixes_FirstKeepsItsName()
    {
        var items = new[] { Item("1.txt", "a.txt"), Item("2.txt", "a.txt"), Item("3.txt", "a.txt") };

        var r = ConflictResolver.Resolve(items, None);

        Assert.Equal(new[] { "a.txt", "a (2).txt", "a (3).txt" }, r.Items.Select(i => i.NewName));
        Assert.Equal(2, r.ResolvedCount);
    }

    [Fact]
    public void Suffix_GoesBeforeTheExtension_AndExtensionIsKept()
    {
        var r = ConflictResolver.Resolve(new[] { Item("1.tar.gz", "x.tar.gz"), Item("2.tar.gz", "x.tar.gz") }, None);

        Assert.Equal("x.tar (2).gz", r.Items[1].NewName);
    }

    [Fact]
    public void NoExtension_SuffixIsAppended()
    {
        var r = ConflictResolver.Resolve(new[] { Item("1", "README"), Item("2", "README") }, None);

        Assert.Equal("README (2)", r.Items[1].NewName);
    }

    [Fact]
    public void DuplicatesAreCaseInsensitive_LikeWindows()
    {
        var r = ConflictResolver.Resolve(new[] { Item("1.txt", "Photo.TXT"), Item("2.txt", "photo.txt") }, None);

        Assert.Equal("photo (2).txt", r.Items[1].NewName);
    }

    [Fact]
    public void CollisionWithFileOutsideTheBatch_IsResolved()
    {
        var outside = Set("report.txt", "report (2).txt");

        var r = ConflictResolver.Resolve(new[] { Item("draft.txt", "report.txt") }, _ => outside);

        Assert.Equal("report (3).txt", r.Items[0].NewName);   // bỏ qua cả (2) vì đã có ngoài lô
    }

    [Fact]
    public void UnchangedFile_KeepsItsNameAndReservesIt()
    {
        // b.txt giữ nguyên; a.txt đổi thành b.txt thì phải tránh tên b.txt của tệp kia.
        var items = new[] { Item("a.txt", "b.txt"), Item("b.txt", "b.txt") };

        var r = ConflictResolver.Resolve(items, None);

        Assert.Equal("b (2).txt", r.Items[0].NewName);
        Assert.Equal("b.txt", r.Items[1].NewName);
    }

    [Fact]
    public void DifferentDirectories_AreNotConflicts()
    {
        var items = new[] { Item("1.txt", "a.txt", @"C:\one"), Item("2.txt", "a.txt", @"C:\two") };

        var r = ConflictResolver.Resolve(items, None);

        Assert.Equal(0, r.ResolvedCount);
        Assert.All(r.Items, i => Assert.Equal("a.txt", i.NewName));
    }

    [Fact]
    public void InvalidNames_AreLeftAlone_ForTheDetectorToBlock()
    {
        var r = ConflictResolver.Resolve(new[] { Item("1.txt", "bad:name.txt"), Item("2.txt", "bad:name.txt") }, None);

        Assert.Equal(0, r.ResolvedCount);
        Assert.All(r.Items, i => Assert.Equal("bad:name.txt", i.NewName));
    }

    [Fact]
    public void Resolution_AddsATraceLine()
    {
        var r = ConflictResolver.Resolve(new[] { Item("1.txt", "a.txt"), Item("2.txt", "a.txt") }, None);

        Assert.Contains(r.Items[1].Trace, t => t.Contains("(2)"));
        Assert.Empty(r.Items[0].Trace);
    }

    [Fact]
    public void NoConflicts_ReturnsTheSameNames()
    {
        var items = new[] { Item("1.txt", "x.txt"), Item("2.txt", "y.txt") };

        var r = ConflictResolver.Resolve(items, None);

        Assert.Equal(0, r.ResolvedCount);
        Assert.Equal(new[] { "x.txt", "y.txt" }, r.Items.Select(i => i.NewName));
    }

    [Fact]
    public void PreviewEngine_WithAutoResolve_ProducesNoBlockingConflict()
    {
        var pipeline = new RulePipeline();
        pipeline.Add(new ReplaceDictRule { Pairs = "one\tsame\ntwo\tsame" });
        var files = new[]
        {
            Item("one.txt", "one.txt").File,
            Item("two.txt", "two.txt").File
        };
        var ctx = new RuleContext(DateTime.UtcNow, "seed");

        var without = new PreviewEngine(pipeline).Run(files, ctx);
        var engine = new PreviewEngine(pipeline) { AutoResolveConflicts = true };
        var with = engine.Run(files, ctx);

        Assert.True(without.HasBlockingError);
        Assert.False(with.HasBlockingError);
        Assert.Equal(new[] { "same.txt", "same (2).txt" }, with.Items.Select(i => i.NewName));
        Assert.Equal(1, engine.AutoResolvedCount);
    }

    [Fact]
    public void PreviewEngine_WithAutoResolve_AvoidsFilesOutsideTheBatch()
    {
        var pipeline = new RulePipeline();
        pipeline.Add(new ReplaceDictRule { Pairs = "draft\tfinal" });
        var files = new[] { Item("draft.txt", "draft.txt").File };

        var result = new PreviewEngine(pipeline) { AutoResolveConflicts = true }
            .Run(files, new RuleContext(DateTime.UtcNow, "seed"), Set("final.txt"));

        Assert.False(result.HasBlockingError);
        Assert.Equal("final (2).txt", result.Items[0].NewName);
    }

    [Fact]
    public void ManyDuplicates_AreResolvedQuickly_AndAllNamesAreUnique()
    {
        // Trước khi sửa: dò lại từ (2) cho mỗi tệp => O(n²) và dừng ở (99999), tệp thứ 100.000 vẫn trùng.
        var items = Enumerable.Range(0, 120_000).Select(i => Item($"f{i}.txt", "a.txt")).ToArray();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = ConflictResolver.Resolve(items, None);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"mất {sw.Elapsed.TotalSeconds:F1}s");
        Assert.Equal(120_000, r.Items.Select(i => i.NewName.ToLowerInvariant()).Distinct().Count());
        Assert.Equal(119_999, r.ResolvedCount);
    }

    [Fact]
    public void SuffixSearch_SkipsNamesTakenOutsideTheBatch_WithTheCache()
    {
        var items = new[] { Item("1.txt", "a.txt"), Item("2.txt", "a.txt"), Item("3.txt", "a.txt") };

        var r = ConflictResolver.Resolve(items, _ => Set("a (3).txt"));

        Assert.Equal(new[] { "a.txt", "a (2).txt", "a (4).txt" }, r.Items.Select(i => i.NewName));
    }
}
