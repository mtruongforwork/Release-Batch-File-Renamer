namespace BFR.Core;

using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Tạo IRule từ RuleDefinition trong preset.
/// </summary>
public static class RuleFactory
{
    public static IRule Create(RuleDefinition def, bool allowExtensionChange = false, int maxListRows = int.MaxValue)
    {
        var enabled = def.Enabled;
        return def.Type switch
        {
            "FindReplace" => CreateFindReplace(def, enabled),
            "Numbering" => CreateNumbering(def, enabled),
            "Trim" => CreateTrim(def, enabled),
            "Insert" => CreateInsert(def, enabled),
            "Case" => CreateCase(def, enabled),
            "Sanitize" => CreateSanitize(def, enabled),
            "Date" => CreateDate(def, enabled),
            "Extension" => CreateExtension(def, enabled, allowExtensionChange),
            "Template" => new TemplateRule { Enabled = enabled, Template = S(def, "template") ?? "{name}" },
            "ListMap" => CreateListMap(def, enabled, maxListRows),
            "ReplaceDict" => new ReplaceDictRule
            {
                Enabled = enabled,
                IgnoreCase = !def.Params.ContainsKey("ignoreCase") || B(def, "ignoreCase"),
                MaxRows = maxListRows,
                Pairs = S(def, "pairs") ?? string.Empty
            },
            _ => throw new ArgumentException($"Loại quy tắc không hỗ trợ: {def.Type}")
        };
    }

    private static ListMapRule CreateListMap(RuleDefinition d, bool enabled, int maxListRows) => new()
    {
        Enabled = enabled,
        Mode = Enum.TryParse<ListMapRule.ListMode>(S(d, "mode"), true, out var mode) ? mode : ListMapRule.ListMode.Map,
        MatchBy = Enum.TryParse<ListMapRule.MatchTarget>(S(d, "matchBy"), true, out var by) ? by : ListMapRule.MatchTarget.Name,
        IgnoreCase = !d.Params.ContainsKey("ignoreCase") || B(d, "ignoreCase"),
        MaxRows = maxListRows,
        Mapping = S(d, "mapping") ?? string.Empty
    };

    private static FindReplaceRule CreateFindReplace(RuleDefinition d, bool enabled) => new()
    {
        Enabled = enabled,
        Pattern = S(d, "pattern") ?? string.Empty,
        Replacement = S(d, "replacement") ?? string.Empty,
        IsRegex = B(d, "isRegex"),
        IgnoreCase = B(d, "ignoreCase"),
        ReplaceAll = !d.Params.ContainsKey("replaceAll") || B(d, "replaceAll"),
        Scope = Enum.TryParse<FindReplaceRule.ApplyScope>(S(d, "scope"), true, out var scope) ? scope : FindReplaceRule.ApplyScope.Name
    };

    private static NumberingRule CreateNumbering(RuleDefinition d, bool enabled) => new()
    {
        Enabled = enabled,
        Start = I(d, "start", 1),
        Step = I(d, "step", 1),
        Width = d.Params.ContainsKey("width") ? I(d, "width", 1) : null,
        Position = Enum.TryParse<NumberingRule.NumberPosition>(S(d, "position"), true, out var pos) ? pos : NumberingRule.NumberPosition.Start,
        Separator = S(d, "separator") ?? "_",
        ResetMode = Enum.TryParse<NumberingRule.NumberResetMode>(S(d, "resetMode"), true, out var reset) ? reset : NumberingRule.NumberResetMode.None,
        Kind = Enum.TryParse<NumberingRule.NumberKind>(S(d, "kind"), true, out var kind) ? kind : NumberingRule.NumberKind.Decimal
    };

    private static TrimRule CreateTrim(RuleDefinition d, bool enabled) => new()
    {
        Enabled = enabled,
        RemoveChars = S(d, "removeChars") ?? string.Empty,
        CollapseSpaces = B(d, "collapseSpaces"),
        TrimEnds = B(d, "trimEnds"),
        RemoveFirstN = I(d, "removeFirstN", 0),
        RemoveLastN = I(d, "removeLastN", 0),
        RemoveRangeStart = d.Params.ContainsKey("removeRangeStart") ? I(d, "removeRangeStart", 0) : null,
        RemoveRangeEnd = d.Params.ContainsKey("removeRangeEnd") ? I(d, "removeRangeEnd", 0) : null,
        RemoveTrailingDotSpace = B(d, "removeTrailingDotSpace")
    };

