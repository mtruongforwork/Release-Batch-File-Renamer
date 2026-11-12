namespace BFR.Core.Tests;

using BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

using Xunit;

public class OperationTests
{
    private const string J = "/journal";
    private const string Q = "/q";

    private static (FakeFileSystem Fs, OperationExecutor Exec) Setup()
    {
        var fs = new FakeFileSystem();
        return (fs, new OperationExecutor(fs, J, new QuarantineStore(fs, Q)));
    }

    private static string Text(FakeFileSystem fs, string path) => fs.ReadAllText(path);

    [Fact]
    public void MoveCopyCreateDir_RunInOrder_AndUndoRestoresEverything()
    {
        var (fs, exec) = Setup();
        fs.WriteAllText("/data/a.txt", "A");
        fs.WriteAllText("/data/b.txt", "B");

        var result = exec.Execute(new[]
        {
            Operation.CreateDir("/data/sorted"),
            Operation.Move("/data/a.txt", "/data/sorted/a.txt"),
            Operation.Copy("/data/b.txt", "/data/sorted/b-copy.txt")
        });

        Assert.Equal(3, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.True(fs.FileExists("/data/sorted/a.txt"));
        Assert.False(fs.FileExists("/data/a.txt"));
        Assert.Equal("B", Text(fs, "/data/sorted/b-copy.txt"));

        var undo = new UndoService(fs).UndoJournal(result.JournalPath);

        Assert.Equal(3, undo.Undone);
        Assert.Equal(0, undo.Failed);
        Assert.True(fs.FileExists("/data/a.txt"));
        Assert.False(fs.FileExists("/data/sorted/a.txt"));
        Assert.False(fs.FileExists("/data/sorted/b-copy.txt"));
        Assert.True(fs.FileExists("/data/b.txt"));
        Assert.False(fs.DirectoryExists("/data/sorted"));
    }

    [Fact]
    public void Journal_HasSchema2Header_AndMoveStepsStayReadableAsV1()
    {
        var (fs, exec) = Setup();
        fs.WriteAllText("/d/a.txt", "A");

        var result = exec.Execute(new[] { Operation.Move("/d/a.txt", "/d/b.txt") });

        var entries = UndoService.ReadSteps(result.JournalPath, fs);
        Assert.Equal(2, entries.First(e => e.Type == "header").Schema);
        var done = entries.Single(e => e.Type == "step" && e.Status == "done");
        Assert.Equal("/d/a.txt", done.From);
        Assert.Equal("/d/b.txt", done.To);
        Assert.Equal("Direct", done.Kind);
        Assert.Equal("completed", entries.Last().FooterStatus);
    }

    [Fact]
    public void V1Journal_WrittenByOldVersion_CanStillBeUndone()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText("/d/new.txt", "A");
        var v1 =
            "{\"Type\":\"header\",\"Id\":\"abc\",\"TotalSteps\":1,\"AppVersion\":\"1.4.0\"}\n" +
            "{\"Type\":\"step\",\"Seq\":0,\"From\":\"/d/old.txt\",\"To\":\"/d/new.txt\",\"Kind\":\"Direct\",\"Status\":\"pending\"}\n" +
            "{\"Type\":\"step\",\"Seq\":0,\"From\":\"/d/old.txt\",\"To\":\"/d/new.txt\",\"Kind\":\"Direct\",\"Status\":\"done\"}\n" +
            "{\"Type\":\"footer\",\"FooterStatus\":\"completed\"}\n";
        fs.WriteAllText("/journal/v1.jsonl", v1);

        var undo = new UndoService(fs).UndoJournal("/journal/v1.jsonl");

        Assert.Equal(1, undo.Undone);
        Assert.True(fs.FileExists("/d/old.txt"));
        Assert.False(fs.FileExists("/d/new.txt"));
    }

