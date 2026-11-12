namespace BFR.Core.Tests;

using BFR.Core;
using BFR.Core.Licensing;
using BFR.Core.Reports;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

/// <summary>
/// Test Giai đoạn 3 (v1.1 — bật Pro): phân tách Free/Pro, dùng thử, IAP,
/// EXIF/media, bảo vệ tệp OneDrive, undo nhiều bước và xuất báo cáo.
/// </summary>
public class Phase3Tests
{
    // ---------------------------------------------------------------------
    // Giấy phép và giới hạn Free/Pro
    // ---------------------------------------------------------------------

    [Fact]
    public void License_DefaultIsFree_WithSpecLimits()
    {
        var fs = new FakeFileSystem();
        var service = new LicenseService(new LicenseStore(fs, "license.json"), trialEnabled: false);

        Assert.Equal(LicenseTier.Free, service.Status.Tier);
        Assert.Equal(3, service.MaxRules);
        Assert.Equal(2, service.MaxRecursionDepth);
        Assert.Equal(3, service.MaxSavedPresets);
        Assert.Equal(1, service.MaxUndoEntries);

        Assert.False(service.Allows(ProFeature.ExifDate));
        Assert.False(service.Allows(ProFeature.MediaDate));
        Assert.False(service.Allows(ProFeature.ReportExport));
        Assert.False(service.Allows(ProFeature.CommandLine));
        Assert.False(service.Allows(ProFeature.UnlimitedRecursion));
    }

    [Fact]
    public void License_AfterPurchase_UnlocksEveryProFeature()
    {
        var fs = new FakeFileSystem();
        var service = new LicenseService(new LicenseStore(fs, "license.json"), trialEnabled: false);

        service.RecordPurchase();

        Assert.Equal(LicenseTier.Pro, service.Status.Tier);
        Assert.Equal("store", service.Status.Source);
        Assert.Equal(int.MaxValue, service.MaxRules);
        Assert.Equal(int.MaxValue, service.MaxRecursionDepth);
        Assert.Equal(int.MaxValue, service.MaxSavedPresets);
        Assert.Equal(int.MaxValue, service.MaxUndoEntries);

        foreach (ProFeature feature in Enum.GetValues<ProFeature>())
        {
            Assert.True(service.Allows(feature), feature.ToString());
        }
    }

    [Fact]
    public void License_Trial_Lasts14Days_AndCannotBeRestarted()
    {
        var fs = new FakeFileSystem();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = new LicenseService(
            new LicenseStore(fs, "license.json"),
            clock: () => now,
            trialEnabled: true);

        Assert.True(service.StartTrial());
        Assert.Equal(LicenseTier.Pro, service.Status.Tier);
        Assert.True(service.Status.IsTrial);
        Assert.Equal(now + TimeSpan.FromDays(30), service.Status.TrialEndsUtc);

        // Hết hạn -> về Free.
        now = now.AddDays(31);
        service.Evaluate();
        Assert.Equal(LicenseTier.Free, service.Status.Tier);

        // Không dùng lại được dùng thử.
        Assert.False(service.StartTrial());
        Assert.Equal(LicenseTier.Free, service.Status.Tier);
    }

    [Fact]
    public void License_TrialRemaining_ShrinksOverTime()
    {
        var fs = new FakeFileSystem();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = new LicenseService(new LicenseStore(fs, "license.json"), clock: () => now, trialEnabled: true);

        service.StartTrial();
        now = now.AddDays(3);
        service.Evaluate();

        var remaining = service.TrialRemaining();
        Assert.NotNull(remaining);
        Assert.Equal(27, remaining!.Value.TotalDays);
    }

    [Fact]
    public void License_PurchasePersists_AcrossServiceInstances()
    {
        var fs = new FakeFileSystem();
        var store = new LicenseStore(fs, "license.json");

        new LicenseService(store, trialEnabled: false).RecordPurchase();

        var reopened = new LicenseService(new LicenseStore(fs, "license.json"), trialEnabled: false);
        Assert.Equal(LicenseTier.Pro, reopened.Status.Tier);
    }

