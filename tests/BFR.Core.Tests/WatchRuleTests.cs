namespace BFR.Core.Tests;

using BFR.Core;
using BFR.Core.WatchFolder;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

public sealed class WatchRuleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bfr-watch-" + Guid.NewGuid().ToString("N"));
    private readonly RealFileSystem _fs = new();

    public WatchRuleTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Dir(string name)
    {
        var p = Path.Combine(_root, name);
        Directory.CreateDirectory(p);
        return p;
    }

    private static Preset Lower() => BuiltInPresets.All.First(p => p.Name == "Chữ thường toàn bộ");

    private WatchRule Rule(string folder, params WorkflowStep[] steps) => new()
    {
        Name = Path.GetFileName(folder),
        Folder = folder,
        SettleDelayMs = 500,
        Steps = steps.ToList()
    };

    private WorkflowWatchProcessor Processor() => new(_fs, Path.Combine(_root, "_journal"));

    [Fact]
    public void Processor_RunsWholeWorkflowAsOneJournal_AndUndoRestores()
    {
        var folder = Dir("in");
        File.WriteAllText(Path.Combine(folder, "Photo.JPG"), "a");
        File.WriteAllText(Path.Combine(folder, "NOTES.TXT"), "b");
        var rule = Rule(folder, new RenameStep(Lower()), new OrganizeStep("{ext.upper}"));

        var result = Processor().Process(new[] { Path.Combine(folder, "Photo.JPG"), Path.Combine(folder, "NOTES.TXT") }, rule);

        Assert.True(result.Succeeded, string.Join("; ", result.Errors));
        Assert.True(File.Exists(Path.Combine(folder, "JPG", "photo.jpg")));
        Assert.True(File.Exists(Path.Combine(folder, "TXT", "notes.txt")));
        Assert.Equal(2, result.RenamedPaths.Count);
        Assert.NotNull(result.JournalPath);

        new UndoService(_fs).UndoJournal(result.JournalPath!);
        Assert.True(File.Exists(Path.Combine(folder, "Photo.JPG")));
        Assert.True(File.Exists(Path.Combine(folder, "NOTES.TXT")));
        Assert.False(Directory.Exists(Path.Combine(folder, "JPG")));
    }

    [Fact]
    public void Processor_DryRunAndFailingWorkflow_ChangeNothing()
    {
        var folder = Dir("dry");
        var file = Path.Combine(folder, "A.TXT");
        File.WriteAllText(file, "x");

        var dry = Rule(folder, new OrganizeStep("{ext}"));
        dry.DryRun = true;
        Assert.Equal(0, Processor().Process(new[] { file }, dry).Renamed);
        Assert.True(File.Exists(file));

        var bad = Rule(folder, new OrganizeStep("{nope}"));
        var result = Processor().Process(new[] { file }, bad);
        Assert.Equal(1, result.Skipped);
        Assert.NotEmpty(result.Errors);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Validate_RejectsUnsafeOrBrokenRules()
    {
        var ok = Rule(_root, new WrapStep());
        Assert.Empty(ok.Validate());

        Assert.NotEmpty(Rule("relative\\path", new WrapStep()).Validate());
        Assert.NotEmpty(Rule(Environment.GetFolderPath(Environment.SpecialFolder.Windows), new WrapStep()).Validate());
        Assert.NotEmpty(Rule(_root).Validate()); // không có bước nào

        var fast = Rule(_root, new WrapStep());
        fast.SettleDelayMs = 10;
        Assert.NotEmpty(fast.Validate());
    }

    [Fact]
    public void Manager_RejectsOverlappingFolders_AndTooManyRules()
    {
        var a = Rule(Dir("a"), new WrapStep());
        var inner = Rule(Dir("a\\inner"), new WrapStep());
        var b = Rule(Dir("b"), new WrapStep());

        Assert.Empty(WatchFolderManager.Validate(new[] { a, b }));
        Assert.Contains(WatchFolderManager.Validate(new[] { a, inner }), e => e.Contains("a") && e.Length > 0);
        Assert.NotEmpty(WatchFolderManager.Validate(new[] { a, Rule(a.Folder, new WrapStep()) }));

        a.Recursive = false; // không đệ quy: thư mục con không bị theo dõi hai lần
        Assert.Empty(WatchFolderManager.Validate(new[] { a, inner }));

        var many = Enumerable.Range(0, WatchRule.MaxRules + 1).Select(i => Rule(Dir("m" + i), new WrapStep())).ToList();
        Assert.NotEmpty(WatchFolderManager.Validate(many));

        inner.Enabled = false;
        a.Recursive = true;
        Assert.Empty(WatchFolderManager.Validate(new[] { a, inner })); // quy tắc tắt không tính
    }

    [Fact]
    public void Store_RoundTripsRules_AndDropsInvalidOnes()
    {
        var store = new WatchRuleStore(_fs, Path.Combine(_root, "rules.json"));
        var good = Rule(Dir("g"), new RenameStep(Lower()), new OrganizeStep("{year}"));
        var bad = Rule("not-absolute", new WrapStep());

        store.Save(new[] { good, bad });
        var loaded = store.Load();

        var only = Assert.Single(loaded);
        Assert.Equal(good.Folder, only.Folder);
        Assert.Equal(2, only.Steps.Count);
        Assert.IsType<OrganizeStep>(only.Steps[1]);

        File.WriteAllText(Path.Combine(_root, "rules.json"), "{ garbage");
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Manager_WatchesTwoFolders_EndToEnd_WithoutReprocessingItsOwnOutput()
    {
        var inA = Dir("watchA");
        var inB = Dir("watchB");
        var rules = new List<WatchRule>
        {
            Rule(inA, new RenameStep(Lower())),
            Rule(inB, new OrganizeStep("{ext}"))
        };

        var processed = new List<WatchFolderResult>();
        using var manager = new WatchFolderManager(_fs, Processor());
        manager.BatchProcessed += (_, e) => { lock (processed) processed.Add(e.Result); };
        manager.Start(rules);
        Assert.Equal(2, manager.RunningCount);

        File.WriteAllText(Path.Combine(inA, "NEW.TXT"), "a");
        File.WriteAllText(Path.Combine(inB, "pic.png"), "b");

        Assert.True(WaitFor(() => File.Exists(Path.Combine(inA, "new.txt")) && File.Exists(Path.Combine(inB, "png", "pic.png"))),
            "Watch Folder không xử lý tệp mới trong thời gian chờ");

        // Chờ thêm vài chu kỳ: kết quả của chính ta (new.txt, png\pic.png) không được xử lý lại.
        Thread.Sleep(2500);
        lock (processed)
        {
            Assert.Equal(2, processed.Count);
            Assert.All(processed, r => Assert.True(r.Succeeded));
        }
        Assert.False(Directory.Exists(Path.Combine(inB, "png", "png")));
    }

    private static bool WaitFor(Func<bool> condition, int timeoutMs = 15000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < until)
        {
            if (condition()) return true;
            Thread.Sleep(100);
        }
        return condition();
    }
}
