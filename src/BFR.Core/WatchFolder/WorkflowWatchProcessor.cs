namespace BFR.Core.WatchFolder;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

/// <summary>
/// Xử lý một lô tệp mới của Watch Folder bằng cả workflow: lập kế hoạch trên trạng thái mô phỏng, rồi thực thi
/// bằng MỘT journal (hoàn tác được cả lô). Lô có bước lỗi bị bỏ nguyên vẹn, không thực thi dở chừng.
/// </summary>
public sealed class WorkflowWatchProcessor
{
    private readonly IFileSystem _fs;
    private readonly string _journalDir;
    private readonly Func<DateTime> _clock;
    private readonly IMetadataProvider? _metadata;

    public WorkflowWatchProcessor(IFileSystem fs, string? journalDir = null, IMetadataProvider? metadata = null, Func<DateTime>? clock = null)
    {
        _fs = fs;
        _metadata = metadata;
        _clock = clock ?? (() => DateTime.UtcNow);
        _journalDir = journalDir ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatchFileRenamer", "journal");
    }

    public WatchFolderResult Process(IReadOnlyList<string> paths, WatchRule rule, CancellationToken ct = default)
    {
        var result = new WatchFolderResult();
        var options = rule.ToOptions();

        var files = new List<FileItem>();
        var order = 0;
        foreach (var path in paths)
        {
            ct.ThrowIfCancellationRequested();
            if (!FileScanner.MatchesGlobs(path, options.IncludeGlob, options.ExcludeGlob)) continue;
            if (!_fs.FileExists(path)) continue;

            try { files.Add(FileScanner.FromFile(_fs, path, order++)); }
            catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { }
        }

        result.Candidates = files.Count;
        if (files.Count == 0) return result;

        var plan = WorkflowPlanner.Plan(files, rule.Steps, _fs, new RuleContext(_clock(), Guid.NewGuid().ToString("N")), _metadata, rule.Folder, ct);
        if (!plan.IsValid)
        {
            result.Skipped = files.Count;
            foreach (var error in plan.Errors) result.Errors.Add((rule.Folder, error));
            return result;
        }

        if (plan.Operations.Count == 0)
        {
            result.Unchanged = files.Count;
            return result;
        }

        if (options.DryRun) return result;

        var execution = new OperationExecutor(_fs, _journalDir).Execute(plan.Operations, AppVersion.Current, ct);

        result.JournalId = execution.JournalId;
        result.JournalPath = execution.JournalPath;
        result.Renamed = execution.Succeeded;
        result.Failed = execution.Failed;
        // Mọi đường dẫn cuối cùng (kể cả bản sao) là "do chính ta tạo ra": không xử lý lại.
        result.RenamedPaths.AddRange(plan.FinalFiles.Select(f => f.FullPath));
        foreach (var (step, error) in execution.Errors) result.Errors.Add((step, error));

        return result;
    }
}
