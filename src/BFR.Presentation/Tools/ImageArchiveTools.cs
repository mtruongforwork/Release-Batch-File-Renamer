namespace BFR.Presentation.Tools;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BFR.Core;
using BFR.Core.Licensing;

/// <summary>Đổi định dạng, resize, xoay theo EXIF, nén JPEG (M5). Mặc định không ghi đè bản gốc.</summary>
public sealed class ImageTool : ITool
{
    public string Key => "images";
    public string TitleKey => "ToolImagesTitle";
    public string DescriptionKey => "ToolImagesDesc";
    public string Icon => "";
    public bool ReadOnly => false;
    public bool AcceptsFiles => true;
    public string? QuickOptionKey => "resize";

    public IReadOnlyList<ToolOption> Options { get; } = new ToolOption[]
    {
        new("format", "ToolImagesFormat", OptionKind.Choice, "keep", new (string, string)[]
        {
            ("keep", "ToolImagesKeep"), ("jpg", "ToolImagesJpeg"), ("png", "ToolImagesPng"), ("bmp", "ToolImagesBmp"), ("tif", "ToolImagesTiff")
        }),
        new("resize", "ToolImagesResize", OptionKind.Choice, "none", new (string, string)[]
        {
            ("none", "ToolImagesResizeNone"), ("long", "ToolImagesResizeLong"), ("percent", "ToolImagesResizePercent"), ("fit", "ToolImagesResizeFit")
        }),
        new("size", "ToolImagesSize", OptionKind.Number, "2048", VisibleWhen: "resize=long|percent"),
        new("fitWidth", "ToolImagesFitWidth", OptionKind.Number, "1920", VisibleWhen: "resize=fit"),
        new("fitHeight", "ToolImagesFitHeight", OptionKind.Number, "1080", VisibleWhen: "resize=fit"),
        new("quality", "ToolImagesQuality", OptionKind.Number, "85", HintKey: "ToolImagesQualityHint"),
        new("orient", "ToolImagesOrient", OptionKind.Toggle, "true"),
        new("upscale", "ToolImagesUpscale", OptionKind.Toggle, "false"),
        new("suffix", "ToolImagesSuffix", OptionKind.Text, "_edit"),
        new("outFolder", "ToolImagesOutFolder", OptionKind.Folder, ""),
        new("overwrite", "ToolImagesOverwrite", OptionKind.Toggle, "false", HintKey: "ToolImagesOverwriteHint"),
        new("recursive", "ToolRecursive", OptionKind.Toggle, "false")
    };

    public Task<ToolPlan> PlanAsync(ToolContext ctx, CancellationToken ct, IProgress<string>? progress = null) =>
        Task.Run(() => Plan(ctx, ct, progress), ct);

