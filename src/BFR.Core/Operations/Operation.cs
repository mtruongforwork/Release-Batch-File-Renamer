namespace BFR.Core;

/// <summary>Ba mốc thời gian của một tệp, lưu bằng tick UTC để ghi vào journal không mất độ chính xác.</summary>
public readonly record struct FileTimes(long CreatedUtcTicks, long ModifiedUtcTicks, long AccessedUtcTicks)
{
    public static FileTimes From(DateTime createdUtc, DateTime modifiedUtc, DateTime accessedUtc) =>
        new(createdUtc.Ticks, modifiedUtc.Ticks, accessedUtc.Ticks);

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime CreatedUtc => new(CreatedUtcTicks, DateTimeKind.Utc);

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime ModifiedUtc => new(ModifiedUtcTicks, DateTimeKind.Utc);

    [System.Text.Json.Serialization.JsonIgnore]
    public DateTime AccessedUtc => new(AccessedUtcTicks, DateTimeKind.Utc);
}

/// <summary>Loại thao tác mà <see cref="OperationExecutor"/> thực thi và <see cref="UndoService"/> đảo ngược được.</summary>
public enum OperationKind
{
    /// <summary>Đổi tên hoặc di chuyển tệp. Hoàn tác: chuyển lại.</summary>
    Move,

    /// <summary>Sao chép tệp. Hoàn tác: xoá bản sao.</summary>
    Copy,

    /// <summary>Tạo thư mục (nếu chưa có). Hoàn tác: xoá nếu còn rỗng và do BFR tạo.</summary>
    CreateDir,

    /// <summary>Xoá thư mục RỖNG. Hoàn tác: tạo lại.</summary>
    RemoveDir,

    /// <summary>"Xoá" an toàn: chuyển tệp vào khu cách ly. Hoàn tác: chuyển về chỗ cũ.</summary>
    Quarantine,

    /// <summary>Đặt ngày tạo/sửa/truy cập. Hoàn tác: khôi phục giá trị cũ.</summary>
    SetTimestamps,

    /// <summary>Đặt thuộc tính tệp. Hoàn tác: khôi phục giá trị cũ.</summary>
    SetAttributes,

    /// <summary>
    /// Thay nội dung tệp bằng một tệp tạm đã dựng sẵn (ảnh, EXIF, ZIP...). Tệp cũ được giữ trong khu cách ly.
    /// Hoàn tác: trả tệp cũ về.
    /// </summary>
    WriteFile
}

/// <summary>
/// Một thao tác trong kế hoạch. Phẳng và bất biến để planner (code thuần) dựng, journal ghi và đọc lại dễ dàng.
/// Dùng các hàm dựng tĩnh để không điền nhầm trường.
/// </summary>
public sealed record Operation
{
    public OperationKind Kind { get; init; }

    /// <summary>Tệp/thư mục nguồn (Move, Copy, Quarantine), đối tượng (CreateDir, RemoveDir, Set*) hoặc tệp tạm (WriteFile).</summary>
    public string From { get; init; } = string.Empty;

    /// <summary>Đích (Move, Copy, WriteFile). Với Quarantine, executor tự điền khi chạy.</summary>
    public string To { get; init; } = string.Empty;

    /// <summary>Chỉ dùng cho Move: bước trực tiếp hay qua tên tạm (xử lý vòng hoán đổi).</summary>
    public StepKind MoveKind { get; init; } = StepKind.Direct;

    public FileTimes? TimesAfter { get; init; }
    public FileAttributes? AttributesAfter { get; init; }

    public static Operation Move(string from, string to, StepKind kind = StepKind.Direct) =>
        new() { Kind = OperationKind.Move, From = from, To = to, MoveKind = kind };

    public static Operation Copy(string from, string to) =>
        new() { Kind = OperationKind.Copy, From = from, To = to };

    public static Operation CreateDir(string path) =>
        new() { Kind = OperationKind.CreateDir, From = path };

    public static Operation RemoveDir(string path) =>
        new() { Kind = OperationKind.RemoveDir, From = path };

    public static Operation Quarantine(string path) =>
        new() { Kind = OperationKind.Quarantine, From = path };

    public static Operation SetTimestamps(string path, FileTimes after) =>
        new() { Kind = OperationKind.SetTimestamps, From = path, TimesAfter = after };

    public static Operation SetAttributes(string path, FileAttributes after) =>
        new() { Kind = OperationKind.SetAttributes, From = path, AttributesAfter = after };

    public static Operation WriteFile(string tempSource, string target) =>
        new() { Kind = OperationKind.WriteFile, From = tempSource, To = target };

    /// <summary>Chuyển từ kế hoạch đổi tên cũ.</summary>
    public static Operation FromStep(Step step) => Move(step.From, step.To, step.Kind);
}