    [Fact]
    public void Quarantine_MovesFileAway_AndUndoBringsItBack()
    {
        var (fs, exec) = Setup();
        fs.WriteAllText("/d/dup.txt", "same");

        var result = exec.Execute(new[] { Operation.Quarantine("/d/dup.txt") });

        Assert.Equal(1, result.Succeeded);
        Assert.False(fs.FileExists("/d/dup.txt"));
        var stored = fs.GetFiles(Q, "*", true).Single();
        Assert.Contains(result.JournalId, stored);
        Assert.EndsWith("00000-dup.txt", stored);

        new UndoService(fs).UndoJournal(result.JournalPath);

        Assert.Equal("same", Text(fs, "/d/dup.txt"));
        Assert.Empty(fs.GetFiles(Q, "*", true));
    }

    [Fact]
    public void Quarantine_SameFileNameFromTwoFolders_DoesNotCollide()
    {
        var (fs, exec) = Setup();
        fs.WriteAllText("/a/x.txt", "1");
        fs.WriteAllText("/b/x.txt", "2");

        var result = exec.Execute(new[] { Operation.Quarantine("/a/x.txt"), Operation.Quarantine("/b/x.txt") });

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(2, fs.GetFiles(Q, "*", true).Length);
        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.Equal("1", Text(fs, "/a/x.txt"));
        Assert.Equal("2", Text(fs, "/b/x.txt"));
    }

    [Fact]
    public void SetTimestampsAndAttributes_RoundTripThroughUndo()
    {
        var (fs, exec) = Setup();
        fs.WriteAllText("/d/p.jpg", "x");
        var before = fs.GetTimes("/d/p.jpg");
        var after = FileTimes.From(new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            new DateTime(2021, 2, 3, 4, 5, 6, DateTimeKind.Utc), new DateTime(2022, 3, 4, 5, 6, 7, DateTimeKind.Utc));

        var result = exec.Execute(new[]
        {
            Operation.SetTimestamps("/d/p.jpg", after),
            Operation.SetAttributes("/d/p.jpg", FileAttributes.ReadOnly | FileAttributes.Hidden)
        });

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(after, fs.GetTimes("/d/p.jpg"));
        Assert.Equal(FileAttributes.ReadOnly | FileAttributes.Hidden, fs.GetAttributes("/d/p.jpg"));

        var undo = new UndoService(fs).UndoJournal(result.JournalPath);

        Assert.Equal(2, undo.Undone);
        Assert.Equal(before, fs.GetTimes("/d/p.jpg"));
        Assert.Equal(FileAttributes.Normal, fs.GetAttributes("/d/p.jpg"));
    }

    [Fact]
    public void WriteFile_ReplacingExisting_KeepsOldVersionAndUndoRestoresIt()
    {
        var (fs, exec) = Setup();
        fs.WriteAllText("/d/photo.jpg", "ORIGINAL");
        fs.WriteAllText("/tmp/photo.jpg.new", "RESIZED");

        var result = exec.Execute(new[] { Operation.WriteFile("/tmp/photo.jpg.new", "/d/photo.jpg") });

        Assert.Equal(1, result.Succeeded);
        Assert.Equal("RESIZED", Text(fs, "/d/photo.jpg"));
        Assert.Contains(fs.GetFiles(Q, "*", true), f => Text(fs, f) == "ORIGINAL");

        new UndoService(fs).UndoJournal(result.JournalPath);

        Assert.Equal("ORIGINAL", Text(fs, "/d/photo.jpg"));
        Assert.Empty(fs.GetFiles(Q, "*", true));
    }

    [Fact]
    public void WriteFile_NewTarget_UndoRemovesIt()
    {
        var (fs, exec) = Setup();
        fs.WriteAllText("/tmp/out.zip", "ZIP");

        var result = exec.Execute(new[] { Operation.WriteFile("/tmp/out.zip", "/d/out.zip") });

        Assert.Equal("ZIP", Text(fs, "/d/out.zip"));
        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.False(fs.FileExists("/d/out.zip"));
    }

