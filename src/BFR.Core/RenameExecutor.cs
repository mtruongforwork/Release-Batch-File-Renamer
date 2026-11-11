namespace BFR.Core;

using System.Text.Json;

/// <summary>
/// Thực thi kế hoạch đổi tên với journal ghi trước (Write-Ahead Journal).
/// Mỗi bước: ghi pending, flush, đổi tên, ghi done/failed, flush.
/// </summary>
public sealed class JournalEntry
{
    public string Type { get; set; } = string.Empty; // header, step, footer
    public string? Id { get; set; }
    public string? StartedUtc { get; set; }
    public int TotalSteps { get; set; }
    public string? AppVersion { get; set; }

    public int Seq { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Kind { get; set; }
    public string? Status { get; set; } // pending, done, failed

    public string? FooterStatus { get; set; }
    public string? DoneUtc { get; set; }
}

public sealed class RenameExecutorResult
{
    public string JournalId { get; set; } = string.Empty;

    /// <summary>Đường dẫn đầy đủ của tệp journal vừa ghi (dùng để hoàn tác).</summary>
    public string JournalPath { get; set; } = string.Empty;

    public int TotalSteps { get; set; }
    public int Succeeded { get; set; }
    public int Failed { get; set; }
    public bool Cancelled { get; set; }
    public List<(string Step, string Error)> Errors { get; set; } = new();
}

public sealed class RenameExecutor
{
    private readonly IFileSystem _fs;
    private readonly string _journalDir;

    public RenameExecutor(IFileSystem fs, string? journalDir = null)
    {
        _fs = fs;
        _journalDir = journalDir ?? System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "BatchFileRenamer", "journal");
    }

    /// <summary>
    /// Thực thi danh sách bước theo journal. Trả về kết quả.
    /// </summary>
    public RenameExecutorResult Execute(IReadOnlyList<Step> steps, string appVersion = "1.0.0", System.Threading.CancellationToken ct = default)
    {
        var journalId = Guid.NewGuid().ToString("N")[..8];
        _fs.EnsureDirectory(_journalDir);
        var journalPath = System.IO.Path.Combine(_journalDir, $"{System.DateTime.UtcNow:yyyyMMdd-HHmmss}-{journalId}.jsonl");

        var result = new RenameExecutorResult
        {
            JournalId = journalId,
            JournalPath = journalPath,
            TotalSteps = steps.Count
        };

        // Header
        AppendLine(journalPath, new JournalEntry
        {
            Type = "header", Id = journalId, StartedUtc = System.DateTime.UtcNow.ToString("O"),
            TotalSteps = steps.Count, AppVersion = appVersion
        });

        for (int i = 0; i < steps.Count; i++)
        {
            if (ct.IsCancellationRequested)
            {
                AppendLine(journalPath, new JournalEntry { Type = "footer", FooterStatus = "cancelled", DoneUtc = System.DateTime.UtcNow.ToString("O") });
                result.Cancelled = true;
                return result;
            }

            var step = steps[i];
            AppendLine(journalPath, new JournalEntry { Type = "step", Seq = i, From = step.From, To = step.To, Kind = step.Kind.ToString(), Status = "pending" });

            try
            {
                // Lớp bảo vệ cuối (C10): dù phía trên đã chặn, executor không bao giờ chạm vùng hệ thống.
                if (ProtectedZones.Default.IsProtected(step.From) || ProtectedZones.Default.IsProtected(step.To))
                {
                    throw new UnauthorizedAccessException(CoreText.Get("CoreProtectedZone", "Nằm trong vùng hệ thống được bảo vệ"));
                }

                switch (step.Kind)
                {
                    case StepKind.Direct:
                    case StepKind.ToTemp:
                    case StepKind.FromTemp:
                        _fs.Move(step.From, step.To, overwrite: false);
                        break;
                }
                AppendLine(journalPath, new JournalEntry { Type = "step", Seq = i, From = step.From, To = step.To, Kind = step.Kind.ToString(), Status = "done" });
                result.Succeeded++;
            }
            catch (Exception ex)
            {
                AppendLine(journalPath, new JournalEntry { Type = "step", Seq = i, From = step.From, To = step.To, Kind = step.Kind.ToString(), Status = "failed" });
                result.Failed++;
                result.Errors.Add((step.From, ex.Message));
                // Tiếp tục các bước độc lập (ghi failed rồi sang bước tiếp theo)
            }
        }

        AppendLine(journalPath, new JournalEntry { Type = "footer", FooterStatus = result.Failed > 0 ? "failed" : "completed", DoneUtc = System.DateTime.UtcNow.ToString("O") });
        return result;
    }

    private void AppendLine(string path, JournalEntry entry)
    {
        var line = JsonSerializer.Serialize(entry);
        _fs.AppendAllText(path, line + "\n");
    }
}
