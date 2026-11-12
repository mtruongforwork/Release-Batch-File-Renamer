namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Linq;
using Xunit;

public class FolderToolsTests
{
    private static string State(FakeFileSystem fs) =>
        string.Join("\n",
            fs.GetFiles("/r", "*", true).OrderBy(x => x, StringComparer.Ordinal).Select(f => f + "=" + fs.ReadAllText(f))
                .Concat(fs.GetDirectories("/r", true).OrderBy(x => x, StringComparer.Ordinal).Select(d => "D " + d)));

    private static OperationExecutor Exec(FakeFileSystem fs) => new(fs, "/journal", new QuarantineStore(fs, "/q"));

    private static void AssertRoundTrip(FakeFileSystem fs, FolderPlan plan)
    {
        var before = State(fs);
        var result = Exec(fs).Execute(plan.Operations);
        Assert.Equal(0, result.Failed);
        var undo = new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.Equal(0, undo.Failed);
        Assert.Equal(before, State(fs));
    }

    [Fact]
    public void Flatten_MovesFilesUp_RenamesDuplicates_AndRemovesEmptiedFolders()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText("/r/a.txt", "root-a");
        fs.WriteAllText("/r/x/a.txt", "x-a");
        fs.WriteAllText("/r/x/y/b.txt", "b");
        fs.EnsureDirectory("/r/empty");

        var plan = FolderToolsPlanner.Flatten("/r", new FlattenOptions(), fs);
        var result = Exec(fs).Execute(plan.Operations);