    [Fact]
    public void CreateDir_AlreadyExisting_IsNotRemovedByUndo()
    {
        var (fs, exec) = Setup();
        fs.EnsureDirectory("/d/keep");
        fs.WriteAllText("/d/keep/mine.txt", "x");
        fs.WriteAllText("/d/a.txt", "a");

        var result = exec.Execute(new[]
        {
            Operation.CreateDir("/d/keep"),
            Operation.Move("/d/a.txt", "/d/keep/a.txt")
        });
        new UndoService(fs).UndoJournal(result.JournalPath);

        Assert.True(fs.DirectoryExists("/d/keep"));
        Assert.True(fs.FileExists("/d/keep/mine.txt"));
        Assert.True(fs.FileExists("/d/a.txt"));
    }

    [Fact]
    public void CreateDir_UndoRefusesToDeleteDirectoryThatNowHoldsUserFiles()
    {
        var (fs, exec) = Setup();
        var result = exec.Execute(new[] { Operation.CreateDir("/d/new") });
        fs.WriteAllText("/d/new/user-added.txt", "x");

        var undo = new UndoService(fs).UndoJournal(result.JournalPath);

        Assert.Equal(1, undo.Failed);
        Assert.True(fs.FileExists("/d/new/user-added.txt"));
    }

    [Fact]
    public void RemoveDir_OnlyEmptyDirectories_AndUndoRecreates()
    {
        var (fs, exec) = Setup();
        fs.EnsureDirectory("/d/empty");
        fs.WriteAllText("/d/full/f.txt", "x");

        var result = exec.Execute(new[] { Operation.RemoveDir("/d/empty"), Operation.RemoveDir("/d/full") });

        Assert.Equal(1, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.False(fs.DirectoryExists("/d/empty"));
        Assert.True(fs.FileExists("/d/full/f.txt"));

        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.True(fs.DirectoryExists("/d/empty"));
    }

    [Fact]
    public void FailedOperation_IsRecorded_AndOthersStillRun()
    {
        var (fs, exec) = Setup();
        fs.WriteAllText("/d/a.txt", "a");
        fs.WriteAllText("/d/c.txt", "c");

        var result = exec.Execute(new[]
        {
            Operation.Move("/d/a.txt", "/d/b.txt"),
            Operation.Move("/d/missing.txt", "/d/x.txt"),
            Operation.Move("/d/c.txt", "/d/d.txt")
        });

        Assert.Equal(2, result.Succeeded);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Errors);
        Assert.Equal("failed", UndoService.ReadSteps(result.JournalPath, fs).Last().FooterStatus);
    }

    [Fact]
    public void SetTimestamps_OnMissingFile_FailsWithoutTouchingAnything()
    {
        var (_, exec) = Setup();
        var result = exec.Execute(new[] { Operation.SetTimestamps("/d/none.txt", default) });
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public void Cancel_StopsBetweenOperations_AndFooterSaysCancelled()
    {
        var (fs, exec) = Setup();
        for (var i = 0; i < 5; i++) fs.WriteAllText($"/d/{i}.txt", "x");
        using var cts = new CancellationTokenSource();
        var ops = Enumerable.Range(0, 5).Select(i => Operation.Move($"/d/{i}.txt", $"/d/n{i}.txt")).ToList();
        var progress = new SyncProgress(n => { if (n == 2) cts.Cancel(); });

        var result = exec.Execute(ops, ct: cts.Token, progress: progress);

        Assert.True(result.Cancelled);
        Assert.Equal(2, result.Succeeded);
        Assert.Equal("cancelled", UndoService.ReadSteps(result.JournalPath, fs).Last().FooterStatus);
        new UndoService(fs).UndoJournal(result.JournalPath);
        Assert.True(Enumerable.Range(0, 5).All(i => fs.FileExists($"/d/{i}.txt")));
    }

    private sealed class SyncProgress : IProgress<int>
    {
        private readonly Action<int> _a;
        public SyncProgress(Action<int> a) => _a = a;
        public void Report(int value) => _a(value);
    }

    [Fact]
    public void ProtectedPaths_AreRefused_ButQuarantineRootIsExempt()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var fs = new FakeFileSystem();
        fs.WriteAllText(Path.Combine(windows, "x.txt"), "x");
        // khu cách ly mặc định nằm trong dữ liệu của BFR (vùng cấm) nhưng vẫn phải dùng được
        var exec = new OperationExecutor(fs, "/journal");
        fs.WriteAllText(@"C:\Users\me\d.txt", "x");

        var blocked = exec.Execute(new[] { Operation.Quarantine(Path.Combine(windows, "x.txt")) });
        var allowed = exec.Execute(new[] { Operation.Quarantine(@"C:\Users\me\d.txt") });

        Assert.Equal(1, blocked.Failed);
        Assert.Equal(1, allowed.Succeeded);
    }

