namespace BFR.Core.Tests;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

public class WorkflowTests
{
    private static readonly RuleContext Ctx = new(new DateTime(2024, 1, 1), "seed");

    private static Preset Lower() => BuiltInPresets.All.First(p => p.Name == "Chữ thường toàn bộ");

    private static FileItem Add(FakeFileSystem fs, string path, string content = "x")
    {
        fs.WriteAllText(path, content);
        var t = new DateTime(2022, 6, 1, 10, 0, 0, DateTimeKind.Utc);
        fs.SetTimes(path, FileTimes.From(t, t, t));
        var dir = path[..path.LastIndexOf('/')];
        return new FileItem(path, dir, Path.GetFileNameWithoutExtension(path), Path.GetExtension(path).TrimStart('.'), false, content.Length, t, t, 0);
    }

    private static string Snapshot(FakeFileSystem fs) =>
        string.Join("\n",
            fs.GetFiles("/r", "*", true).OrderBy(x => x, StringComparer.Ordinal).Select(f => f + "=" + fs.ReadAllText(f) + "@" + fs.GetTimes(f).ModifiedUtcTicks)
                .Concat(fs.GetDirectories("/r", true).OrderBy(x => x, StringComparer.Ordinal).Select(d => "D " + d)));

    private static OperationExecutor Exec(FakeFileSystem fs) => new(fs, "/journal", new QuarantineStore(fs, "/q"));

    private sealed class FakeMetadata : IMetadataProvider
    {
        public Dictionary<string, string> TakenByOriginalPath { get; } = new(StringComparer.OrdinalIgnoreCase);
        public IReadOnlyDictionary<string, string> Read(FileItem file) =>
            TakenByOriginalPath.TryGetValue(file.FullPath, out var t) ? new Dictionary<string, string> { ["taken"] = t } : new Dictionary<string, string>();
    }

    [Fact]
    public void Chain_RenameThenOrganizeThenTimestamps_PredictsExactlyWhatExecutionDoes()
    {
        var fs = new FakeFileSystem();
        var files = new[] { Add(fs, "/r/Photo.JPG"), Add(fs, "/r/NOTES.TXT"), Add(fs, "/r/Zed.JPG") };
        var before = Snapshot(fs);
        var steps = new WorkflowStep[]
        {
            new RenameStep(Lower()),
            new OrganizeStep("{ext.upper}"),
            new TimestampStep(new TimestampOptions(TimestampSource.Shift, SetModified: true, Offset: TimeSpan.FromHours(2)))
        };

        var plan = WorkflowPlanner.Plan(files, steps, fs, Ctx);

        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.Equal(before, Snapshot(fs)); // lập kế hoạch không chạm hệ thống tệp nền
        Assert.Equal(3, plan.Steps.Count);

        var result = Exec(fs).Execute(plan.Operations);
        Assert.Equal(0, result.Failed);

        // Bước sau thấy kết quả của bước trước: tên đã là chữ thường khi Organize chạy.
        Assert.True(fs.FileExists("/r/JPG/photo.jpg"));
        Assert.True(fs.FileExists("/r/TXT/notes.txt"));
        Assert.Equal(new[] { "/r/JPG/photo.jpg", "/r/JPG/zed.jpg", "/r/TXT/notes.txt" }, plan.FinalFiles.Select(f => f.FullPath).OrderBy(x => x));
        Assert.All(plan.FinalFiles, f => Assert.True(fs.FileExists(f.FullPath), f.FullPath));
        Assert.Equal(new DateTime(2022, 6, 1, 12, 0, 0, DateTimeKind.Utc), fs.GetTimes("/r/JPG/photo.jpg").ModifiedUtc);

        // Một journal duy nhất: hoàn tác một lần trả lại cả chuỗi.
        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.Equal(before, Snapshot(fs));
    }

    [Fact]
    public void Metadata_FollowsFilesAcrossRenames()
    {
        var fs = new FakeFileSystem();
        var a = Add(fs, "/r/IMG_A.jpg");
        var meta = new FakeMetadata();
        meta.TakenByOriginalPath[a.FullPath] = new DateTime(2019, 7, 8, 0, 0, 0, DateTimeKind.Local).ToString("O");

        var plan = WorkflowPlanner.Plan(new[] { a },
            new WorkflowStep[] { new RenameStep(Lower()), new OrganizeStep("{exif.year}") }, fs, Ctx, meta);

        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Assert.EndsWith("2019/img_a.jpg", plan.FinalFiles[0].FullPath.Replace('\\', '/'));
    }

