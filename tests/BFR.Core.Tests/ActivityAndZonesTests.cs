namespace BFR.Core.Tests;

using BFR.Core;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using Xunit;

/// <summary>Thư mục tạm thật, tự xoá khi xong.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BFR_Tests_" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public class ProtectedZonesTests
{
    private static readonly ProtectedZones Zones = new(new[] { @"C:\Windows", @"C:\Program Files", @"C:\Users\u\AppData\Local\BatchFileRenamer" });

    [Theory]
    [InlineData(@"C:\Windows", true)]
    [InlineData(@"C:\Windows\System32\kernel32.dll", true)]
    [InlineData(@"c:\WINDOWS\notepad.exe", true)]
    [InlineData(@"C:\Windows\..\Windows\x.txt", true)]
    [InlineData(@"C:\Program Files\App\a.dll", true)]
    [InlineData(@"C:\Users\u\AppData\Local\BatchFileRenamer\journal\j.jsonl", true)]
    [InlineData(@"D:\$Recycle.Bin\S-1-5\x.txt", true)]
    [InlineData(@"D:\System Volume Information\x", true)]
    [InlineData(@"C:\WindowsOld\x.txt", false)]          // chỉ trùng tiền tố, không phải con
    [InlineData(@"C:\Program Files (x86)\a.dll", false)] // khác thư mục (đã được liệt kê riêng ở cấu hình thật)
    [InlineData(@"C:\Users\u\Documents\Windows\x.txt", false)]
    [InlineData(@"D:\Photos\$Recycle.Bin.txt", false)]    // chỉ trùng một phần tên
    [InlineData(@"D:\Photos\a.jpg", false)]
    [InlineData("relative\\path", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsProtected_Cases(string? path, bool expected)
    {
        Assert.Equal(expected, Zones.IsProtected(path));
    }

    [Fact]
    public void Default_ProtectsRealWindowsAndProgramFiles()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Assert.True(ProtectedZones.Default.IsProtected(Path.Combine(windows, "System32", "cmd.exe")));
        Assert.True(ProtectedZones.Default.IsProtected(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "x", "y.dll")));
        Assert.False(ProtectedZones.Default.IsProtected(Path.Combine(Path.GetTempPath(), "photo.jpg")));
    }
}

public class ProtectedZoneEnforcementTests
{
    [Fact]
    public void Preview_MarksProtectedFile_AsBlocking_AndNeverOk()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var path = Path.Combine(windows, "bfr-test-file.txt");
        var file = new FileItem(path, windows, "bfr-test-file", "txt", false, 1, DateTime.UtcNow, DateTime.UtcNow, 0);

        var pipeline = new RulePipeline();
        pipeline.Add(new FindReplaceRule { Pattern = "bfr", Replacement = "x" });
        var result = new PreviewEngine(pipeline).Run(new List<FileItem> { file }, new RuleContext(DateTime.Now, "t"));

