namespace BFR.Core;

using System.IO;
using System.Text.Json.Serialization;

/// <summary>
/// Một bước của workflow (M10). Chỉ gồm các bước chỉ dựa trên đường dẫn và metadata — không cần ghi nội dung tệp —
/// để toàn bộ chuỗi chạy thử được trên <see cref="OverlayFileSystem"/> và ghi vào MỘT journal duy nhất.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "step")]
[JsonDerivedType(typeof(RenameStep), "rename")]
[JsonDerivedType(typeof(OrganizeStep), "organize")]
[JsonDerivedType(typeof(WrapStep), "wrap")]
[JsonDerivedType(typeof(FlattenStep), "flatten")]
[JsonDerivedType(typeof(RemoveEmptyStep), "removeEmpty")]
[JsonDerivedType(typeof(TimestampStep), "timestamps")]
[JsonDerivedType(typeof(AttributeStep), "attributes")]
public abstract record WorkflowStep
{
    [JsonIgnore]
    public abstract string Kind { get; }
}

/// <summary>Đổi tên theo một preset đổi tên.</summary>
public sealed record RenameStep(Preset Preset) : WorkflowStep
{
    public override string Kind => "rename";
}

/// <summary>Sắp xếp vào thư mục theo mẫu. <see cref="Root"/> null = thư mục gốc của workflow.</summary>
public sealed record OrganizeStep(
    string Template,
    OrganizeMode Mode = OrganizeMode.Move,
    OrganizeConflictPolicy Conflict = OrganizeConflictPolicy.Rename,
    string? Root = null) : WorkflowStep
{
    public override string Kind => "organize";
}

public sealed record WrapStep(bool GroupSameName = true) : WorkflowStep
{
    public override string Kind => "wrap";
}

/// <summary>Làm phẳng cây thư mục gốc của workflow.</summary>
public sealed record FlattenStep(bool PrefixPath = false, string Separator = "_", bool RemoveEmptiedFolders = true) : WorkflowStep
{
    public override string Kind => "flatten";
}

/// <summary>Xoá (cách ly) thư mục rỗng trong cây gốc của workflow.</summary>
public sealed record RemoveEmptyStep(bool TreatJunkAsEmpty = true) : WorkflowStep
{
    public override string Kind => "removeEmpty";
}

public sealed record TimestampStep(TimestampOptions Options) : WorkflowStep
{
    public override string Kind => "timestamps";
}

public sealed record AttributeStep(FileAttributes Set = 0, FileAttributes Clear = 0) : WorkflowStep
{
    public override string Kind => "attributes";
}

/// <summary>Định nghĩa một workflow có tên, lưu được thành preset.</summary>
public sealed record WorkflowDefinition(string Name, System.Collections.Generic.List<WorkflowStep> Steps);
