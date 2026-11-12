namespace BFR.Core;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

/// <summary>
/// Khôi phục sau sự cố (PHẦN 7.5): phát hiện journal dở dang,
/// hoàn tất các bước pending hoặc khôi phục về ban đầu.
/// </summary>
public sealed class RecoveryCandidate
{
    public string JournalPath { get; init; } = string.Empty;
    public int PendingSteps { get; init; }
    public int DoneSteps { get; init; }
}

public sealed class RecoveryResult
{
    public bool Success { get; set; }
    public int Processed { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; } = new();
}

public sealed class RecoveryService
{
    private readonly IFileSystem _fs;
    public RecoveryService(IFileSystem fs) => _fs = fs;

    public IReadOnlyList<RecoveryCandidate> FindIncomplete(string journalDir)
    {
        var result = new List<RecoveryCandidate>();
        if (!_fs.DirectoryExists(journalDir)) return result;

        foreach (var path in _fs.GetFiles(journalDir, "*.jsonl", false))
        {
            var entries = UndoService.ReadSteps(path, _fs);
            if (entries.Any(e => e.Type == "footer")) continue; // đã kết thúc

            var steps = entries.Where(e => e.Type == "step" && e.Status == "pending" && e.From is not null && e.To is not null).ToList();
            var done = entries.Count(e => e.Type == "step" && e.Status == "done");
            result.Add(new RecoveryCandidate { JournalPath = path, PendingSteps = steps.Count, DoneSteps = done });
        }
        return result;
    }

    /// <summary>
    /// Chạy tiếp các bước pending sau khi kiểm tra trạng thái thực của hệ thống tệp.
    /// </summary>
    public RecoveryResult Complete(string journalPath)
    {
        var result = new RecoveryResult();
        var entries = UndoService.ReadSteps(journalPath, _fs);
        var doneSeqs = entries
            .Where(e => e.Type == "step" && e.Status == "done")
            .Select(e => e.Seq)
            .ToHashSet();
        var pending = entries
            .Where(e => e.Type == "step" && e.Status == "pending" && e.From is not null && (e.To is not null || !e.IsMoveLike)
                        && !doneSeqs.Contains(e.Seq))
            .OrderBy(e => e.Seq)
            .ToList();

        foreach (var step in pending)
        {
            if (!step.IsMoveLike)
            {
                CompleteOperation(journalPath, step, result);
                continue;
            }

            var from = step.From!;
            var to = step.To!;

            if (_fs.FileExists(to) && !_fs.FileExists(from))
            {
                // Bước đã xảy ra nhưng chưa kịp ghi done
                result.Processed++;
                Append(journalPath, step, "done");
                continue;
            }
            if (!_fs.FileExists(from))
            {
                result.Failed++;
                result.Errors.Add(CoreText.Get("CoreRecSourceMissing", "Không tìm thấy tệp nguồn: {0}", from));
                Append(journalPath, step, "failed");
                continue;
            }
            if (_fs.FileExists(to))
            {
                result.Failed++;
                result.Errors.Add(CoreText.Get("CoreRecTargetExists", "Tên đích đã tồn tại: {0}", to));
                Append(journalPath, step, "failed");
                continue;
            }

            try
            {
                _fs.Move(from, to, overwrite: false);
                result.Processed++;
                Append(journalPath, step, "done");
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"{from}: {ex.Message}");
                Append(journalPath, step, "failed");
            }
        }

        result.Success = result.Failed == 0;
        _fs.AppendAllText(journalPath, JsonSerializer.Serialize(new JournalEntry
        {
            Type = "footer",
            FooterStatus = result.Failed == 0 ? "recovered" : "recovered-with-errors",
            DoneUtc = System.DateTime.UtcNow.ToString("O")
        }) + "\n");
        return result;
    }

    /// <summary>
    /// Khôi phục về ban đầu: hoàn tác cả bước done và bước pending đã thực sự xảy ra,
    /// theo thứ tự ngược. Xử lý trường hợp sự cố xảy ra giữa lúc Move và lúc ghi done.
    /// </summary>
    public RecoveryResult Rollback(string journalPath)
    {
        var result = new RecoveryResult();
        var entries = UndoService.ReadSteps(journalPath, _fs);

        // Mỗi seq có thể có dòng pending và done; ưu tiên dòng done.
        var steps = entries
            .Where(e => e.Type == "step" && e.From is not null && (e.To is not null || !e.IsMoveLike))
            .GroupBy(e => e.Seq)
            .Select(g => g.OrderByDescending(e => e.Status == "done" ? 1 : 0).First())
            .OrderByDescending(e => e.Seq)
            .ToList();

        foreach (var step in steps)
        {
            if (!step.IsMoveLike)
            {
                RollbackOperation(step, result);
                continue;
            }

            var from = step.From!;
            var to = step.To!;

            if (_fs.FileExists(to) && !_fs.FileExists(from))
            {
                try
                {
                    _fs.Move(to, from, overwrite: false);
                    result.Processed++;
                }
                catch (Exception ex)
                {
                    result.Failed++;
                    result.Errors.Add($"{to}: {ex.Message}");
                }
            }
            else if (_fs.FileExists(from) && !_fs.FileExists(to))
            {
                // Bước chưa xảy ra: không cần làm gì.
            }
            else if (!_fs.FileExists(from))
            {
                result.Failed++;
                result.Errors.Add(CoreText.Get("CoreRecBothMissing", "{0}: cả tệp nguồn và đích đều không tồn tại", to));
            }
            else
            {
                result.Failed++;
                result.Errors.Add(CoreText.Get("CoreRecSourceTaken", "{0}: tên nguồn đã bị chiếm", to));
            }
        }

        result.Success = result.Failed == 0;
        return result;
    }

    private static readonly JsonSerializerOptions WriteOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    /// <summary>Ghi lại bước với trạng thái mới, giữ nguyên mọi trường (journal v2 cần chúng để hoàn tác).</summary>
    private void Append(string journalPath, JournalEntry step, string status)
    {
        _fs.AppendAllText(journalPath, JsonSerializer.Serialize(step.WithStatus(status), WriteOptions) + "\n");
    }

    /// <summary>Hoàn tất một thao tác (không phải đổi tên) còn dang dở.</summary>
    private void CompleteOperation(string journalPath, JournalEntry step, RecoveryResult result)
    {
        try
        {
            if (!OperationUndo.IsApplied(_fs, step)) OperationExecutor.Apply(_fs, step);
            result.Processed++;
            Append(journalPath, step, "done");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            result.Failed++;
            result.Errors.Add($"{step.From}: {ex.Message}");
            Append(journalPath, step, "failed");
        }
    }

    /// <summary>Đưa một thao tác (không phải đổi tên) về trạng thái ban đầu nếu nó đã thực sự xảy ra.</summary>
    private void RollbackOperation(JournalEntry step, RecoveryResult result)
    {
        if (step.Status != "done" && !OperationUndo.IsApplied(_fs, step)) return; // chưa xảy ra

        var error = OperationUndo.Undo(_fs, step);
        if (error is null)
        {
            result.Processed++;
        }
        else
        {
            result.Failed++;
            result.Errors.Add($"{step.From}: {error}");
        }
    }
}
