namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Một preset "đổi tên nhanh" hiện trong menu chuột phải Explorer (B1).</summary>
public sealed record QuickPreset(string Key, string PresetName, string TitleVi, string TitleEn)
{
    /// <summary>Tiêu đề trong menu con Explorer theo ngôn ngữ giao diện.</summary>
    public string Title(bool vietnamese) => vietnamese ? TitleVi : TitleEn;
}

/// <summary>Danh sách preset nhanh; khoá ngắn được dùng trong dòng lệnh <c>--quick &lt;key&gt;</c>.</summary>
public static class QuickPresets
{
    public static IReadOnlyList<QuickPreset> All { get; } = new[]
    {
        new QuickPreset("number", "Đánh số theo thứ tự", "Đánh số theo thứ tự", "Number in order"),
        new QuickPreset("ascii", "Bỏ dấu tiếng Việt + gạch nối", "Bỏ dấu và chuẩn hoá tên", "Remove accents and slugify"),
        new QuickPreset("date", "Ảnh theo ngày chụp", "Đặt tên theo ngày", "Name by date"),
        new QuickPreset("clean", "Dọn dẹp tên tệp", "Dọn dẹp tên tệp", "Clean up names"),
        new QuickPreset("lower", "Chữ thường toàn bộ", "Chữ thường toàn bộ", "All lowercase")
    };

    public static bool TryGet(string? key, out QuickPreset quick, out Preset preset)
    {
        quick = null!;
        preset = null!;
        if (string.IsNullOrWhiteSpace(key)) return false;

        var found = All.FirstOrDefault(q => q.Key.Equals(key.Trim(), StringComparison.OrdinalIgnoreCase));
        if (found is null) return false;

        var builtIn = BuiltInPresets.All.FirstOrDefault(p => p.Name == found.PresetName);
        if (builtIn is null) return false;

        quick = found;
        preset = builtIn;
        return true;
    }
}

public enum QuickRenameStatus
{
    /// <summary>Đã đổi tên (có thể hoàn tác bằng journal).</summary>
    Done,
    /// <summary>Không có tệp nào cần đổi.</summary>
    NothingToChange,
    /// <summary>Có tên không hợp lệ nên KHÔNG đổi gì cả.</summary>
    Blocked,
    /// <summary>Lỗi khi thực thi (một phần có thể đã đổi; journal vẫn để hoàn tác).</summary>
    Failed
}

public sealed record QuickRenameResult(
    QuickRenameStatus Status,
    int Renamed,
    int Total,
    string? JournalPath,
    string? Message,
    int AutoResolved = 0);

/// <summary>
/// Đổi tên nhanh không giao diện (B1): biên dịch preset → xem trước (tự xử lý trùng tên) →
/// nếu còn tên không hợp lệ thì dừng, không đổi gì → ghi journal ghi-trước → thực thi.
/// Kết quả có <see cref="QuickRenameResult.JournalPath"/> để hoàn tác bằng <see cref="UndoService"/>.
/// </summary>
public sealed class QuickRenamer
{
    private readonly IFileSystem _fs;
    private readonly string _journalDir;
    private readonly HistoryStore? _history;
    private readonly Func<DateTime> _clock;

    public QuickRenamer(IFileSystem fs, string journalDir, HistoryStore? history = null, Func<DateTime>? clock = null)
    {
        _fs = fs;
        _journalDir = journalDir;
        _history = history;
        _clock = clock ?? (() => DateTime.Now);
    }

    /// <param name="files">Các tệp đã chọn (đã sắp xếp).</param>
    /// <param name="maxListRows">Hạn mức dòng danh sách theo giấy phép.</param>
    /// <param name="maxUndoEntries">Số mục lịch sử được giữ theo giấy phép.</param>
    public QuickRenameResult Run(
        IReadOnlyList<FileItem> files,
        Preset preset,
        int maxListRows = int.MaxValue,
        int maxUndoEntries = int.MaxValue,
        string appVersion = "")
    {
        if (files.Count == 0) return new QuickRenameResult(QuickRenameStatus.NothingToChange, 0, 0, null, null);

        var pipeline = new PresetCompiler().Compile(preset, maxListRows);
        var engine = new PreviewEngine(pipeline) { AutoResolveConflicts = true };
        var ctx = new RuleContext(_clock(), Guid.NewGuid().ToString("N"));
        var outside = OutsideBatchIndex.Build(files, _fs);

        var preview = engine.Run(files, ctx, outsideByDirectory: outside);

        if (preview.HasBlockingError)
        {
            var bad = preview.Items.First(i => i.Status is PreviewStatus.Conflict or PreviewStatus.InvalidName or PreviewStatus.PathTooLong or PreviewStatus.Protected);
            return new QuickRenameResult(QuickRenameStatus.Blocked, 0, files.Count, null,
                $"{bad.OldName}: {bad.Detail ?? bad.Status.ToString()}", engine.AutoResolvedCount);
        }

        var pairs = preview.Items
            .Where(i => i.Status == PreviewStatus.Ok && !string.Equals(i.NewName, i.OldName, StringComparison.Ordinal))
            .Select(i => (From: i.File.FullPath, To: System.IO.Path.Combine(i.File.Directory, i.NewName)))
            .ToList();

        if (pairs.Count == 0)
            return new QuickRenameResult(QuickRenameStatus.NothingToChange, 0, files.Count, null, null, engine.AutoResolvedCount);

        var steps = new RenamePlanner().Plan(pairs);
        var result = new RenameExecutor(_fs, _journalDir).Execute(steps, string.IsNullOrEmpty(appVersion) ? AppVersion.Current : appVersion);

        _history?.Add(new HistoryEntry
        {
            Id = result.JournalId,
            StartedUtc = DateTime.UtcNow,
            Preset = preset.Name,
            Total = result.TotalSteps,
            Succeeded = result.Succeeded,
            Failed = result.Failed,
            Status = result.Failed > 0 ? "partial" : "completed",
            JournalPath = result.JournalPath
        });
        _history?.PruneAndLimit(maxUndoEntries);

        return new QuickRenameResult(
            result.Failed > 0 ? QuickRenameStatus.Failed : QuickRenameStatus.Done,
            result.Succeeded,
            files.Count,
            result.JournalPath,
            result.Errors.Count > 0 ? result.Errors[0].Error : null,
            engine.AutoResolvedCount);
    }

    /// <summary>Hoàn tác một lần đổi tên nhanh theo journal. Trả về số bước đã hoàn tác.</summary>
    public int Undo(string journalPath) => new UndoService(_fs).UndoJournal(journalPath).Undone;
}