    // ----------------------------------------------------------------- Khôi phục sự cố

    private static void WriteJournal(FakeFileSystem fs, string path, params JournalEntry[] entries)
    {
        var opts = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        fs.WriteAllText(path, string.Join("\n", entries.Select(e => JsonSerializer.Serialize(e, opts))) + "\n");
    }

    [Fact]
    public void Recovery_Complete_FinishesPendingCopy_AndMarksAlreadyAppliedOnesDone()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText("/d/a.txt", "A");
        fs.WriteAllText("/d/b.txt", "B");
        fs.WriteAllText("/d/b-copy.txt", "B"); // đã sao chép xong nhưng chưa kịp ghi done
        WriteJournal(fs, "/journal/crash.jsonl",
            new JournalEntry { Type = "header", Schema = 2, TotalSteps = 2 },
            new JournalEntry { Type = "step", Seq = 0, Op = "Copy", From = "/d/a.txt", To = "/d/a-copy.txt", Status = "pending" },
            new JournalEntry { Type = "step", Seq = 1, Op = "Copy", From = "/d/b.txt", To = "/d/b-copy.txt", Status = "pending" });

        var svc = new RecoveryService(fs);
        Assert.Single(svc.FindIncomplete("/journal"));

        var result = svc.Complete("/journal/crash.jsonl");

