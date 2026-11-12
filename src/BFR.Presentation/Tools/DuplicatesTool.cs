namespace BFR.Presentation.Tools;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BFR.Core;
using BFR.Core.Licensing;

/// <summary>
/// Tìm tệp trùng và ảnh gần giống (M3). Không bao giờ tự xoá: bản thừa đi vào khu cách ly (hoàn tác được) hoặc sang thư mục khác;
/// mỗi nhóm luôn giữ lại ít nhất một tệp. Người dùng bấm vào một dòng để chọn bản giữ lại của nhóm đó.
/// </summary>
public sealed class DuplicatesTool : ITool
{
    private IReadOnlyList<DuplicateGroup> _groups = Array.Empty<DuplicateGroup>();
    private List<FileItem> _keepers = new();
    private ToolContext? _context;
    private int _truncatedFrom;

    public string Key => "duplicates";
    public string TitleKey => "ToolDuplicatesTitle";
    public string DescriptionKey => "ToolDuplicatesDesc";
    public string Icon => "";
    public bool ReadOnly => false;
    public bool AcceptsFiles => true;
    public string? QuickOptionKey => "keep";

    public IReadOnlyList<ToolOption> Options { get; } = new ToolOption[]
    {
        new("similar", "ToolDuplicatesSimilar", OptionKind.Toggle, "false", HintKey: "ToolDuplicatesSimilarHint"),
        new("threshold", "ToolDuplicatesThreshold", OptionKind.Number, "6", VisibleWhen: "similar=true"),
        new("keep", "ToolDuplicatesKeep", OptionKind.Choice, "oldest", new (string, string)[]
        {
            ("oldest", "ToolKeepOldest"), ("newest", "ToolKeepNewest"), ("shortest", "ToolKeepShortest"), ("clean", "ToolKeepClean")
        }),
        new("action", "ToolDuplicatesAction", OptionKind.Choice, "quarantine", new (string, string)[]
        {
            ("quarantine", "ToolActionQuarantine"), ("move", "ToolActionMove")
        }),
        new("moveTo", "ToolDuplicatesMoveTo", OptionKind.Folder, "", VisibleWhen: "action=move"),
        new("recursive", "ToolRecursive", OptionKind.Toggle, "true")
    };

    public async Task<ToolPlan> PlanAsync(ToolContext ctx, CancellationToken ct, IProgress<string>? progress = null)
    {
        _context = ctx;
        var files = ctx.ScanFiles(defaultRecursive: true).ToList();
        if (files.Count == 0) return ToolPlan.Empty(ToolText.T("CoreToolNoFiles", "Chưa có tệp nào. Thêm tệp hoặc thư mục ở phía trên."));

        var similar = ctx.Bool("similar");
        if (similar)
        {
            if (!ctx.License.Allows(ProFeature.SimilarImages))
                return new ToolPlan { Blocked = true, UpgradeFeature = ProFeature.SimilarImages, Messages = new[] { FeatureMessages.For(ProFeature.SimilarImages) }, Summary = FeatureMessages.For(ProFeature.SimilarImages) };
            if (ctx.Images is null)
                return ToolPlan.Error(ToolText.T("CoreToolNoImageEngine", "Tính năng này chỉ có trên Windows."));
        }

        // Giới hạn Free nói rõ, không âm thầm cắt bớt.
        var limit = ctx.License.MaxDuplicateFiles;
        _truncatedFrom = 0;
        ProFeature? upgrade = null;
        if (files.Count > limit)
        {
            _truncatedFrom = files.Count;
            files = files.Take(limit).ToList();
            upgrade = ProFeature.DuplicatesUnlimited;
        }

        progress?.Report(ToolText.T("CoreToolScanning", "Đang quét…"));
        _groups = await Task.Run(() => similar
            ? new SimilarImageFinder(ctx.Images!).Find(files, new SimilarImageOptions(Math.Clamp(ctx.Int("threshold", 6), 0, 16)), ct: ct)
            : new DuplicateFinder(ctx.Fs).Find(files, new DuplicateOptions(), ct), ct);

        var rule = ctx.Text("keep", "oldest") switch
        {
            "newest" => KeepRule.Newest,
            "shortest" => KeepRule.ShortestPath,
            "clean" => KeepRule.CleanName,
            _ => KeepRule.Oldest
        };
        _keepers = _groups.Select(g => DuplicatePlanner.Choose(g, rule)).ToList();

        return Build(upgrade);
    }

