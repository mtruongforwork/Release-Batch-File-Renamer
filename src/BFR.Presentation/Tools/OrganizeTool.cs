namespace BFR.Presentation.Tools;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BFR.Core;
using BFR.Core.Licensing;

/// <summary>Sắp xếp tệp vào thư mục dựng theo mẫu token (M2).</summary>
public sealed class OrganizeTool : ITool
{
    public string Key => "organize";
    public string TitleKey => "ToolOrganizeTitle";
    public string DescriptionKey => "ToolOrganizeDesc";
    public string Icon => "";
    public bool ReadOnly => false;
    public bool AcceptsFiles => true;

    public IReadOnlyList<ToolOption> Options { get; } = new ToolOption[]
    {
        new("template", "ToolOrganizeTemplate", OptionKind.Choice, "{type}", new (string, string)[]
        {
            ("{type}", "ToolOrganizeByType"),
            ("{year}\\{month}", "ToolOrganizeByDate"),
            ("{exif.year}\\{exif.month:00}", "ToolOrganizeByPhotoDate"),
            ("{artist}\\{album}", "ToolOrganizeByMusic"),
            ("{ext.upper}", "ToolOrganizeByExt"),
            ("{name.first:1}", "ToolOrganizeByInitial"),
            ("{size.bucket}", "ToolOrganizeBySize"),
            ("custom", "ToolOrganizeCustom")
        }),
        new("customTemplate", "ToolOrganizeCustomTemplate", OptionKind.Text, "{year}\\{month}", VisibleWhen: "template=custom", HintKey: "ToolOrganizeTokenHint"),
        new("destination", "ToolOrganizeDestination", OptionKind.Folder, "", HintKey: "ToolOrganizeDestinationHint"),
        new("mode", "ToolOrganizeMode", OptionKind.Choice, "move", new (string, string)[] { ("move", "ToolModeMove"), ("copy", "ToolModeCopy") }),
        new("conflict", "ToolOrganizeConflict", OptionKind.Choice, "rename", new (string, string)[] { ("rename", "ToolConflictRename"), ("skip", "ToolConflictSkip") }),
        new("recursive", "ToolRecursive", OptionKind.Toggle, "false")
    };

    public Task<ToolPlan> PlanAsync(ToolContext ctx, CancellationToken ct, IProgress<string>? progress = null) =>
        Task.Run(() => Plan(ctx, ct), ct);

    private static bool UsesMetadata(string template) =>
        template.Contains("exif.", StringComparison.OrdinalIgnoreCase)
        || template.Contains("{artist", StringComparison.OrdinalIgnoreCase)
        || template.Contains("{album", StringComparison.OrdinalIgnoreCase);

    private ToolPlan Plan(ToolContext ctx, CancellationToken ct)
    {
        var template = ctx.Text("template", "{type}");
        if (template == "custom") template = ctx.Text("customTemplate");

        var files = ctx.ScanFiles();
        if (files.Count == 0) return ToolPlan.Empty(ToolText.T("CoreToolNoFiles", "Chưa có tệp nào. Thêm tệp hoặc thư mục ở phía trên."));
        ct.ThrowIfCancellationRequested();

        var parsed = OrganizeTemplate.Parse(template);
        var maxDepth = ctx.License.MaxOrganizeDepth;
        if (parsed.Depth > maxDepth)
        {
            var message = FeatureMessages.For(ProFeature.OrganizeDeepTemplate);
            return new ToolPlan { Blocked = true, Messages = new[] { message }, UpgradeFeature = ProFeature.OrganizeDeepTemplate, Summary = message };
        }

        if (UsesMetadata(template) && !ctx.License.Allows(ProFeature.ExifDate))
        {
            var message = FeatureMessages.For(ProFeature.ExifDate);
            return new ToolPlan { Blocked = true, Messages = new[] { message }, UpgradeFeature = ProFeature.ExifDate, Summary = message };
        }

        var root = ctx.Text("destination", ToolText.CommonDirectory(files));
        var options = new OrganizeOptions(root, template,
            ctx.Text("mode", "move") == "copy" ? OrganizeMode.Copy : OrganizeMode.Move,
            ctx.Text("conflict", "rename") == "skip" ? OrganizeConflictPolicy.Skip : OrganizeConflictPolicy.Rename);

        var plan = OrganizePlanner.Plan(files, options, ctx.RuleContext(files), ctx.Fs);
        ct.ThrowIfCancellationRequested();

        var rows = plan.Items.Select(i => new ToolRow(
            ToolText.Name(i.File.FullPath),
            i.Status switch
            {
                OrganizeStatus.Ready or OrganizeStatus.Unchanged => ToolText.Relative(root, i.TargetPath),
                _ => i.Message ?? i.Status.ToString()
            },
            i.Status switch
            {
                OrganizeStatus.Ready => i.UsedFallbackFolder ? ToolRowState.Warning : ToolRowState.Ok,
                OrganizeStatus.Unchanged => ToolRowState.Info,
                OrganizeStatus.Skipped => ToolRowState.Info,
                _ => ToolRowState.Error
            },
            i.CrossVolume ? ToolText.T("CoreToolCrossVolume", "Khác ổ đĩa: sẽ sao chép rồi xoá (chậm hơn)") : null)).ToList();

        var messages = new List<string>(plan.TemplateErrors);
        if (plan.Items.Any(i => i.CrossVolume)) messages.Add(ToolText.T("CoreToolCrossVolume", "Khác ổ đĩa: sẽ sao chép rồi xoá (chậm hơn)"));
        var fallback = plan.Items.Count(i => i.UsedFallbackFolder);
        if (fallback > 0) messages.Add(ToolText.T("CoreToolOrganizeFallback", "{0} tệp không có dữ liệu cho mẫu, sẽ vào thư mục không xác định.", fallback));

        var folders = plan.FolderCounts().Count;
        return new ToolPlan
        {
            Rows = rows,
            Operations = plan.Operations,
            Messages = messages,
            Blocked = plan.HasBlockingIssues,
            Summary = ToolText.T("CoreToolOrganizeSummary", "{0} tệp sẽ được sắp xếp vào {1} thư mục.", plan.Operations.Count, folders)
        };
    }
}