        Assert.Equal(PreviewStatus.Protected, result.Items[0].Status);
        Assert.True(result.HasBlockingError);
    }

    [Fact]
    public void Preview_NormalFile_StillOk()
    {
        using var dir = new TempDir();
        var path = dir.Combine("bfr-test-file.txt");
        var file = new FileItem(path, dir.Path, "bfr-test-file", "txt", false, 1, DateTime.UtcNow, DateTime.UtcNow, 0);

        var pipeline = new RulePipeline();
        pipeline.Add(new FindReplaceRule { Pattern = "bfr", Replacement = "x" });
        var result = new PreviewEngine(pipeline).Run(new List<FileItem> { file }, new RuleContext(DateTime.Now, "t"));

        Assert.Equal(PreviewStatus.Ok, result.Items[0].Status);
    }

    [Fact]
    public void Executor_RefusesProtectedPaths_AndRecordsFailure()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var from = Path.Combine(windows, "a.txt");
        var to = Path.Combine(windows, "b.txt");
        var fs = new FakeFileSystem();
        fs.WriteAllText(from, "x");

        var result = new RenameExecutor(fs, "/journal").Execute(new[] { new Step(from, to, StepKind.Direct) });

        Assert.Equal(1, result.Failed);
        Assert.Equal(0, result.Succeeded);
        Assert.True(fs.FileExists(from));
        Assert.False(fs.FileExists(to));
    }

    [Fact]
    public void Executor_RefusesMoveIntoProtectedFolder()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var from = @"C:\Users\me\a.txt";
        var fs = new FakeFileSystem();
        fs.WriteAllText(from, "x");

        var result = new RenameExecutor(fs, "/journal").Execute(new[] { new Step(from, Path.Combine(windows, "a.txt"), StepKind.Direct) });

        Assert.Equal(1, result.Failed);
        Assert.True(fs.FileExists(from));
    }

    [Fact]
    public void Scanner_Recursive_SkipsProtectedSubfolders_ButTopLevelFilesStay()
    {
        using var dir = new TempDir();
        Directory.CreateDirectory(dir.Combine("sub"));
        File.WriteAllText(dir.Combine("a.txt"), "x");
        File.WriteAllText(dir.Combine("sub", "b.txt"), "x");

        var items = new FileScanner().Scan(new[] { dir.Path }, new ScanOptions { Recursive = true });
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public void Scanner_Recursive_DoesNotFollowJunction()
    {
        using var dir = new TempDir();
        using var outside = new TempDir();
        File.WriteAllText(outside.Combine("secret.txt"), "x");
        File.WriteAllText(dir.Combine("a.txt"), "x");

        var link = dir.Combine("link");
        if (!TryMakeJunction(link, outside.Path)) return; // môi trường không tạo được junction: bỏ qua

        var items = new FileScanner().Scan(new[] { dir.Path }, new ScanOptions { Recursive = true });

        Assert.Single(items);
        Assert.Equal("a.txt", Path.GetFileName(items[0].FullPath));
        Assert.True(File.Exists(outside.Combine("secret.txt")));
    }

    private static bool TryMakeJunction(string link, string target)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
            {
                CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(10_000);
            return p.ExitCode == 0 && Directory.Exists(link);
        }
        catch { return false; }
    }
}

public class HistoryRetentionTests
{
    private static HistoryEntry Entry(string id, string journal, DateTime started) =>
        new() { Id = id, StartedUtc = started, JournalPath = journal };