    public ToolPlan? OnRowActivated(ToolRow row)
    {
        if (row.Tag is not (int group, string path) || group < 0 || group >= _groups.Count) return null;
        var file = _groups[group].Files.FirstOrDefault(f => string.Equals(f.FullPath, path, StringComparison.OrdinalIgnoreCase));
        if (file is null) return null;

        _keepers[group] = file;
        return Build(_truncatedFrom > 0 ? ProFeature.DuplicatesUnlimited : null);
    }

    private ToolPlan Build(ProFeature? upgrade)
    {
        var ctx = _context!;
        var messages = new List<string>();
        if (_truncatedFrom > 0)
            messages.Add(ToolText.T("CoreToolDuplicatesLimit", "Đã xử lý {0}/{1} tệp. Nâng cấp Pro để quét hết.", ctx.License.MaxDuplicateFiles, _truncatedFrom));

        if (_groups.Count == 0)
            return new ToolPlan { Messages = messages, UpgradeFeature = upgrade, Summary = ToolText.T("CoreToolDuplicatesNone", "Không có tệp trùng.") };

        var action = ctx.Text("action", "quarantine") == "move" ? DuplicateAction.MoveToFolder : DuplicateAction.Quarantine;
        var moveTo = ctx.Text("moveTo");
        if (action == DuplicateAction.MoveToFolder && moveTo.Length == 0)
            messages.Add(ToolText.T("CoreToolDuplicatesNeedFolder", "Chọn thư mục để chuyển các bản thừa vào."));

        var plan = DuplicatePlanner.Plan(_groups, _keepers, action, ctx.Fs, moveTo.Length == 0 ? null : moveTo);
        foreach (var skip in plan.Skipped) messages.Add($"{skip.Path}: {skip.Reason}");

        var rows = new List<ToolRow>();
        for (var i = 0; i < _groups.Count; i++)
        {
            var g = _groups[i];
            rows.Add(new ToolRow(
                ToolText.T("CoreToolDuplicatesGroup", "Nhóm {0}: {1} tệp, {2}", i + 1, g.Files.Count, ToolText.Size(g.Size)),
                string.Empty, ToolRowState.Header));

            foreach (var f in g.Files)
            {
                var keep = string.Equals(f.FullPath, _keepers[i].FullPath, StringComparison.OrdinalIgnoreCase);
                rows.Add(new ToolRow(f.FullPath,
                    keep ? ToolText.T("CoreToolKeep", "Giữ lại") : (action == DuplicateAction.Quarantine ? ToolText.T("CoreToolToQuarantine", "Vào khu cách ly") : ToolText.T("CoreToolToFolder", "Chuyển đi")),
                    keep ? ToolRowState.Ok : ToolRowState.Warning,
                    Tag: (i, f.FullPath)));
            }
        }

        var blocked = action == DuplicateAction.MoveToFolder && moveTo.Length == 0;
        return new ToolPlan
        {
            Rows = rows,
            Operations = blocked ? Array.Empty<Operation>() : plan.Operations,
            Messages = messages,
            Blocked = blocked,
            UpgradeFeature = upgrade,
            Summary = ToolText.T("CoreToolDuplicatesSummary", "{0} nhóm trùng, {1} bản thừa, tiết kiệm {2}.", _groups.Count, plan.Operations.Count, ToolText.Size(plan.BytesFreed))
        };
    }
}
