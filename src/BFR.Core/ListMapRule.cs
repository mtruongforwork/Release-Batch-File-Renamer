namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Quy tắc Đổi tên theo danh sách (B2).
/// <list type="bullet">
/// <item><b>Map</b>: mỗi dòng "tên cũ ⇥ tên mới"; tệp nào khớp tên cũ sẽ lấy tên mới.</item>
/// <item><b>ByOrder</b>: mỗi dòng là một tên mới; tệp thứ i (theo thứ tự đã sắp xếp) lấy dòng thứ i.</item>
/// </list>
/// </summary>
public sealed class ListMapRule : IRule
{
    public enum ListMode { Map, ByOrder }
    public enum MatchTarget { Name, FileName }

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;

    public ListMode Mode { get; set; } = ListMode.Map;

    /// <summary>Name: so với tên không đuôi. FileName: so với "tên.đuôi" và tên mới có thể kèm đuôi.</summary>
    public MatchTarget MatchBy { get; set; } = MatchTarget.Name;

    public bool IgnoreCase { get; set; } = true;

    /// <summary>Số dòng tối đa được dùng (bản Free bị giới hạn); phần dư bị bỏ qua.</summary>
    public int MaxRows { get; set; } = int.MaxValue;

    private string _mapping = string.Empty;
    private bool _parsed;
    private IReadOnlyList<ListRow> _rows = Array.Empty<ListRow>();
    private Dictionary<string, string>? _lookup;

    public string Mapping
    {
        get => _mapping;
        set { _mapping = value ?? string.Empty; _parsed = false; }
    }

    private void EnsureParsed()
    {
        if (_parsed) return;

        _rows = ListParser.Parse(_mapping, MaxRows);
        _lookup = null;
        if (Mode == ListMode.Map)
        {
            var cmp = IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            _lookup = new Dictionary<string, string>(cmp);
            foreach (var row in _rows)
            {
                if (row.Second is null || row.First.Length == 0) continue;
                _lookup.TryAdd(row.First, row.Second);   // dòng đầu tiên thắng
            }
        }
        _parsed = true;
    }

    public RenameState Apply(FileItem file, RenameState state, RuleContext ctx)
    {
        EnsureParsed();
        var trace = new List<string>(state.Trace);

        string? replacement = null;
        if (Mode == ListMode.ByOrder)
        {
            if (state.Index >= 0 && state.Index < _rows.Count) replacement = _rows[state.Index].First;
        }
        else if (_lookup is not null)
        {
            var key = MatchBy == MatchTarget.FileName && state.Extension.Length > 0
                ? state.Name + "." + state.Extension
                : state.Name;
            if (_lookup.TryGetValue(key, out var found)) replacement = found;
        }

        if (replacement is null)
        {
            trace.Add($"{Id}: " + CoreText.Get("CoreListNoMatch", "ListMap (không khớp)"));
            return state with { Trace = trace };
        }

        var name = replacement;
        var ext = state.Extension;
        if (MatchBy == MatchTarget.FileName)
        {
            var newExt = Path.GetExtension(replacement).TrimStart('.');
            if (newExt.Length > 0)
            {
                name = Path.GetFileNameWithoutExtension(replacement);
                ext = newExt;
            }
        }

        trace.Add($"{Id}: ListMap → {name}");
        return state with { Name = name, Extension = ext, Trace = trace };
    }
}