    [Fact]
    public void License_Checks_FlagLimitViolations()
    {
        var fs = new FakeFileSystem();
        var free = new LicenseService(new LicenseStore(fs, "license.json"), trialEnabled: false);

        Assert.True(free.CheckRuleCount(3).Allowed);
        Assert.False(free.CheckRuleCount(4).Allowed);
        Assert.True(free.CheckRecursionDepth(2).Allowed);
        Assert.False(free.CheckRecursionDepth(3).Allowed);
        Assert.True(free.CheckPresetCount(3).Allowed);
        Assert.False(free.CheckPresetCount(4).Allowed);

        var pro = new LicenseService(new LicenseStore(fs, "pro.json"), trialEnabled: false);
        pro.RecordPurchase();
        Assert.True(pro.CheckRuleCount(50).Allowed);
        Assert.True(pro.CheckRecursionDepth(99).Allowed);
        Assert.True(pro.CheckPresetCount(50).Allowed);
    }

    [Fact]
    public void License_DevTierOverride_WorksOnlyWhenNotPackaged()
    {
        var envName = "BFR_DEV_TIER";
        var previous = Environment.GetEnvironmentVariable(envName);
        try
        {
            Environment.SetEnvironmentVariable(envName, "pro");
            Assert.NotNull(LicenseService.ResolveDevTierOverride(packaged: false));
            Assert.Null(LicenseService.ResolveDevTierOverride(packaged: true));
        }
        finally
        {
            Environment.SetEnvironmentVariable(envName, previous);
        }
    }

    // ---------------------------------------------------------------------
    // IAP (Microsoft Store) — kiểm thử bằng nhà cung cấp giả
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Iap_StoreReportsPurchase_GrantsPro()
    {
        var fs = new FakeFileSystem();
        var service = new LicenseService(new LicenseStore(fs, "license.json"), trialEnabled: false);
        var coordinator = new LicenseCoordinator(service, new FakeStoreProvider
        {
            Snapshot = new StoreLicenseSnapshot { IsProActive = true }
        });

        var status = await coordinator.RefreshAsync();

        Assert.Equal(LicenseTier.Pro, status.Tier);
        Assert.Equal("store", status.Source);
    }

    [Fact]
    public async Task Iap_PurchaseCompleted_RecordsEntitlement()
    {
        var fs = new FakeFileSystem();
        var service = new LicenseService(new LicenseStore(fs, "license.json"), trialEnabled: false);
        var provider = new FakeStoreProvider { PurchaseResult = new PurchaseOutcome(PurchaseStatus.Completed) };
        var coordinator = new LicenseCoordinator(service, provider);

        var outcome = await coordinator.PurchaseProAsync();

        Assert.True(outcome.IsSuccess);
        Assert.Equal(LicenseTier.Pro, service.Status.Tier);
        Assert.Equal(LicenseService.ProProductId, service.Status.ProductId);
    }

    [Fact]
    public async Task Iap_PurchaseCancelled_LeavesTierUnchanged()
    {
        var fs = new FakeFileSystem();
        var service = new LicenseService(new LicenseStore(fs, "license.json"), trialEnabled: false);
        var provider = new FakeStoreProvider { PurchaseResult = new PurchaseOutcome(PurchaseStatus.UserCancelled) };
        var coordinator = new LicenseCoordinator(service, provider);

        var outcome = await coordinator.PurchaseProAsync();

        Assert.Equal(PurchaseStatus.UserCancelled, outcome.Status);
        Assert.Equal(LicenseTier.Free, service.Status.Tier);
    }

    [Fact]
    public async Task Iap_StoreUnavailable_ReportsNotSupported()
    {
        var fs = new FakeFileSystem();
        var service = new LicenseService(new LicenseStore(fs, "license.json"), trialEnabled: false);
        var coordinator = new LicenseCoordinator(service, new FakeStoreProvider { Available = false });

        var outcome = await coordinator.PurchaseProAsync();

        Assert.Equal(PurchaseStatus.NotSupported, outcome.Status);
        Assert.Equal(LicenseTier.Free, service.Status.Tier);
    }