        Assert.Equal(0, result.Failed);
        Assert.Equal("root-a", fs.ReadAllText("/r/a.txt"));
        Assert.Equal("x-a", fs.ReadAllText("/r/a (2).txt"));
        Assert.True(fs.FileExists("/r/b.txt"));
        Assert.Empty(fs.GetDirectories("/r", true));
    }

    [Fact]
    public void Flatten_ThenUndo_RestoresTreeIncludingEmptyFolders()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText("/r/a.txt", "1");
        fs.WriteAllText("/r/x/a.txt", "2");
        fs.WriteAllText("/r/x/y/z/c.txt", "3");
        fs.EnsureDirectory("/r/x/empty");

        AssertRoundTrip(fs, FolderToolsPlanner.Flatten("/r", new FlattenOptions(), fs));
    }

    [Fact]
    public void Flatten_PrefixPath_JoinsFolderNamesIntoFileName()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText("/r/Album/2024/IMG001.jpg", "p");

        var plan = FolderToolsPlanner.Flatten("/r", new FlattenOptions(PrefixPath: true), fs);

        Assert.EndsWith("Album_2024_IMG001.jpg", plan.Operations[0].To);
    }

    [Fact]
    public void Flatten_SkipsLinkedFolders_AndKeepsTheirParent()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText("/r/x/a.txt", "a");
        fs.WriteAllText("/r/x/link/secret.txt", "s");
        fs.MarkLink("/r/x/link");

        var plan = FolderToolsPlanner.Flatten("/r", new FlattenOptions(), fs);

        Assert.DoesNotContain(plan.Operations, o => o.From.Contains("secret"));
        Assert.DoesNotContain(plan.Operations, o => o.Kind == OperationKind.RemoveDir && o.From == "/r/x");
        Assert.NotEmpty(plan.Skipped);
    }

    [Fact]
    public void RemoveEmpty_RemovesNestedEmpties_AndJunkOnlyFolders_NeverRoot()
    {
        var fs = new FakeFileSystem();
        fs.EnsureDirectory("/r/e1/e2");
        fs.WriteAllText("/r/junk/Thumbs.db", "t");
        fs.WriteAllText("/r/keep/file.txt", "k");
        fs.EnsureDirectory("/r/keep/inner");

        var plan = FolderToolsPlanner.RemoveEmpty("/r", new RemoveEmptyOptions(), fs);
        var removed = plan.Operations.Where(o => o.Kind == OperationKind.RemoveDir).Select(o => o.From).ToList();

        Assert.Contains("/r/e1", removed);
        Assert.Contains("/r/e1/e2", removed);
        Assert.Contains("/r/junk", removed);
        Assert.Contains("/r/keep/inner", removed);
        Assert.DoesNotContain("/r/keep", removed);
        Assert.DoesNotContain("/r", removed);
        Assert.True(removed.IndexOf("/r/e1/e2") < removed.IndexOf("/r/e1"));
    }

    [Fact]
    public void RemoveEmpty_JunkNotEmpty_WhenOptionOff_AndRoundTrips()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText("/r/junk/Thumbs.db", "t");
        fs.EnsureDirectory("/r/e1/e2");

        Assert.DoesNotContain(FolderToolsPlanner.RemoveEmpty("/r", new RemoveEmptyOptions(false), fs).Operations,
            o => o.From == "/r/junk");

        AssertRoundTrip(fs, FolderToolsPlanner.RemoveEmpty("/r", new RemoveEmptyOptions(), fs));
        Assert.True(fs.FileExists("/r/junk/Thumbs.db"));
    }

    [Fact]
    public void Wrap_GroupsSameStem_OrSeparatesPerFile()
    {
        var fs = new FakeFileSystem();
        var jpg = new FileItem("/r/IMG_001.jpg", "/r", "IMG_001", "jpg", false, 1, DateTime.UtcNow, DateTime.UtcNow, 0);
        var raw = new FileItem("/r/IMG_001.CR2", "/r", "IMG_001", "CR2", false, 1, DateTime.UtcNow, DateTime.UtcNow, 1);
        fs.WriteAllText(jpg.FullPath, "j");
        fs.WriteAllText(raw.FullPath, "r");

        var grouped = FolderToolsPlanner.Wrap(new[] { jpg, raw }, new WrapOptions(true), fs);
        Assert.Single(grouped.Operations.Select(o => System.IO.Path.GetDirectoryName(o.To)).Distinct());

        var single = FolderToolsPlanner.Wrap(new[] { jpg, raw }, new WrapOptions(false), fs);
        var dirs = single.Operations.Select(o => System.IO.Path.GetDirectoryName(o.To)).Distinct().ToList();
        Assert.Equal(2, dirs.Count);

        AssertRoundTrip(fs, grouped);
    }

    [Fact]
    public void Wrap_SkipsWhenFileAlreadyBlocksFolderName()
    {
        var fs = new FakeFileSystem();
        var f = new FileItem("/r/a.txt", "/r", "a", "txt", false, 1, DateTime.UtcNow, DateTime.UtcNow, 0);
        fs.WriteAllText(f.FullPath, "x");
        fs.WriteAllText("/r/a", "blocker");

        var plan = FolderToolsPlanner.Wrap(new[] { f }, new WrapOptions(), fs);

        Assert.Empty(plan.Operations);
        Assert.Single(plan.Skipped);
    }

    [Fact]
    public void BuildTree_FromIndentedAndPathLists_StaysInsideRoot()
    {
        var fs = new FakeFileSystem();
        fs.EnsureDirectory("/r");

        var indented = FolderToolsPlanner.BuildTree("/r", "2024\n  Q1\n  Q2\n    notes\n2025", fs);
        Assert.Equal(new[] { "/r/2024", "/r/2024/Q1", "/r/2024/Q2", "/r/2024/Q2/notes", "/r/2025" },
            indented.Operations.Select(o => o.From.Replace('\\', '/')));

        var paths = FolderToolsPlanner.BuildTree("/r", "..\\..\\evil\nA/B\nCON", fs);
        Assert.All(paths.Operations, o => Assert.StartsWith("/r/", o.From.Replace('\\', '/')));
        Assert.DoesNotContain(paths.Operations, o => o.From.Contains(".."));

        AssertRoundTrip(fs, indented);
    }

    [Fact]
    public void BuildTree_LimitsFolderCount()
    {
        var fs = new FakeFileSystem();
        fs.EnsureDirectory("/r");
        var text = string.Join("\n", Enumerable.Range(0, FolderToolsPlanner.MaxTreeFolders + 50).Select(i => "d" + i));

        var plan = FolderToolsPlanner.BuildTree("/r", text, fs);

        Assert.Equal(FolderToolsPlanner.MaxTreeFolders, plan.Operations.Count);
        Assert.NotEmpty(plan.Skipped);
    }
}
