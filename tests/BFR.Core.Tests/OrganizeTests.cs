namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

public class OrganizeTests
{
    private static FileItem File(string dir, string name, string ext, long size = 10, DateTime? modified = null) =>
        new($"{dir}/{name}.{ext}", dir, name, ext, false, size,
            modified ?? new DateTime(2023, 5, 17, 12, 0, 0, DateTimeKind.Utc),
            modified ?? new DateTime(2023, 5, 17, 12, 0, 0, DateTimeKind.Utc), 0);

    private static RuleContext Ctx(Dictionary<string, IReadOnlyDictionary<string, string>>? per = null) =>
        new(new DateTime(2024, 1, 1), "seed", null, per);

    private static OrganizePlan Plan(FakeFileSystem fs, IReadOnlyList<FileItem> files, string template,
        OrganizeMode mode = OrganizeMode.Move, OrganizeConflictPolicy conflict = OrganizeConflictPolicy.Rename,
        int maxDepth = 0, RuleContext? ctx = null) =>
        OrganizePlanner.Plan(files, new OrganizeOptions("/root", template, mode, conflict, maxDepth), ctx ?? Ctx(), fs);

    [Fact]
    public void TypeTemplate_GroupsByCategory()
    {
        var fs = new FakeFileSystem();
        var files = new[] { File("/root", "a", "jpg"), File("/root", "b", "mp3"), File("/root", "c", "pdf"), File("/root", "d", "xyz") };
        foreach (var f in files) fs.WriteAllText(f.FullPath, "x");

        var plan = Plan(fs, files, "{type}");

        Assert.Empty(plan.TemplateErrors);
        Assert.Equal(4, plan.Operations.Count);
        Assert.Equal(4, plan.FolderCounts().Count);
        Assert.All(plan.Items, i => Assert.Equal(OrganizeStatus.Ready, i.Status));
    }

    [Fact]
    public void ExifDate_UsesTakenAndPadsMonth()
    {
        var fs = new FakeFileSystem();
        var f = File("/root", "img", "jpg");
        fs.WriteAllText(f.FullPath, "x");
        var meta = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            [f.FullPath] = new Dictionary<string, string> { ["taken"] = new DateTime(2021, 3, 9, 0, 0, 0, DateTimeKind.Local).ToString("O") }
        };

        var plan = Plan(fs, new[] { f }, @"{exif.year}\{exif.month:00}", ctx: Ctx(meta));