    private sealed class FakeStoreProvider : IStoreLicenseProvider
    {
        public bool Available { get; set; } = true;
        public StoreLicenseSnapshot Snapshot { get; set; } = new();
        public PurchaseOutcome PurchaseResult { get; set; } = new(PurchaseStatus.NotPurchased);

        public bool IsAvailable => Available;

        public Task<StoreLicenseSnapshot> GetLicenseAsync(CancellationToken ct = default) =>
            Task.FromResult(Snapshot);

        public Task<PurchaseOutcome> PurchaseAsync(string productId, CancellationToken ct = default) =>
            Task.FromResult(PurchaseResult);
    }

    // ---------------------------------------------------------------------
    // EXIF / media (QA-09) và tệp OneDrive (QA-14)
    // ---------------------------------------------------------------------

    [Fact]
    public void DateRule_Taken_UsesExif_WhenAvailable()
    {
        var file = MakeFile("IMG_1.jpg");
        var ctx = new RuleContext(new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc), "seed",
            FileMetadata: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [file.FullPath] = new Dictionary<string, string>
                {
                    ["taken"] = new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Local).ToString("O")
                }
            });

        var rule = new DateRule { Source = DateRule.DateSource.Taken, Format = "yyyy-MM-dd", Fallback = new List<DateRule.DateSource>() };
        var result = rule.Apply(file, new RenameState(file.Name, file.Extension, 0, 0, new List<string>()), ctx);

        Assert.StartsWith("2024-05-01", result.Name);
    }

    [Fact]
    public void DateRule_Taken_FallsBackToCreated_WhenNoExif()
    {
        var file = MakeFile("IMG_2.jpg", created: new DateTime(2023, 3, 3, 8, 0, 0, DateTimeKind.Utc));
        var ctx = new RuleContext(new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc), "seed");

        var rule = new DateRule
        {
            Source = DateRule.DateSource.Taken,
            Format = "yyyy-MM-dd",
            Fallback = new List<DateRule.DateSource> { DateRule.DateSource.Created }
        };
        var result = rule.Apply(file, new RenameState(file.Name, file.Extension, 0, 0, new List<string>()), ctx);

        Assert.StartsWith("2023-03-03", result.Name);
        Assert.Contains(result.Trace, t => t.Contains("dự phòng"));
    }

    [Fact]
    public void DateRule_Media_UsesMediaMetadata_WhenAvailable()
    {
        var file = MakeFile("clip.mp4");
        var ctx = new RuleContext(new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc), "seed",
            FileMetadata: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [file.FullPath] = new Dictionary<string, string>
                {
                    ["media"] = new DateTime(2022, 9, 9, 12, 0, 0, DateTimeKind.Local).ToString("O")
                }
            });

        var rule = new DateRule { Source = DateRule.DateSource.Media, Format = "yyyy-MM-dd", Fallback = new List<DateRule.DateSource>() };
        var result = rule.Apply(file, new RenameState(file.Name, file.Extension, 0, 0, new List<string>()), ctx);

        Assert.StartsWith("2022-09-09", result.Name);
    }

    [Fact]
    public void DateRule_Media_FallsBack_WhenMissing()
    {
        var file = MakeFile("clip2.mp4", created: new DateTime(2021, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        var ctx = new RuleContext(new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc), "seed");

        var rule = new DateRule
        {
            Source = DateRule.DateSource.Media,
            Format = "yyyy-MM-dd",
            Fallback = new List<DateRule.DateSource> { DateRule.DateSource.Created }
        };
        var result = rule.Apply(file, new RenameState(file.Name, file.Extension, 0, 0, new List<string>()), ctx);

        Assert.StartsWith("2021-01-02", result.Name);
    }

    [Fact]
    public void Metadata_OfflineFiles_AreNotRead()
    {
        var offline = MakeFile("cloud.jpg") with { IsOffline = true };

        var exif = new ExifMetadataReader().Read(offline);
        var media = new MediaMetadataReader().Read(offline);

        Assert.Empty(exif);
        Assert.Empty(media);
    }

    [Fact]
    public void Scanner_DetectsOneDrivePlaceholderAttributes()
    {
        // FILE_ATTRIBUTE_OFFLINE = 0x1000, FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x400000
        Assert.True(FileScanner.IsOfflineAttribute((FileAttributes)0x1000));
        Assert.True(FileScanner.IsOfflineAttribute((FileAttributes)0x400000));
        Assert.True(FileScanner.IsOfflineAttribute((FileAttributes)0x00040000));
        Assert.False(FileScanner.IsOfflineAttribute(FileAttributes.Normal));
        Assert.False(FileScanner.IsOfflineAttribute(FileAttributes.ReadOnly));
    }

    [Fact]
    public void MetadataRequirement_DetectsMediaTokens()
    {
        var pipeline = new RulePipeline();
        pipeline.Add(new TemplateRule { Template = "{media:yyyy}" });

        Assert.True(MetadataRequirement.NeedsMetadata(pipeline));
        Assert.True(MetadataRequirement.HasMediaToken("{media}"));
        Assert.False(MetadataRequirement.HasMediaToken("{taken}"));
    }

    // ---------------------------------------------------------------------
    // Undo nhiều bước (FR-C5)
    // ---------------------------------------------------------------------

    [Fact]
    public void History_Free_KeepsOnlyLatestEntry()
    {
        var fs = new FakeFileSystem();
        var history = new HistoryStore(fs, "history.json");

        history.Add(new HistoryEntry { Id = "a", StartedUtc = DateTime.UtcNow });
        history.Add(new HistoryEntry { Id = "b", StartedUtc = DateTime.UtcNow });
        history.Add(new HistoryEntry { Id = "c", StartedUtc = DateTime.UtcNow });

        var kept = history.PruneAndLimit(TimeSpan.FromDays(30), maxEntries: 1);

        Assert.Equal(1, kept);
        Assert.Equal("c", history.LoadAll().Single().Id);
    }

    [Fact]
    public void History_Pro_KeepsEntriesWithin30Days()
    {
        var fs = new FakeFileSystem();
        var history = new HistoryStore(fs, "history.json");
        var now = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        history.Add(new HistoryEntry { Id = "old", StartedUtc = now.AddDays(-40) });
        history.Add(new HistoryEntry { Id = "new", StartedUtc = now.AddDays(-2) });

        var kept = history.PruneAndLimit(TimeSpan.FromDays(30), maxEntries: int.MaxValue, nowUtc: now);

        Assert.Equal(1, kept);
        Assert.Equal("new", history.LoadAll().Single().Id);
    }

    [Fact]
    public void Undo_CanRollbackSelectedHistoryEntry()
    {
        var fs = new FakeFileSystem();
        fs.WriteAllText("a.txt", "A");
        fs.WriteAllText("b.txt", "B");

        var steps = new RenamePlanner().Plan(new[]
        {
            (From: "a.txt", To: "c.txt"),
            (From: "b.txt", To: "d.txt")
        });

        var result = new RenameExecutor(fs, "journal").Execute(steps);
        var journal = fs.GetFiles("journal", "*.jsonl", false).Single();

        var undo = new UndoService(fs).UndoJournal(journal);

        Assert.Equal(2, undo.Undone);
        Assert.True(fs.FileExists("a.txt"));
        Assert.True(fs.FileExists("b.txt"));
    }

    // ---------------------------------------------------------------------
    // Xuất báo cáo (FR-C7)
    // ---------------------------------------------------------------------

    [Fact]
    public void Report_Csv_ContainsHeaderAndEscapesCommas()
    {
        var entries = new[]
        {
            new RenameReportEntry
            {
                OldPath = @"C:\data\a,b.txt",
                NewPath = @"C:\data\c.txt",
                Status = "ok",
                TimestampUtc = new DateTime(2026, 2, 3, 4, 5, 6, DateTimeKind.Utc)
            }
        };

        var csv = ReportExporter.ToCsv(entries);

        Assert.StartsWith("old_path,new_path,status,error,timestamp_utc", csv);
        Assert.Contains("\"C:\\data\\a,b.txt\"", csv);
    }

    [Fact]
    public void Report_Json_IsParseableAndKeepsFields()
    {
        var entries = new[]
        {
            new RenameReportEntry { OldPath = "old.txt", NewPath = "new.txt", Status = "ok" }
        };

        var json = ReportExporter.ToJson(entries);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement[0];

        Assert.Equal("old.txt", root.GetProperty("OldPath").GetString());
        Assert.Equal("new.txt", root.GetProperty("NewPath").GetString());
        Assert.Equal("ok", root.GetProperty("Status").GetString());
    }

    [Fact]
    public void Report_Export_WritesFileToDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bfr-report-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "report.csv");

        try
        {
            ReportExporter.Export(new[]
            {
                new RenameReportEntry { OldPath = "a.txt", NewPath = "b.txt", Status = "ok" }
            }, path, ReportFormat.Csv);

            Assert.True(File.Exists(path));
            Assert.Contains("a.txt", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------------
    // Thư viện preset: giới hạn số preset theo cấp
    // ---------------------------------------------------------------------

    [Fact]
    public void Scanner_FreeDepth2_StopsAtThirdLevel()
    {
        var root = Path.Combine(Path.GetTempPath(), "bfr-depth-" + Guid.NewGuid().ToString("N"));
        try
        {
            var level1 = Path.Combine(root, "c1");
            var level2 = Path.Combine(level1, "c2");
            var level3 = Path.Combine(level2, "c3");
            Directory.CreateDirectory(level3);
            File.WriteAllText(Path.Combine(root, "a.txt"), "1");
            File.WriteAllText(Path.Combine(level1, "b.txt"), "2");
            File.WriteAllText(Path.Combine(level2, "c.txt"), "3");
            File.WriteAllText(Path.Combine(level3, "d.txt"), "4");

            var scanner = new FileScanner();

            var free = scanner.Scan(new[] { root }, new ScanOptions { Recursive = true, MaxDepth = 2 });
            var freeNames = free.Select(f => f.Name).OrderBy(n => n).ToList();
            Assert.Equal(new[] { "a", "b", "c" }, freeNames);
            Assert.DoesNotContain("d", freeNames);

            var pro = scanner.Scan(new[] { root }, new ScanOptions { Recursive = true, MaxDepth = int.MaxValue });
            Assert.Contains("d", pro.Select(f => f.Name));
        }
        finally
        {
            try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void PresetLibrary_Free_LimitsToThreePresets()
    {
        var fs = new FakeFileSystem();
        var store = new PresetStore(fs, "presets");
        var service = new LicenseService(new LicenseStore(fs, "license.json"), trialEnabled: false);

        for (int i = 1; i <= 3; i++)
        {
            store.Save(new Preset { Name = "p" + i, Description = "d" });
        }

        var count = store.LoadAll().Count;
        Assert.Equal(3, count);
        Assert.False(service.CheckPresetCount(count + 1).Allowed);
    }

    [Fact]
    public void PresetLibrary_Delete_RemovesPreset()
    {
        var fs = new FakeFileSystem();
        var store = new PresetStore(fs, "presets");
        store.Save(new Preset { Name = "x", Description = "d" });

        Assert.True(store.Delete("x"));
        Assert.Empty(store.LoadAll());
    }

    private static FileItem MakeFile(string name, DateTime? created = null) => new(
        FullPath: Path.Combine("C:\\data", name),
        Directory: "C:\\data",
        Name: Path.GetFileNameWithoutExtension(name),
        Extension: Path.GetExtension(name).TrimStart('.'),
        IsDirectory: false,
        Size: 1024,
        CreatedUtc: created ?? new DateTime(2022, 2, 2, 0, 0, 0, DateTimeKind.Utc),
        ModifiedUtc: new DateTime(2022, 2, 2, 0, 0, 0, DateTimeKind.Utc),
        LoadOrder: 0);
}