    private static string Journal(TempDir dir, string name, bool complete = true)
    {
        var path = dir.Combine("journal", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{\"type\":\"header\"}\n" + (complete ? "{\"type\":\"footer\",\"footerStatus\":\"completed\"}\n" : ""));
        return path;
    }

    [Fact]
    public void Prune_DeletesJournalFilesOfDroppedEntries_KeepsOthers()
    {
        using var dir = new TempDir();
        var oldJ = Journal(dir, "old.jsonl");
        var newJ = Journal(dir, "new.jsonl");
        var store = new HistoryStore(new RealFileSystem(), dir.Combine("history.json"));
        store.Save(new[] { Entry("new", newJ, DateTime.UtcNow), Entry("old", oldJ, DateTime.UtcNow.AddDays(-90)) });

        store.PruneAndLimit(TimeSpan.FromDays(30), int.MaxValue);

        Assert.False(File.Exists(oldJ));
        Assert.True(File.Exists(newJ));
        Assert.Single(store.LoadAll());
    }

    [Fact]
    public void Limit_DeletesJournalOfEntriesBeyondTheLimit()
    {
        using var dir = new TempDir();
        var a = Journal(dir, "a.jsonl");
        var b = Journal(dir, "b.jsonl");
        var store = new HistoryStore(new RealFileSystem(), dir.Combine("history.json"));
        store.Save(new[] { Entry("b", b, DateTime.UtcNow), Entry("a", a, DateTime.UtcNow.AddMinutes(-1)) });

        store.PruneAndLimit(TimeSpan.FromDays(30), 1);

        Assert.True(File.Exists(b));
        Assert.False(File.Exists(a));
    }

    [Fact]
    public void Prune_NeverDeletesFilesOutsideJournalDirectory_EvenIfHistoryIsTampered()
    {
        using var dir = new TempDir();
        var victim = dir.Combine("Documents", "important.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(victim)!);
        File.WriteAllText(victim, "keep me");
        var notJsonl = Journal(dir, "x.txt");

        var store = new HistoryStore(new RealFileSystem(), dir.Combine("history.json"));
        store.Save(new[]
        {
            Entry("evil1", victim, DateTime.UtcNow.AddDays(-90)),
            Entry("evil2", notJsonl, DateTime.UtcNow.AddDays(-90)),
            Entry("evil3", dir.Combine("journal", "..", "Documents", "important.jsonl"), DateTime.UtcNow.AddDays(-90))
        });

        store.PruneAndLimit(TimeSpan.FromDays(30), int.MaxValue);

        Assert.True(File.Exists(victim));
        Assert.True(File.Exists(notJsonl));
    }

    [Fact]
    public void Sweep_RemovesOldOrphanCompleteJournals_KeepsReferencedRecentAndIncomplete()
    {
        using var dir = new TempDir();
        var referenced = Journal(dir, "ref.jsonl");
        var orphanOld = Journal(dir, "orphan-old.jsonl");
        var orphanNew = Journal(dir, "orphan-new.jsonl");
        var incomplete = Journal(dir, "incomplete.jsonl", complete: false);
        var past = DateTime.UtcNow.AddDays(-90);
        File.SetLastWriteTimeUtc(referenced, past);
        File.SetLastWriteTimeUtc(orphanOld, past);
        File.SetLastWriteTimeUtc(incomplete, past);

        var store = new HistoryStore(new RealFileSystem(), dir.Combine("history.json")) { Retention = TimeSpan.FromDays(30) };
        store.Save(new[] { Entry("r", referenced, DateTime.UtcNow) });

        var removed = store.SweepOrphanJournals();

        Assert.Equal(1, removed);
        Assert.False(File.Exists(orphanOld));
        Assert.True(File.Exists(referenced));
        Assert.True(File.Exists(orphanNew));
        Assert.True(File.Exists(incomplete)); // dở dang: để khôi phục sự cố
    }

    [Fact]
    public void Retention_DefaultsToStaticValue_AndCanBeOverridden()
    {
        var original = HistoryStore.DefaultRetention;
        try
        {
            HistoryStore.DefaultRetention = TimeSpan.FromDays(7);
            var store = new HistoryStore(new FakeFileSystem(), "history.json");
            Assert.Equal(TimeSpan.FromDays(7), store.Retention);
            store.Retention = TimeSpan.FromDays(90);
            Assert.Equal(TimeSpan.FromDays(90), store.Retention);
        }
        finally { HistoryStore.DefaultRetention = original; }
    }
}

public class ActivityDataTests
{
    [Fact]
    public void EraseAll_RemovesActivityData_KeepsPresetsSettingsLicense()
    {
        using var app = new TempDir();
        using var tmp = new TempDir();
        var tempRoot = tmp.Combine("BatchFileRenamer");

        File.WriteAllText(app.Combine("history.json"), "[]");
        Directory.CreateDirectory(app.Combine("journal"));
        File.WriteAllText(app.Combine("journal", "a.jsonl"), "x");
        Directory.CreateDirectory(app.Combine("watch"));
        File.WriteAllText(app.Combine("watch", "watch.log"), "x");
        Directory.CreateDirectory(Path.Combine(tempRoot, "shell"));
        File.WriteAllText(Path.Combine(tempRoot, "shell", "shell-1.txt"), "x");
        var crash = tmp.Combine("bfr-crash.txt");
        File.WriteAllText(crash, "x");

        Directory.CreateDirectory(app.Combine("presets"));
        File.WriteAllText(app.Combine("presets", "p.bfrpreset"), "{}");
        File.WriteAllText(app.Combine("settings.json"), "{}");
        File.WriteAllText(app.Combine("license.json"), "{}");

        var result = ActivityData.EraseAll(app.Path, tempRoot, crash);

        Assert.Equal(5, result.FilesDeleted);
        Assert.Equal(0, result.Failed);
        Assert.False(File.Exists(app.Combine("history.json")));
        Assert.False(File.Exists(app.Combine("journal", "a.jsonl")));
        Assert.False(File.Exists(app.Combine("watch", "watch.log")));
        Assert.False(File.Exists(Path.Combine(tempRoot, "shell", "shell-1.txt")));
        Assert.False(File.Exists(crash));
        Assert.True(File.Exists(app.Combine("presets", "p.bfrpreset")));
        Assert.True(File.Exists(app.Combine("settings.json")));
        Assert.True(File.Exists(app.Combine("license.json")));
    }

    [Fact]
    public void EraseAll_RefusesTempRootThatIsNotBfrs()
    {
        using var app = new TempDir();
        using var other = new TempDir();
        File.WriteAllText(other.Combine("precious.txt"), "x");

        ActivityData.EraseAll(app.Path, other.Path);

        Assert.True(File.Exists(other.Combine("precious.txt")));
    }

    [Fact]
    public void TrimLog_DropsOldStampedLines_KeepsRecentAndContinuations()
    {
        using var dir = new TempDir();
        var log = dir.Combine("watch.log");
        var now = new DateTime(2026, 6, 1, 12, 0, 0);
        File.WriteAllLines(log, new[]
        {
            "[2026-01-01 10:00:00] INFO    old entry",
            "   continuation of old",
            "[2026-05-30 10:00:00] INFO    recent entry",
            "   continuation of recent"
        });

        var dropped = ActivityData.TrimLog(log, TimeSpan.FromDays(30), now);

        Assert.Equal(2, dropped);
        var left = File.ReadAllLines(log);
        Assert.Equal(2, left.Length);
        Assert.Contains("recent entry", left[0]);
    }

    [Fact]
    public void TrimLog_MissingFile_IsNoOp()
    {
        Assert.Equal(0, ActivityData.TrimLog(Path.Combine(Path.GetTempPath(), "nope-" + Guid.NewGuid().ToString("N") + ".log"), TimeSpan.FromDays(1)));
    }
}

public class LogRedactorTests
{
    [Theory]
    [InlineData(@"Could not find file 'C:\Users\Jane Doe\Documents\tax 2025.xlsx'.", "Could not find file '<path>'.")]
    [InlineData(@"Access to the path ""D:\Photos\Trip\a b.jpg"" is denied", @"Access to the path ""<path>"" is denied")]
    [InlineData(@"start C:\Users\x\a.txt D:\b.txt", "start <path> <path>")]
    [InlineData(@"open \\server\share\folder\file.docx now", "open <path> now")]
    [InlineData("nothing to hide", "nothing to hide")]
    [InlineData("", "")]
    public void Redact_Cases(string input, string expected)
    {
        Assert.Equal(expected, LogRedactor.Redact(input));
    }

    [Fact]
    public void Redact_Null_ReturnsEmpty() => Assert.Equal(string.Empty, LogRedactor.Redact(null));

    [Fact]
    public void Redact_UserNameAndFileNameNeverSurvive()
    {
        var text = @"System.IO.IOException: The process cannot access the file 'C:\Users\truong\Secret Plans\q3-layoffs.docx' because it is being used";
        var clean = LogRedactor.Redact(text);
        Assert.DoesNotContain("truong", clean);
        Assert.DoesNotContain("q3-layoffs", clean);
        Assert.DoesNotContain("Secret", clean);
    }
}
