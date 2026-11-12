namespace BFR.Presentation.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BFR.Core;
using BFR.Core.Licensing;
using BFR.Presentation.Tools;
using Xunit;

public sealed class ToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bfr-tools-" + Guid.NewGuid().ToString("N"));
    private readonly RealFileSystem _fs = new();

    public ToolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_root, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string P(params string[] parts) => Path.Combine(new[] { _root }.Concat(parts).ToArray());

    private string Write(string relative, string content = "x")
    {
        var path = P(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private LicenseService License(LicenseTier tier) =>
        new(new LicenseStore(_fs, P("license.json")), tierOverride: () => tier, trialEnabled: false);

    private ToolContext Ctx(IEnumerable<string> paths, Dictionary<string, string>? values = null, LicenseTier tier = LicenseTier.Pro) =>
        new(paths.ToList(), values ?? new Dictionary<string, string>(), _fs, License(tier), P("_tmp", Guid.NewGuid().ToString("N")));

    private static Dictionary<string, string> V(params (string, string)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    private ToolRunner Runner(out HistoryStore history)
    {
        history = new HistoryStore(_fs, P("history.json"));
        return new ToolRunner(_fs, P("_journal"), history, "test", P("_quarantine"));
    }

    [Fact]
    public void Catalog_HasConsistentOptionDescriptors()
    {
        var tools = ToolCatalog.Create();
        Assert.Equal(tools.Count, tools.Select(t => t.Key).Distinct().Count());

        foreach (var tool in tools)
        {
            Assert.NotEmpty(tool.TitleKey);
            Assert.NotEmpty(tool.DescriptionKey);
            Assert.Equal(tool.Options.Count, tool.Options.Select(o => o.Key).Distinct().Count());

            foreach (var o in tool.Options)
            {
                Assert.NotEmpty(o.LabelKey);
                if (o.Kind == OptionKind.Choice)
                {
                    Assert.NotNull(o.Choices);
                    Assert.Contains(o.Choices!, c => c.Value == o.Default);
                }

                if (o.VisibleWhen is not null)
                {
                    var key = o.VisibleWhen.Split('=')[0];
                    Assert.Contains(tool.Options, x => x.Key == key);
                }
            }
        }
    }

    [Fact]
    public async Task Organize_PlansAppliesAndUndoes_AndFreeDepthLimitIsExplained()
    {
        var a = Write("in/a.txt");
        var b = Write("in/b.png");
        var tool = new OrganizeTool();

        var plan = await tool.PlanAsync(Ctx(new[] { a, b }, V(("template", "{ext.upper}"))), default);
        Assert.False(plan.Blocked);
        Assert.Equal(2, plan.Operations.Count);
        Assert.Contains(plan.Rows, r => r.Secondary.Contains("TXT"));

        var runner = Runner(out var history);
        var result = runner.Execute(plan, "organize");
        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(P("in", "TXT", "a.txt")));
        Assert.Equal("organize", history.LoadAll().Last().Preset);

        runner.Undo(result.JournalPath);
        Assert.True(File.Exists(a));

        var deep = await tool.PlanAsync(Ctx(new[] { a }, V(("template", "custom"), ("customTemplate", "{year}\\{month}\\{day}")), LicenseTier.Free), default);
        Assert.True(deep.Blocked);
        Assert.Equal(ProFeature.OrganizeDeepTemplate, deep.UpgradeFeature);

        var exif = await tool.PlanAsync(Ctx(new[] { a }, V(("template", "{exif.year}")), LicenseTier.Free), default);
        Assert.True(exif.Blocked);
        Assert.Equal(ProFeature.ExifDate, exif.UpgradeFeature);
    }

    [Fact]
    public async Task Duplicates_GroupsAndLetsTheUserChangeTheKeeper()
    {
        Write("d/one.txt", "same");
        Write("d/two.txt", "same");
        Write("d/solo.txt", "different");
        var tool = new DuplicatesTool();

        var plan = await tool.PlanAsync(Ctx(new[] { P("d") }), default);

        Assert.Equal(1, plan.Rows.Count(r => r.State == ToolRowState.Header));
        Assert.Single(plan.Operations);
        var keeper = plan.Rows.Single(r => r.State == ToolRowState.Ok);
        var other = plan.Rows.Single(r => r.State == ToolRowState.Warning);

        var swapped = tool.OnRowActivated(other);
        Assert.NotNull(swapped);
        Assert.Equal(keeper.Primary, swapped!.Rows.Single(r => r.State == ToolRowState.Warning).Primary);
        Assert.Equal(other.Primary, swapped.Rows.Single(r => r.State == ToolRowState.Ok).Primary);

        // Chuyển sang thư mục khác mà chưa chọn thư mục: bị chặn, không có thao tác.
        var blocked = await tool.PlanAsync(Ctx(new[] { P("d") }, V(("action", "move"))), default);
        Assert.True(blocked.Blocked);
        Assert.Empty(blocked.Operations);
    }

    [Fact]
    public async Task Duplicates_FreeTierLimitIsNeverSilent()
    {
        for (var i = 0; i < 6; i++) Write($"many/f{i}.txt", "same");
        var limited = new LicenseService(new LicenseStore(_fs, P("lic.json")), tierOverride: () => LicenseTier.Free, trialEnabled: false);
        Assert.Equal(1000, limited.MaxDuplicateFiles); // kiểm tra giới hạn có thật; test dùng số tệp nhỏ nên chỉ xác nhận không bị cắt
        var plan = await new DuplicatesTool().PlanAsync(Ctx(new[] { P("many") }, tier: LicenseTier.Free), default);

        Assert.Null(plan.UpgradeFeature);
        Assert.Equal(5, plan.Operations.Count);
    }

    [Fact]
    public async Task Folders_FlattenEmptyWrapAndTree()
    {
        Write("t/a/x.txt");
        Write("t/a/b/y.txt");
        Directory.CreateDirectory(P("t", "empty"));
        var tool = new FolderTool();

        var flatten = await tool.PlanAsync(Ctx(new[] { P("t") }, V(("mode", "flatten"))), default);
        Assert.Equal(2, flatten.Operations.Count(o => o.Kind == OperationKind.Move));

        var empty = await tool.PlanAsync(Ctx(new[] { P("t") }, V(("mode", "empty"))), default);
        Assert.Contains(empty.Operations, o => o.Kind == OperationKind.RemoveDir && o.From.EndsWith("empty"));

        var tree = await tool.PlanAsync(Ctx(new[] { P("t") }, V(("mode", "tree"), ("treeText", "2024\n  Q1\n2025"))), default);
        Assert.Equal(3, tree.Operations.Count);

        var wrap = await tool.PlanAsync(Ctx(new[] { P("t", "a", "x.txt") }, V(("mode", "wrap"))), default);
        Assert.Single(wrap.Operations);

        var runner = Runner(out _);
        Assert.Equal(0, runner.Execute(flatten, "folders").Failed);
        Assert.True(File.Exists(P("t", "x.txt")));
    }

    [Fact]
    public async Task Metadata_TimestampValidationAndFreeGating()
    {
        var f = Write("m/a.txt");
        var tool = new MetadataTool();

        var badValue = await tool.PlanAsync(Ctx(new[] { f }, V(("mode", "timestamps"), ("source", "fixed"), ("fixedValue", "not a date"))), default);
        Assert.True(badValue.Blocked);

        var badOffset = await tool.PlanAsync(Ctx(new[] { f }, V(("mode", "timestamps"), ("source", "shift"), ("offset", "soon"))), default);
        Assert.True(badOffset.Blocked);

        var shift = await tool.PlanAsync(Ctx(new[] { f }, V(("mode", "timestamps"), ("source", "shift"), ("offset", "+2h"))), default);
        Assert.Single(shift.Operations);

        var exifFree = await tool.PlanAsync(Ctx(new[] { f }, V(("mode", "timestamps"), ("source", "exif")), LicenseTier.Free), default);
        Assert.Equal(ProFeature.ExifDate, exifFree.UpgradeFeature);

        // Xoá GPS luôn miễn phí: không bị chặn ở bản Free.
        var gps = await tool.PlanAsync(Ctx(new[] { f }, V(("mode", "gps")), LicenseTier.Free), default);
        Assert.False(gps.Blocked);
        Assert.Null(gps.UpgradeFeature);

        var attr = await tool.PlanAsync(Ctx(new[] { f }, V(("mode", "attributes"), ("readonly", "set"))), default);
        Assert.Single(attr.Operations);

        Assert.True(MetadataTool.TryParseOffset("-30m", out var t) && t == TimeSpan.FromMinutes(-30));
        Assert.False(MetadataTool.TryParseOffset("x", out _));
    }

    [Fact]
    public async Task Archive_CompressThenExtract_AndFreeAdvancedGating()
    {
        var a = Write("z/a.txt", "alpha");
        var b = Write("z/b.txt", "bravo");
        var tool = new ArchiveTool();

        var plan = await tool.PlanAsync(Ctx(new[] { a, b }, V(("name", "{name}"))), default);
        Assert.Single(plan.Operations);
        var runner = Runner(out _);
        Assert.Equal(0, runner.Execute(plan, "archive").Failed);
        Assert.True(File.Exists(P("z", "Archive.zip")));

        var extract = await tool.PlanAsync(Ctx(new[] { P("z", "Archive.zip") }, V(("mode", "extract"))), default);
        Assert.Contains(extract.Operations, o => o.Kind == OperationKind.Move);
        Assert.Equal(0, runner.Execute(extract, "extract").Failed);
        Assert.Equal("alpha", File.ReadAllText(P("z", "Archive", "a.txt")));

        var gated = await tool.PlanAsync(Ctx(new[] { a }, V(("group", "folder")), LicenseTier.Free), default);
        Assert.Equal(ProFeature.ArchiveAdvanced, gated.UpgradeFeature);

        var none = await tool.PlanAsync(Ctx(new[] { a }, V(("mode", "extract"))), default);
        Assert.Empty(none.Operations);
    }

    [Fact]
    public async Task Checksum_ComputeThenVerify()
    {
        var a = Write("h/a.txt", "abc");
        var tool = new ChecksumTool();

        var compute = await tool.PlanAsync(Ctx(new[] { a }), default);
        Assert.Contains("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", compute.Rows[0].Secondary);
        Assert.NotNull(compute.Export);
        Assert.Empty(compute.Operations);

        var sums = P("h", "sums.sha256");
        File.WriteAllText(sums, compute.Export!.Content);
        var ok = await tool.PlanAsync(Ctx(Array.Empty<string>(), V(("mode", "verify"), ("checkFile", sums))), default);
        Assert.All(ok.Rows, r => Assert.Equal(ToolRowState.Ok, r.State));

        File.WriteAllText(a, "changed");
        var bad = await tool.PlanAsync(Ctx(Array.Empty<string>(), V(("mode", "verify"), ("checkFile", sums))), default);
        Assert.Contains(bad.Rows, r => r.State == ToolRowState.Error);

        Assert.True((await tool.PlanAsync(Ctx(Array.Empty<string>(), V(("mode", "verify"))), default)).Blocked);
    }

    [Fact]
    public async Task Analyze_ProducesRowsAndAProExport()
    {
        Write("an/a.txt", new string('a', 100));
        Write("an/sub/b.bin", new string('b', 500));

        var plan = await new AnalyzeTool().PlanAsync(Ctx(new[] { P("an") }), default);

        Assert.Contains(plan.Rows, r => r.State == ToolRowState.Header);
        Assert.Contains(plan.Rows, r => r.Primary.EndsWith("b.bin"));
        Assert.Equal(ProFeature.AnalyzeExport, plan.Export!.RequiresFeature);
        Assert.Empty(plan.Operations);
        Assert.Null(plan.Blocked ? "blocked" : null);
    }

    [Fact]
    public async Task Workflow_IsProOnly_AndRunsTemplates()
    {
        var f = Write("w/Photo.JPG");
        var tool = new WorkflowTool();

        var free = await tool.PlanAsync(Ctx(new[] { f }, tier: LicenseTier.Free), default);
        Assert.Equal(ProFeature.Workflow, free.UpgradeFeature);
        Assert.True(free.Blocked);

        var tidy = await tool.PlanAsync(Ctx(new[] { P("w") }, V(("template", "tidy"), ("recursive", "true"))), default);
        Assert.False(tidy.Blocked, string.Join(";", tidy.Messages));

        var photos = await tool.PlanAsync(Ctx(new[] { f }, V(("template", "photos"))), default);
        Assert.False(photos.Blocked, string.Join(";", photos.Messages));
        Assert.True(photos.Operations.Count >= 2);

        var missingFile = await tool.PlanAsync(Ctx(new[] { f }, V(("template", "file"))), default);
        Assert.True(missingFile.Blocked);
    }

    [Fact]
    public async Task Runner_RefusesUnapplicablePlans_AndCleansTemporaryFiles()
    {
        var runner = Runner(out _);
        Assert.Throws<InvalidOperationException>(() => runner.Execute(ToolPlan.Empty("x"), "x"));

        var cleaned = false;
        var a = Write("r/a.txt");
        var plan = await new OrganizeTool().PlanAsync(Ctx(new[] { a }, V(("template", "{ext}"))), default);
        var withCleanup = new ToolPlan { Rows = plan.Rows, Operations = plan.Operations, Cleanup = () => cleaned = true };
        runner.Execute(withCleanup, "organize");
        Assert.True(cleaned);
    }

    [Fact]
    public async Task Cancellation_IsHonoured()
    {
        for (var i = 0; i < 3; i++) Write($"c/f{i}.txt");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new OrganizeTool().PlanAsync(Ctx(new[] { P("c") }), cts.Token));
    }
}