    private static InsertRule CreateInsert(RuleDefinition d, bool enabled) => new()
    {
        Enabled = enabled,
        Text = S(d, "text") ?? string.Empty,
        Position = Enum.TryParse<InsertRule.InsertPosition>(S(d, "position"), true, out var pos) ? pos : InsertRule.InsertPosition.Start,
        Index = I(d, "index", 0),
        CountFromEnd = B(d, "countFromEnd")
    };

    private static CaseRule CreateCase(RuleDefinition d, bool enabled)
    {
        var rule = new CaseRule
        {
            Enabled = enabled,
            Mode = Enum.TryParse<CaseRule.CaseMode>(S(d, "mode"), true, out var mode) ? mode : CaseRule.CaseMode.Lower,
            Scope = Enum.TryParse<CaseRule.ApplyScope>(S(d, "scope"), true, out var scope) ? scope : CaseRule.ApplyScope.Name
        };
        foreach (var word in StringArray(d, "exceptions")) rule.Exceptions.Add(word);
        return rule;
    }

    private static SanitizeRule CreateSanitize(RuleDefinition d, bool enabled) => new()
    {
        Enabled = enabled,
        RemoveDiacritics = B(d, "removeDiacritics"),
        NormalizeNfc = B(d, "normalizeNfc"),
        ReplaceInvalidChars = B(d, "replaceInvalidChars"),
        InvalidCharReplacement = (S(d, "invalidCharReplacement") ?? "_").FirstOrDefault('_'),
        Spaces = Enum.TryParse<SanitizeRule.SpaceMode>(S(d, "spaces"), true, out var spaces) ? spaces : SanitizeRule.SpaceMode.Keep,
        Slugify = B(d, "slugify")
    };

    private static DateRule CreateDate(RuleDefinition d, bool enabled)
    {
        var rule = new DateRule
        {
            Enabled = enabled,
            Source = Enum.TryParse<DateRule.DateSource>(S(d, "source"), true, out var source) ? source : DateRule.DateSource.Created,
            Format = S(d, "format") ?? "yyyy-MM-dd",
            Position = Enum.TryParse<DateRule.DatePosition>(S(d, "position"), true, out var pos) ? pos : DateRule.DatePosition.Start,
            Separator = S(d, "separator") ?? "_",
            UseUtc = B(d, "useUtc")
        };
        var fallback = StringArray(d, "fallback");
        if (fallback.Count > 0)
        {
            rule.Fallback.Clear();
            foreach (var f in fallback)
            {
                if (Enum.TryParse<DateRule.DateSource>(f, true, out var ds)) rule.Fallback.Add(ds);
            }
        }
        return rule;
    }

    private static ExtensionRule CreateExtension(RuleDefinition d, bool enabled, bool allow) => new()
    {
        Enabled = enabled,
        Mode = Enum.TryParse<ExtensionRule.ExtensionMode>(S(d, "mode"), true, out var mode) ? mode : ExtensionRule.ExtensionMode.Keep,
        NewExtension = S(d, "newExtension") ?? string.Empty,
        AllowExtensionChange = allow || B(d, "allowExtensionChange")
    };

    private static string? S(RuleDefinition d, string key) =>
        d.Params.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;

    private static bool B(RuleDefinition d, string key) =>
        d.Params.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.True;

    private static int I(RuleDefinition d, string key, int fallback) =>
        d.Params.TryGetValue(key, out var e) && e.TryGetInt32(out var v) ? v : fallback;

    private static List<string> StringArray(RuleDefinition d, string key)
    {
        var list = new List<string>();
        if (d.Params.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in e.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String) list.Add(item.GetString()!);
        }
        return list;
    }
}
