namespace BFR.Presentation.Tests;

using BFR.Core;
using BFR.Core.Licensing;
using BFR.Presentation;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Xunit;

/// <summary>Test ViewModel cho B2 (đổi tên theo danh sách, giới hạn Free/Pro) và B3 (tự xử lý trùng tên).</summary>
public class ListAndConflictViewModelTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "bfr-v14-" + Guid.NewGuid().ToString("N"));
    private readonly string _files;

    public ListAndConflictViewModelTests()
    {
        _files = Path.Combine(_temp, "files");
        Directory.CreateDirectory(_files);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_temp)) Directory.Delete(_temp, recursive: true); } catch (IOException) { }
    }

    private MainViewModel CreateViewModel(LicenseTier tier = LicenseTier.Free)
    {
        var fs = new RealFileSystem();
        var license = new LicenseService(new LicenseStore(fs, Path.Combine(_temp, "license.json")), trialEnabled: false);
        if (tier == LicenseTier.Pro) license.RecordPurchase();
        return new MainViewModel(new LicenseCoordinator(license), null, _temp);
    }

    private void Touch(params string[] names)
    {
        foreach (var n in names) File.WriteAllText(Path.Combine(_files, n), "x");
    }

    private static void SetParam(MainViewModel vm, string ruleIndexKey, string value)
    {
        var card = vm.Rules[0];
        card.Parameters.First(p => p.Key == ruleIndexKey).TextValue = value;
    }

    private static string[] NewNames(MainViewModel vm) =>
        vm.Rows.OrderBy(r => r.OldName, StringComparer.Ordinal).Select(r => r.NewName).ToArray();

    // ------------------------------------------------------------------ thẻ quy tắc

    [Fact]
    public void RuleTypes_IncludeTheNewListRules()
    {
        Assert.Contains("ListMap", RuleCardViewModel.AllRuleTypes);
        Assert.Contains("ReplaceDict", RuleCardViewModel.AllRuleTypes);
    }

    [Fact]
    public void AddRule_ListMap_StartsWithMatchingDefaults()
    {
        var vm = CreateViewModel();

        Assert.True(vm.AddRule("ListMap"));

        var card = vm.Rules.Single();
        Assert.Equal("Map", card.Parameters.First(p => p.Key == "mode").ChoiceValue);
        Assert.Equal("Name", card.Parameters.First(p => p.Key == "matchBy").ChoiceValue);
        Assert.True(card.Parameters.First(p => p.Key == "ignoreCase").BoolValue);
    }

    [Fact]
    public void ListMapCard_ExposesAMultilineMappingParameter_ThatCanImportFiles()
    {
        var vm = CreateViewModel();
        vm.AddRule("ListMap");

        var mapping = vm.Rules.Single().Parameters.First(p => p.Key == "mapping");

        Assert.Equal(RuleParameterKind.Multiline, mapping.Kind);
        Assert.True(mapping.CanImportFile);
        Assert.All(vm.Rules.Single().Parameters.Where(p => p.Key != "mapping"), p => Assert.False(p.CanImportFile));
    }

    [Fact]
    public void ReplaceDictCard_HasPairsParameter()
    {
        var vm = CreateViewModel();
        vm.AddRule("ReplaceDict");

        var pairs = vm.Rules.Single().Parameters.First(p => p.Key == "pairs");

        Assert.Equal(RuleParameterKind.Multiline, pairs.Kind);
        Assert.True(pairs.CanImportFile);
    }

    [Fact]
    public void Summary_ShowsTheNumberOfRows()
    {
        var vm = CreateViewModel();
        vm.AddRule("ListMap");
        SetParam(vm, "mapping", "a\t1\nb\t2\nc\t3");

        Assert.Contains("3", vm.Rules.Single().Summary);
        Assert.StartsWith("RuleSummaryListMap", vm.Rules.Single().Summary);
    }

    // ------------------------------------------------------------------ đổi tên theo danh sách

    [Fact]
    public async Task ListMap_RenamesFilesFromThePastedList()
    {
        Touch("IMG_001.jpg", "IMG_002.jpg", "other.txt");
        var vm = CreateViewModel();
        vm.AddPaths(new[] { _files });
        vm.AddRule("ListMap");
        SetParam(vm, "mapping", "IMG_001\tBeach\nIMG_002\tForest");

        await vm.RebuildAsync();

        Assert.Equal(new[] { "Beach.jpg", "Forest.jpg", "other.txt" }.OrderBy(n => n, StringComparer.Ordinal),
            vm.Rows.Select(r => r.NewName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(2, vm.WillChangeCount);
    }

    // ------------------------------------------------------------------ hạn mức Free

    private static string BigList(int rows) =>
        string.Join("\n", Enumerable.Range(1, rows).Select(i => $"f{i:000}\tn{i:000}"));

    [Fact]
    public async Task Free_ListOver50Rows_RaisesTheUpgradePromptOnce_AndIgnoresTheExtraRows()
    {
        Touch("f050.txt", "f051.txt");
        var vm = CreateViewModel(LicenseTier.Free);
        var raised = new List<ProFeature>();
        vm.ProLimitReached += (_, f) => raised.Add(f);
        vm.AddPaths(new[] { _files });
        vm.AddRule("ListMap");
        SetParam(vm, "mapping", BigList(60));

        await vm.RebuildAsync();
        await vm.RebuildAsync();   // lượt tính lại thứ hai không được báo lại

        Assert.Equal(new[] { ProFeature.UnlimitedListRows }, raised);
        var byOld = vm.Rows.ToDictionary(r => r.OldName, r => r.NewName);
        Assert.Equal("n050.txt", byOld["f050.txt"]);
        Assert.Equal("f051.txt", byOld["f051.txt"]);   // dòng 51 bị bỏ qua ở bản Free
    }

    [Fact]
    public async Task Free_ListWithin50Rows_DoesNotPrompt()
    {
        Touch("f001.txt");
        var vm = CreateViewModel(LicenseTier.Free);
        var raised = new List<ProFeature>();
        vm.ProLimitReached += (_, f) => raised.Add(f);
        vm.AddPaths(new[] { _files });
        vm.AddRule("ListMap");
        SetParam(vm, "mapping", BigList(50));

        await vm.RebuildAsync();

        Assert.Empty(raised);
    }

    [Fact]
    public async Task Pro_ListOver50Rows_AppliesEveryRow_WithoutPrompt()
    {
        Touch("f051.txt");
        var vm = CreateViewModel(LicenseTier.Pro);
        var raised = new List<ProFeature>();
        vm.ProLimitReached += (_, f) => raised.Add(f);
        vm.AddPaths(new[] { _files });
        vm.AddRule("ListMap");
        SetParam(vm, "mapping", BigList(60));

        await vm.RebuildAsync();

        Assert.Empty(raised);
        Assert.Equal("n051.txt", vm.Rows.Single().NewName);
    }

    [Theory]
    [InlineData(LicenseTier.Free, false)]
    [InlineData(LicenseTier.Pro, true)]
    public void CsvImport_IsProOnly_AndPromptsFreeUsers(LicenseTier tier, bool allowed)
    {
        var vm = CreateViewModel(tier);
        var raised = new List<ProFeature>();
        vm.ProLimitReached += (_, f) => raised.Add(f);

        var result = vm.CanImportListFile();

        Assert.Equal(allowed, result);
        Assert.Equal(allowed ? 0 : 1, raised.Count);
        if (!allowed) Assert.Equal(ProFeature.ListCsvImport, raised[0]);
    }

    [Fact]
    public void UpgradeDialog_KnowsTheListFeatures()
    {
        Assert.Equal("FeatureUnlimitedListRows", UpgradeViewModel.FeatureResourceKey(ProFeature.UnlimitedListRows));
        Assert.Equal("FeatureListCsvImport", UpgradeViewModel.FeatureResourceKey(ProFeature.ListCsvImport));
    }

    // ------------------------------------------------------------------ tự xử lý trùng tên

    private async Task<MainViewModel> DuplicateSetup()
    {
        Touch("a.txt", "b.txt");
        var vm = CreateViewModel();
        vm.AddPaths(new[] { _files });
        vm.AddRule("ReplaceDict");
        SetParam(vm, "pairs", "a\tsame\nb\tsame");
        await vm.RebuildAsync();
        return vm;
    }

    [Fact]
    public async Task DuplicateNames_AreConflicts_AndBlockApply_ByDefault()
    {
        var vm = await DuplicateSetup();

        Assert.True(vm.HasConflicts);
        Assert.Equal(2, vm.ConflictCount);
        Assert.False(vm.CanApply);
        Assert.False(vm.AutoResolveConflicts);
    }

    [Fact]
    public async Task ResolveConflictsCommand_AddsSuffixes_AndUnblocksApply()
    {
        var vm = await DuplicateSetup();

        vm.ResolveConflictsCommand.Execute(null);
        await vm.RebuildAsync();

        Assert.True(vm.AutoResolveConflicts);
        Assert.False(vm.HasConflicts);
        Assert.Equal(new[] { "same.txt", "same (2).txt" }, NewNames(vm));
        Assert.Equal(1, vm.AutoResolvedCount);
        Assert.True(vm.HasAutoResolved);
        Assert.True(vm.CanApply);
    }

    [Fact]
    public async Task StopAutoResolve_RestoresTheConflict()
    {
        var vm = await DuplicateSetup();
        vm.ResolveConflictsCommand.Execute(null);
        await vm.RebuildAsync();

        vm.StopAutoResolveCommand.Execute(null);
        await vm.RebuildAsync();

        Assert.False(vm.AutoResolveConflicts);
        Assert.True(vm.HasConflicts);
        Assert.False(vm.HasAutoResolved);
    }

    [Fact]
    public async Task ApplyWithAutoResolve_RenamesOnDisk_WithoutOverwriting()
    {
        var vm = await DuplicateSetup();
        vm.ResolveConflictsCommand.Execute(null);
        await vm.RebuildAsync();

        await vm.ApplyAsync();

        var onDisk = Directory.GetFiles(_files).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "same (2).txt", "same.txt" }, onDisk);
    }

    // ------------------------------------------------------------------ va chạm với tệp NGOÀI lô (lỗi có sẵn đã sửa)

    private async Task<MainViewModel> OutsideCollisionSetup()
    {
        Touch("a.txt", "001.txt");                       // 001.txt đã có sẵn và không thuộc lô
        var vm = CreateViewModel();
        vm.AddPaths(new[] { Path.Combine(_files, "a.txt") });   // lô chỉ gồm a.txt
        vm.AddRule("ReplaceDict");
        SetParam(vm, "pairs", "a\t001");                // a.txt -> 001.txt
        await vm.RebuildAsync();
        return vm;
    }

    [Fact]
    public async Task CollisionWithAFileOutsideTheBatch_IsDetected_AndBlocksApply()
    {
        var vm = await OutsideCollisionSetup();

        var row = Assert.Single(vm.Rows);
        Assert.Equal("001.txt", row.NewName);
        Assert.Equal(PreviewStatus.Conflict, row.Status);
        Assert.True(vm.HasConflicts);
        Assert.False(vm.CanApply);   // trước khi sửa: không bao giờ phát hiện, đến lúc đổi tên mới lỗi
    }

    [Fact]
    public async Task AutoResolve_AvoidsTheFileOutsideTheBatch()
    {
        var vm = await OutsideCollisionSetup();

        vm.ResolveConflictsCommand.Execute(null);
        await vm.RebuildAsync();

        Assert.Equal("001 (2).txt", Assert.Single(vm.Rows).NewName);
        Assert.False(vm.HasConflicts);
        Assert.True(vm.CanApply);

        await vm.ApplyAsync();

        var onDisk = Directory.GetFiles(_files).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "001 (2).txt", "001.txt" }, onDisk);   // 001.txt có sẵn không bị đè
    }

    [Fact]
    public void NewlyAddedRule_IsExpandedSoItsParametersCanBeFilledIn()
    {
        var vm = CreateViewModel();
        vm.AddRule("ListMap");
        Assert.True(vm.Rules.Single().IsExpanded);
    }

    [Fact]
    public async Task EditingAParameterOfAFreshlyAddedRule_RefreshesThePreview()
    {
        Touch("a.txt");
        var vm = CreateViewModel();
        vm.AddPaths(new[] { Path.Combine(_files, "a.txt") });
        vm.AddRule("ReplaceDict");
        await vm.RebuildAsync();
        Assert.Equal("a.txt", Assert.Single(vm.Rows).NewName);

        SetParam(vm, "pairs", "a\tZ1");   // chỉ sửa tham số, không gọi RebuildAsync

        for (var i = 0; i < 40 && vm.Rows.Single().NewName != "Z1.txt"; i++) await Task.Delay(50);
        Assert.Equal("Z1.txt", vm.Rows.Single().NewName);
    }
}
