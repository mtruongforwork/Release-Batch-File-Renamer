namespace BFR.Core;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Kết hợp RulePipeline, NameValidator và ConflictDetector để tạo preview hoàn chỉnh
/// với trạng thái từng dòng và cờ chặn áp dụng (FR-C1..C3).
/// </summary>
public sealed class PreviewEngine
{
    private readonly RulePipeline _pipeline;
    private readonly NameValidator _validator;
    private readonly ConflictDetector _detector;
    private readonly IMetadataProvider? _metadataProvider;

    /// <summary>Tự thêm hậu tố " (2)"… khi tên mới trùng (B3). Mặc định tắt.</summary>
    public bool AutoResolveConflicts { get; init; }

    /// <summary>Số tệp đã được tự đổi tên khác ở lượt Run gần nhất.</summary>
    public int AutoResolvedCount { get; private set; }

    public PreviewEngine(
        RulePipeline pipeline,
        NameValidator? validator = null,
        ConflictDetector? detector = null,
        IMetadataProvider? metadataProvider = null)
    {
        _pipeline = pipeline;
        _validator = validator ?? new NameValidator();
        _detector = detector ?? new ConflictDetector();
        _metadataProvider = metadataProvider;
    }

    /// <summary>
    /// outsideBatchNames: tên các tệp đang tồn tại trong thư mục nhưng không thuộc lô đang đổi.
    /// </summary>
    public PreviewResult Run(
        IReadOnlyList<FileItem> files,
        RuleContext ctx,
        IReadOnlySet<string>? outsideBatchNames = null,
        System.Threading.CancellationToken ct = default,
        IReadOnlyDictionary<string, IReadOnlySet<string>>? outsideByDirectory = null)
    {
        // Chỉ đọc metadata khi chuỗi quy tắc thực sự cần (ví dụ quy tắc ngày EXIF).
        if (_metadataProvider is not null && MetadataRequirement.NeedsMetadata(_pipeline))
        {
            var map = new System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyDictionary<string, string>>(System.StringComparer.OrdinalIgnoreCase);
            System.Threading.Tasks.Parallel.ForEach(files, new System.Threading.Tasks.ParallelOptions { CancellationToken = ct }, f =>
            {
                var metadata = _metadataProvider.Read(f);
                if (metadata.Count > 0) map[f.FullPath] = metadata;
            });
            ctx = ctx with { FileMetadata = map };
        }

        var raw = _pipeline.Run(files, ctx, ct);
        AutoResolvedCount = 0;
        if (AutoResolveConflicts)
        {
            var resolved = ConflictResolver.Resolve(raw.Items, item =>
                outsideByDirectory is not null
                    ? (outsideByDirectory.TryGetValue(ConflictDetector.NormalizeDirectory(item.File.Directory), out var names) ? names : null)
                    : outsideBatchNames);
            AutoResolvedCount = resolved.ResolvedCount;
            raw = raw with { Items = resolved.Items };
        }
        var conflicts = outsideByDirectory is not null
            ? _detector.Detect(raw.Items, outsideByDirectory)
            : _detector.Detect(raw.Items, outsideBatchNames ?? new HashSet<string>(System.StringComparer.OrdinalIgnoreCase));

        var items = new List<PreviewItem>(raw.Items.Count);
        for (int i = 0; i < raw.Items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var item = raw.Items[i];
            var status = PreviewStatus.Ok;
            string? detail = null;

            if (string.Equals(item.NewName, item.OldName, System.StringComparison.Ordinal))
            {
                status = PreviewStatus.Unchanged;
            }

            var issue = _validator.Validate(item.NewName);
            if (issue == NameIssue.None)
            {
                // Kiểm tra cả đường dẫn đầy đủ, không chỉ tên tệp (QA-13).
                var candidatePath = System.IO.Path.Combine(item.File.Directory, item.NewName);
                if (candidatePath.Length > 260) issue = NameIssue.PathTooLong;
            }
            if (issue != NameIssue.None)
            {
                status = issue == NameIssue.PathTooLong ? PreviewStatus.PathTooLong : PreviewStatus.InvalidName;
                detail = CoreText.Get("CoreInvalidName", "Tên không hợp lệ: {0}", issue);
            }

            var conflict = conflicts[i];
            if (conflict is not null)
            {
                status = conflict.Type == ConflictType.ReservedName
                    ? PreviewStatus.InvalidName
                    : PreviewStatus.Conflict;
                detail = conflict.Message ?? conflict.Type.ToString();
            }

            // Vùng hệ thống được bảo vệ (C10): ghi đè mọi trạng thái cần thay đổi để không bao giờ áp dụng.
            if (status != PreviewStatus.Unchanged && ProtectedZones.Default.TryGetReason(item.File.FullPath, out _))
            {
                status = PreviewStatus.Protected;
                detail = CoreText.Get("CoreProtectedZone", "Nằm trong vùng hệ thống được bảo vệ");
            }

            items.Add(item with { Status = status, Detail = detail });
        }

        var hasBlocking = items.Any(i =>
            i.Status is PreviewStatus.Conflict or PreviewStatus.InvalidName or PreviewStatus.PathTooLong or PreviewStatus.Protected);

        return new PreviewResult(items, hasBlocking);
    }
}
