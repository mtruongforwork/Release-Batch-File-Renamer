namespace BFR.Presentation.Tools;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BFR.Core;
using BFR.Core.Licensing;

/// <summary>Tính checksum và kiểm tra tệp .md5/.sha1/.sha256 (M9). Chỉ đọc.</summary>
public sealed class ChecksumTool : ITool
{
    public string Key => "checksum";
    public string TitleKey => "ToolChecksumTitle";
    public string DescriptionKey => "ToolChecksumDesc";
    public string Icon => "";
    public bool ReadOnly => true;
    public bool AcceptsFiles => true;
    public string? QuickOptionKey => "mode";

    public IReadOnlyList<ToolOption> Options { get; } = new ToolOption[]
    {
        new("mode", "ToolChecksumMode", OptionKind.Choice, "compute", new (string, string)[] { ("compute", "ToolChecksumCompute"), ("verify", "ToolChecksumVerify") }),
        new("algo", "ToolChecksumAlgo", OptionKind.Choice, "sha256", new (string, string)[] { ("sha256", "SHA-256"), ("sha1", "SHA-1"), ("md5", "MD5") }, VisibleWhen: "mode=compute"),
        new("checkFile", "ToolChecksumFile", OptionKind.File, "", VisibleWhen: "mode=verify", Filter: "Checksum|*.sha256;*.sha1;*.md5"),
        new("recursive", "ToolRecursive", OptionKind.Toggle, "false", VisibleWhen: "mode=compute")
    };

    public Task<ToolPlan> PlanAsync(ToolContext ctx, CancellationToken ct, IProgress<string>? progress = null) =>
        Task.Run(() => ctx.Text("mode", "compute") == "verify" ? Verify(ctx, ct) : Compute(ctx, ct, progress), ct);

    private static ToolPlan Compute(ToolContext ctx, CancellationToken ct, IProgress<string>? progress)
    {
        var files = ctx.ScanFiles();
        if (files.Count == 0) return ToolPlan.Empty(ToolText.T("CoreToolNoFiles", "Chưa có tệp nào. Thêm tệp hoặc thư mục ở phía trên."));

        var algo = ctx.Text("algo", "sha256") switch { "md5" => ChecksumAlgorithm.Md5, "sha1" => ChecksumAlgorithm.Sha1, _ => ChecksumAlgorithm.Sha256 };
        var baseDir = ToolText.CommonDirectory(files);
        var rows = new List<ToolRow>();
        var entries = new List<ChecksumEntry>();
        var done = 0;

        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"{++done}/{files.Count}");
            try
            {
                var hash = Checksum.Compute(ctx.Fs, f.FullPath, algo, ct);
                var relative = ToolText.Relative(baseDir, f.FullPath);
                entries.Add(new ChecksumEntry(hash, relative));
                rows.Add(new ToolRow(relative, hash));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                rows.Add(new ToolRow(ToolText.Name(f.FullPath), ex.Message, ToolRowState.Error));
            }
        }

        return new ToolPlan
        {
            Rows = rows,
            Summary = ToolText.T("CoreToolChecksumSummary", "{0} tệp đã tính {1}.", entries.Count, algo.ToString().ToUpperInvariant()),
            Export = new ToolExport("checksums" + Checksum.Extension(algo), "Checksum|*" + Checksum.Extension(algo), Checksum.Format(entries))
        };
    }

    private static ToolPlan Verify(ToolContext ctx, CancellationToken ct)
    {
        var file = ctx.Text("checkFile");
        if (file.Length == 0 || !ctx.Fs.FileExists(file)) return ToolPlan.Error(ToolText.T("CoreToolChecksumNeedFile", "Chọn tệp .md5, .sha1 hoặc .sha256 để kiểm tra."));

        var algo = Checksum.FromExtension(file);
        if (algo is null) return ToolPlan.Error(ToolText.T("CoreToolChecksumBadExt", "Không nhận ra thuật toán từ đuôi tệp."));

        var entries = Checksum.Parse(ctx.Fs.ReadAllText(file), algo.Value);
        if (entries.Count == 0) return ToolPlan.Error(ToolText.T("CoreToolChecksumEmpty", "Tệp kiểm tra không có dòng hợp lệ."));

        var results = Checksum.Verify(ctx.Fs, Path.GetDirectoryName(Path.GetFullPath(file)) ?? ".", entries, algo.Value, ct);
        var rows = results.Select(r => new ToolRow(r.RelativePath,
            r.Status switch
            {
                ChecksumStatus.Match => ToolText.T("CoreToolChecksumMatch", "Khớp"),
                ChecksumStatus.Mismatch => ToolText.T("CoreToolChecksumMismatch", "Sai"),
                ChecksumStatus.Missing => ToolText.T("CoreToolChecksumMissing", "Thiếu"),
                _ => ToolText.T("CoreToolChecksumInvalid", "Đường dẫn không an toàn")
            },
            r.Status == ChecksumStatus.Match ? ToolRowState.Ok : ToolRowState.Error)).ToList();

        var bad = results.Count(r => r.Status != ChecksumStatus.Match);
        return new ToolPlan
        {
            Rows = rows,
            Blocked = false,
            Summary = bad == 0
                ? ToolText.T("CoreToolChecksumAllOk", "Tất cả {0} tệp khớp.", results.Count)
                : ToolText.T("CoreToolChecksumSomeBad", "{0}/{1} tệp không khớp.", bad, results.Count)
        };
    }
}

