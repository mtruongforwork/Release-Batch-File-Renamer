namespace BFR.Presentation.Tools;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BFR.Core;
using BFR.Core.Licensing;

public enum OptionKind
{
    /// <summary>Một dòng văn bản.</summary>
    Text,

    /// <summary>Nhiều dòng văn bản.</summary>
    MultiLine,

    /// <summary>Chọn một trong các giá trị.</summary>
    Choice,

    /// <summary>Bật/tắt (giá trị "true"/"false").</summary>
    Toggle,

    /// <summary>Số nguyên.</summary>
    Number,

    /// <summary>Chọn thư mục.</summary>
    Folder,

    /// <summary>Chọn tệp.</summary>
    File
}

/// <param name="Key">Khoá giá trị trong <see cref="ToolContext.Values"/>.</param>
/// <param name="LabelKey">Khoá chuỗi giao diện của nhãn.</param>
/// <param name="Choices">Với <see cref="OptionKind.Choice"/>: (giá trị, khoá chuỗi nhãn). Giá trị có thể là mẫu dùng trực tiếp.</param>
/// <param name="VisibleWhen">Điều kiện hiện, dạng <c>"khoá=giá trị"</c> hoặc <c>"khoá=a|b"</c>; null = luôn hiện.</param>
/// <param name="Filter">Với <see cref="OptionKind.File"/>: bộ lọc hộp thoại, ví dụ <c>"JSON|*.json"</c>.</param>
public sealed record ToolOption(
    string Key,
    string LabelKey,
    OptionKind Kind,
    string Default = "",
    IReadOnlyList<(string Value, string LabelKey)>? Choices = null,
    string? VisibleWhen = null,
    string? HintKey = null,
    string? Filter = null);

public enum ToolRowState
{
    Ok,
    Info,
    Warning,
    Error,
    /// <summary>Tiêu đề nhóm trong danh sách kết quả.</summary>
    Header
}

/// <param name="Tag">Dữ liệu riêng của công cụ (ví dụ chỉ số nhóm trùng) để xử lý khi người dùng bấm vào dòng.</param>
public sealed record ToolRow(string Primary, string Secondary, ToolRowState State = ToolRowState.Ok, string? Detail = null, object? Tag = null);

/// <summary>Văn bản có thể xuất ra tệp (báo cáo, tệp checksum).</summary>
public sealed record ToolExport(string SuggestedName, string FileFilter, string Content, ProFeature? RequiresFeature = null);

/// <summary>
/// Kết quả lập kế hoạch của một công cụ: danh sách hiển thị, các thao tác sẽ thực thi, cảnh báo và giới hạn Free.
/// </summary>
public sealed class ToolPlan
{
    public IReadOnlyList<ToolRow> Rows { get; init; } = Array.Empty<ToolRow>();
    public IReadOnlyList<Operation> Operations { get; init; } = Array.Empty<Operation>();

    /// <summary>Thông báo hiển thị phía trên danh sách (cảnh báo, lý do bị chặn).</summary>
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();

    public string Summary { get; init; } = string.Empty;

    /// <summary>Có lỗi chặn: không được áp dụng.</summary>
    public bool Blocked { get; init; }

    /// <summary>Tính năng Pro cần để dùng đầy đủ; giao diện hiện nút nâng cấp khi khác null.</summary>
    public ProFeature? UpgradeFeature { get; init; }

    public ToolExport? Export { get; init; }

    /// <summary>Dọn tệp tạm khi kế hoạch bị bỏ (huỷ, lập lại) hoặc sau khi áp dụng.</summary>
    public Action? Cleanup { get; init; }

    public bool CanApply => !Blocked && Operations.Count > 0;

    public static ToolPlan Empty(string summary) => new() { Summary = summary };

    public static ToolPlan Error(string message) => new() { Blocked = true, Messages = new[] { message }, Summary = message };
}

/// <summary>Giá trị tuỳ chọn người dùng nhập + các dịch vụ mà công cụ cần.</summary>
public sealed class ToolContext
{
    public ToolContext(
        IReadOnlyList<string> paths, IReadOnlyDictionary<string, string> values, IFileSystem fs, LicenseService license,
        string tempRoot, IMetadataProvider? metadata = null, IImageProcessor? images = null, Func<DateTime>? clock = null)
    {
        Paths = paths;
        Values = values;
        Fs = fs;
        License = license;
        TempRoot = tempRoot;
        Metadata = metadata;
        Images = images;
        Clock = clock ?? (() => DateTime.Now);
    }

    public IReadOnlyList<string> Paths { get; }
    public IReadOnlyDictionary<string, string> Values { get; }
    public IFileSystem Fs { get; }
    public LicenseService License { get; }
    public string TempRoot { get; }
    public IMetadataProvider? Metadata { get; }
    public IImageProcessor? Images { get; }
    public Func<DateTime> Clock { get; }

