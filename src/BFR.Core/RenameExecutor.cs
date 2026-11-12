namespace BFR.Core;

using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Một dòng của journal (JSON Lines). Journal v1 chỉ có đổi tên; journal v2 (header có <see cref="Schema"/> = 2)
/// thêm <see cref="Op"/> và các trường phụ. Bước Move của v2 giữ nguyên From/To/Kind/Status như v1
/// nên mã đọc journal cũ vẫn hiểu được chúng.
/// </summary>
public sealed class JournalEntry
{
    public string Type { get; set; } = string.Empty; // header, step, footer
    public string? Id { get; set; }
    public string? StartedUtc { get; set; }
    public int TotalSteps { get; set; }
    public string? AppVersion { get; set; }

    /// <summary>Phiên bản định dạng journal (chỉ có ở header). Null hoặc 1 = đổi tên thuần tuý.</summary>
    public int? Schema { get; set; }

    public int Seq { get; set; }
    public string? From { get; set; }
    public string? To { get; set; }
    public string? Kind { get; set; }
    public string? Status { get; set; } // pending, done, failed

    /// <summary>Loại thao tác (<see cref="OperationKind"/>); null nghĩa là Move (journal v1).</summary>
    public string? Op { get; set; }

    /// <summary>Thông tin phụ theo loại: "existed" cho CreateDir đã tồn tại trước đó.</summary>
    public string? Aux { get; set; }

    /// <summary>WriteFile: đường dẫn trong khu cách ly của tệp cũ bị thay thế (null nếu trước đó chưa có tệp).</summary>
    public string? Backup { get; set; }

    public FileTimes? Before { get; set; }
    public FileTimes? After { get; set; }
    public int? AttrBefore { get; set; }
    public int? AttrAfter { get; set; }

    public string? FooterStatus { get; set; }
    public string? DoneUtc { get; set; }

    /// <summary>True nếu bước này là đổi tên/di chuyển kiểu v1 (kể cả Quarantine, vốn là một lần di chuyển).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsMoveLike =>
        string.IsNullOrEmpty(Op)
        || string.Equals(Op, nameof(OperationKind.Move), System.StringComparison.Ordinal)
        || string.Equals(Op, nameof(OperationKind.Quarantine), System.StringComparison.Ordinal);

    public JournalEntry WithStatus(string status)
    {
        var copy = (JournalEntry)MemberwiseClone();
        copy.Status = status;
        return copy;
    }
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

/// <summary>
/// Thực thi kế hoạch đổi tên. Từ v2.0 chỉ là lớp mỏng chuyển các <see cref="Step"/> thành
/// <see cref="Operation"/> rồi giao cho <see cref="OperationExecutor"/>: một bộ thực thi và một journal cho mọi công cụ.
/// </summary>
public sealed class RenameExecutor
{
    private readonly OperationExecutor _executor;

    public RenameExecutor(IFileSystem fs, string? journalDir = null)
    {
        _executor = new OperationExecutor(fs, journalDir);
    }

    public RenameExecutorResult Execute(IReadOnlyList<Step> steps, string appVersion = "1.0.0", System.Threading.CancellationToken ct = default) =>
        _executor.Execute(steps.Select(Operation.FromStep).ToList(), appVersion, ct);
}