/// <summary>Phân tích thư mục: dung lượng theo loại, thư mục và tệp lớn nhất, tệp cũ, đuôi sai (M8). Chỉ đọc.</summary>
public sealed class AnalyzeTool : ITool
{
    public string Key => "analyze";
    public string TitleKey => "ToolAnalyzeTitle";
    public string DescriptionKey => "ToolAnalyzeDesc";
    public string Icon => "";
    public bool ReadOnly => true;
    public bool AcceptsFiles => false;

    public IReadOnlyList<ToolOption> Options { get; } = new ToolOption[]
    {
        new("top", "ToolAnalyzeTop", OptionKind.Number, "20"),
        new("olderThan", "ToolAnalyzeOlder", OptionKind.Number, "0", HintKey: "ToolAnalyzeOlderHint"),
        new("checkExt", "ToolAnalyzeCheckExt", OptionKind.Toggle, "true", HintKey: "ToolAnalyzeCheckExtHint")
    };

    public Task<ToolPlan> PlanAsync(ToolContext ctx, CancellationToken ct, IProgress<string>? progress = null) =>
        Task.Run(() => Plan(ctx, ct, progress), ct);

    private static ToolPlan Plan(ToolContext ctx, CancellationToken ct, IProgress<string>? progress)
    {
        var folder = ctx.Paths.FirstOrDefault(Directory.Exists);
        if (folder is null) return ToolPlan.Empty(ToolText.T("CoreToolNeedFolder", "Thêm một thư mục để làm việc."));

        var top = Math.Clamp(ctx.Int("top", 20), 1, 500);
        var report = FolderAnalyzer.Analyze(ctx.Fs, folder,
            new AnalyzeOptions(top, Math.Max(0, ctx.Int("olderThan")), ctx.Bool("checkExt", true)), DateTime.UtcNow,
            ct: ct, progress: new Progress<int>(n => progress?.Report(ToolText.T("CoreToolAnalyzeProgress", "{0} tệp…", n))));

        var rows = new List<ToolRow>();
        void Header(string text) => rows.Add(new ToolRow(text, string.Empty, ToolRowState.Header));

        Header(ToolText.T("CoreToolAnalyzeByType", "Theo loại tệp"));
        foreach (var t in report.ByType) rows.Add(new ToolRow(t.Category.ToString(), $"{t.Count:N0} · {ToolText.Size(t.Bytes)}"));

        Header(ToolText.T("CoreToolAnalyzeFolders", "Thư mục lớn nhất"));
        foreach (var f in report.TopFolders.Take(15)) rows.Add(new ToolRow(f.Path, ToolText.Size(f.Bytes)));

        Header(ToolText.T("CoreToolAnalyzeLargest", "Tệp lớn nhất"));
        foreach (var f in report.Largest) rows.Add(new ToolRow(f.Path, ToolText.Size(f.Size)));

        if (report.Old.Count > 0)
        {
            Header(ToolText.T("CoreToolAnalyzeOld", "Tệp cũ ({0})", report.Old.Count));
            foreach (var f in report.Old.Take(200)) rows.Add(new ToolRow(f.Path, f.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd")));
        }

        if (report.Mismatched.Count > 0)
        {
            Header(ToolText.T("CoreToolAnalyzeMismatch", "Đuôi sai ({0})", report.Mismatched.Count));
            foreach (var m in report.Mismatched)
                rows.Add(new ToolRow(m.Path, $".{m.CurrentExtension} → .{m.DetectedExtension}", ToolRowState.Warning));
        }

        if (report.Skipped.Count > 0)
        {
            Header(ToolText.T("CoreToolAnalyzeSkipped", "Bỏ qua ({0})", report.Skipped.Count));
            foreach (var s in report.Skipped.Take(50)) rows.Add(new ToolRow(s, ToolText.T("CoreToolAnalyzeSkippedWhy", "Liên kết, vùng hệ thống hoặc không đọc được"), ToolRowState.Info));
        }

        var json = JsonSerializer.Serialize(new
        {
            totalBytes = report.TotalBytes, files = report.TotalFiles, folders = report.TotalFolders,
            byType = report.ByType.Select(t => new { type = t.Category.ToString(), t.Count, t.Bytes }),
            topFolders = report.TopFolders, largest = report.Largest, old = report.Old, mismatched = report.Mismatched
        }, new JsonSerializerOptions { WriteIndented = true });

        return new ToolPlan
        {
            Rows = rows,
            Summary = ToolText.T("CoreToolAnalyzeSummary", "{0:N0} tệp, {1:N0} thư mục, {2}.", report.TotalFiles, report.TotalFolders, ToolText.Size(report.TotalBytes)),
            Export = new ToolExport("analysis.json", "JSON|*.json", json, ProFeature.AnalyzeExport),
            Treemap = report.ByType.Select(t => new BFR.Presentation.Design.TreemapItem($"{t.Category} · {ToolText.Size(t.Bytes)}", t.Bytes)).ToList()
        };
    }
}

/// <summary>Chạy workflow nhiều bước (M10, Pro) từ mẫu có sẵn hoặc tệp JSON.</summary>
public sealed class WorkflowTool : ITool
{
    public static IReadOnlyList<(string Value, WorkflowDefinition Definition)> Templates() => new[]
    {
        ("photos", new WorkflowDefinition("Photos", new List<WorkflowStep>
        {
            new RenameStep(BuiltInPresets.All.First(p => p.Name == "Chữ thường toàn bộ")),
            new OrganizeStep("{year}\\{month}")
        })),
        ("tidy", new WorkflowDefinition("Tidy", new List<WorkflowStep>
        {
            new FlattenStep(),
            new RemoveEmptyStep()
        }))
    };

    public string Key => "workflow";
    public string TitleKey => "ToolWorkflowTitle";
    public string DescriptionKey => "ToolWorkflowDesc";
    public string Icon => "";
    public bool ReadOnly => false;
    public bool AcceptsFiles => true;

    public IReadOnlyList<ToolOption> Options { get; } = new ToolOption[]
    {
        new("template", "ToolWorkflowTemplate", OptionKind.Choice, "photos", new (string, string)[]
        {
            ("photos", "ToolWorkflowPhotos"), ("tidy", "ToolWorkflowTidy"), ("file", "ToolWorkflowFromFile")
        }),
        new("file", "ToolWorkflowFile", OptionKind.File, "", VisibleWhen: "template=file", Filter: "Workflow|*.json"),
        new("recursive", "ToolRecursive", OptionKind.Toggle, "false")
    };

    public Task<ToolPlan> PlanAsync(ToolContext ctx, CancellationToken ct, IProgress<string>? progress = null) =>
        Task.Run(() => Plan(ctx, ct), ct);

    private static ToolPlan Plan(ToolContext ctx, CancellationToken ct)
    {
        if (!ctx.License.Allows(ProFeature.Workflow))
        {
            var m = FeatureMessages.For(ProFeature.Workflow);
            return new ToolPlan { Blocked = true, Messages = new[] { m }, UpgradeFeature = ProFeature.Workflow, Summary = m };
        }

        var files = ctx.ScanFiles();
        if (files.Count == 0) return ToolPlan.Empty(ToolText.T("CoreToolNoFiles", "Chưa có tệp nào. Thêm tệp hoặc thư mục ở phía trên."));

        WorkflowDefinition definition;
        var template = ctx.Text("template", "photos");
        if (template == "file")
        {
            var path = ctx.Text("file");
            if (path.Length == 0 || !ctx.Fs.FileExists(path)) return ToolPlan.Error(ToolText.T("CoreToolWorkflowNeedFile", "Chọn tệp workflow (.json)."));
            try { definition = WorkflowSerializer.Parse(ctx.Fs.ReadAllText(path)); }
            catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or IOException)
            {
                return ToolPlan.Error(ToolText.T("CoreToolWorkflowBad", "Workflow không hợp lệ: {0}", ex.Message));
            }
        }
        else
        {
            definition = Templates().FirstOrDefault(t => t.Value == template).Definition ?? Templates()[0].Definition;
        }

        var plan = WorkflowPlanner.Plan(files, definition.Steps, ctx.Fs, ctx.RuleContext(files), ctx.Metadata is not null && ctx.License.Allows(ProFeature.ExifDate) ? ctx.Metadata : null, ct: ct);

        var rows = new List<ToolRow>();
        foreach (var s in plan.Steps)
        {
            rows.Add(new ToolRow(ToolText.T("CoreToolWorkflowStep", "Bước {0}: {1}", s.Index + 1, s.Kind),
                ToolText.T("CoreToolWorkflowStepInfo", "{0} thao tác · {1} → {2} tệp", s.Operations.Count, s.FilesIn, s.FilesOut),
                s.Issues.Count > 0 ? ToolRowState.Error : ToolRowState.Header,
                s.Issues.FirstOrDefault()));
        }
        foreach (var f in plan.FinalFiles.Take(500)) rows.Add(new ToolRow(f.FullPath, string.Empty, ToolRowState.Ok));

        return new ToolPlan
        {
            Rows = rows,
            Operations = plan.Operations,
            Messages = plan.Errors,
            Blocked = !plan.IsValid,
            Summary = plan.IsValid
                ? ToolText.T("CoreToolWorkflowSummary", "{0} bước, {1} thao tác.", plan.Steps.Count, plan.Operations.Count)
                : plan.Errors[0]
        };
    }
}

/// <summary>Danh sách công cụ của khung công cụ, theo thứ tự hiển thị.</summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ITool> Create() => new ITool[]
    {
        new OrganizeTool(),
        new DuplicatesTool(),
        new FolderTool(),
        new MetadataTool(),
        new ImageTool(),
        new ArchiveTool(),
        new ChecksumTool(),
        new AnalyzeTool(),
        new WorkflowTool()
    };
}
