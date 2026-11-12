namespace BFR.Presentation.Tools;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BFR.Core;
using BFR.Core.Licensing;

/// <summary>Công cụ thư mục (M7): làm phẳng, xoá thư mục rỗng, bọc vào thư mục, tạo cây thư mục.</summary>
public sealed class FolderTool : ITool
{
    public string Key => "folders";
    public string TitleKey => "ToolFoldersTitle";
    public string DescriptionKey => "ToolFoldersDesc";
    public string Icon => "";
    public bool ReadOnly => false;
    public bool AcceptsFiles => true;

    public IReadOnlyList<ToolOption> Options { get; } = new ToolOption[]
    {
        new("mode", "ToolFoldersMode", OptionKind.Choice, "flatten", new (string, string)[]
        {
            ("flatten", "ToolFoldersFlatten"), ("empty", "ToolFoldersEmpty"), ("wrap", "ToolFoldersWrap"), ("tree", "ToolFoldersTree")
        }),
        new("prefixPath", "ToolFoldersPrefixPath", OptionKind.Toggle, "false", VisibleWhen: "mode=flatten"),
        new("removeEmptied", "ToolFoldersRemoveEmptied", OptionKind.Toggle, "true", VisibleWhen: "mode=flatten"),
        new("junk", "ToolFoldersJunk", OptionKind.Toggle, "true", VisibleWhen: "mode=empty", HintKey: "ToolFoldersJunkHint"),
        new("groupSameName", "ToolFoldersGroupSameName", OptionKind.Toggle, "true", VisibleWhen: "mode=wrap"),
        new("treeText", "ToolFoldersTreeText", OptionKind.MultiLine, "2024\n  Q1\n  Q2\n2025", VisibleWhen: "mode=tree", HintKey: "ToolFoldersTreeHint"),
        new("recursive", "ToolRecursive", OptionKind.Toggle, "false", VisibleWhen: "mode=wrap")
    };

    public Task<ToolPlan> PlanAsync(ToolContext ctx, CancellationToken ct, IProgress<string>? progress = null) =>
        Task.Run(() => Plan(ctx), ct);

    private static string? FirstFolder(ToolContext ctx) =>
        ctx.Paths.FirstOrDefault(p => Directory.Exists(p)) ?? ctx.Paths.Select(p => Path.GetDirectoryName(p)).FirstOrDefault(d => !string.IsNullOrEmpty(d));

    private ToolPlan Plan(ToolContext ctx)
    {
        var mode = ctx.Text("mode", "flatten");

        if (mode == "wrap")
        {
            var files = ctx.ScanFiles();
            if (files.Count == 0) return ToolPlan.Empty(ToolText.T("CoreToolNoFiles", "Chưa có tệp nào. Thêm tệp hoặc thư mục ở phía trên."));
            var plan = FolderToolsPlanner.Wrap(files, new WrapOptions(ctx.Bool("groupSameName", true)), ctx.Fs);
            return FromFolderPlan(plan, ToolText.T("CoreToolWrapSummary", "{0} tệp sẽ được bọc vào thư mục.", plan.Operations.Count));
        }

        var root = FirstFolder(ctx);
        if (root is null) return ToolPlan.Empty(ToolText.T("CoreToolNeedFolder", "Thêm một thư mục để làm việc."));

        switch (mode)
        {
            case "empty":
            {
                var plan = FolderToolsPlanner.RemoveEmpty(root, new RemoveEmptyOptions(ctx.Bool("junk", true)), ctx.Fs);
                var dirs = plan.Operations.Count(o => o.Kind == OperationKind.RemoveDir);
                return FromFolderPlan(plan, ToolText.T("CoreToolEmptySummary", "{0} thư mục rỗng sẽ được dọn (tệp rác vào khu cách ly).", dirs));
            }
            case "tree":
            {
                var plan = FolderToolsPlanner.BuildTree(root, ctx.Text("treeText"), ctx.Fs);
                return FromFolderPlan(plan, ToolText.T("CoreToolTreeSummary", "{0} thư mục sẽ được tạo.", plan.Operations.Count));
            }
            default:
            {
                var plan = FolderToolsPlanner.Flatten(root, new FlattenOptions(ctx.Bool("prefixPath"), "_", ctx.Bool("removeEmptied", true)), ctx.Fs);
                var moves = plan.Operations.Count(o => o.Kind == OperationKind.Move);
                return FromFolderPlan(plan, ToolText.T("CoreToolFlattenSummary", "{0} tệp sẽ được đưa lên thư mục gốc.", moves));
            }
        }
    }

