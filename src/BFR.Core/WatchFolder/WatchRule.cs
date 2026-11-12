namespace BFR.Core.WatchFolder;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Một quy tắc theo dõi (B6): thư mục nào, lọc tệp nào, chạy workflow nào cho tệp mới.
/// Đổi tên thuần tuý là workflow một bước <see cref="RenameStep"/>.
/// </summary>
public sealed class WatchRule
{
    public const int MaxRules = 10;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string Folder { get; set; } = string.Empty;
    public bool Recursive { get; set; } = true;
    public string? IncludeGlob { get; set; }
    public string? ExcludeGlob { get; set; }
    public int SettleDelayMs { get; set; } = 1500;
    public int MaxFilesPerBatch { get; set; } = 500;
    public bool DryRun { get; set; }
    public List<WorkflowStep> Steps { get; set; } = new();

    /// <summary>Lỗi cấu hình (rỗng = hợp lệ). Không chạm đĩa.</summary>
    public IReadOnlyList<string> Validate(ProtectedZones? zones = null)
    {
        zones ??= ProtectedZones.Default;
        var errors = new List<string>();

        if (Name.Length > PresetValidator.MaxNameLength) errors.Add(CoreText.Get("CoreWatchRuleName", "Tên quy tắc quá dài"));
        if (!PathGuard.TryNormalize(Folder, out var normalized)) errors.Add(CoreText.Get("CoreWatchRuleFolder", "Thư mục theo dõi không hợp lệ"));
        else if (zones.IsProtected(normalized)) errors.Add(CoreText.Get("CoreProtectedZone", "Vùng hệ thống được bảo vệ"));

        if (Steps.Count == 0 || Steps.Count > WorkflowPlanner.MaxSteps) errors.Add(CoreText.Get("CoreWorkflowEmpty", "Workflow chưa có bước nào"));
        foreach (var step in Steps)
        {
            if (step is null) errors.Add(CoreText.Get("CoreWorkflowInvalid", "Workflow không hợp lệ"));
            else if (step is RenameStep r) { try { PresetValidator.Validate(r.Preset); } catch (Exception ex) when (ex is InvalidDataException or JsonException or ArgumentException) { errors.Add(ex.Message); } }
        }

        if (SettleDelayMs is < 500 or > 600_000) errors.Add(CoreText.Get("CoreWatchRuleSettle", "Thời gian chờ ổn định phải từ 0,5 đến 600 giây"));
        if (MaxFilesPerBatch is < 1 or > 5000) errors.Add(CoreText.Get("CoreWatchRuleBatch", "Số tệp mỗi lô phải từ 1 đến 5000"));
        if (IncludeGlob?.Length > 500 || ExcludeGlob?.Length > 500) errors.Add(CoreText.Get("CoreWatchRuleGlob", "Mẫu lọc quá dài"));

        return errors;
    }

    public WatchFolderOptions ToOptions() => new()
    {
        Folder = Folder,
        Recursive = Recursive,
        IncludeGlob = IncludeGlob,
        ExcludeGlob = ExcludeGlob,
        SettleDelayMs = SettleDelayMs,
        MaxFilesPerBatch = MaxFilesPerBatch,
        DryRun = DryRun
    };
}

/// <summary>Lưu danh sách quy tắc theo dõi dạng JSON, có giới hạn khi đọc (C4).</summary>
public sealed class WatchRuleStore
{
    private const int MaxBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        MaxDepth = PresetValidator.MaxJsonDepth,
        PropertyNameCaseInsensitive = true
    };

    private readonly IFileSystem _fs;
    private readonly string _path;

    public WatchRuleStore(IFileSystem fs, string path)
    {
        _fs = fs;
        _path = path;
    }

    public List<WatchRule> Load()
    {
        try
        {
            if (!_fs.FileExists(_path)) return new List<WatchRule>();
            var json = _fs.ReadAllText(_path);
            if (json.Length > MaxBytes) return new List<WatchRule>();

            var rules = JsonSerializer.Deserialize<List<WatchRule>>(json, Options) ?? new List<WatchRule>();
            // Quy tắc hỏng hoặc không an toàn bị bỏ, không làm hỏng các quy tắc còn lại.
            return rules.Take(WatchRule.MaxRules).Where(r => r is not null && r.Validate().Count == 0).ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new List<WatchRule>();
        }
    }

    public void Save(IEnumerable<WatchRule> rules)
    {
        var json = JsonSerializer.Serialize(rules.Take(WatchRule.MaxRules).ToList(), Options);
        _fs.WriteAllText(_path, json);
    }
}
