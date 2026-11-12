namespace BFR.Presentation;

using BFR.Core.Licensing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

/// <summary>Kết quả của một lần thả tệp vào Shelf.</summary>
public readonly record struct ShelfAddResult(int Added, int Duplicates, int OverLimit)
{
    /// <summary>True nếu có tệp bị bỏ vì vượt giới hạn của bản Free.</summary>
    public bool HitLimit => OverLimit > 0;
}

/// <summary>
/// Khay gom tệp từ nhiều thư mục để đổi tên chung một lần.
///
/// Không phụ thuộc WPF; giới hạn số tệp lấy từ <see cref="LicenseService.MaxShelfItems"/>
/// (Free: 50, Pro: không giới hạn).
/// </summary>
public sealed class ShelfModel : ObservableObject
{
    private readonly LicenseService _license;
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public ShelfModel(LicenseService license)
    {
        _license = license ?? throw new ArgumentNullException(nameof(license));
        Items = new ObservableCollection<string>();
    }

    public ObservableCollection<string> Items { get; }

    public int Count => Items.Count;

    public int Limit => _license.MaxShelfItems;

    public bool IsEmpty => Items.Count == 0;

    /// <summary>Thêm đường dẫn, bỏ qua trùng lặp và phần vượt giới hạn.</summary>
    public ShelfAddResult Add(IEnumerable<string> paths)
    {
        int added = 0, duplicates = 0, over = 0;
        foreach (var raw in paths)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var path = raw.Trim();
            if (_keys.Contains(path))
            {
                duplicates++;
                continue;
            }

            if (Items.Count >= Limit)
            {
                over++;
                continue;
            }

            _keys.Add(path);
            Items.Add(path);
            added++;
        }

        if (added > 0) NotifyChanged();
        return new ShelfAddResult(added, duplicates, over);
    }

    public bool Remove(string path)
    {
        if (!_keys.Remove(path)) return false;
        var index = Items.ToList().FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        if (index >= 0) Items.RemoveAt(index);
        NotifyChanged();
        return true;
    }

    public void Clear()
    {
        if (Items.Count == 0) return;
        Items.Clear();
        _keys.Clear();
        NotifyChanged();
    }

    /// <summary>Lấy toàn bộ đường dẫn hiện có (bản sao) để đẩy vào cửa sổ chính.</summary>
    public IReadOnlyList<string> Snapshot() => Items.ToList();

    private void NotifyChanged()
    {
        Raise(nameof(Count));
        Raise(nameof(IsEmpty));
    }
}