    public string Text(string key, string fallback = "") =>
        Values.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    public bool Bool(string key, bool fallback = false) =>
        Values.TryGetValue(key, out var v) ? string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) : fallback;

    public int Int(string key, int fallback = 0) =>
        Values.TryGetValue(key, out var v) && int.TryParse(v, out var n) ? n : fallback;

    /// <summary>Quét đường dẫn người dùng thêm thành danh sách tệp (thư mục được mở rộng theo tuỳ chọn "recursive").</summary>
    public IReadOnlyList<FileItem> ScanFiles(bool defaultRecursive = false) =>
        new FileScanner().Scan(Paths, new ScanOptions
        {
            Recursive = Bool("recursive", defaultRecursive),
            MaxDepth = Bool("recursive", defaultRecursive) ? License.MaxRecursionDepth : 0
        });

    /// <summary>Metadata của từng tệp (chỉ đọc khi có nhà cung cấp, và đã cấp quyền Pro cho {taken}).</summary>
    public RuleContext RuleContext(IReadOnlyList<FileItem> files)
    {
        var ctx = new RuleContext(Clock(), Guid.NewGuid().ToString("N"));
        if (Metadata is null || !License.Allows(ProFeature.ExifDate)) return ctx;

        var map = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in files)
        {
            var m = Metadata.Read(f);
            if (m.Count > 0) map[f.FullPath] = m;
        }
        return ctx with { FileMetadata = map };
    }
}

/// <summary>Một công cụ trong khung công cụ dùng chung: khai báo tuỳ chọn, lập kế hoạch, thực thi qua <see cref="ToolRunner"/>.</summary>
public interface ITool
{
    string Key { get; }

    /// <summary>Khoá chuỗi tiêu đề và mô tả.</summary>
    string TitleKey { get; }
    string DescriptionKey { get; }

    /// <summary>Mã glyph biểu tượng (Segoe MDL2).</summary>
    string Icon { get; }

    /// <summary>Công cụ chỉ đọc (checksum, phân tích): không có nút Áp dụng.</summary>
    bool ReadOnly { get; }

    /// <summary>Công cụ nhận tệp (true) hay thư mục (false) làm đầu vào chính.</summary>
    bool AcceptsFiles { get; }

    IReadOnlyList<ToolOption> Options { get; }

    Task<ToolPlan> PlanAsync(ToolContext context, CancellationToken ct, IProgress<string>? progress = null);

    /// <summary>Người dùng bấm vào một dòng (ví dụ chọn bản giữ lại). Trả về kế hoạch mới hoặc null nếu không có gì đổi.</summary>
    ToolPlan? OnRowActivated(ToolRow row) => null;
}

public sealed record ToolRunResult(string JournalId, string JournalPath, int Total, int Succeeded, int Failed, bool Cancelled, IReadOnlyList<(string Step, string Error)> Errors);

/// <summary>Thực thi một <see cref="ToolPlan"/> bằng MỘT journal, ghi lịch sử và cho phép hoàn tác.</summary>
public sealed class ToolRunner
{
    private readonly IFileSystem _fs;
    private readonly string _journalDir;
    private readonly HistoryStore? _history;
    private readonly string _version;
    private readonly string? _quarantineRoot;

    public ToolRunner(IFileSystem fs, string journalDir, HistoryStore? history, string version, string? quarantineRoot = null)
    {
        _fs = fs;
        _journalDir = journalDir;
        _history = history;
        _version = version;
        _quarantineRoot = quarantineRoot;
    }

    public ToolRunResult Execute(ToolPlan plan, string label, int maxUndoEntries = int.MaxValue, CancellationToken ct = default, IProgress<int>? progress = null)
    {
        if (!plan.CanApply) throw new InvalidOperationException("Kế hoạch không áp dụng được.");

        try
        {
            var result = new OperationExecutor(_fs, _journalDir, new QuarantineStore(_fs, _quarantineRoot)).Execute(plan.Operations, _version, ct, progress);

            _history?.Add(new HistoryEntry
            {
                Id = result.JournalId,
                StartedUtc = DateTime.UtcNow,
                Preset = label,
                Total = result.TotalSteps,
                Succeeded = result.Succeeded,
                Failed = result.Failed,
                Status = result.Cancelled ? "cancelled" : result.Failed > 0 ? "partial" : "completed",
                JournalPath = result.JournalPath
            });
            _history?.PruneAndLimit(maxUndoEntries);

            return new ToolRunResult(result.JournalId, result.JournalPath, result.TotalSteps, result.Succeeded, result.Failed, result.Cancelled, result.Errors);
        }
        finally
        {
            plan.Cleanup?.Invoke();
        }
    }

    public UndoResult Undo(string journalPath) => new UndoService(_fs).UndoJournal(journalPath);
}

/// <summary>Tiện ích dùng chung cho các công cụ.</summary>
internal static class ToolText
{
    public static string T(string key, string fallback, params object[] args) => CoreText.Get(key, fallback, args);

    public static string Size(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{v:0.#} {units[i]}";
    }

    public static string Name(string path)
    {
        var t = path.TrimEnd('\\', '/');
        var i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
        return i < 0 ? t : t[(i + 1)..];
    }

    /// <summary>Đường dẫn tương đối so với thư mục gốc để danh sách ngắn gọn.</summary>
    public static string Relative(string root, string path)
    {
        var r = root.Replace('\\', '/').TrimEnd('/') + "/";
        var p = path.Replace('\\', '/');
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? p[r.Length..] : path;
    }

    public static string CommonDirectory(IReadOnlyList<FileItem> files)
    {
        if (files.Count == 0) return string.Empty;
        var parts = files.Select(f => f.Directory.Replace('\\', '/').TrimEnd('/').Split('/')).ToList();
        var common = parts[0];
        foreach (var p in parts.Skip(1))
        {
            var len = 0;
            while (len < common.Length && len < p.Length && string.Equals(common[len], p[len], StringComparison.OrdinalIgnoreCase)) len++;
            common = common[..len];
        }
        var joined = string.Join(Path.DirectorySeparatorChar, common);
        return joined.Length == 0 ? files[0].Directory : joined;
    }
}