    private static ToolPlan FromFolderPlan(FolderPlan plan, string summary)
    {
        var rows = plan.Operations.Select(o => new ToolRow(
            o.Kind is OperationKind.Move or OperationKind.Copy ? ToolText.Name(o.From) : o.From,
            o.Kind switch
            {
                OperationKind.Move or OperationKind.Copy => o.To,
                OperationKind.RemoveDir => ToolText.T("CoreToolRemoveDir", "Xoá thư mục rỗng"),
                OperationKind.Quarantine => ToolText.T("CoreToolToQuarantine", "Vào khu cách ly"),
                OperationKind.CreateDir => ToolText.T("CoreToolCreateDir", "Tạo thư mục"),
                _ => o.Kind.ToString()
            },
            o.Kind == OperationKind.Quarantine ? ToolRowState.Warning : ToolRowState.Ok)).ToList();

        rows.AddRange(plan.Skipped.Select(s => new ToolRow(s.Path, s.Reason, ToolRowState.Info)));

        return new ToolPlan { Rows = rows, Operations = plan.Operations, Summary = summary };
    }
}

/// <summary>Ngày giờ, thuộc tính và xoá GPS (M4). Xoá GPS luôn miễn phí vì là quyền riêng tư.</summary>
public sealed class MetadataTool : ITool
{
    public string Key => "metadata";
    public string TitleKey => "ToolMetadataTitle";
    public string DescriptionKey => "ToolMetadataDesc";
    public string Icon => "";
    public bool ReadOnly => false;
    public bool AcceptsFiles => true;

    public IReadOnlyList<ToolOption> Options { get; } = new ToolOption[]
    {
        new("mode", "ToolMetadataMode", OptionKind.Choice, "gps", new (string, string)[]
        {
            ("gps", "ToolMetadataGps"), ("timestamps", "ToolMetadataTimestamps"), ("attributes", "ToolMetadataAttributes")
        }, HintKey: "ToolMetadataGpsHint"),
        new("source", "ToolTimeSource", OptionKind.Choice, "exif", new (string, string)[]
        {
            ("exif", "ToolTimeExif"), ("filename", "ToolTimeFileName"), ("shift", "ToolTimeShift"), ("fixed", "ToolTimeFixed"), ("copy", "ToolTimeCopy")
        }, VisibleWhen: "mode=timestamps"),
        new("setCreated", "ToolTimeSetCreated", OptionKind.Toggle, "false", VisibleWhen: "mode=timestamps"),
        new("setModified", "ToolTimeSetModified", OptionKind.Toggle, "true", VisibleWhen: "mode=timestamps"),
        new("setAccessed", "ToolTimeSetAccessed", OptionKind.Toggle, "false", VisibleWhen: "mode=timestamps"),
        new("fixedValue", "ToolTimeFixedValue", OptionKind.Text, "", VisibleWhen: "mode=timestamps", HintKey: "ToolTimeFixedHint"),
        new("offset", "ToolTimeOffset", OptionKind.Text, "", VisibleWhen: "mode=timestamps", HintKey: "ToolTimeOffsetHint"),
        new("pattern", "ToolTimePattern", OptionKind.Text, "", VisibleWhen: "mode=timestamps", HintKey: "ToolTimePatternHint"),
        new("copyFrom", "ToolTimeCopyFrom", OptionKind.File, "", VisibleWhen: "mode=timestamps"),
        new("readonly", "ToolAttrReadOnly", OptionKind.Choice, "keep", AttrChoices(), VisibleWhen: "mode=attributes"),
        new("hidden", "ToolAttrHidden", OptionKind.Choice, "keep", AttrChoices(), VisibleWhen: "mode=attributes"),
        new("archive", "ToolAttrArchive", OptionKind.Choice, "keep", AttrChoices(), VisibleWhen: "mode=attributes"),
        new("recursive", "ToolRecursive", OptionKind.Toggle, "false")
    };

