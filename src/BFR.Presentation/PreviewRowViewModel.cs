namespace BFR.Presentation;

using BFR.Core;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Một đoạn chữ trong tên, dùng để tô màu phần thêm/bị xóa (không chỉ dựa vào màu: có gạch ngang).</summary>
public readonly record struct DiffSegment(string Text, bool Highlight);

/// <summary>Một dòng của bảng xem trước.</summary>
public sealed class PreviewRowViewModel : ObservableObject
{
    private readonly IStringProvider _strings;

    public PreviewRowViewModel(
        FileItem file,
        string oldName,
        string newName,
        PreviewStatus status,
        string? detail,
        bool isSelected = true,
        IStringProvider? strings = null)
    {
        _strings = strings ?? new KeyOnlyStringProvider();

        File = file;
        OldName = oldName;
        NewName = newName;
        Status = status;
        Detail = detail ?? string.Empty;
        IsSelected = isSelected;

        var diff = Diff(oldName, newName);
        OldSegments = diff.Old;
        NewSegments = diff.New;
    }

    public FileItem File { get; }

    public string OldName { get; }

    public string NewName { get; }

    public PreviewStatus Status { get; }

    public string Detail { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    private bool _isSelected = true;

    public IReadOnlyList<DiffSegment> OldSegments { get; }

    public IReadOnlyList<DiffSegment> NewSegments { get; }

    public string FolderText => File.Directory;

    public string StatusText => Status switch
    {
        PreviewStatus.Ok => _strings.T("StatusOk"),
        PreviewStatus.Unchanged => _strings.T("StatusUnchanged"),
        PreviewStatus.Conflict => _strings.T("StatusConflict"),
        PreviewStatus.InvalidName => _strings.T("StatusInvalidName"),
        PreviewStatus.PathTooLong => _strings.T("StatusPathTooLong"),
        PreviewStatus.Locked => _strings.T("StatusLocked"),
        PreviewStatus.MissingData => _strings.T("StatusMissingData"),
        PreviewStatus.Protected => _strings.T("StatusProtected"),
        _ => Status.ToString()
    };

    /// <summary>Icon trạng thái (Segoe Fluent Icons).</summary>
    public string StatusIcon => Status switch
    {
        PreviewStatus.Ok => "",           // E7BE check
        PreviewStatus.Unchanged => "",       // E712 dash
        PreviewStatus.Conflict => "",        // E703 warning
        PreviewStatus.InvalidName => "",     // E711 error circle
        PreviewStatus.PathTooLong => "",
        PreviewStatus.Locked => "",
        PreviewStatus.MissingData => "",
        _ => ""
    };

    /// <summary>Tính diff đơn giản: tiền tố và hậu tố chung, phần giữa là khác biệt.</summary>
    public static (IReadOnlyList<DiffSegment> Old, IReadOnlyList<DiffSegment> New) Diff(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.Ordinal))
        {
            return (new[] { new DiffSegment(oldName, false) }, new[] { new DiffSegment(newName, false) });
        }

        var oldChars = oldName.AsSpan();
        var newChars = newName.AsSpan();

        int prefix = 0;
        int maxPrefix = Math.Min(oldChars.Length, newChars.Length);
        while (prefix < maxPrefix && oldChars[prefix] == newChars[prefix]) prefix++;

        int suffix = 0;
        int maxSuffix = Math.Min(oldChars.Length - prefix, newChars.Length - prefix);
        while (suffix < maxSuffix &&
               oldChars[oldChars.Length - 1 - suffix] == newChars[newChars.Length - 1 - suffix]) suffix++;

        var oldMiddle = oldName[prefix..(oldName.Length - suffix)];
        var newMiddle = newName[prefix..(newName.Length - suffix)];

        var oldSegments = new List<DiffSegment>();
        var newSegments = new List<DiffSegment>();

        if (prefix > 0)
        {
            oldSegments.Add(new DiffSegment(oldName[..prefix], false));
            newSegments.Add(new DiffSegment(newName[..prefix], false));
        }

        if (oldMiddle.Length > 0) oldSegments.Add(new DiffSegment(oldMiddle, true));
        if (newMiddle.Length > 0) newSegments.Add(new DiffSegment(newMiddle, true));

        if (suffix > 0)
        {
            oldSegments.Add(new DiffSegment(oldName[(oldName.Length - suffix)..], false));
            newSegments.Add(new DiffSegment(newName[(newName.Length - suffix)..], false));
        }

        return (oldSegments, newSegments);
    }
}
