namespace BFR.Core;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// Quy tắc Từ điển thay thế (B2): nhiều cặp "tìm ⇥ thay" áp dụng cùng một lượt.
/// Các cặp được thay đồng thời (cặp dài khớp trước, kết quả của cặp này không bị cặp khác thay lại).
/// </summary>
public sealed class ReplaceDictRule : IRule
{
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public bool Enabled { get; set; } = true;

    public bool IgnoreCase { get; set; } = true;
    public int MaxRows { get; set; } = int.MaxValue;

    private string _pairs = string.Empty;
    private bool _built;
    private Regex? _regex;
    private Dictionary<string, string> _map = new();

    public string Pairs
    {
        get => _pairs;
        set { _pairs = value ?? string.Empty; _built = false; }
    }

    private void Build()
    {
        if (_built) return;

        var cmp = IgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _map = new Dictionary<string, string>(cmp);
        foreach (var row in ListParser.Parse(_pairs, MaxRows))
        {
            if (row.First.Length == 0) continue;
            _map.TryAdd(row.First, row.Second ?? string.Empty);
        }

        _regex = _map.Count == 0
            ? null
            : new Regex(
                string.Join("|", _map.Keys.OrderByDescending(k => k.Length).Select(Regex.Escape)),
                IgnoreCase ? RegexOptions.IgnoreCase | RegexOptions.CultureInvariant : RegexOptions.CultureInvariant,
                RegexLimits.Timeout);
        _built = true;
    }

    public RenameState Apply(FileItem file, RenameState state, RuleContext ctx)
    {
        Build();
        var trace = new List<string>(state.Trace);
        if (_regex is null)
        {
            trace.Add($"{Id}: " + CoreText.Get("CoreDictEmpty", "ReplaceDict (trống)"));
            return state with { Trace = trace };
        }

        string result;
        try
        {
            result = _regex.Replace(state.Name, m => _map.TryGetValue(m.Value, out var r) ? r : m.Value);
        }
        catch (RegexMatchTimeoutException)
        {
            trace.Add($"{Id}: " + CoreText.Get("CoreRegexTimeout", "Regex quá chậm (timeout)"));
            return state with { Trace = trace };
        }

        trace.Add($"{Id}: ReplaceDict → {result}");
        return state with { Name = result, Trace = trace };
    }
}