    private static (string, string)[] AttrChoices() => new[] { ("keep", "ToolAttrKeep"), ("set", "ToolAttrSet"), ("clear", "ToolAttrClear") };

    public Task<ToolPlan> PlanAsync(ToolContext ctx, CancellationToken ct, IProgress<string>? progress = null) =>
        Task.Run(() => Plan(ctx), ct);

    private ToolPlan Plan(ToolContext ctx)
    {
        var files = ctx.ScanFiles();
        if (files.Count == 0) return ToolPlan.Empty(ToolText.T("CoreToolNoFiles", "Chưa có tệp nào. Thêm tệp hoặc thư mục ở phía trên."));

        return ctx.Text("mode", "gps") switch
        {
            "timestamps" => Timestamps(ctx, files),
            "attributes" => Attributes(ctx, files),
            _ => Gps(ctx, files)
        };
    }

    private static ToolPlan Gps(ToolContext ctx, IReadOnlyList<FileItem> files)
    {
        var plan = GpsStripPlanner.Plan(files, ctx.Fs, ctx.TempRoot);
        var rows = new List<ToolRow>();
        var messages = new List<string>();

        foreach (var item in plan.Items)
        {
            var (text, state) = item.Status switch
            {
                GpsStripStatus.Ready => (ToolText.T("CoreToolGpsReady", "Có GPS: sẽ xoá"), item.XmpGpsRemains ? ToolRowState.Warning : ToolRowState.Ok),
                GpsStripStatus.NoGps => (ToolText.T("CoreToolGpsNone", "Không có GPS"), ToolRowState.Info),
                GpsStripStatus.Unsupported => (ToolText.T("CoreToolGpsUnsupported", "Không hỗ trợ (chỉ JPEG)"), ToolRowState.Info),
                _ => (item.Message ?? item.Status.ToString(), ToolRowState.Error)
            };
            rows.Add(new ToolRow(ToolText.Name(item.File.FullPath), text, state,
                item.XmpGpsRemains ? ToolText.T("CoreToolGpsXmp", "Còn toạ độ trong XMP: không xoá được mà không mã hoá lại ảnh") : null));
        }

        if (plan.Items.Any(i => i.XmpGpsRemains))
            messages.Add(ToolText.T("CoreToolGpsXmp", "Còn toạ độ trong XMP: không xoá được mà không mã hoá lại ảnh"));

        return new ToolPlan
        {
            Rows = rows,
            Operations = plan.Operations,
            Messages = messages,
            Cleanup = () => CleanTemp(ctx, plan.Operations),
            Summary = ToolText.T("CoreToolGpsSummary", "{0} ảnh sẽ được xoá GPS (giữ nguyên chất lượng ảnh).", plan.Operations.Count(o => o.Kind == OperationKind.WriteFile))
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

    private static ToolPlan Timestamps(ToolContext ctx, IReadOnlyList<FileItem> files)
    {
        var source = ctx.Text("source", "exif") switch
        {
            "filename" => TimestampSource.FileName,
            "shift" => TimestampSource.Shift,
            "fixed" => TimestampSource.Fixed,
            "copy" => TimestampSource.CopyFromFile,
            _ => TimestampSource.Exif
        };

        if (source == TimestampSource.Exif && !ctx.License.Allows(ProFeature.ExifDate))
        {
            var m = FeatureMessages.For(ProFeature.ExifDate);
            return new ToolPlan { Blocked = true, Messages = new[] { m }, UpgradeFeature = ProFeature.ExifDate, Summary = m };
        }

        DateTime? fixedLocal = null;
        if (source == TimestampSource.Fixed)
        {
            if (!DateTime.TryParse(ctx.Text("fixedValue"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeLocal, out var parsed))
                return ToolPlan.Error(ToolText.T("CoreToolTimeBadValue", "Nhập ngày giờ hợp lệ, ví dụ 2024-05-12 10:30:00."));
            fixedLocal = parsed;
        }

        var offset = TimeSpan.Zero;
        if (ctx.Text("offset").Length > 0 && !TryParseOffset(ctx.Text("offset"), out offset))
            return ToolPlan.Error(ToolText.T("CoreToolTimeBadOffset", "Khoảng lệch không hợp lệ, ví dụ +2h, -30m, 1d."));

        var options = new TimestampOptions(source, ctx.Bool("setCreated"), ctx.Bool("setModified", true), ctx.Bool("setAccessed"),
            fixedLocal, offset, ctx.Text("pattern") is { Length: > 0 } p ? p : null, ctx.Text("copyFrom") is { Length: > 0 } c ? c : null);
        if (!options.SetCreated && !options.SetModified && !options.SetAccessed)
            return ToolPlan.Error(ToolText.T("CoreToolTimeNoField", "Chọn ít nhất một loại ngày để đặt."));

        var plan = TimestampPlanner.Plan(files, options, ctx.RuleContext(files), ctx.Fs);
        var rows = plan.Items.Select(i => new ToolRow(
            ToolText.Name(i.File.FullPath),
            i.Status switch
            {
                TimestampStatus.Ready => $"{Format(i.Before)} → {Format(i.After)}",
                TimestampStatus.Unchanged => ToolText.T("CoreToolUnchanged", "Không đổi"),
                _ => i.Message ?? i.Status.ToString()
            },
            i.Status switch
            {
                TimestampStatus.Ready => ToolRowState.Ok,
                TimestampStatus.Unchanged => ToolRowState.Info,
                TimestampStatus.NoData => ToolRowState.Warning,
                _ => ToolRowState.Error
            })).ToList();

        return new ToolPlan
        {
            Rows = rows,
            Operations = plan.Operations,
            Summary = ToolText.T("CoreToolTimeSummary", "{0} tệp sẽ được đổi ngày giờ.", plan.Operations.Count)
        };

        static string Format(FileTimes? t) => t is { } v ? v.ModifiedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") : "—";
    }

    public static bool TryParseOffset(string text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var t = text.Trim();
        if (t.Length < 2) return false;

        var sign = t[0] == '-' ? -1 : 1;
        var body = t[0] is '+' or '-' ? t[1..] : t;
        var unit = char.ToLowerInvariant(body[^1]);
        if (!double.TryParse(body[..^1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n)) return false;

        switch (unit)
        {
            case 's': value = TimeSpan.FromSeconds(n * sign); return true;
            case 'm': value = TimeSpan.FromMinutes(n * sign); return true;
            case 'h': value = TimeSpan.FromHours(n * sign); return true;
            case 'd': value = TimeSpan.FromDays(n * sign); return true;
            default: return false;
        }
    }

    private static ToolPlan Attributes(ToolContext ctx, IReadOnlyList<FileItem> files)
    {
        FileAttributes set = 0, clear = 0;
        void Apply(string key, FileAttributes flag)
        {
            switch (ctx.Text(key, "keep"))
            {
                case "set": set |= flag; break;
                case "clear": clear |= flag; break;
            }
        }
        Apply("readonly", FileAttributes.ReadOnly);
        Apply("hidden", FileAttributes.Hidden);
        Apply("archive", FileAttributes.Archive);

        if (set == 0 && clear == 0) return ToolPlan.Empty(ToolText.T("CoreToolAttrNone", "Chọn thuộc tính cần đặt hoặc bỏ."));

        var plan = AttributePlanner.Plan(files, set, clear, ctx.Fs);
        var rows = plan.Operations.Select(o => new ToolRow(ToolText.Name(o.From), (o.AttributesAfter ?? 0).ToString())).ToList();
        rows.AddRange(plan.Skipped.Select(s => new ToolRow(s.Path, s.Reason, ToolRowState.Info)));
        return new ToolPlan
        {
            Rows = rows,
            Operations = plan.Operations,
            Summary = ToolText.T("CoreToolAttrSummary", "{0} tệp sẽ được đổi thuộc tính.", plan.Operations.Count)
        };
    }
}