        Assert.Equal("/root/2021/03", plan.Items[0].TargetDirectory.Replace('\\', '/'));
    }

    [Fact]
    public void MissingExif_GoesToUnknownFolder_ButPlainYearFallsBackToModified()
    {
        var fs = new FakeFileSystem();
        var f = File("/root", "img", "jpg");
        fs.WriteAllText(f.FullPath, "x");

        var exif = Plan(fs, new[] { f }, "{exif.year}");
        Assert.True(exif.Items[0].UsedFallbackFolder);
        Assert.EndsWith("_Không xác định", exif.Items[0].TargetDirectory);

        var plain = Plan(fs, new[] { f }, "{year}");
        Assert.False(plain.Items[0].UsedFallbackFolder);
        Assert.EndsWith("2023", plain.Items[0].TargetDirectory);
    }

    [Fact]
    public void ConflictPolicy_RenamesOrSkips()
    {
        var fs = new FakeFileSystem();
        var a = File("/root/x", "same", "txt");
        var b = File("/root/y", "same", "txt");
        fs.WriteAllText(a.FullPath, "a");
        fs.WriteAllText(b.FullPath, "b");
        fs.WriteAllText("/root/txt/same.txt", "existing");

        var rename = Plan(fs, new[] { a, b }, "{ext}");
        Assert.EndsWith("same (2).txt", rename.Items[0].TargetPath);
        Assert.EndsWith("same (3).txt", rename.Items[1].TargetPath);

        var skip = Plan(fs, new[] { a, b }, "{ext}", conflict: OrganizeConflictPolicy.Skip);
        Assert.All(skip.Items, i => Assert.Equal(OrganizeStatus.Skipped, i.Status));
        Assert.Empty(skip.Operations);
    }

    [Fact]
    public void AlreadyInPlace_IsUnchanged()
    {
        var fs = new FakeFileSystem();
        var f = File("/root/txt", "n", "txt");
        fs.WriteAllText(f.FullPath, "x");

        var plan = Plan(fs, new[] { f }, "{ext}");

        Assert.Equal(OrganizeStatus.Unchanged, plan.Items[0].Status);
        Assert.Empty(plan.Operations);
    }

    [Fact]
    public void Template_NeverEscapesRoot()
    {
        var fs = new FakeFileSystem();
        var f = File("/root", "...evil", "txt");
        var bad = File("/root", @"..\..\evil", "txt"); // tên chứa dấu phân cách: không bao giờ là tên tệp thật
        fs.WriteAllText(f.FullPath, "x");

        var plan = Plan(fs, new[] { f, bad }, @"..\{name.first:5}\..\{parent}");

        Assert.Empty(plan.TemplateErrors);
        Assert.Equal(OrganizeStatus.Invalid, plan.Items[1].Status);
        var ready = plan.Items.Where(i => i.Status == OrganizeStatus.Ready).ToList();
        Assert.NotEmpty(ready);
        foreach (var i in ready)
        {
            var canon = i.TargetPath.Replace('\\', '/');
            Assert.StartsWith("/root/", canon);
            Assert.DoesNotContain("/../", canon);
        }
    }

    [Theory]
    [InlineData("{nope}")]
    [InlineData("{year")]
    [InlineData("")]
    public void BadTemplate_BlocksEverything(string template)
    {
        var fs = new FakeFileSystem();
        var f = File("/root", "a", "txt");
        fs.WriteAllText(f.FullPath, "x");

        var plan = Plan(fs, new[] { f }, template);

        Assert.NotEmpty(plan.TemplateErrors);
        Assert.Empty(plan.Operations);
        Assert.True(plan.HasBlockingIssues);
    }

    [Fact]
    public void FreeDepthLimit_RejectsDeepTemplates()
    {
        var fs = new FakeFileSystem();
        var f = File("/root", "a", "txt");
        fs.WriteAllText(f.FullPath, "x");

        Assert.Empty(Plan(fs, new[] { f }, @"{year}\{month}", maxDepth: 2).TemplateErrors);
        Assert.NotEmpty(Plan(fs, new[] { f }, @"{year}\{month}\{day}", maxDepth: 2).TemplateErrors);
    }

    [Fact]
    public void CopyMode_EmitsCopyOperations()
    {
        var fs = new FakeFileSystem();
        var f = File("/root", "a", "txt");
        fs.WriteAllText(f.FullPath, "x");

        var plan = Plan(fs, new[] { f }, "{ext}", OrganizeMode.Copy);

        Assert.Equal(OperationKind.Copy, plan.Operations.Single().Kind);
    }

    [Fact]
    public void ExecuteAndUndo_RestoresOriginalLayout_AndRemovesCreatedFolders()
    {
        var fs = new FakeFileSystem();
        var files = new[] { File("/root", "a", "jpg"), File("/root", "b", "txt"), File("/root", "c", "jpg") };
        foreach (var f in files) fs.WriteAllText(f.FullPath, f.Name);

        var plan = Plan(fs, files, "{type}");
        var exec = new OperationExecutor(fs, "/journal", new QuarantineStore(fs, "/q"));
        var result = exec.Execute(plan.Operations);

        Assert.Equal(3, result.Succeeded);
        Assert.False(fs.FileExists("/root/a.jpg"));

        var undo = new UndoService(fs).UndoJournal(result.JournalPath);

        Assert.Equal(0, undo.Failed);
        Assert.True(fs.FileExists("/root/a.jpg"));
        Assert.True(fs.FileExists("/root/b.txt"));
        Assert.Empty(fs.GetDirectories("/root", true));
    }

    [Theory]
    [InlineData("mp3", FileCategory.Audio)]
    [InlineData("JPG", FileCategory.Image)]
    [InlineData(".mkv", FileCategory.Video)]
    [InlineData("docx", FileCategory.Document)]
    [InlineData("zip", FileCategory.Archive)]
    [InlineData("", FileCategory.Other)]
    [InlineData("qqq", FileCategory.Other)]
    public void Classifier_MapsExtensions(string ext, FileCategory expected) =>
        Assert.Equal(expected, FileTypeClassifier.Classify(ext));
}
