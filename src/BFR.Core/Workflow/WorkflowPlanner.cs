namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

public sealed record WorkflowStepResult(
    int Index,
    string Kind,
    IReadOnlyList<Operation> Operations,
    IReadOnlyList<string> Issues,
    int FilesIn,
    int FilesOut);

public sealed record WorkflowPlan(
    IReadOnlyList<WorkflowStepResult> Steps,
    IReadOnlyList<Operation> Operations,
    IReadOnlyList<FileItem> FinalFiles,
    IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Lập kế hoạch cho cả chuỗi bước (M10, Pro). Mỗi bước được lập trên trạng thái mô phỏng sau bước trước
/// (<see cref="OverlayFileSystem"/>) và được chạy thử bằng chính <see cref="OperationExecutor"/>; đĩa thật không bị chạm.
/// Kết quả là danh sách thao tác nối tiếp để thực thi bằng MỘT journal, nên hoàn tác một lần trả lại cả chuỗi.
/// Một bước có lỗi chặn (xung đột, tên sai, vùng cấm, thao tác thất bại) làm cả workflow không hợp lệ.
/// </summary>
public static class WorkflowPlanner
{
    public const int MaxSteps = 20;

    public static WorkflowPlan Plan(
        IReadOnlyList<FileItem> files,
        IReadOnlyList<WorkflowStep> steps,
        IFileSystem realFs,
        RuleContext context,
        IMetadataProvider? metadata = null,
        string? root = null,
        CancellationToken ct = default)
    {
        var errors = new List<string>();
        var results = new List<WorkflowStepResult>();
        var allOps = new List<Operation>();

        if (steps.Count == 0) errors.Add(CoreText.Get("CoreWorkflowEmpty", "Workflow chưa có bước nào"));
        if (steps.Count > MaxSteps) errors.Add(CoreText.Get("CoreWorkflowTooMany", "Workflow có quá nhiều bước (tối đa {0})", MaxSteps));
        if (errors.Count > 0) return new WorkflowPlan(results, allOps, files, errors);

        var overlay = new OverlayFileSystem(realFs);
        var current = files.Where(f => !f.IsDirectory).ToList();
        var workRoot = (root ?? CommonDirectory(current)).TrimEnd('\\', '/');

        // Metadata đọc MỘT lần theo đường dẫn gốc, rồi đổi khoá theo đường dẫn hiện tại khi tệp bị di chuyển.
        var meta = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        if (metadata is not null)
        {
            foreach (var f in current)
            {
                ct.ThrowIfCancellationRequested();
                var m = metadata.Read(f);
                if (m.Count > 0) meta[Canon(f.FullPath)] = m;
            }
        }

        for (var i = 0; i < steps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var step = steps[i];
            var issues = new List<string>();
            var ctx = context with { FileMetadata = meta };

            List<Operation> ops;
            try
            {
                ops = PlanStep(step, current, workRoot, overlay, ctx, issues);
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException or RegexMatchTimeoutException)
            {
                ops = new List<Operation>();
                issues.Add(ex.Message);
            }

            if (issues.Count == 0 && ops.Count > 0)
            {
                // Chạy thử trên lớp phủ bằng đúng bộ thực thi sẽ chạy thật.
                var result = new OperationExecutor(overlay, "/__bfr_wf_journal", new QuarantineStore(overlay, "/__bfr_wf_quarantine")).Execute(ops, "workflow", ct);
                if (result.Failed > 0)
                {
                    issues.Add(result.Errors.Count > 0 ? result.Errors[0].Error : CoreText.Get("CoreWorkflowStepFailed", "Bước thất bại khi chạy thử"));
                }
            }

            var before = current.Count;
            if (issues.Count == 0)
            {
                current = Advance(current, ops, overlay, meta);
                allOps.AddRange(ops);
            }

            results.Add(new WorkflowStepResult(i, step.Kind, ops, issues, before, current.Count));
            if (issues.Count > 0)
            {
                errors.Add(CoreText.Get("CoreWorkflowStepError", "Bước {0} ({1}): {2}", i + 1, step.Kind, issues[0]));
                break; // các bước sau phụ thuộc trạng thái của bước lỗi nên không lập tiếp
            }
        }

        return new WorkflowPlan(results, allOps, current, errors);
    }

    private static List<Operation> PlanStep(
        WorkflowStep step, IReadOnlyList<FileItem> files, string root, OverlayFileSystem fs, RuleContext ctx, List<string> issues)
    {
        switch (step)
        {
            case RenameStep rename:
            {
                var pipeline = new PresetCompiler().Compile(rename.Preset);
                var engine = new PreviewEngine(pipeline) { AutoResolveConflicts = true };
                var outside = OutsideBatchIndex.Build(files, fs);
                var preview = engine.Run(files, ctx, outsideByDirectory: outside);
                foreach (var bad in preview.Items.Where(i => i.Status is PreviewStatus.Conflict or PreviewStatus.InvalidName or PreviewStatus.PathTooLong or PreviewStatus.Protected))
                {
                    issues.Add($"{bad.OldName}: {bad.Detail ?? bad.Status.ToString()}");
                }
                if (issues.Count > 0) return new List<Operation>();

                var pairs = preview.Items
                    .Where(i => i.Status == PreviewStatus.Ok && !string.Equals(i.NewName, i.OldName, StringComparison.Ordinal))
                    .Select(i => (From: i.File.FullPath, To: Join(i.File.Directory, i.NewName)))
                    .ToList();
                return pairs.Count == 0
                    ? new List<Operation>()
                    : new RenamePlanner().Plan(pairs).Select(Operation.FromStep).ToList();
            }

            case OrganizeStep organize:
            {
                var plan = OrganizePlanner.Plan(files, new OrganizeOptions(organize.Root ?? root, organize.Template, organize.Mode, organize.Conflict), ctx, fs);
                foreach (var e in plan.TemplateErrors) issues.Add(e);
                foreach (var bad in plan.Items.Where(i => i.Status is OrganizeStatus.Invalid or OrganizeStatus.Protected))
                {
                    issues.Add($"{bad.File.FullPath}: {bad.Message}");
                }
                return issues.Count > 0 ? new List<Operation>() : plan.Operations.ToList();
            }

            case WrapStep wrap:
                return Collect(FolderToolsPlanner.Wrap(files, new WrapOptions(wrap.GroupSameName), fs), issues);

            case FlattenStep flatten:
                return Collect(FolderToolsPlanner.Flatten(root, new FlattenOptions(flatten.PrefixPath, flatten.Separator, flatten.RemoveEmptiedFolders), fs), issues, blocking: false);

            case RemoveEmptyStep removeEmpty:
                return Collect(FolderToolsPlanner.RemoveEmpty(root, new RemoveEmptyOptions(removeEmpty.TreatJunkAsEmpty), fs), issues, blocking: false);

            case TimestampStep timestamps:
            {
                var plan = TimestampPlanner.Plan(files, timestamps.Options, ctx, fs);
                foreach (var bad in plan.Items.Where(i => i.Status is TimestampStatus.Protected or TimestampStatus.Error))
                {
                    issues.Add($"{bad.File.FullPath}: {bad.Message}");
                }
                return issues.Count > 0 ? new List<Operation>() : plan.Operations.ToList();
            }

            case AttributeStep attributes:
                return Collect(AttributePlanner.Plan(files, attributes.Set, attributes.Clear, fs), issues, blocking: false);

            default:
                issues.Add(CoreText.Get("CoreWorkflowUnknownStep", "Loại bước không được hỗ trợ"));
                return new List<Operation>();
        }
    }

    /// <summary>Lấy thao tác của một kế hoạch thư mục. <paramref name="blocking"/>: mục bị bỏ qua có làm bước lỗi hay không.</summary>
    private static List<Operation> Collect(FolderPlan plan, List<string> issues, bool blocking = true)
    {
        // Vùng cấm luôn chặn; các mục bỏ qua khác (ví dụ đã có tệp trùng tên) chỉ là thông tin, không làm hỏng cả chuỗi.
        foreach (var skip in plan.Skipped)
        {
            if (blocking && skip.Reason == CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ")) issues.Add($"{skip.Path}: {skip.Reason}");
        }
        return issues.Count > 0 ? new List<Operation>() : plan.Operations.ToList();
    }

    /// <summary>Tính danh sách tệp sau bước: theo dõi di chuyển/sao chép/cách ly và đọc lại thông tin từ lớp phủ.</summary>
    private static List<FileItem> Advance(
        List<FileItem> current, IReadOnlyList<Operation> ops, OverlayFileSystem fs,
        Dictionary<string, IReadOnlyDictionary<string, string>> meta)
    {
        var next = new List<FileItem>(current.Count);

        foreach (var file in current)
        {
            var path = file.FullPath;
            var alive = true;
            foreach (var op in ops)
            {
                if (!SamePath(op.From, path)) continue;
                switch (op.Kind)
                {
                    case OperationKind.Move:
                        path = op.To;
                        break;
                    case OperationKind.Copy:
                        path = op.To; // sau sao chép, các bước sau làm việc với bản sao
                        break;
                    case OperationKind.Quarantine:
                        alive = false;
                        break;
                }
                if (!alive) break;
            }

            var oldKey = Canon(file.FullPath);
            if (!alive)
            {
                meta.Remove(oldKey);
                continue;
            }

            if (!SamePath(path, file.FullPath) && meta.Remove(oldKey, out var m)) meta[Canon(path)] = m;
            next.Add(Rebase(file, path, fs));
        }

        return next;
    }

    private static FileItem Rebase(FileItem file, string path, IFileSystem fs)
    {
        var name = path.TrimEnd('\\', '/');
        var cut = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        var directory = cut <= 0 ? string.Empty : name[..cut];
        var fileName = cut < 0 ? name : name[(cut + 1)..];
        var dot = fileName.LastIndexOf('.');
        var stem = dot > 0 ? fileName[..dot] : fileName;
        var ext = dot > 0 ? fileName[(dot + 1)..] : string.Empty;

        var times = fs.GetTimes(path);
        return file with
        {
            FullPath = path,
            Directory = directory,
            Name = stem,
            Extension = ext,
            Size = fs.GetLength(path),
            CreatedUtc = times.CreatedUtc,
            ModifiedUtc = times.ModifiedUtc
        };
    }

    private static string CommonDirectory(IReadOnlyList<FileItem> files)
    {
        if (files.Count == 0) return string.Empty;
        var parts = files.Select(f => Canon(f.Directory).Split('/')).ToList();
        var common = parts[0];
        foreach (var p in parts.Skip(1))
        {
            var len = 0;
            while (len < common.Length && len < p.Length && string.Equals(common[len], p[len], StringComparison.OrdinalIgnoreCase)) len++;
            common = common[..len];
        }
        var joined = string.Join("/", common);
        if (joined.Length == 0) return files[0].Directory;
        return files[0].Directory.Contains('\\') ? joined.Replace('/', '\\') : joined;
    }

    private static string Join(string dir, string name)
    {
        var sep = dir.Contains('\\') || dir.Contains(':') ? '\\' : '/';
        return dir.TrimEnd('\\', '/') + sep + name;
    }

    private static string Canon(string p) => p.Replace('\\', '/').TrimEnd('/');

    private static bool SamePath(string a, string b) => string.Equals(Canon(a), Canon(b), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Đọc/ghi workflow dạng JSON, có giới hạn như preset nhập từ ngoài (C4).</summary>
public static class WorkflowSerializer
{
    public const int MaxJsonBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        MaxDepth = PresetValidator.MaxJsonDepth,
        PropertyNameCaseInsensitive = true
    };

    public static string Serialize(WorkflowDefinition definition) => JsonSerializer.Serialize(definition, Options);

    /// <summary>Đọc workflow từ JSON không đáng tin: chặn kích thước, độ sâu, số bước; kiểm tra từng preset đổi tên.</summary>
    public static WorkflowDefinition Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new JsonException(CoreText.Get("CoreWorkflowInvalid", "Workflow không hợp lệ"));
        if (json.Length > MaxJsonBytes) throw new JsonException(CoreText.Get("CoreWorkflowInvalid", "Workflow không hợp lệ"));

        var definition = JsonSerializer.Deserialize<WorkflowDefinition>(json, Options)
            ?? throw new JsonException(CoreText.Get("CoreWorkflowInvalid", "Workflow không hợp lệ"));

        if (definition.Steps is null || definition.Steps.Count == 0 || definition.Steps.Count > WorkflowPlanner.MaxSteps)
            throw new JsonException(CoreText.Get("CoreWorkflowTooMany", "Workflow có quá nhiều bước (tối đa {0})", WorkflowPlanner.MaxSteps));
        if ((definition.Name ?? string.Empty).Length > PresetValidator.MaxNameLength)
            throw new JsonException(CoreText.Get("CoreWorkflowInvalid", "Workflow không hợp lệ"));

        foreach (var step in definition.Steps)
        {
            switch (step)
            {
                case null:
                    throw new JsonException(CoreText.Get("CoreWorkflowInvalid", "Workflow không hợp lệ"));
                case RenameStep rename:
                    PresetValidator.Validate(rename.Preset);
                    break;
                case OrganizeStep organize when (organize.Template ?? string.Empty).Length > 1000:
                    throw new JsonException(CoreText.Get("CoreWorkflowInvalid", "Workflow không hợp lệ"));
            }
        }

        return definition;
    }
}