    [Fact]
    public void FailingStep_StopsThePlan_AndLaterStepsAreNotPlanned()
    {
        var fs = new FakeFileSystem();
        var files = new[] { Add(fs, "/r/a.txt") };

        var plan = WorkflowPlanner.Plan(files,
            new WorkflowStep[] { new RenameStep(Lower()), new OrganizeStep("{nope}"), new WrapStep() }, fs, Ctx);

        Assert.False(plan.IsValid);
        Assert.Contains("2", plan.Errors[0]);
        Assert.Equal(2, plan.Steps.Count);          // bước 3 không được lập
        Assert.NotEmpty(plan.Steps[1].Issues);
    }

    [Fact]
    public void FlattenThenRemoveEmpty_RoundTrips_AndWrapAfterFlattenUsesNewLocations()
    {
        var fs = new FakeFileSystem();
        var files = new[] { Add(fs, "/r/x/a.txt"), Add(fs, "/r/x/y/b.txt"), Add(fs, "/r/c.txt") };
        fs.EnsureDirectory("/r/empty/inner");
        var before = Snapshot(fs);

        var plan = WorkflowPlanner.Plan(files,
            new WorkflowStep[] { new FlattenStep(), new RemoveEmptyStep(), new WrapStep() }, fs, Ctx);

        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        var result = Exec(fs).Execute(plan.Operations);
        Assert.Equal(0, result.Failed);
        Assert.True(fs.FileExists("/r/a/a.txt"));
        Assert.True(fs.FileExists("/r/b/b.txt"));
        Assert.False(fs.DirectoryExists("/r/empty"));
        Assert.All(plan.FinalFiles, f => Assert.True(fs.FileExists(f.FullPath), f.FullPath));

        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.Equal(before, Snapshot(fs));
    }

    [Fact]
    public void CopyMode_ContinuesWithTheCopies()
    {
        var fs = new FakeFileSystem();
        var a = Add(fs, "/r/a.txt");

        var plan = WorkflowPlanner.Plan(new[] { a },
            new WorkflowStep[] { new OrganizeStep("{ext}", OrganizeMode.Copy), new AttributeStep(FileAttributes.ReadOnly) }, fs, Ctx);

        Assert.True(plan.IsValid, string.Join("; ", plan.Errors));
        Exec(fs).Execute(plan.Operations);
        Assert.True(fs.FileExists("/r/a.txt"));                                 // bản gốc còn
        Assert.Equal(FileAttributes.ReadOnly, fs.GetAttributes("/r/txt/a.txt")); // thuộc tính áp dụng lên bản sao
        Assert.Equal(FileAttributes.Normal, fs.GetAttributes("/r/a.txt"));
    }

    [Fact]
    public void EmptyOrTooLongWorkflows_AreRejected()
    {
        var fs = new FakeFileSystem();
        var f = Add(fs, "/r/a.txt");

        Assert.False(WorkflowPlanner.Plan(new[] { f }, Array.Empty<WorkflowStep>(), fs, Ctx).IsValid);
        Assert.False(WorkflowPlanner.Plan(new[] { f }, Enumerable.Range(0, 21).Select(_ => (WorkflowStep)new WrapStep()).ToList(), fs, Ctx).IsValid);
    }

    [Fact]
    public void Serializer_RoundTripsAllStepTypes_AndRejectsBadInput()
    {
        var def = new WorkflowDefinition("Dọn ảnh", new List<WorkflowStep>
        {
            new RenameStep(Lower()),
            new OrganizeStep(@"{exif.year}\{exif.month:00}", OrganizeMode.Copy, OrganizeConflictPolicy.Skip, "/root"),
            new WrapStep(false),
            new FlattenStep(true, "-", false),
            new RemoveEmptyStep(false),
            new TimestampStep(new TimestampOptions(TimestampSource.FileName, SetCreated: true, FileNamePattern: "IMG_{yyyyMMdd}")),
            new AttributeStep(FileAttributes.Hidden, FileAttributes.ReadOnly)
        });

        var json = WorkflowSerializer.Serialize(def);
        var back = WorkflowSerializer.Parse(json);

        Assert.Equal(def.Name, back.Name);
        Assert.Equal(def.Steps.Select(s => s.Kind), back.Steps.Select(s => s.Kind));
        Assert.Equal(def.Steps[1], back.Steps[1]);
        Assert.Equal(def.Steps[5], back.Steps[5]);
        Assert.Equal(def.Steps[6], back.Steps[6]);

        Assert.ThrowsAny<Exception>(() => WorkflowSerializer.Parse(null));
        Assert.ThrowsAny<Exception>(() => WorkflowSerializer.Parse("{ not json"));
        Assert.ThrowsAny<Exception>(() => WorkflowSerializer.Parse("{\"Name\":\"x\",\"Steps\":[]}"));
        Assert.ThrowsAny<Exception>(() => WorkflowSerializer.Parse("{\"Name\":\"x\",\"Steps\":[{\"step\":\"hack\"}]}"));
        Assert.ThrowsAny<Exception>(() => WorkflowSerializer.Parse(new string(' ', WorkflowSerializer.MaxJsonBytes + 1)));
    }
}