    private static ToolPlan Plan(ToolContext ctx, CancellationToken ct, IProgress<string>? progress)
    {
        if (ctx.Images is null) return ToolPlan.Error(ToolText.T("CoreToolNoImageEngine", "Tính năng này chỉ có trên Windows."));

        var files = ctx.ScanFiles();
        if (files.Count == 0) return ToolPlan.Empty(ToolText.T("CoreToolNoFiles", "Chưa có tệp nào. Thêm tệp hoặc thư mục ở phía trên."));

        var resize = ctx.Text("resize", "none") switch
        {
            "long" => ImageResizeMode.LongEdge,
            "percent" => ImageResizeMode.Percent,
            "fit" => ImageResizeMode.Fit,
            _ => ImageResizeMode.None
        };
        var format = ctx.Text("format", "keep") switch
        {
            "jpg" => ImageFormat.Jpeg,
            "png" => ImageFormat.Png,
            "bmp" => ImageFormat.Bmp,
            "tif" => ImageFormat.Tiff,
            _ => ImageFormat.Keep
        };
        var options = new ImageOptions(format, resize, ctx.Int("size", 2048), ctx.Int("fitWidth", 1920), ctx.Int("fitHeight", 1080),
            ctx.Int("quality", 85), ctx.Bool("orient", true), ctx.Bool("upscale"),
            ctx.Values.TryGetValue("suffix", out var suffix) ? suffix : "_edit",
            ctx.Text("outFolder") is { Length: > 0 } o ? o : null, ctx.Bool("overwrite"));

        var plan = ImagePlanner.Plan(files, options, ctx.Fs);
        if (plan.Errors.Count > 0) return new ToolPlan { Blocked = true, Messages = plan.Errors, Summary = plan.Errors[0] };

        // Giới hạn Free: nói rõ đã xử lý bao nhiêu trên tổng số.
        var ready = plan.Items.Where(i => i.Status == ImageStatus.Ready).ToList();
        var limit = ctx.License.MaxImagesPerBatch;
        var messages = new List<string>();
        ProFeature? upgrade = null;
        if (ready.Count > limit)
        {
            messages.Add(ToolText.T("CoreToolImagesLimit", "Đã xử lý {0}/{1} ảnh. Nâng cấp Pro để xử lý hết.", limit, ready.Count));
            upgrade = ProFeature.ImageUnlimited;
            var keep = ready.Take(limit).Select(i => i.File.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            plan = new ImagePlan(plan.Items.Select(i => i.Status == ImageStatus.Ready && !keep.Contains(i.File.FullPath)
                ? i with { Status = ImageStatus.Unsupported }
                : i).ToList(), plan.Errors);
        }

        progress?.Report(ToolText.T("CoreToolProcessing", "Đang xử lý…"));
        var batch = ImageBatch.Run(plan, options, ctx.Images, ctx.Fs, ctx.TempRoot, ct: ct);

        var rows = new List<ToolRow>();
        foreach (var item in plan.Items)
        {
            if (item.Status == ImageStatus.Ready)
            {
                var failure = batch.Failures.FirstOrDefault(f => string.Equals(f.Path, item.File.FullPath, StringComparison.OrdinalIgnoreCase));
                rows.Add(failure is null
                    ? new ToolRow(ToolText.Name(item.File.FullPath), ToolText.Name(item.OutputPath))
                    : new ToolRow(ToolText.Name(item.File.FullPath), failure.Error, ToolRowState.Error));
            }
            else if (item.Status == ImageStatus.Protected)
            {
                rows.Add(new ToolRow(ToolText.Name(item.File.FullPath), item.Message ?? string.Empty, ToolRowState.Error));
            }
            else
            {
                rows.Add(new ToolRow(ToolText.Name(item.File.FullPath), ToolText.T("CoreToolImagesSkipped", "Bỏ qua"), ToolRowState.Info));
            }
        }

        foreach (var f in batch.Failures) messages.Add($"{ToolText.Name(f.Path)}: {f.Error}");

        var saved = batch.BytesBefore - batch.BytesAfter;
        return new ToolPlan
        {
            Rows = rows,
            Operations = batch.Operations,
            Messages = messages,
            UpgradeFeature = upgrade,
            Cleanup = () => CleanTemp(ctx, batch.Operations),
            Summary = ToolText.T("CoreToolImagesSummary", "{0} ảnh: {1} → {2} ({3}).", batch.Operations.Count(o => o.Kind == OperationKind.WriteFile),
                ToolText.Size(batch.BytesBefore), ToolText.Size(batch.BytesAfter),
                saved >= 0 ? "−" + ToolText.Size(saved) : "+" + ToolText.Size(-saved))
        };
    }

    private static void CleanTemp(ToolContext ctx, IReadOnlyList<Operation> ops)
    {
        foreach (var op in ops.Where(o => o.Kind == OperationKind.WriteFile))
        {
            try { if (ctx.Fs.FileExists(op.From)) ctx.Fs.Delete(op.From, recursive: false); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}

/// <summary>Nén ZIP và giải nén an toàn (M6).</summary>
public sealed class ArchiveTool : ITool
{
    public string Key => "archive";
    public string TitleKey => "ToolArchiveTitle";
    public string DescriptionKey => "ToolArchiveDesc";
    public string Icon => "";
    public bool ReadOnly => false;
    public bool AcceptsFiles => true;
    public string? QuickOptionKey => "mode";

    public IReadOnlyList<ToolOption> Options { get; } = new ToolOption[]
    {
        new("mode", "ToolArchiveMode", OptionKind.Choice, "compress", new (string, string)[] { ("compress", "ToolArchiveCompress"), ("extract", "ToolArchiveExtract") }),
        new("group", "ToolArchiveGroup", OptionKind.Choice, "all", new (string, string)[]
        {
            ("all", "ToolArchiveGroupAll"), ("folder", "ToolArchiveGroupFolder"), ("files", "ToolArchiveGroupFiles"), ("mb", "ToolArchiveGroupMb")
        }, VisibleWhen: "mode=compress"),
        new("groupSize", "ToolArchiveGroupSize", OptionKind.Number, "100", VisibleWhen: "group=files|mb"),
        new("name", "ToolArchiveName", OptionKind.Text, "{name}", VisibleWhen: "mode=compress", HintKey: "ToolArchiveNameHint"),
        new("level", "ToolArchiveLevel", OptionKind.Choice, "normal", new (string, string)[] { ("fast", "ToolLevelFast"), ("normal", "ToolLevelNormal"), ("small", "ToolLevelSmall") }, VisibleWhen: "mode=compress"),
        new("outFolder", "ToolArchiveOutFolder", OptionKind.Folder, ""),
        new("layout", "ToolArchiveLayout", OptionKind.Choice, "own", new (string, string)[] { ("own", "ToolLayoutOwn"), ("single", "ToolLayoutSingle") }, VisibleWhen: "mode=extract"),
        new("recursive", "ToolRecursive", OptionKind.Toggle, "false", VisibleWhen: "mode=compress")
    };

    public Task<ToolPlan> PlanAsync(ToolContext ctx, CancellationToken ct, IProgress<string>? progress = null) =>
        Task.Run(() => ctx.Text("mode", "compress") == "extract" ? Extract(ctx, ct) : Compress(ctx, ct), ct);

    private static ToolPlan Compress(ToolContext ctx, CancellationToken ct)
    {
        var files = ctx.ScanFiles();
        if (files.Count == 0) return ToolPlan.Empty(ToolText.T("CoreToolNoFiles", "Chưa có tệp nào. Thêm tệp hoặc thư mục ở phía trên."));

        var group = ctx.Text("group", "all");
        var name = ctx.Text("name", "{name}");
        var advanced = group != "all" || name != "{name}";
        if (advanced && !ctx.License.Allows(ProFeature.ArchiveAdvanced))
        {
            var m = FeatureMessages.For(ProFeature.ArchiveAdvanced);
            return new ToolPlan { Blocked = true, Messages = new[] { m }, UpgradeFeature = ProFeature.ArchiveAdvanced, Summary = m };
        }

        var size = Math.Max(1, ctx.Int("groupSize", 100));
        var grouping = group switch
        {
            "folder" => ArchiveGrouping.PerFolder,
            "files" or "mb" => ArchiveGrouping.PerGroup,
            _ => ArchiveGrouping.AllInOne
        };
        var level = ctx.Text("level", "normal") switch
        {
            "fast" => CompressionLevel.Fastest,
            "small" => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };
        var options = new CompressOptions(grouping, ctx.Text("outFolder") is { Length: > 0 } o ? o : null, name, level,
            group == "files" ? size : 0, group == "mb" ? size * 1024L * 1024L : 0);

        var jobs = ArchiveCompressor.Plan(files, options, ctx.Fs);
        if (jobs.Count == 0) return ToolPlan.Empty(ToolText.T("CoreToolArchiveNothing", "Không có tệp nào để nén."));
        ct.ThrowIfCancellationRequested();

        var run = ArchiveCompressor.Run(jobs, options, ctx.Fs, ctx.TempRoot, ct);
        var rows = jobs.Select(j => new ToolRow(ToolText.Name(j.ZipPath), ToolText.T("CoreToolArchiveEntries", "{0} tệp", j.Entries.Count))).ToList();
        var messages = run.Skipped.Select(s => $"{ToolText.Name(s.Path)}: {s.Reason}").ToList();

        return new ToolPlan
        {
            Rows = rows,
            Operations = run.Operations,
            Messages = messages,
            Cleanup = () =>
            {
                foreach (var op in run.Operations.Where(o => o.Kind == OperationKind.WriteFile))
                {
                    try { if (ctx.Fs.FileExists(op.From)) ctx.Fs.Delete(op.From, recursive: false); }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
                }
            },
            Summary = ToolText.T("CoreToolArchiveSummary", "{0} tệp ZIP: {1} → {2}.", jobs.Count, ToolText.Size(run.BytesBefore), ToolText.Size(run.BytesAfter))
        };
    }

    private static ToolPlan Extract(ToolContext ctx, CancellationToken ct)
    {
        var zips = ctx.Paths.Where(p => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).Select(Path.GetFullPath).ToList();
        if (zips.Count == 0) return ToolPlan.Empty(ToolText.T("CoreToolNeedZip", "Thêm ít nhất một tệp .zip."));

        var options = new ExtractOptions(
            ctx.Text("layout", "own") == "single" ? ExtractLayout.SingleFolder : ExtractLayout.OwnFolder,
            ctx.Text("outFolder") is { Length: > 0 } o ? o : null);
        var result = ArchiveExtractor.Extract(zips, options, ctx.Fs, ctx.TempRoot, ct: ct);

        var rows = new List<ToolRow>();
        foreach (var r in result.Rejected) rows.Add(new ToolRow(ToolText.Name(r.ZipPath), r.Reason, ToolRowState.Error));
        foreach (var s in result.Skipped) rows.Add(new ToolRow(s.Path, s.Reason, ToolRowState.Info));
        var folders = result.Operations.Where(o => o.Kind == OperationKind.CreateDir).Select(o => o.From).Distinct().ToList();
        var fileCount = result.Operations.Count(o => o.Kind == OperationKind.Move);
        rows.InsertRange(0, folders.Select(f => new ToolRow(f, ToolText.T("CoreToolArchiveExtractTo", "Giải nén vào đây"))));

        return new ToolPlan
        {
            Rows = rows,
            Operations = result.Operations,
            Messages = result.Rejected.Select(r => $"{ToolText.Name(r.ZipPath)}: {r.Reason}").ToList(),
            Cleanup = () =>
            {
                try { if (ctx.Fs.DirectoryExists(ctx.TempRoot)) ctx.Fs.Delete(ctx.TempRoot, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            },
            Summary = ToolText.T("CoreToolExtractSummary", "{0} tệp sẽ được giải nén ({1}), {2} ZIP bị từ chối.", fileCount, ToolText.Size(result.BytesWritten), result.Rejected.Count)
        };
    }
}