        Assert.True(result.Success);
        Assert.Equal(2, result.Processed);
        Assert.Equal("A", Text(fs, "/d/a-copy.txt"));
        Assert.Empty(svc.FindIncomplete("/journal"));
    }

    [Fact]
    public void Recovery_Rollback_UndoesDoneAndAppliedPendingOps_LeavesUnstartedAlone()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText("/d/a.txt", "A");
        fs.WriteAllText("/d/c.txt", "C");
        fs.WriteAllText("/d/a-copy.txt", "A"); // bước 0 đã xong
        // bước 1 (copy c) chưa bắt đầu; bước 2 (CreateDir) đã tạo nhưng chưa ghi done
        fs.EnsureDirectory("/d/new");
        WriteJournal(fs, "/journal/crash.jsonl",
            new JournalEntry { Type = "header", Schema = 2, TotalSteps = 3 },
            new JournalEntry { Type = "step", Seq = 0, Op = "Copy", From = "/d/a.txt", To = "/d/a-copy.txt", Status = "pending" },
            new JournalEntry { Type = "step", Seq = 0, Op = "Copy", From = "/d/a.txt", To = "/d/a-copy.txt", Status = "done" },
            new JournalEntry { Type = "step", Seq = 1, Op = "Copy", From = "/d/c.txt", To = "/d/c-copy.txt", Status = "pending" },
            new JournalEntry { Type = "step", Seq = 2, Op = "CreateDir", From = "/d/new", Status = "pending" });

        var result = new RecoveryService(fs).Rollback("/journal/crash.jsonl");

        Assert.True(result.Success);
        Assert.False(fs.FileExists("/d/a-copy.txt"));
        Assert.False(fs.DirectoryExists("/d/new"));
        Assert.True(fs.FileExists("/d/a.txt"));
        Assert.True(fs.FileExists("/d/c.txt"));
    }

    [Fact]
    public void Recovery_Complete_ThenUndo_StillRestoresEverything()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText("/d/a.txt", "A");
        var before = FileTimes.From(DateTime.UtcNow.AddDays(-9), DateTime.UtcNow.AddDays(-9), DateTime.UtcNow.AddDays(-9));
        var after = FileTimes.From(new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2019, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        fs.SetTimes("/d/a.txt", before);
        WriteJournal(fs, "/journal/crash.jsonl",
            new JournalEntry { Type = "header", Schema = 2, TotalSteps = 1 },
            new JournalEntry { Type = "step", Seq = 0, Op = "SetTimestamps", From = "/d/a.txt", Before = before, After = after, Status = "pending" });

        new RecoveryService(fs).Complete("/journal/crash.jsonl");
        Assert.Equal(after, fs.GetTimes("/d/a.txt"));

        new UndoService(fs).UndoJournal("/journal/crash.jsonl");
        Assert.Equal(before, fs.GetTimes("/d/a.txt"));
    }

    // ----------------------------------------------------------------- Khu cách ly

    [Fact]
    public void Quarantine_Purge_DeletesOnlyExpiredRuns()
    {
        using var dir = new TempDir();
        var store = new QuarantineStore(new RealFileSystem(), dir.Combine("q"));
        var oldRun = dir.Combine("q", "oldrun");
        var newRun = dir.Combine("q", "newrun");
        Directory.CreateDirectory(oldRun);
        Directory.CreateDirectory(newRun);
        var oldFile = Path.Combine(oldRun, "00000-a.txt");
        File.WriteAllText(oldFile, "a");
        File.WriteAllText(Path.Combine(newRun, "00000-b.txt"), "b");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow.AddDays(-45));

        var removed = store.Purge(TimeSpan.FromDays(30));

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(oldRun));
        Assert.True(Directory.Exists(newRun));
        Assert.Equal((1, 1L), store.GetUsage());
    }

    [Fact]
    public void Quarantine_Purge_OnMissingRoot_IsNoOp()
    {
        using var dir = new TempDir();
        Assert.Equal(0, new QuarantineStore(new RealFileSystem(), dir.Combine("nope")).Purge(TimeSpan.FromDays(1)));
    }

    // ----------------------------------------------------------------- Hệ thống tệp thật

    [Fact]
    public void RealFileSystem_FullRoundTrip_MoveCopyTimesAttributesQuarantine()
    {
        using var dir = new TempDir();
        var fs = new RealFileSystem();
        var exec = new OperationExecutor(fs, dir.Combine("journal"), new QuarantineStore(fs, dir.Combine("q")));
        var a = dir.Combine("a.txt");
        var b = dir.Combine("b.txt");
        File.WriteAllText(a, "AAA");
        File.WriteAllText(b, "BBB");
        var originalTimes = fs.GetTimes(a);
        var newTimes = FileTimes.From(new DateTime(2018, 5, 6, 7, 8, 9, DateTimeKind.Utc), new DateTime(2018, 5, 6, 7, 8, 9, DateTimeKind.Utc), new DateTime(2018, 5, 6, 7, 8, 9, DateTimeKind.Utc));

        var result = exec.Execute(new[]
        {
            Operation.CreateDir(dir.Combine("sorted", "2018")),
            Operation.SetTimestamps(a, newTimes),
            Operation.Move(a, dir.Combine("sorted", "2018", "a.txt")),
            Operation.Copy(b, dir.Combine("sorted", "b.txt")),
            Operation.SetAttributes(dir.Combine("sorted", "b.txt"), FileAttributes.ReadOnly),
            Operation.Quarantine(b)
        });

        Assert.Equal(6, result.Succeeded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(newTimes.ModifiedUtcTicks, fs.GetTimes(dir.Combine("sorted", "2018", "a.txt")).ModifiedUtcTicks);
        Assert.False(File.Exists(b));

        var undo = new UndoService(fs).UndoJournal(result.JournalPath);

        Assert.Equal(6, undo.Undone);
        Assert.Equal("AAA", File.ReadAllText(a));
        Assert.Equal("BBB", File.ReadAllText(b));
        Assert.False(Directory.Exists(dir.Combine("sorted")));
        Assert.Equal(originalTimes.ModifiedUtcTicks, fs.GetTimes(a).ModifiedUtcTicks);
        Assert.Equal(0, new QuarantineStore(fs, dir.Combine("q")).GetUsage().Files);
    }
}
